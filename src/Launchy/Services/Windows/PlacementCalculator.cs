using Launchy.Models;
using WindowState = Launchy.Models.WindowState;

namespace Launchy.Services.Windows;

/// <summary>The values to apply to one window.</summary>
/// <param name="State">The state to restore.</param>
/// <param name="Rect">Screen coordinates, for SetWindowPos.</param>
/// <param name="NormalRect">Workspace coordinates (relative to the primary monitor's work area), for SetWindowPlacement.</param>
public sealed record PlacementTarget(WindowState State, IntRect Rect, IntRect NormalRect);

/// <summary>
/// Converts live windows to saved placements and saved placements to target rectangles. No Win32 calls.
/// Screen coordinates (GetWindowRect) and workspace coordinates (rcNormalPosition) are never mixed:
/// workspace = screen - primary work area origin.
/// </summary>
public static class PlacementCalculator
{
    public const int TitleBarHeight = 40;
    public const int MinVisibleWidth = 100;
    public const int MinScaledWidth = 200;
    public const int MinScaledHeight = 100;

    // Maximized and snapped windows overhang the work area by their invisible frame (about 7-8 px).
    public const int FrameSlack = 8;

    public static WindowState DetectState(WindowInfo window, IReadOnlyList<MonitorInfo> monitors)
    {
        if (window.IsMinimized)
        {
            return WindowState.Minimized;
        }

        if (window.IsMaximized)
        {
            return WindowState.Maximized;
        }

        var normalOnScreen = ToScreen(window.NormalRect, Primary(monitors));
        if (window.IsArranged || window.Rect != normalOnScreen)
        {
            return WindowState.Snapped;
        }

        return WindowState.Normal;
    }

    public static WindowPlacement Capture(WindowInfo window, IReadOnlyList<MonitorInfo> monitors)
    {
        var primary = Primary(monitors);
        var state = DetectState(window, monitors);

        // A minimized window's GetWindowRect is (-32000,-32000); use its restore rectangle instead.
        var rect = state == WindowState.Minimized ? ToScreen(window.NormalRect, primary) : window.Rect;

        var monitor = state == WindowState.Minimized
            ? MonitorWithLargestOverlap(rect, monitors) ?? primary
            : monitors.FirstOrDefault(m => m.Device == window.MonitorDevice)
              ?? MonitorWithLargestOverlap(rect, monitors)
              ?? primary;

        return new WindowPlacement
        {
            State = state,
            X = rect.Left,
            Y = rect.Top,
            Width = rect.Width,
            Height = rect.Height,
            NormalX = window.NormalRect.Left,
            NormalY = window.NormalRect.Top,
            NormalWidth = window.NormalRect.Width,
            NormalHeight = window.NormalRect.Height,
            MonitorDevice = monitor.Device,
            MonitorId = monitor.Id.Length == 0 ? null : monitor.Id,
            MonX = monitor.Bounds.Left,
            MonY = monitor.Bounds.Top,
            MonWidth = monitor.Bounds.Width,
            MonHeight = monitor.Bounds.Height,
            WorkX = monitor.WorkArea.Left,
            WorkY = monitor.WorkArea.Top,
            WorkWidth = monitor.WorkArea.Width,
            WorkHeight = monitor.WorkArea.Height,
            Dpi = monitor.Dpi
        };
    }

    public static PlacementTarget ComputeTarget(WindowPlacement saved, IReadOnlyList<MonitorInfo> monitors)
    {
        var primary = Primary(monitors);
        var rect = new IntRect(saved.X, saved.Y, saved.X + saved.Width, saved.Y + saved.Height);
        var normalOnScreen = ToScreen(
            new IntRect(saved.NormalX, saved.NormalY, saved.NormalX + saved.NormalWidth, saved.NormalY + saved.NormalHeight),
            primary);

        var savedWorkArea = new IntRect(saved.WorkX, saved.WorkY, saved.WorkX + saved.WorkWidth, saved.WorkY + saved.WorkHeight);
        var savedMonitor = FindSavedMonitor(saved, monitors);

        if (savedMonitor is null)
        {
            var dx = primary.WorkArea.Left - saved.WorkX;
            var dy = primary.WorkArea.Top - saved.WorkY;
            rect = FitInside(Offset(rect, dx, dy), primary.WorkArea);
            normalOnScreen = FitInside(Offset(normalOnScreen, dx, dy), primary.WorkArea);
        }
        else if (!IsSameMonitor(saved, savedMonitor))
        {
            var target = savedMonitor.WorkArea;
            if (savedWorkArea.Width > 0 && savedWorkArea.Height > 0)
            {
                normalOnScreen = Scale(normalOnScreen, savedWorkArea, target);
                rect = saved.State == WindowState.Normal
                    ? Scale(rect, savedWorkArea, target)
                    : FitInside(Offset(rect, target.Left - savedWorkArea.Left, target.Top - savedWorkArea.Top), target);
            }
            else
            {
                rect = FitInside(rect, target);
                normalOnScreen = FitInside(normalOnScreen, target);
            }
        }

        var workAreas = monitors.Select(m => m.WorkArea).ToList();
        return new PlacementTarget(
            saved.State,
            Clamp(rect, workAreas),
            ToWorkspace(Clamp(normalOnScreen, workAreas), primary));
    }

