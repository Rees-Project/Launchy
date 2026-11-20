using Launchy.Services.Windows;

namespace Launchy.Services;

/// <summary>A window a workspace run found for an item. Reused windows were open before the run and are never closed.</summary>
public sealed record OpenedWindow(IntPtr Handle, uint ProcessId, string ClassName, bool Reused)
{
    public static OpenedWindow From(WindowInfo window, bool reused) =>
        new(window.Handle, window.ProcessId, window.ClassName, reused);
}

/// <summary>Remembers, in memory only, the windows of the last run of each workspace.</summary>
public sealed class WorkspaceSessions
{
    private readonly Dictionary<string, IReadOnlyList<OpenedWindow>> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string workspaceName, IEnumerable<OpenedWindow> windows) =>
        _sessions[workspaceName] = windows.ToList();

    public IReadOnlyList<OpenedWindow>? Get(string workspaceName) =>
        _sessions.TryGetValue(workspaceName, out var windows) ? windows : null;
}

public interface IWindowCloser
{
    /// <summary>Posts a close request (WM_CLOSE). Returns false if the window did not accept the post.</summary>
    bool RequestClose(IntPtr handle);
}

public sealed class Win32WindowCloser : IWindowCloser
{
    public bool RequestClose(IntPtr handle) =>
        NativeMethods.PostMessage(handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
}

/// <param name="HadRun">False when the workspace was not run since Launchy started.</param>
/// <param name="Requested">Windows that got a close request.</param>
/// <param name="SkippedReused">Windows the run reused instead of launching.</param>
/// <param name="NotOpen">Windows that are gone, or whose handle now belongs to another window.</param>
public sealed record CloseResult(bool HadRun, int Requested, int SkippedReused, int NotOpen)
{
    public string Describe(string workspaceName)
    {
        if (!HadRun)
        {
            return $"\"{workspaceName}\" has not been run since Launchy started.";
        }

        var text = $"Asked {Requested} window(s) of \"{workspaceName}\" to close.";
        if (SkippedReused > 0)
        {
            text += $" Left {SkippedReused} already-open window(s) alone.";
        }

        if (NotOpen > 0)
        {
            text += $" {NotOpen} no longer open.";
        }

        return text;
    }
}

/// <summary>
/// Sends WM_CLOSE to the windows the last run of a workspace launched. A window gets the request only if it
/// still exists with the same handle, process id and class. Nothing is killed and nothing is forced.
/// </summary>
public sealed class WorkspaceCloser(WorkspaceSessions sessions, IWindowSource source, IWindowCloser closer)
{
    public CloseResult Close(string workspaceName)
    {
        if (sessions.Get(workspaceName) is not { } recorded)
        {
            return new CloseResult(false, 0, 0, 0);
        }

        var current = source.GetTopLevelWindows();
        int requested = 0, skippedReused = 0, notOpen = 0;

        foreach (var opened in recorded)
        {
            if (opened.Reused)
            {
                skippedReused++;
                continue;
            }

            var window = current.FirstOrDefault(w => w.Handle == opened.Handle);
            var stillSame = window is not null
                && window.ProcessId == opened.ProcessId
                && string.Equals(window.ClassName, opened.ClassName, StringComparison.Ordinal);

            if (stillSame && closer.RequestClose(opened.Handle))
            {
                requested++;
            }
            else
            {
                notOpen++;
            }
        }

        return new CloseResult(true, requested, skippedReused, notOpen);
    }
}

/// <summary>Parses the launcher text <c>:close &lt;workspace&gt;</c>.</summary>
public static class CloseCommand
{
    public const string Keyword = ":close";

    /// <summary>True when the text starts with the keyword as a whole word. Name is empty if none was typed.</summary>
    public static bool TryParse(string text, out string name)
    {
        name = "";
        text = text.Trim();
        if (!text.StartsWith(Keyword, StringComparison.OrdinalIgnoreCase)
            || (text.Length > Keyword.Length && !char.IsWhiteSpace(text[Keyword.Length])))
        {
            return false;
        }

        name = text[Keyword.Length..].Trim();
        return true;
    }
}
