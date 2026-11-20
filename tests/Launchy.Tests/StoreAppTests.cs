using System.Diagnostics;
using Launchy.Models;
using Launchy.Services;
using Launchy.Services.Windows;
using Xunit;
using WindowState = Launchy.Models.WindowState;

namespace Launchy.Tests;

/// <summary>Store (UWP) apps and packaged desktop apps: AUMID choice, capture, matching and a runner pass.</summary>
public class StoreAppTests
{
    private const string CalculatorAumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
    private const string CalculatorExe = @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2210.0.0_x64__8wekyb3d8bbwe\CalculatorApp.exe";
    private const string FrameHost = @"C:\Windows\System32\ApplicationFrameHost.exe";
    private const string ClaudeAumid = "Claude_pzs8sxrjxfjjc!Claude";
    private const string ClaudeExe = @"C:\Program Files\WindowsApps\Claude_6.6395.1.0_x64__pzs8sxrjxfjjc\app\claude.exe";
    private const string ChromeClass = "Chrome_WidgetWin_1";
    private const string Display1 = @"\\.\DISPLAY1";
    private const string Frame = LayoutCapture.StoreAppFrameClass;

    private static readonly MonitorInfo Monitor =
        new(Display1, new IntRect(0, 0, 1920, 1080), new IntRect(0, 0, 1920, 1040), 96, true);

    private static readonly IReadOnlyList<MonitorInfo> Monitors = [Monitor];
    private static readonly string[] NoBrowsers = [];

    private static WindowInfo Win(int handle, string path, string cls, string? aumid, bool minimized = false,
                                  uint pid = 1) =>
        new(new IntPtr(handle), pid, path, cls, $"Window {handle}", aumid, null,
            minimized ? new IntRect(-32000, -32000, -31840, -31972) : new IntRect(100, 100, 900, 700),
            new IntRect(100, 100, 900, 700), minimized ? 2 : 1, false, minimized, false, true, Display1);

    private static Func<string?> Returns(string? value) => () => value;

    private static Func<string?> MustNotCall(string name) =>
        () => throw new InvalidOperationException($"{name} must not be read");

    // AppIdentity.Resolve

