using static Launchy.Services.Windows.NativeMethods;

namespace Launchy.Services.Windows;

/// <summary>Decides which top-level windows count as user app windows. No Win32 calls, so it can be unit tested.</summary>
public static class WindowFilter
{
    public static bool IsAppWindow(
        bool isVisible, bool isCloaked, long exStyle, bool hasOwner, string title, uint processId, uint ownProcessId)
    {
        if (!isVisible || isCloaked)
        {
            return false;
        }

        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        if (hasOwner && (exStyle & WS_EX_APPWINDOW) == 0)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(title) && processId != ownProcessId;
    }
}
