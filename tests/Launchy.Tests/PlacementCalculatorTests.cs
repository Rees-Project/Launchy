using Launchy.Models;
using Launchy.Services.Windows;
using Xunit;
using WindowState = Launchy.Models.WindowState;

namespace Launchy.Tests;

public class PlacementCalculatorTests
{
    private const string Display1 = @"\\.\DISPLAY1";
    private const string Display2 = @"\\.\DISPLAY2";

    // Owner layout: DISPLAY1 2560x1440 primary with a 40 px taskbar at the bottom; DISPLAY2 portrait at (2560,-330).
    private static readonly MonitorInfo Monitor1 =
        new(Display1, new IntRect(0, 0, 2560, 1440), new IntRect(0, 0, 2560, 1400), 96, true);

    private static readonly MonitorInfo Monitor2 =
        new(Display2, new IntRect(2560, -330, 3640, 1590), new IntRect(2560, -330, 3640, 1550), 96, false);

    private static readonly MonitorInfo[] OwnerMonitors = [Monitor1, Monitor2];

    private static readonly IntRect NotepadPlusPlusRect = new(1272, -8, 2568, 1408);
    private static readonly IntRect NotepadPlusPlusNormal = new(211, 393, 1209, 1407);

    private static WindowInfo Window(
        IntRect rect, IntRect? normal = null, bool maximized = false, bool minimized = false,
        bool arranged = false, string device = Display1) =>
        new(IntPtr.Zero, 1, @"C:\Windows\notepad.exe", "Notepad", "Untitled - Notepad", null, null,
            rect, normal ?? rect, minimized ? 2 : maximized ? 3 : 1, maximized, minimized, arranged, true, device);

    private static PlacementTarget RoundTrip(WindowInfo window, IReadOnlyList<MonitorInfo> monitors) =>
        PlacementCalculator.ComputeTarget(PlacementCalculator.Capture(window, monitors), monitors);

    private static bool Contains(IntRect outer, IntRect inner) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static bool TitleBarReachable(IntRect rect, IEnumerable<MonitorInfo> monitors) =>
        monitors.Any(m =>
            Math.Min(rect.Right, m.WorkArea.Right) - Math.Max(rect.Left, m.WorkArea.Left) >= 100
            && rect.Top >= m.WorkArea.Top
            && rect.Top + 40 <= m.WorkArea.Bottom);

    [Fact]
    public void DetectState_Maximized()
    {
        var window = Window(new IntRect(-8, -8, 2568, 1408), new IntRect(100, 100, 900, 700), maximized: true);
        Assert.Equal(WindowState.Maximized, PlacementCalculator.DetectState(window, OwnerMonitors));
    }

    [Fact]
    public void DetectState_Minimized()
    {
        var window = Window(new IntRect(-32000, -32000, -31840, -31972), new IntRect(100, 100, 900, 700), minimized: true);
        Assert.Equal(WindowState.Minimized, PlacementCalculator.DetectState(window, OwnerMonitors));
    }

    [Fact]
    public void DetectState_Normal()
    {
        var window = Window(new IntRect(100, 100, 900, 700));
        Assert.Equal(WindowState.Normal, PlacementCalculator.DetectState(window, OwnerMonitors));
    }

    [Fact]
    public void DetectState_NotepadPlusPlusSnappedRight_IsSnapped()
    {
        var window = Window(NotepadPlusPlusRect, NotepadPlusPlusNormal);
        Assert.Equal(1, window.ShowCmd);
        Assert.Equal(WindowState.Snapped, PlacementCalculator.DetectState(window, OwnerMonitors));
    }

    [Fact]
    public void DetectState_ArrangedWithSameRects_IsSnapped()
    {
        var window = Window(new IntRect(100, 100, 900, 700), arranged: true);
        Assert.Equal(WindowState.Snapped, PlacementCalculator.DetectState(window, OwnerMonitors));
    }

    [Fact]
    public void DetectState_TaskbarOnLeft_TranslatesNormalRectToScreen()
    {
        var primary = new MonitorInfo(Display1, new IntRect(0, 0, 2560, 1440), new IntRect(48, 0, 2560, 1440), 96, true);
        var window = Window(new IntRect(148, 100, 948, 700), new IntRect(100, 100, 900, 700));

        Assert.Equal(WindowState.Normal, PlacementCalculator.DetectState(window, [primary]));
    }

