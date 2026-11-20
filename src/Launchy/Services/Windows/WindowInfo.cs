namespace Launchy.Services.Windows;

public sealed record IntRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public override string ToString() => $"({Left},{Top}) {Width}x{Height}";
}

/// <summary>
/// A snapshot of one top-level window. <see cref="Rect"/> is in screen coordinates;
/// <see cref="NormalRect"/> is the restore rectangle from GetWindowPlacement, in workspace coordinates.
/// </summary>
public sealed record WindowInfo(
    IntPtr Handle,
    uint ProcessId,
    string ProcessPath,
    string ClassName,
    string Title,
    string? AppUserModelId,
    string? FolderPath,
    IntRect Rect,
    IntRect NormalRect,
    int ShowCmd,
    bool IsMaximized,
    bool IsMinimized,
    bool IsArranged,
    bool IsResizable,
    string MonitorDevice);
