using System.Runtime.InteropServices;
using System.Text;
using static Launchy.Services.Windows.NativeMethods;

namespace Launchy.Services.Windows;

/// <summary>Read-only view of the desktop's top-level windows and monitors.</summary>
public sealed class Win32WindowSource : IWindowSource
{
    private const string ExplorerClass = "CabinetWClass";
    private const string StoreAppFrameClass = AppIdentity.StoreAppFrameClass;
    private const string StoreAppCoreClass = "Windows.UI.Core.CoreWindow";

    private static bool _isWindowArrangedMissing;

    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public IReadOnlyList<WindowInfo> GetTopLevelWindows()
    {
        var ownProcessId = (uint)Environment.ProcessId;
        var handles = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            handles.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        var kept = handles.Where(h => IsAppWindow(h, ownProcessId)).ToList();
        var explorerPaths = kept.Any(h => GetClassName(h) == ExplorerClass)
            ? GetExplorerFolderPaths()
            : new Dictionary<long, string>();

        var windows = new List<WindowInfo>(kept.Count);
        foreach (var handle in kept)
        {
            var info = ReadWindow(handle, explorerPaths);
            if (info is not null)
            {
                windows.Add(info);
            }
        }

        return windows;
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            if (ReadMonitor(hMonitor) is { } monitor)
            {
                monitors.Add(monitor);
            }

            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    /// <summary>DWM frame bounds without the invisible resize border; null if DWM gives no answer.</summary>
    public static IntRect? GetExtendedFrameBounds(IntPtr hWnd) =>
        DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, Marshal.SizeOf<RECT>()) == 0
            ? rect.ToIntRect()
            : null;

    public static bool AttachToParentConsole() => AttachConsole(ATTACH_PARENT_PROCESS);

    private static bool IsAppWindow(IntPtr hWnd, uint ownProcessId)
    {
        var isVisible = IsWindowVisible(hWnd);
        if (!isVisible)
        {
            return false;
        }

        GetWindowThreadProcessId(hWnd, out var processId);
        return WindowFilter.IsAppWindow(
            isVisible,
            IsCloaked(hWnd),
            GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64(),
            GetWindow(hWnd, GW_OWNER) != IntPtr.Zero,
            GetWindowText(hWnd),
            processId,
            ownProcessId);
    }