    [Fact]
    public void TaskbarOnLeft_NormalRectStaysInWorkspaceCoordinates()
    {
        var primary = new MonitorInfo(Display1, new IntRect(0, 0, 2560, 1440), new IntRect(48, 0, 2560, 1440), 96, true);
        var window = Window(new IntRect(148, 100, 948, 700), new IntRect(100, 100, 900, 700));

        var target = RoundTrip(window, [primary]);

        Assert.Equal(new IntRect(148, 100, 948, 700), target.Rect);
        Assert.Equal(new IntRect(100, 100, 900, 700), target.NormalRect);
    }

    [Fact]
    public void Capture_FillsMonitorAndNormalValues()
    {
        var window = Window(NotepadPlusPlusRect, NotepadPlusPlusNormal);

        var saved = PlacementCalculator.Capture(window, OwnerMonitors);

        Assert.Equal(WindowState.Snapped, saved.State);
        Assert.Equal((1272, -8, 1296, 1416), (saved.X, saved.Y, saved.Width, saved.Height));
        Assert.Equal((211, 393, 998, 1014), (saved.NormalX, saved.NormalY, saved.NormalWidth, saved.NormalHeight));
        Assert.Equal(Display1, saved.MonitorDevice);
        Assert.Equal((0, 0, 2560, 1440), (saved.MonX, saved.MonY, saved.MonWidth, saved.MonHeight));
        Assert.Equal((0, 0, 2560, 1400), (saved.WorkX, saved.WorkY, saved.WorkWidth, saved.WorkHeight));
        Assert.Equal(96, saved.Dpi);
    }

    [Fact]
    public void Capture_Minimized_UsesNormalRectNotOffscreenRect()
    {
        var window = Window(new IntRect(-32000, -32000, -31840, -31972), new IntRect(100, 100, 900, 700),
            minimized: true, device: Display2);

        var saved = PlacementCalculator.Capture(window, OwnerMonitors);

        Assert.Equal(WindowState.Minimized, saved.State);
        Assert.Equal((100, 100, 800, 600), (saved.X, saved.Y, saved.Width, saved.Height));
        Assert.Equal(Display1, saved.MonitorDevice);
    }

    [Fact]
    public void SameMonitor_NormalWindow_Unchanged()
    {
        var target = RoundTrip(Window(new IntRect(100, 100, 900, 700)), OwnerMonitors);

        Assert.Equal(WindowState.Normal, target.State);
        Assert.Equal(new IntRect(100, 100, 900, 700), target.Rect);
        Assert.Equal(new IntRect(100, 100, 900, 700), target.NormalRect);
    }

    [Fact]
    public void SameMonitor_SnappedWindow_Unchanged()
    {
        var target = RoundTrip(Window(NotepadPlusPlusRect, NotepadPlusPlusNormal), OwnerMonitors);

        Assert.Equal(WindowState.Snapped, target.State);
        Assert.Equal(NotepadPlusPlusRect, target.Rect);
        Assert.Equal(NotepadPlusPlusNormal, target.NormalRect);
    }

    [Fact]
    public void SameMonitor_MaximizedWindow_Unchanged()
    {
        var maximized = new IntRect(-8, -8, 2568, 1408);
        var normal = new IntRect(300, 200, 1500, 1000);

        var target = RoundTrip(Window(maximized, normal, maximized: true), OwnerMonitors);

        Assert.Equal(WindowState.Maximized, target.State);
        Assert.Equal(maximized, target.Rect);
        Assert.Equal(normal, target.NormalRect);
    }

    [Fact]
    public void Display2NegativeY_Unchanged()
    {
        var rect = new IntRect(2600, -300, 3400, 500);

        var target = RoundTrip(Window(rect, device: Display2), OwnerMonitors);

        Assert.Equal(rect, target.Rect);
        Assert.Equal(rect, target.NormalRect);
    }

    [Fact]
    public void Display2Missing_MovesOntoDisplay1FullyOnScreen()
    {
        var rect = new IntRect(2600, 1000, 3400, 1500);
        var saved = PlacementCalculator.Capture(Window(rect, device: Display2), OwnerMonitors);

        var target = PlacementCalculator.ComputeTarget(saved, [Monitor1]);

        Assert.True(Contains(Monitor1.WorkArea, target.Rect), target.Rect.ToString());
        Assert.True(Contains(Monitor1.WorkArea, target.NormalRect), target.NormalRect.ToString());
        Assert.Equal((800, 500), (target.Rect.Width, target.Rect.Height));
        Assert.Equal(40, target.Rect.Left);
    }

