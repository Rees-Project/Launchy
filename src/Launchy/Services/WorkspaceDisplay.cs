using System.IO;
using Launchy.Models;

namespace Launchy.Services;

/// <summary>Text shown for workspaces in the launcher and the workspace editor. Pure, so xUnit can test it.</summary>
public static class WorkspaceDisplay
{
    public static string ItemCountText(int count) => $"Workspace: {count} {(count == 1 ? "item" : "items")}";

    /// <summary>Monitor, state and size of a saved placement, or "Not captured".</summary>
    public static string PlacementSummary(WindowPlacement? placement)
    {
        if (placement is null)
        {
            return "Not captured";
        }

        var monitor = placement.MonitorDevice.StartsWith(@"\\.\", StringComparison.Ordinal)
            ? placement.MonitorDevice[4..]
            : placement.MonitorDevice;
        var (width, height) = placement.State == WindowState.Normal
            ? (placement.Width, placement.Height)
            : (placement.NormalWidth, placement.NormalHeight);

        return placement.State switch
        {
            WindowState.Maximized => $"{monitor} · Maximized",
            WindowState.Minimized => $"{monitor} · Minimized",
            WindowState.Snapped => $"{monitor} · Snapped · {width}×{height}",
            _ => $"{monitor} · Normal · {width}×{height}"
        };
    }

    public static string ResultLine(int number, ItemResult result)
    {
        var name = Path.GetFileName(result.Item.Location.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name))
        {
            name = result.Item.Location;
        }

        var outcome = result.Outcome switch
        {
            ItemOutcome.Placed => "placed",
            ItemOutcome.LaunchedNotPlaced => "started, not placed",
            ItemOutcome.NotFound => "window not found",
            _ => "failed"
        };

        var message = string.IsNullOrEmpty(result.Message) ? "" : $" ({result.Message})";
        return $"{number}. {name}: {outcome}{message}";
    }
}