    [Fact]
    public void Resolve_Frame_PrefersCoreWindowProcessAumid()
    {
        var aumid = AppIdentity.Resolve(Frame, CalculatorExe, Returns(CalculatorAumid),
                                        MustNotCall("window property"), MustNotCall("process"));

        Assert.Equal(CalculatorAumid, aumid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_FrameWithoutCoreWindow_UsesWindowProperty(string? coreAumid)
    {
        var aumid = AppIdentity.Resolve(Frame, FrameHost, Returns(coreAumid), Returns(CalculatorAumid),
                                        MustNotCall("process"));

        Assert.Equal(CalculatorAumid, aumid);
    }

    [Fact]
    public void Resolve_FrameWithNoSource_IsNull()
    {
        Assert.Null(AppIdentity.Resolve(Frame, FrameHost, Returns(null), Returns(""), MustNotCall("process")));
    }

    [Fact]
    public void Resolve_PackagedDesktopApp_UsesProcessAumid()
    {
        var aumid = AppIdentity.Resolve(ChromeClass, ClaudeExe, MustNotCall("core"),
                                        MustNotCall("window property"), Returns(ClaudeAumid));

        Assert.Equal(ClaudeAumid, aumid);
    }

    [Fact]
    public void Resolve_UnpackagedApp_IsNullAndReadsNothing()
    {
        var aumid = AppIdentity.Resolve("Notepad", @"C:\Windows\notepad.exe", MustNotCall("core"),
                                        MustNotCall("window property"), MustNotCall("process"));

        Assert.Null(aumid);
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Pkg_1.0_x64__abc\app.exe", true)]
    [InlineData(@"D:\WINDOWSAPPS\Pkg_1.0_x64__abc\app.exe", true)]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\python.exe", true)]
    [InlineData(@"C:\Windows\notepad.exe", false)]
    [InlineData(@"C:\Tools\WindowsAppsHelper.exe", false)]
    [InlineData("", false)]
    public void IsPackagedPath(string path, bool expected)
    {
        Assert.Equal(expected, AppIdentity.IsPackagedPath(path));
    }

    // LayoutCapture

    [Fact]
    public void Capture_MinimizedFrameWithAumid_IsCapturable()
    {
        var window = Win(1, FrameHost, Frame, CalculatorAumid, minimized: true);

        Assert.Null(LayoutCapture.WhyNotCapturable(window));
        var item = LayoutCapture.CreateItem(window, Monitors, NoBrowsers)!;

        Assert.Equal(@"shell:AppsFolder\" + CalculatorAumid, item.Location);
        Assert.Equal(CalculatorAumid, item.Match!.AppUserModelId);
        Assert.Null(item.Match.ProcessPath);
        Assert.Equal(WindowState.Minimized, item.Placement!.State);
        Assert.Equal(800, item.Placement.NormalWidth);
    }

    [Fact]
    public void Capture_PackagedDesktopApp_UsesAppsFolderAndAumidMatch()
    {
        var item = LayoutCapture.CreateItem(Win(1, ClaudeExe, ChromeClass, ClaudeAumid), Monitors, NoBrowsers)!;

        Assert.Equal(TargetType.Application, item.Type);
        Assert.Equal(@"shell:AppsFolder\" + ClaudeAumid, item.Location);
        Assert.Equal(ClaudeAumid, item.Match!.AppUserModelId);
        Assert.Equal(ChromeClass, item.Match.ClassName);
        Assert.Null(item.Match.ProcessPath);
    }

    [Fact]
    public void Capture_SameAppOutsideWindowsApps_KeepsProcessPath()
    {
        const string Unpacked = @"C:\Users\me\AppData\Local\AnthropicClaude\claude.exe";
        var item = LayoutCapture.CreateItem(Win(1, Unpacked, ChromeClass, ClaudeAumid), Monitors, NoBrowsers)!;

        Assert.False(LayoutCapture.IsPackagedDesktopApp(Win(1, Unpacked, ChromeClass, ClaudeAumid)));
        Assert.Equal(Unpacked, item.Location);
        Assert.Equal(Unpacked, item.Match!.ProcessPath);
        Assert.Null(item.Match.AppUserModelId);
    }

    [Fact]
    public void Capture_PackagedBrowser_StaysWebsite()
    {
        const string PackagedBrowser = @"C:\Program Files\WindowsApps\Some.Browser_1.0_x64__abc\chrome.exe";
        var item = LayoutCapture.CreateItem(Win(1, PackagedBrowser, ChromeClass, "Some.Browser_abc!App"),
                                            Monitors, NoBrowsers)!;

        Assert.Equal(TargetType.Website, item.Type);
        Assert.Equal("", item.Location);
    }

    [Fact]
    public void Recapture_PackagedDesktopApp_SwitchesMatchToAumid()
    {
        var existing = new WorkspaceItem
        {
            Type = TargetType.Application,
            Location = ClaudeExe,
            Match = new WindowMatch { ProcessPath = ClaudeExe, ClassName = ChromeClass, TitleContains = "Claude" }
        };

        var result = LayoutCapture.Apply([existing],
            [new CaptureSelection(Win(5, ClaudeExe, ChromeClass, ClaudeAumid), 0)], Monitors, NoBrowsers);

        var match = Assert.Single(result).Match!;
        Assert.Equal(ClaudeAumid, match.AppUserModelId);
        Assert.Null(match.ProcessPath);
        Assert.Equal("Claude", match.TitleContains);
    }

    // WindowMatcher

    [Fact]
    public void FindNew_PicksNewFrameByAumid_NotOtherStoreApps()
    {
        var signature = new WindowMatch { AppUserModelId = CalculatorAumid };
        var clock = Win(1, FrameHost, Frame, "Microsoft.WindowsAlarms_8wekyb3d8bbwe!App", minimized: true);
        var otherCalc = Win(2, CalculatorExe, Frame, CalculatorAumid);
        var newCalc = Win(3, CalculatorExe, Frame, CalculatorAumid);

        var found = WindowMatcher.FindNew(signature, [clock, otherCalc], [clock, otherCalc, newCalc],
                                          launchedPid: null, new HashSet<IntPtr>());

        Assert.Same(newCalc, found);
    }

    [Fact]
    public void FindNew_FrameWithoutAumidYet_IsNotMatched()
    {
        var signature = new WindowMatch { AppUserModelId = CalculatorAumid };
        var starting = Win(3, FrameHost, Frame, null);

        Assert.Null(WindowMatcher.FindNew(signature, [], [starting], null, new HashSet<IntPtr>()));
    }

    [Fact]
    public void FindNew_PackagedDesktopApp_MatchesAfterVersionChange()
    {
        var item = LayoutCapture.CreateItem(Win(1, ClaudeExe, ChromeClass, ClaudeAumid), Monitors, NoBrowsers)!;
        var updated = Win(9, ClaudeExe.Replace("6.6395.1.0", "6.7000.0.0"), ChromeClass, ClaudeAumid);

        Assert.Same(updated, WindowMatcher.FindNew(item.Match!, [], [updated], null, new HashSet<IntPtr>()));
    }

    // WorkspaceRunner with fakes

    [Fact]
    public async Task Runner_PlacesFrame_OnceItsAumidIsKnown()
    {
        var time = new StepTime();
        var starting = Win(7, FrameHost, Frame, null, pid: 100);
        var ready = Win(7, CalculatorExe, Frame, CalculatorAumid, pid: 200);
        var source = new TimedSource(time, elapsed =>
            elapsed < TimeSpan.FromMilliseconds(300) ? []
            : elapsed < TimeSpan.FromMilliseconds(800) ? [starting]
            : [ready]);
        var launcher = new RecordingLauncher();
        var placer = new RecordingPlacer(time);
        var item = LayoutCapture.CreateItem(Win(1, CalculatorExe, Frame, CalculatorAumid), Monitors, NoBrowsers)!;

        var results = await new WorkspaceRunner(source, launcher, placer, time)
            .RunAsync(new Workspace { Name = "Store", Items = [item] }, CancellationToken.None);

        Assert.Equal(ItemOutcome.Placed, Assert.Single(results).Outcome);
        Assert.Equal(@"shell:AppsFolder\" + CalculatorAumid, Assert.Single(launcher.Locations));
        var placed = Assert.Single(placer.Placed);
        Assert.Equal(new IntPtr(7), placed.Hwnd);
        Assert.Equal(TimeSpan.FromMilliseconds(800), placed.At);
    }

    /// <summary>A clock that jumps ahead by each timer's due time and fires it at once.</summary>
    private sealed class StepTime : TimeProvider
    {
        private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private DateTimeOffset _now = Start;

        public TimeSpan Elapsed => _now - Start;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _now += dueTime;
            callback(state);
            return new NoTimer();
        }

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class TimedSource(StepTime time, Func<TimeSpan, IReadOnlyList<WindowInfo>> windowsAt) : IWindowSource
    {
        public IReadOnlyList<WindowInfo> GetTopLevelWindows() => windowsAt(time.Elapsed);

        public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;

        public IntPtr GetForegroundWindow() => IntPtr.Zero;
    }

    private sealed class RecordingLauncher : IItemLauncher
    {
        public List<string> Locations { get; } = [];

        public Process? Start(WorkspaceItem item)
        {
            Locations.Add(item.Location);
            return null;
        }
    }

    private sealed class RecordingPlacer(StepTime time) : IWindowPlacer
    {
        public List<(IntPtr Hwnd, TimeSpan At)> Placed { get; } = [];

        public PlaceResult Place(IntPtr hwnd, PlacementTarget target)
        {
            Placed.Add((hwnd, time.Elapsed));
            return PlaceResult.Ok;
        }

        public RecheckStatus Recheck(IntPtr hwnd, PlacementTarget target) => RecheckStatus.InPlace;
    }
}