    [Fact]
    public void Display2Missing_SnappedWindow_MovesBothRectsOntoDisplay1()
    {
        var rect = new IntRect(2552, -338, 3648, 620);
        var normal = new IntRect(2700, 900, 3400, 1500);
        var saved = PlacementCalculator.Capture(Window(rect, normal, device: Display2), OwnerMonitors);

        var target = PlacementCalculator.ComputeTarget(saved, [Monitor1]);

        Assert.Equal(WindowState.Snapped, target.State);
        Assert.True(TitleBarReachable(target.Rect, [Monitor1]), target.Rect.ToString());
        Assert.True(Contains(Monitor1.WorkArea, target.NormalRect), target.NormalRect.ToString());
    }

    [Fact]
    public void SameDeviceWithDifferentBounds_ScalesOntoThatMonitor()
    {
        var rect = new IntRect(2600, 1000, 3400, 1500);
        var saved = PlacementCalculator.Capture(Window(rect, device: Display2), OwnerMonitors);
        var rotated = new MonitorInfo(Display2, new IntRect(2560, 0, 4480, 1080), new IntRect(2560, 0, 4480, 1040), 96, false);

        var target = PlacementCalculator.ComputeTarget(saved, [Monitor1, rotated]);

        Assert.Equal(new IntRect(2631, 736, 4053, 1013), target.Rect);
        Assert.Equal(new IntRect(2631, 736, 4053, 1013), target.NormalRect);
        Assert.True(Contains(rotated.WorkArea, target.Rect), target.Rect.ToString());
    }

    private static MonitorInfo Display1At1080p =>
        new(Display1, new IntRect(0, 0, 1920, 1080), new IntRect(0, 0, 1920, 1040), 96, true);

    [Fact]
    public void ResolutionChange_NormalWindow_ScalesPositionAndSizePerAxis()
    {
        var saved = PlacementCalculator.Capture(Window(new IntRect(100, 100, 900, 700)), OwnerMonitors);

        var target = PlacementCalculator.ComputeTarget(saved, [Display1At1080p]);

        Assert.Equal(new IntRect(75, 74, 675, 520), target.Rect);
        Assert.Equal(new IntRect(75, 74, 675, 520), target.NormalRect);
    }

    [Fact]
    public void ResolutionChange_MaximizedWindow_ScalesOnlyNormalRect()
    {
        var maximized = new IntRect(-8, -8, 2568, 1408);
        var normal = new IntRect(300, 200, 1500, 1000);
        var saved = PlacementCalculator.Capture(Window(maximized, normal, maximized: true), OwnerMonitors);

        var target = PlacementCalculator.ComputeTarget(saved, [Display1At1080p]);

        Assert.Equal(WindowState.Maximized, target.State);
        Assert.Equal(new IntRect(0, 0, 2576, 1416), target.Rect);
        Assert.Equal(new IntRect(225, 149, 1125, 743), target.NormalRect);
    }

    [Fact]
    public void ResolutionChange_SnappedWindow_ScalesNormalAndShiftsLiveRect()
    {
        var saved = PlacementCalculator.Capture(Window(NotepadPlusPlusRect, NotepadPlusPlusNormal), OwnerMonitors);
        var smaller = new MonitorInfo(Display1, new IntRect(0, 0, 1920, 1080), new IntRect(0, 0, 1920, 1040), 96, true);

        var target = PlacementCalculator.ComputeTarget(saved, [smaller]);

        Assert.Equal(WindowState.Snapped, target.State);
        Assert.Equal((1296, 1416), (target.Rect.Width, target.Rect.Height));
        Assert.Equal((748, 753), (target.NormalRect.Width, target.NormalRect.Height));
    }

