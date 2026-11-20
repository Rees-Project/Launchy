using System.IO;
using Launchy.Models;

namespace Launchy.Services.Windows;

public static class WindowMatcher
{
    /// <summary>
    /// True when the window satisfies every field set on the signature.
    /// A signature with no fields set matches nothing.
    /// </summary>
    public static bool Matches(WindowMatch signature, WindowInfo window)
    {
        var hasCriteria = false;

        if (!string.IsNullOrWhiteSpace(signature.ProcessPath))
        {
            hasCriteria = true;
            if (!PathsEqual(signature.ProcessPath, window.ProcessPath)) return false;
        }

        if (!string.IsNullOrEmpty(signature.ClassName))
        {
            hasCriteria = true;
            if (!string.Equals(signature.ClassName, window.ClassName, StringComparison.Ordinal)) return false;
        }

        if (!string.IsNullOrEmpty(signature.AppUserModelId))
        {
            hasCriteria = true;
            if (!string.Equals(signature.AppUserModelId, window.AppUserModelId, StringComparison.Ordinal)) return false;
        }

        if (!string.IsNullOrWhiteSpace(signature.FolderPath))
        {
            hasCriteria = true;
            if (window.FolderPath is null || !FolderPathsEqual(signature.FolderPath, window.FolderPath)) return false;
        }

        if (!string.IsNullOrEmpty(signature.TitleContains))
        {
            hasCriteria = true;
            if (!window.Title.Contains(signature.TitleContains, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return hasCriteria;
    }

    /// <summary>
    /// Finds the window opened by a launch: not in <paramref name="before"/> (compared by handle),
    /// not claimed, and matching the signature. Prefers <paramref name="launchedPid"/>, then list order.
    /// </summary>
    public static WindowInfo? FindNew(WindowMatch signature, IReadOnlyList<WindowInfo> before,
                                      IReadOnlyList<WindowInfo> after, int? launchedPid,
                                      ISet<IntPtr> alreadyClaimed)
    {
        var knownHandles = new HashSet<IntPtr>(before.Select(w => w.Handle));
        var requireResizable = string.IsNullOrEmpty(signature.ClassName);

        WindowInfo? first = null;
        foreach (var window in after)
        {
            if (knownHandles.Contains(window.Handle) || alreadyClaimed.Contains(window.Handle)) continue;
            if (requireResizable && !window.IsResizable) continue;
            if (!Matches(signature, window)) continue;

            if (launchedPid.HasValue && window.ProcessId == (uint)launchedPid.Value) return window;
            first ??= window;
        }

        return first;
    }

    /// <summary>
    /// Finds an already open window for the signature. Prefers the foreground window, then list order.
    /// </summary>
    public static WindowInfo? FindExisting(WindowMatch signature, IReadOnlyList<WindowInfo> windows,
                                           IntPtr foreground, ISet<IntPtr> alreadyClaimed)
    {
        WindowInfo? first = null;
        foreach (var window in windows)
        {
            if (alreadyClaimed.Contains(window.Handle)) continue;
            if (!Matches(signature, window)) continue;

            if (window.Handle == foreground) return window;
            first ??= window;
        }

        return first;
    }

    private static bool PathsEqual(string signaturePath, string windowPath) =>
        string.Equals(Environment.ExpandEnvironmentVariables(signaturePath), windowPath,
                      StringComparison.OrdinalIgnoreCase);

    private static bool FolderPathsEqual(string signaturePath, string windowPath) =>
        string.Equals(NormalizeFolder(signaturePath), NormalizeFolder(windowPath),
                      StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFolder(string path)
    {
        if (path.StartsWith("::", StringComparison.Ordinal)) return path;

        var expanded = Environment.ExpandEnvironmentVariables(path);
        try
        {
            expanded = Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        var root = Path.GetPathRoot(expanded);
        return expanded == root ? expanded : expanded.TrimEnd('\\', '/');
    }
}
