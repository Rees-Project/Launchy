using System.IO;
using Launchy.Models;
using Launchy.Services.Windows;

namespace Launchy.Services;

/// <summary>A window the owner ticked in the picker, and the existing item it updates (null for a new item).</summary>
public sealed record CaptureSelection(WindowInfo Window, int? ItemIndex);

/// <summary>
/// Turns open windows into workspace items. Only <see cref="Refresh"/> reads the desktop;
/// the static methods are pure so they can be unit tested.
/// </summary>
public sealed class LayoutCapture(IWindowSource source, IEnumerable<string>? browserPaths = null)
{
    public const string ExplorerClass = "CabinetWClass";
    public const string StoreAppFrameClass = AppIdentity.StoreAppFrameClass;
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    public static readonly IReadOnlyList<string> KnownBrowserExeNames =
        ["chrome.exe", "msedge.exe", "brave.exe", "vivaldi.exe", "opera.exe", "firefox.exe"];

    private readonly IReadOnlyList<string> _browserPaths = browserPaths?.ToList() ?? [];

    public IReadOnlyList<WindowInfo> Windows { get; private set; } = [];
    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = [];

    /// <summary>Reads windows and monitors. Call on the UI (STA) thread.</summary>
    public void Refresh()
    {
        Windows = source.GetTopLevelWindows();
        Monitors = source.GetMonitors();
    }

    public bool IsBrowser(WindowInfo window) => IsBrowser(window.ProcessPath, _browserPaths);

    public WorkspaceItem? CreateItem(WindowInfo window) => CreateItem(window, Monitors, _browserPaths);

    public List<WorkspaceItem> Apply(IReadOnlyList<WorkspaceItem> existing, IEnumerable<CaptureSelection> selections) =>
        Apply(existing, selections, Monitors, _browserPaths);

    /// <summary>
    /// Null when the window can't become a reliable item: no process path (for example an elevated app),
    /// a Store app without an AUMID, or an Explorer window without a folder path.
    /// </summary>
    public static string? WhyNotCapturable(WindowInfo window)
    {
        if (window.ClassName == StoreAppFrameClass)
        {
            return string.IsNullOrEmpty(window.AppUserModelId)
                ? "Store app ID not available."
                : null;
        }

        if (string.IsNullOrEmpty(window.ProcessPath))
        {
            return "Program path not available (the app may run as administrator).";
        }

        if (window.ClassName == ExplorerClass && string.IsNullOrEmpty(window.FolderPath))
        {
            return "Folder path not available.";
        }

        return null;
    }

    /// <summary>A desktop (Win32) app installed from a package, for example the Claude desktop app.</summary>
    public static bool IsPackagedDesktopApp(WindowInfo window) =>
        window.ClassName != StoreAppFrameClass
        && !string.IsNullOrEmpty(window.AppUserModelId)
        && AppIdentity.IsPackagedPath(window.ProcessPath);