    [Fact]
    public void Scaling_SmallWindow_IsRaisedToMinimumSize()
    {
        var saved = PlacementCalculator.Capture(Window(new IntRect(100, 100, 400, 250)), OwnerMonitors);
        var small = new MonitorInfo(Display1, new IntRect(0, 0, 800, 600), new IntRect(0, 0, 800, 560), 96, true);

        var target = PlacementCalculator.ComputeTarget(saved, [small]);

        Assert.Equal((200, 100), (target.Rect.Width, target.Rect.Height));
        Assert.True(Contains(small.WorkArea, target.Rect), target.Rect.ToString());
    }

    [Fact]
    public void Scaling_LargeWindow_IsLimitedToTargetWorkArea()
    {
        var saved = PlacementCalculator.Capture(Window(new IntRect(0, 0, 1200, 500)), OwnerMonitors);
        saved.MonWidth = 1000;
        saved.WorkWidth = 1000;
        saved.WorkHeight = 500;

        var target = PlacementCalculator.ComputeTarget(saved, OwnerMonitors);

        Assert.Equal((2560, 1400), (target.Rect.Width, target.Rect.Height));
    }

    [Fact]
    public void SameMonitorWithSameBounds_IsNotScaled()
    {
        var saved = PlacementCalculator.Capture(Window(new IntRect(100, 100, 900, 700)), OwnerMonitors);
        var taskbarOnLeft = new MonitorInfo(Display1, Monitor1.Bounds, new IntRect(48, 0, 2560, 1440), 96, true);

        var target = PlacementCalculator.ComputeTarget(saved, [taskbarOnLeft]);

        Assert.Equal((800, 600), (target.Rect.Width, target.Rect.Height));
    }

    [Theory]
    [InlineData(5000, 3000)]
    [InlineData(-3000, -2000)]
    [InlineData(1000, 1430)]
    [InlineData(2500, -200)]
    public void OffscreenWindow_IsClamped(int x, int y)
    {
        var saved = PlacementCalculator.Capture(Window(new IntRect(100, 100, 900, 700)), OwnerMonitors);
        saved.X = x;
        saved.Y = y;
        saved.NormalX = x;
        saved.NormalY = y;

        var target = PlacementCalculator.ComputeTarget(saved, OwnerMonitors);

        Assert.True(TitleBarReachable(target.Rect, OwnerMonitors), target.Rect.ToString());
        Assert.True(TitleBarReachable(target.NormalRect, OwnerMonitors), target.NormalRect.ToString());
        Assert.Equal((800, 600), (target.Rect.Width, target.Rect.Height));
    }

    [Fact]
    public void Clamp_ReachableWindow_IsNotMoved()
    {
        var rect = new IntRect(2400, 300, 3000, 900);
        Assert.Equal(rect, PlacementCalculator.Clamp(rect, OwnerMonitors.Select(m => m.WorkArea).ToList()));
    }

    private const string IdA = @"\\?\DISPLAY#AAA0001#5&1a2b3c4&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string IdB = @"\\?\DISPLAY#BBB0002#5&1a2b3c4&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    // Owner layout with stable ids: physical A is the landscape primary, physical B the portrait monitor.
    private static readonly MonitorInfo[] OwnerMonitorsWithIds = [Monitor1 with { Id = IdA }, Monitor2 with { Id = IdB }];

    // Same physical layout after Windows renumbered the outputs.
    private static readonly MonitorInfo[] OwnerMonitorsSwapped =
        [Monitor1 with { Device = Display2, Id = IdA }, Monitor2 with { Device = Display1, Id = IdB }];

    // Snapped to the top of the portrait monitor; overhangs its work area by the invisible frame.
    private static readonly IntRect PortraitSnappedRect = new(2552, -338, 3648, 620);
    private static readonly IntRect PortraitNormal = new(2700, 100, 3400, 700);

    [Fact]
    public void Capture_StoresMonitorId()
    {
        var saved = PlacementCalculator.Capture(Window(PortraitSnappedRect, PortraitNormal, device: Display2), OwnerMonitorsWithIds);

        Assert.Equal(Display2, saved.MonitorDevice);
        Assert.Equal(IdB, saved.MonitorId);
    }

    [Fact]
    public void Capture_MonitorWithoutId_StoresNoMonitorId()
    {
        var saved = PlacementCalculator.Capture(Window(PortraitSnappedRect, PortraitNormal, device: Display2), OwnerMonitors);

        Assert.Null(saved.MonitorId);
    }

