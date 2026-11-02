using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Launchy.Models;

namespace Launchy.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyIdBase = 0x4C41;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource _source;
    private readonly HashSet<HotkeyAction> _registered = [];

    public event EventHandler<HotkeyAction>? Pressed;

    public HotkeyService(Window owner)
    {
        var handle = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source.AddHook(WndProc);
    }

    /// <returns>False when another app already owns the combination.</returns>
    public bool Register(HotkeyAction action, Hotkey hotkey)
    {
        Unregister(action);

        // WPF's ModifierKeys values match the Win32 MOD_* flags.
        var mods = (uint)hotkey.Modifiers | ModNoRepeat;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
        if (!RegisterHotKey(_source.Handle, IdFor(action), mods, vk))
        {
            return false;
        }

        _registered.Add(action);
        return true;
    }

    public void Unregister(HotkeyAction action)
    {
        if (_registered.Remove(action))
        {
            UnregisterHotKey(_source.Handle, IdFor(action));
        }
    }

    public void UnregisterAll()
    {
        foreach (var action in _registered.ToList())
        {
            Unregister(action);
        }
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }

    private static int IdFor(HotkeyAction action) => HotkeyIdBase + (int)action;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            var action = (HotkeyAction)(wParam.ToInt32() - HotkeyIdBase);
            if (_registered.Contains(action))
            {
                Pressed?.Invoke(this, action);
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