    /// <summary>Moves the rectangle (same size) so its title bar can be reached in some work area.</summary>
    public static IntRect Clamp(IntRect rect, IReadOnlyList<IntRect> workAreas)
    {
        if (workAreas.Count == 0 || workAreas.Any(wa => IsReachable(rect, wa)))
        {
            return rect;
        }

        return workAreas
            .Select(wa => MoveIntoReach(rect, wa))
            .MinBy(moved => Distance(rect, moved))!;
    }

    private static bool IsReachable(IntRect rect, IntRect workArea)
    {
        var minWidth = Math.Min(MinVisibleWidth, rect.Width);
        var minHeight = Math.Min(TitleBarHeight, rect.Height);
        var overlap = Math.Min(rect.Right, workArea.Right) - Math.Max(rect.Left, workArea.Left);

        return overlap >= minWidth
            && rect.Top >= workArea.Top - FrameSlack
            && rect.Top + minHeight <= workArea.Bottom;
    }

    private static IntRect MoveIntoReach(IntRect rect, IntRect workArea)
    {
        var minWidth = Math.Min(MinVisibleWidth, rect.Width);
        var minHeight = Math.Min(TitleBarHeight, rect.Height);
        var left = Math.Clamp(rect.Left, workArea.Left - rect.Width + minWidth, workArea.Right - minWidth);
        var top = Math.Clamp(rect.Top, workArea.Top, workArea.Bottom - minHeight);
        return Offset(rect, left - rect.Left, top - rect.Top);
    }

    /// <summary>Shifts the rectangle (same size) to lie inside the work area. A larger rectangle aligns to the top-left.</summary>
    private static IntRect FitInside(IntRect rect, IntRect workArea)
    {
        var left = Math.Max(Math.Min(rect.Left, workArea.Right - rect.Width), workArea.Left);
        var top = Math.Max(Math.Min(rect.Top, workArea.Bottom - rect.Height), workArea.Top);
        return Offset(rect, left - rect.Left, top - rect.Top);
    }

    /// <summary>
    /// The monitor the placement was saved on: by stable id first, then by device name.
    /// Null when neither matches; the caller then falls back to the primary monitor.
    /// </summary>
    public static MonitorInfo? FindSavedMonitor(WindowPlacement saved, IReadOnlyList<MonitorInfo> monitors)
    {
        if (!string.IsNullOrEmpty(saved.MonitorId)
            && monitors.FirstOrDefault(m => string.Equals(m.Id, saved.MonitorId, StringComparison.OrdinalIgnoreCase)) is { } byId)
        {
            return byId;
        }

        return monitors.FirstOrDefault(m => m.Device == saved.MonitorDevice);
    }

    /// <summary>The monitor is already matched by id or name, so only its bounds decide.</summary>
    private static bool IsSameMonitor(WindowPlacement saved, MonitorInfo monitor) =>
        monitor.Bounds == new IntRect(saved.MonX, saved.MonY, saved.MonX + saved.MonWidth, saved.MonY + saved.MonHeight);

    /// <summary>Maps position and size per axis from one work area to another. The size stays within [MinScaledWidth x MinScaledHeight, target].</summary>
    private static IntRect Scale(IntRect rect, IntRect from, IntRect to)
    {
        var factorX = (double)to.Width / from.Width;
        var factorY = (double)to.Height / from.Height;

        var width = ClampSize((int)Math.Round(rect.Width * factorX), MinScaledWidth, to.Width);
        var height = ClampSize((int)Math.Round(rect.Height * factorY), MinScaledHeight, to.Height);
        var left = to.Left + (int)Math.Round((rect.Left - from.Left) * factorX);
        var top = to.Top + (int)Math.Round((rect.Top - from.Top) * factorY);

        return new IntRect(left, top, left + width, top + height);
    }

    private static int ClampSize(int size, int min, int max) => Math.Min(Math.Max(size, min), max);

    private static MonitorInfo? MonitorWithLargestOverlap(IntRect rect, IReadOnlyList<MonitorInfo> monitors) =>
        monitors
            .Select(m => (Monitor: m, Area: OverlapArea(rect, m.Bounds)))
            .Where(x => x.Area > 0)
            .OrderByDescending(x => x.Area)
            .Select(x => x.Monitor)
            .FirstOrDefault();

    private static long OverlapArea(IntRect a, IntRect b)
    {
        long width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        long height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return width > 0 && height > 0 ? width * height : 0;
    }

    private static MonitorInfo Primary(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    private static IntRect ToScreen(IntRect workspaceRect, MonitorInfo primary) =>
        Offset(workspaceRect, primary.WorkArea.Left, primary.WorkArea.Top);

    private static IntRect ToWorkspace(IntRect screenRect, MonitorInfo primary) =>
        Offset(screenRect, -primary.WorkArea.Left, -primary.WorkArea.Top);

    private static IntRect Offset(IntRect rect, int dx, int dy) =>
        new(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy);

    private static long Distance(IntRect a, IntRect b)
    {
        long dx = a.Left - b.Left;
        long dy = a.Top - b.Top;
        return dx * dx + dy * dy;
    }
}