    [Fact]
    public void DeviceNamesSwapped_IdsStable_WindowStaysOnSamePhysicalMonitor()
    {
        var saved = PlacementCalculator.Capture(Window(PortraitSnappedRect, PortraitNormal, device: Display2), OwnerMonitorsWithIds);

        var target = PlacementCalculator.ComputeTarget(saved, OwnerMonitorsSwapped);

        Assert.Equal(WindowState.Snapped, target.State);
        Assert.Equal(PortraitSnappedRect, target.Rect);
        Assert.Equal(PortraitNormal, target.NormalRect);
        Assert.Same(OwnerMonitorsSwapped[1], PlacementCalculator.FindSavedMonitor(saved, OwnerMonitorsSwapped));
    }

    [Fact]
    public void DeviceNamesSwapped_MaximizedOnPrimary_StaysOnPrimary()
    {
        var maximized = new IntRect(-8, -8, 2568, 1408);
        var normal = new IntRect(300, 200, 1500, 1000);
        var saved = PlacementCalculator.Capture(Window(maximized, normal, maximized: true), OwnerMonitorsWithIds);

        var target = PlacementCalculator.ComputeTarget(saved, OwnerMonitorsSwapped);

        Assert.Equal(maximized, target.Rect);
        Assert.Equal(normal, target.NormalRect);
    }

    [Fact]
    public void OldPlacementWithoutId_MatchesByDeviceName()
    {
        var saved = PlacementCalculator.Capture(Window(PortraitSnappedRect, PortraitNormal, device: Display2), OwnerMonitors);
        Assert.Null(saved.MonitorId);

        var target = PlacementCalculator.ComputeTarget(saved, OwnerMonitorsWithIds);

        Assert.Equal(PortraitSnappedRect, target.Rect);
        Assert.Equal(PortraitNormal, target.NormalRect);
        Assert.Same(OwnerMonitorsWithIds[1], PlacementCalculator.FindSavedMonitor(saved, OwnerMonitorsWithIds));
    }

    [Fact]
    public void SystemGivesNoIds_FallsBackToDeviceName()
    {
        var saved = PlacementCalculator.Capture(Window(PortraitSnappedRect, PortraitNormal, device: Display2), OwnerMonitorsWithIds);

        var target = PlacementCalculator.ComputeTarget(saved, OwnerMonitors);

        Assert.Equal(PortraitSnappedRect, target.Rect);
        Assert.Equal(PortraitNormal, target.NormalRect);
        Assert.Same(Monitor2, PlacementCalculator.FindSavedMonitor(saved, OwnerMonitors));
    }

    [Fact]
    public void SavedIdNotOnSystem_FallsBackToDeviceName()
    {
        var saved = PlacementCalculator.Capture(Window(PortraitSnappedRect, PortraitNormal, device: Display2), OwnerMonitorsWithIds);
        MonitorInfo[] replaced = [Monitor1 with { Id = IdA }, Monitor2 with { Id = @"\\?\DISPLAY#CCC0003#other" }];

        Assert.Same(replaced[1], PlacementCalculator.FindSavedMonitor(saved, replaced));
    }

    [Fact]
    public void IdMatch_IgnoresCase()
    {
        var saved = PlacementCalculator.Capture(Window(PortraitSnappedRect, PortraitNormal, device: Display2), OwnerMonitorsWithIds);
        saved.MonitorId = IdB.ToUpperInvariant();
        saved.MonitorDevice = "";

        Assert.Same(OwnerMonitorsWithIds[1], PlacementCalculator.FindSavedMonitor(saved, OwnerMonitorsWithIds));
    }

    [Fact]
    public void NeitherIdNorDeviceMatch_FallsBackToPrimary()
    {
        var saved = PlacementCalculator.Capture(Window(new IntRect(2600, 1000, 3400, 1500), device: Display2), OwnerMonitorsWithIds);
        MonitorInfo[] onlyPrimary = [Monitor1 with { Id = IdA }];

        Assert.Null(PlacementCalculator.FindSavedMonitor(saved, onlyPrimary));
        var target = PlacementCalculator.ComputeTarget(saved, onlyPrimary);
        Assert.True(Contains(Monitor1.WorkArea, target.Rect), target.Rect.ToString());
    }

    [Fact]
    public void NoMonitors_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PlacementCalculator.Capture(Window(new IntRect(100, 100, 900, 700)), []));
    }
}
