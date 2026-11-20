using static Launchy.Services.Windows.NativeMethods;

namespace Launchy.Services.Windows;

/// <summary>
/// SetWinEventHook for EVENT_OBJECT_SHOW and EVENT_OBJECT_UNCLOAKED. The hooks are out of context, so
/// callbacks arrive through the message loop of the thread that subscribed: subscribe and dispose on the
/// UI thread. Events from Launchy itself and from child objects (carets, controls, menus) are ignored.
/// </summary>
public sealed class Win32WindowShowEvents : IWindowShowEvents
{
    public IDisposable? Subscribe(Action onShown)
    {
        var hook = new Hook(onShown);
        return hook.IsInstalled ? hook : null;
    }

    private sealed class Hook : IDisposable
    {
        private const uint Flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;

        private readonly Action _onShown;

        // Held in a field so the GC keeps the delegate alive while Windows can still call it.
        private readonly WinEventProc _callback;
        private IntPtr _showHook;
        private IntPtr _uncloakHook;

        public Hook(Action onShown)
        {
            _onShown = onShown;
            _callback = OnEvent;
            _showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, Flags);
            _uncloakHook = SetWinEventHook(EVENT_OBJECT_UNCLOAKED, EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, _callback,
                                           0, 0, Flags);
        }

        public bool IsInstalled => _showHook != IntPtr.Zero || _uncloakHook != IntPtr.Zero;

        public void Dispose()
        {
            Unhook(ref _showHook);
            Unhook(ref _uncloakHook);
        }

        private static void Unhook(ref IntPtr hook)
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWinEvent(hook);
                hook = IntPtr.Zero;
            }
        }

        private void OnEvent(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild,
                             uint eventThread, uint eventTime)
        {
            if (idObject == OBJID_WINDOW && idChild == CHILDID_SELF && hWnd != IntPtr.Zero
                && GetAncestor(hWnd, GA_ROOT) == hWnd)
            {
                _onShown();
            }
        }
    }
}
