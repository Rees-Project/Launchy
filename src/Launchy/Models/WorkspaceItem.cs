namespace Launchy.Models;

public sealed class WorkspaceItem
{
    public TargetType Type { get; set; }
    public string Location { get; set; } = "";
    public string? Parameters { get; set; }
    public WindowMatch? Match { get; set; }
    public WindowPlacement? Placement { get; set; }
    public int? TimeoutSeconds { get; set; }

    /// <summary>When true, the runner brings this item's window to the front after the whole run. The last such item wins.</summary>
    public bool Focus { get; set; }

    /// <summary>What the runner does when a matching window is already open. Missing in old files, so it defaults to LaunchNew.</summary>
    public IfRunning IfRunning { get; set; } = IfRunning.LaunchNew;

    /// <summary>All URLs of a Website item when it has more than one; <see cref="Location"/> is the first. Missing in old files.</summary>
    public List<string>? Urls { get; set; }

    public const string UrlSeparator = " ; ";

    /// <summary>The URLs to open: <see cref="Urls"/> when set, else just <see cref="Location"/>.</summary>
    public IReadOnlyList<string> AllUrls() =>
        Urls is { Count: > 0 } ? Urls : (Location.Length == 0 ? [] : [Location]);

    /// <summary>Text for the editor cell: the URLs joined with " ; ".</summary>
    public string LocationText() => Urls is { Count: > 1 } ? string.Join(UrlSeparator, Urls) : Location;

    /// <summary>Sets Location and Urls from the editor cell text. Several ';' separated entries become Urls.</summary>
    public void SetLocationText(string? text)
    {
        var value = text ?? "";
        if (Type != TargetType.Website || !value.Contains(';'))
        {
            Location = value;
            Urls = null;
            return;
        }

        var urls = value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        Location = urls.Count > 0 ? urls[0] : "";
        Urls = urls.Count > 1 ? urls : null;
    }
}

public enum IfRunning
{
    LaunchNew,
    Reuse
}

public sealed class WindowMatch
{
    public string? ProcessPath { get; set; }
    public string? ClassName { get; set; }
    public string? AppUserModelId { get; set; }
    public string? FolderPath { get; set; }
    public string? TitleContains { get; set; }
}

public enum WindowState
{
    Normal,
    Maximized,
    Minimized,
    Snapped
}

public sealed class WindowPlacement
{
    public WindowState State { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int NormalX { get; set; }
    public int NormalY { get; set; }
    public int NormalWidth { get; set; }
    public int NormalHeight { get; set; }
    public string MonitorDevice { get; set; } = "";

    /// <summary>Monitor device interface path. Missing in old files; then only MonitorDevice is used.</summary>
    public string? MonitorId { get; set; }
    public int MonX { get; set; }
    public int MonY { get; set; }
    public int MonWidth { get; set; }
    public int MonHeight { get; set; }
    public int WorkX { get; set; }
    public int WorkY { get; set; }
    public int WorkWidth { get; set; }
    public int WorkHeight { get; set; }
    public int Dpi { get; set; }
}