    public static bool IsBrowser(string processPath, IEnumerable<string> browserPaths)
    {
        if (string.IsNullOrEmpty(processPath))
        {
            return false;
        }

        var exeName = Path.GetFileName(processPath);
        return KnownBrowserExeNames.Contains(exeName, StringComparer.OrdinalIgnoreCase)
               || browserPaths.Any(p => string.Equals(p, processPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A new item for the window, or null when <see cref="WhyNotCapturable"/> gives a reason.</summary>
    public static WorkspaceItem? CreateItem(WindowInfo window, IReadOnlyList<MonitorInfo> monitors,
                                            IEnumerable<string> browserPaths)
    {
        if (WhyNotCapturable(window) is not null)
        {
            return null;
        }

        var (type, location) = window.ClassName switch
        {
            StoreAppFrameClass => (TargetType.Application, AppsFolderPrefix + window.AppUserModelId),
            ExplorerClass => (TargetType.Folder, window.FolderPath!),
            _ when IsBrowser(window.ProcessPath, browserPaths) => (TargetType.Website, ""),
            _ when IsPackagedDesktopApp(window) => (TargetType.Application, AppsFolderPrefix + window.AppUserModelId),
            _ => (TargetType.Application, window.ProcessPath)
        };

        return new WorkspaceItem
        {
            Type = type,
            Location = location,
            Match = CreateMatch(window),
            Placement = PlacementCalculator.Capture(window, monitors)
        };
    }

    public static WindowMatch CreateMatch(WindowInfo window)
    {
        if (window.ClassName == StoreAppFrameClass)
        {
            return new WindowMatch { AppUserModelId = window.AppUserModelId };
        }

        if (IsPackagedDesktopApp(window))
        {
            // The install folder name holds the version, so the process path breaks on every app update.
            return new WindowMatch { AppUserModelId = window.AppUserModelId, ClassName = window.ClassName };
        }

        return new WindowMatch
        {
            ProcessPath = window.ProcessPath,
            ClassName = window.ClassName,
            FolderPath = window.ClassName == ExplorerClass ? window.FolderPath : null
        };
    }

    /// <summary>
    /// For each item, the windows that match its signature. Items without a signature match nothing.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<WindowInfo>> FindCandidates(IReadOnlyList<WorkspaceItem> items,
                                                                         IReadOnlyList<WindowInfo> windows) =>
        items.Select(item => item.Match is null
                ? (IReadOnlyList<WindowInfo>)[]
                : windows.Where(w => WhyNotCapturable(w) is null && WindowMatcher.Matches(item.Match, w)).ToList())
            .ToList();

    /// <summary>
    /// Pairs each item with its first candidate that no earlier item took. Null where no window is left.
    /// </summary>
    public static IReadOnlyList<WindowInfo?> AssignDefaults(IReadOnlyList<IReadOnlyList<WindowInfo>> candidates)
    {
        var claimed = new HashSet<IntPtr>();
        var result = new List<WindowInfo?>(candidates.Count);
        foreach (var itemCandidates in candidates)
        {
            var pick = itemCandidates.FirstOrDefault(w => !claimed.Contains(w.Handle));
            if (pick is not null)
            {
                claimed.Add(pick.Handle);
            }

            result.Add(pick);
        }

        return result;
    }

    /// <summary>
    /// Builds the new item list. Existing items keep their order and every field except
    /// <c>Match</c> and <c>Placement</c>; only items named by a selection change.
    /// The exception is <c>Location</c>: a path under a versioned <c>\WindowsApps\</c> folder becomes the
    /// <c>shell:AppsFolder</c> link when the window is a packaged desktop app. Items are never removed.
    /// Selections without an item index are added at the end as new items.
    /// </summary>
    public static List<WorkspaceItem> Apply(IReadOnlyList<WorkspaceItem> existing,
                                            IEnumerable<CaptureSelection> selections,
                                            IReadOnlyList<MonitorInfo> monitors,
                                            IEnumerable<string> browserPaths)
    {
        var browsers = browserPaths.ToList();
        var result = existing.Select(Copy).ToList();
        var updated = new HashSet<int>();
        var added = new List<WorkspaceItem>();

        foreach (var selection in selections)
        {
            if (selection.ItemIndex is { } index)
            {
                if (index < 0 || index >= result.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(selections), $"Item index {index} is out of range.");
                }

                if (!updated.Add(index))
                {
                    throw new ArgumentException($"Item {index + 1} has more than one window selected.", nameof(selections));
                }

                if (WhyNotCapturable(selection.Window) is not null)
                {
                    continue;
                }

                var item = result[index];
                var match = CreateMatch(selection.Window);
                match.TitleContains = item.Match?.TitleContains;
                item.Match = match;
                item.Placement = PlacementCalculator.Capture(selection.Window, monitors);
                if (IsPackagedDesktopApp(selection.Window) && AppIdentity.IsPackagedPath(item.Location))
                {
                    item.Location = AppsFolderPrefix + selection.Window.AppUserModelId;
                }
            }
            else if (CreateItem(selection.Window, monitors, browsers) is { } newItem)
            {
                added.Add(newItem);
            }
        }

        result.AddRange(added);
        return result;
    }

    private static WorkspaceItem Copy(WorkspaceItem item) => new()
    {
        Type = item.Type,
        Location = item.Location,
        Parameters = item.Parameters,
        Match = item.Match is null
            ? null
            : new WindowMatch
            {
                ProcessPath = item.Match.ProcessPath,
                ClassName = item.Match.ClassName,
                AppUserModelId = item.Match.AppUserModelId,
                FolderPath = item.Match.FolderPath,
                TitleContains = item.Match.TitleContains
            },
        Placement = item.Placement,
        TimeoutSeconds = item.TimeoutSeconds,
        Focus = item.Focus,
        IfRunning = item.IfRunning
    };
}
