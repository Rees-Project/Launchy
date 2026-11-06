using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Launchy.Services;

/// <summary>
/// The notification-area icon. It is registered with a fixed ID (a GUID), so when Launchy starts
/// after being killed or crashing, the leftover icon from the old process is replaced instead of a
/// second "L" appearing next to it. WinForms' NotifyIcon can't use a GUID, so this talks to
/// Shell_NotifyIcon directly.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;

    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const uint NifMessage = 0x01, NifIcon = 0x02, NifTip = 0x04, NifGuid = 0x20;
    private const uint FallbackIconId = 1;

    private readonly CommandStore _store;
    private readonly HwndSource _window;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly System.Drawing.Icon _image;
    private readonly Guid _id = IconIdForThisExe();
    private readonly uint _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private bool _usesGuid = true;
    private string _tooltip = "Launchy";

    public event EventHandler? OpenRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? KeybindsRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon(CommandStore store)
    {
        _store = store;

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("Open Launchy", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add("Edit commands…", null, (_, _) => EditRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add("Open commands file (JSON)", null, (_, _) => OpenCommandsFile());
        _menu.Items.Add("Keybinds…", null, (_, _) => KeybindsRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _image = CreateLogoIcon();

        // A hidden top-level window (not message-only), so it also receives the TaskbarCreated broadcast.
        const int wsExToolWindow = 0x80;
        _window = new HwndSource(new HwndSourceParameters("Launchy tray")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = wsExToolWindow,
            Width = 0,
            Height = 0,
        });
        _window.AddHook(WndProc);

        AddIcon();
    }

    public void SetShortcutText(string shortcut)
    {
        _tooltip = $"Launchy ({shortcut})";
        var data = NewData(NifTip);
        Shell_NotifyIcon(NimModify, ref data);
    }

    public void Dispose()
    {
        var data = NewData(0);
        Shell_NotifyIcon(NimDelete, ref data);
        _window.RemoveHook(WndProc);
        _window.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }

    private void AddIcon()
    {
        _usesGuid = true;

        // An icon with this ID may still be registered by a Launchy that was killed or crashed.
        // Deleting it first lets the add below take its place.
        var stale = NewData(0);
        Shell_NotifyIcon(NimDelete, ref stale);

        var data = NewData(NifMessage | NifIcon | NifTip);
        if (Shell_NotifyIcon(NimAdd, ref data))
        {
            return;
        }

        // Windows refuses a GUID it has already tied to a different exe path. An icon without the
        // GUID can't replace leftovers, but it's better than no icon at all.
        _usesGuid = false;
        data = NewData(NifMessage | NifIcon | NifTip);
        Shell_NotifyIcon(NimAdd, ref data);
    }

    private NotifyIconData NewData(uint flags)
    {
        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _window.Handle,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _image.Handle,
            szTip = _tooltip,
            szInfo = "",
            szInfoTitle = "",
        };

        if (_usesGuid)
        {
            data.uFlags |= NifGuid;
            data.guidItem = _id;
        }
        else
        {
            data.uID = FallbackIconId;
        }

        return data;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case WmLButtonUp:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case WmRButtonUp:
                    ShowMenu();
                    break;
            }

            handled = true;
        }
        else if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            // Explorer restarted and forgot every tray icon.
            AddIcon();
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        // Without the foreground, the menu wouldn't close when the user clicks somewhere else.
        SetForegroundWindow(_window.Handle);
        _menu.Show(Forms.Cursor.Position, Forms.ToolStripDropDownDirection.AboveLeft);
    }

    private void OpenCommandsFile()
    {
        if (!File.Exists(_store.CommandsPath))
        {
            _store.Load();
        }

        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_store.CommandsPath}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Windows ties a tray icon GUID to one exe path, so each install location (alpha, release, a dev
    /// build) gets its own ID, derived from its path. The same install always gets the same ID.
    /// </summary>
    private static Guid IconIdForThisExe()
    {
        var path = (Environment.ProcessPath ?? "Launchy").ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("Launchy.TrayIcon|" + path));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Renders the logo's arrow "L" into a 32 px icon, so the tray always matches the in-app logo.
    /// </summary>
    private static System.Drawing.Icon CreateLogoIcon()
    {
        const int size = 32;
        var geometry = (Geometry)Application.Current.FindResource("LogoArrowGeometry");
        var brush = (Brush)Application.Current.FindResource("BrandGradient");

        var bounds = geometry.Bounds;
        var scale = (size - 2) / Math.Max(bounds.Width, bounds.Height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform((size - bounds.Width * scale) / 2, (size - bounds.Height * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(brush, new Pen(brush, 1) { LineJoin = PenLineJoin.Round }, geometry);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);

        // Wrap the PNG in a single-image .ico container.
        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write((int)png.Length);
            writer.Write(22);
            writer.Write(png.ToArray());
        }

        ico.Position = 0;
        return new System.Drawing.Icon(ico);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