    private static WindowInfo? ReadWindow(IntPtr hWnd, IReadOnlyDictionary<long, string> explorerPaths)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hWnd, ref placement) || !GetWindowRect(hWnd, out var rect))
        {
            // The window closed while we were reading it.
            return null;
        }

        var className = GetClassName(hWnd);
        GetWindowThreadProcessId(hWnd, out var processId);
        uint coreProcessId = 0;

        if (className == StoreAppFrameClass)
        {
            var core = FindWindowEx(hWnd, IntPtr.Zero, StoreAppCoreClass, null);
            if (core != IntPtr.Zero)
            {
                GetWindowThreadProcessId(core, out coreProcessId);
                if (coreProcessId != 0)
                {
                    processId = coreProcessId;
                }
            }
        }

        var processPath = GetProcessPath(processId);
        var appUserModelId = AppIdentity.Resolve(
            className,
            processPath,
            () => coreProcessId != 0 ? GetAppUserModelId(coreProcessId) : null,
            () => GetWindowAppUserModelId(hWnd),
            () => GetAppUserModelId(processId));
        var style = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64();

        return new WindowInfo(
            Handle: hWnd,
            ProcessId: processId,
            ProcessPath: processPath,
            ClassName: className,
            Title: GetWindowText(hWnd),
            AppUserModelId: appUserModelId,
            FolderPath: explorerPaths.GetValueOrDefault(hWnd.ToInt64()),
            Rect: rect.ToIntRect(),
            NormalRect: placement.rcNormalPosition.ToIntRect(),
            ShowCmd: placement.showCmd,
            IsMaximized: IsZoomed(hWnd),
            IsMinimized: IsIconic(hWnd),
            IsArranged: IsArranged(hWnd),
            IsResizable: (style & WS_THICKFRAME) != 0,
            MonitorDevice: GetMonitorDevice(hWnd));
    }

    private static MonitorInfo? ReadMonitor(IntPtr hMonitor)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(hMonitor, ref info))
        {
            return null;
        }

        var dpi = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? (int)dpiX : 96;
        return new MonitorInfo(
            info.szDevice,
            info.rcMonitor.ToIntRect(),
            info.rcWork.ToIntRect(),
            dpi,
            (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
            GetMonitorId(info.szDevice));
    }

    /// <summary>The device interface path of the first active monitor on a display output; empty if there is none.</summary>
    private static string GetMonitorId(string displayDevice)
    {
        for (uint index = 0; ; index++)
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(displayDevice, index, ref device, EDD_GET_DEVICE_INTERFACE_NAME))
            {
                return "";
            }

            // An output can list inactive monitors; skip them.
            if ((device.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0 && !string.IsNullOrEmpty(device.DeviceID))
            {
                return device.DeviceID;
            }
        }
    }

    private static string GetMonitorDevice(IntPtr hWnd)
    {
        var hMonitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return hMonitor != IntPtr.Zero && GetMonitorInfo(hMonitor, ref info) ? info.szDevice : "";
    }

    private static bool IsCloaked(IntPtr hWnd) =>
        DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static bool IsArranged(IntPtr hWnd)
    {
        if (_isWindowArrangedMissing)
        {
            return false;
        }

        try
        {
            return IsWindowArranged(hWnd);
        }
        catch (EntryPointNotFoundException)
        {
            _isWindowArrangedMissing = true;
            return false;
        }
    }

    private static string GetWindowText(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return "";
        }

        var text = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }

    private static string GetClassName(IntPtr hWnd)
    {
        var name = new StringBuilder(256);
        return NativeMethods.GetClassName(hWnd, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    /// <summary>Empty when the process can't be opened, for example an elevated process.</summary>
    private static string GetProcessPath(uint processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero)
        {
            return "";
        }

        try
        {
            var size = 1024;
            var path = new StringBuilder(size);
            return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string? GetAppUserModelId(uint processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var length = 256;
            var aumid = new StringBuilder(length);
            return GetApplicationUserModelId(process, ref length, aumid) == 0 ? aumid.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>The window's System.AppUserModel.ID property. The shell sets it on Store app frames.</summary>
    private static string? GetWindowAppUserModelId(IntPtr hWnd)
    {
        var iid = IID_IPropertyStore;
        if (SHGetPropertyStoreForWindow(hWnd, ref iid, out var store) != 0 || store is null)
        {
            return null;
        }

        try
        {
            var key = PKEY_AppUserModel_ID;
            if (store.GetValue(ref key, out var value) != 0)
            {
                return null;
            }

            try
            {
                return value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.pointerValue) : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>Maps each open File Explorer window's HWND to its folder path via Shell.Application.</summary>
    private static Dictionary<long, string> GetExplorerFolderPaths()
    {
        var paths = new Dictionary<long, string>();
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return paths;
        }

        object? shell = null;
        object? shellWindows = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return paths;
            }

            shellWindows = ((dynamic)shell).Windows();
            foreach (var window in (System.Collections.IEnumerable)shellWindows!)
            {
                try
                {
                    ReadExplorerWindow(window, paths);
                }
                finally
                {
                    Release(window);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException
                                       or InvalidCastException)
        {
        }
        finally
        {
            Release(shellWindows);
            Release(shell);
        }

        return paths;
    }

    private static void ReadExplorerWindow(object window, Dictionary<long, string> paths)
    {
        object? document = null;
        object? folder = null;
        object? self = null;
        try
        {
            long hwnd = ((dynamic)window).HWND;
            if (GetClassName(new IntPtr(hwnd)) != ExplorerClass)
            {
                // Internet Explorer windows share this list.
                return;
            }

            document = ((dynamic)window).Document;
            if (document is null)
            {
                return;
            }

            folder = ((dynamic)document).Folder;
            self = ((dynamic)folder!).Self;
            string path = ((dynamic)self!).Path;
            paths[hwnd] = path;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException
                                       or InvalidCastException)
        {
        }
        finally
        {
            Release(self);
            Release(folder);
            Release(document);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
