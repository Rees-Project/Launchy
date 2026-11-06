using System.Runtime.InteropServices;

namespace Launchy.Services;

public static class ForegroundHelper
{
    /// <summary>
    /// Brings <paramref name="hwnd"/> to the foreground even when Windows' foreground lock would refuse,
    /// by briefly sharing input state with the thread that currently owns the foreground.
    /// </summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var currentThread = GetCurrentThreadId();

        if (foregroundThread == currentThread || foregroundThread == 0)
        {
            SetForegroundWindow(hwnd);
            return;
        }

        AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            SetForegroundWindow(hwnd);
        }
        finally
        {
            AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    public static bool IsForeground(IntPtr hwnd) => GetForegroundWindow() == hwnd;

    /// <summary>True while Alt, Ctrl, Shift or a Windows key is physically held down.</summary>
    public static bool AreModifierKeysDown() =>
        ModifierVirtualKeys.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    private static readonly int[] ModifierVirtualKeys = [0x10, 0x11, 0x12, 0x5B, 0x5C];

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
