using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Launchy.Models;
using Launchy.Services;
using Launchy.Services.Windows;
using Xunit;
using WindowState = Launchy.Models.WindowState;

namespace Launchy.Tests;

public class WorkspaceRunnerTests
{
    private const string Notepad = @"C:\Windows\System32\notepad.exe";
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string Display1 = @"\\.\DISPLAY1";

    private static readonly MonitorInfo Monitor =
        new(Display1, new IntRect(0, 0, 1920, 1080), new IntRect(0, 0, 1920, 1040), 96, true);

    /// <summary>A clock that jumps ahead by each timer's due time and fires it at once, so runs finish instantly.</summary>
    private sealed class AutoAdvanceTime : TimeProvider
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

    private sealed class FakeSource(AutoAdvanceTime time) : IWindowSource
    {
        private readonly List<(WindowInfo Window, TimeSpan AppearsAt)> _windows = [];

        public int Calls { get; private set; }
        public Action<int>? OnCall { get; set; }

        public void Add(WindowInfo window, TimeSpan appearsAt) => _windows.Add((window, appearsAt));

        public IReadOnlyList<WindowInfo> GetTopLevelWindows()
        {
            Calls++;
            OnCall?.Invoke(Calls);
            return _windows.Where(w => w.AppearsAt <= time.Elapsed).Select(w => w.Window).ToList();
        }

        public IReadOnlyList<MonitorInfo> GetMonitors() => [Monitor];

        public IntPtr GetForegroundWindow() => IntPtr.Zero;
    }

    private sealed class FakeLauncher(AutoAdvanceTime time) : IItemLauncher
    {
        public List<(WorkspaceItem Item, TimeSpan At)> Starts { get; } = [];
        public Action<WorkspaceItem>? OnStart { get; set; }

        public Process? Start(WorkspaceItem item)
        {
            Starts.Add((item, time.Elapsed));
            OnStart?.Invoke(item);
            return null;
        }
    }

    private sealed class FakePlacer(AutoAdvanceTime time) : IWindowPlacer
    {
        public List<(IntPtr Hwnd, PlacementTarget Target)> Calls { get; } = [];
        public List<TimeSpan> PlaceTimes { get; } = [];
        public List<(IntPtr Hwnd, TimeSpan At)> Rechecks { get; } = [];
        public PlaceResult Result { get; set; } = PlaceResult.Ok;
        public Func<IntPtr, int, RecheckStatus> OnRecheck { get; set; } = (_, _) => RecheckStatus.InPlace;
        public Action<IntPtr, int>? AfterRecheck { get; set; }

        public PlaceResult Place(IntPtr hwnd, PlacementTarget target)
        {
            Calls.Add((hwnd, target));
            PlaceTimes.Add(time.Elapsed);
            return Result;
        }

        public RecheckStatus Recheck(IntPtr hwnd, PlacementTarget target)
        {
            Rechecks.Add((hwnd, time.Elapsed));
            var attempt = Rechecks.Count(r => r.Hwnd == hwnd);
            var status = OnRecheck(hwnd, attempt);
            AfterRecheck?.Invoke(hwnd, attempt);
            return status;
        }
    }

    [Fact]
    public async Task OpenedWindows_records_launched_window_and_marks_reused_ones()
    {
        var launched = Window(1, Notepad);
        var reusedWindow = Window(2, Chrome);
        _source.Add(reusedWindow, TimeSpan.Zero);
        _source.Add(launched, TimeSpan.FromMilliseconds(300));
        var runner = new WorkspaceRunner(_source, _launcher, _placer, _time);
        var reuse = Item(Chrome);
        reuse.IfRunning = IfRunning.Reuse;

        await runner.RunAsync(new Workspace { Name = "Test", Items = [reuse, Item(Notepad)] }, CancellationToken.None);

        Assert.Collection(runner.OpenedWindows,
            w => { Assert.Equal(reusedWindow.Handle, w.Handle); Assert.True(w.Reused); },
            w => { Assert.Equal(launched.Handle, w.Handle); Assert.False(w.Reused); });
    }

    private readonly AutoAdvanceTime _time = new();
    private readonly FakeSource _source;
    private readonly FakeLauncher _launcher;
    private readonly FakePlacer _placer;

    public WorkspaceRunnerTests()
    {
        _source = new FakeSource(_time);
        _launcher = new FakeLauncher(_time);
        _placer = new FakePlacer(_time);
    }

    private Task<IReadOnlyList<ItemResult>> Run(params WorkspaceItem[] items) => Run(CancellationToken.None, items);

    private Task<IReadOnlyList<ItemResult>> Run(CancellationToken ct, params WorkspaceItem[] items) =>
        new WorkspaceRunner(_source, _launcher, _placer, _time)
            .RunAsync(new Workspace { Name = "Test", Items = [.. items] }, ct);

    private static WorkspaceItem Item(string processPath, bool withPlacement = true, int? timeoutSeconds = null) => new()
    {
        Type = TargetType.Application,
        Location = processPath,
        Match = new WindowMatch { ProcessPath = processPath },
        Placement = withPlacement ? Placement() : null,
        TimeoutSeconds = timeoutSeconds
    };

    private static WindowPlacement Placement() => new()
    {
        State = WindowState.Normal,
        X = 100, Y = 100, Width = 800, Height = 600,
        NormalX = 100, NormalY = 100, NormalWidth = 800, NormalHeight = 600,
        MonitorDevice = Display1,
        MonX = 0, MonY = 0, MonWidth = 1920, MonHeight = 1080,
        WorkX = 0, WorkY = 0, WorkWidth = 1920, WorkHeight = 1040,
        Dpi = 96
    };

    private static WindowInfo Window(int handle, string processPath) => new(
        new IntPtr(handle), 1000, processPath, "TestClass", $"Window {handle}", null, null,
        new IntRect(0, 0, 640, 480), new IntRect(0, 0, 640, 480), 1,
        false, false, false, true, Display1);

    [Fact]
    public async Task Window_that_appears_after_300_ms_is_placed()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(300));

        var results = await Run(Item(Notepad));

        var result = Assert.Single(results);
        Assert.Equal(ItemOutcome.Placed, result.Outcome);
        var call = Assert.Single(_placer.Calls);
        Assert.Equal(new IntPtr(1), call.Hwnd);
        Assert.Equal(new IntRect(100, 100, 900, 700), call.Target.Rect);
        Assert.Equal(TimeSpan.FromMilliseconds(300), Assert.Single(_placer.PlaceTimes));
    }

    [Fact]
    public async Task No_window_gives_NotFound_at_the_item_timeout()
    {
        var results = await Run(Item(Notepad, timeoutSeconds: 2));

        Assert.Equal(ItemOutcome.NotFound, Assert.Single(results).Outcome);
        Assert.Equal(TimeSpan.FromSeconds(2), _time.Elapsed);
        Assert.Empty(_placer.Calls);
    }

    [Fact]
    public async Task No_window_uses_the_15_second_default_timeout()
    {
        var results = await Run(Item(Notepad));

        Assert.Equal(ItemOutcome.NotFound, Assert.Single(results).Outcome);
        Assert.Equal(TimeSpan.FromSeconds(WorkspaceRunner.DefaultTimeoutSeconds), _time.Elapsed);
    }

    [Fact]
    public async Task Matching_window_that_was_open_before_the_launch_is_not_used()
    {
        _source.Add(Window(1, Notepad), TimeSpan.Zero);

        var results = await Run(Item(Notepad, timeoutSeconds: 1));

        Assert.Equal(ItemOutcome.NotFound, Assert.Single(results).Outcome);
        Assert.Empty(_placer.Calls);
    }

    [Fact]
    public async Task Cancel_mid_run_returns_the_finished_items_at_once()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource();
        _source.OnCall = call =>
        {
            if (call == 5)
            {
                cts.Cancel();
            }
        };

        var results = await Run(cts.Token, Item(Notepad), Item(Chrome), Item(Notepad));

        var result = Assert.Single(results);
        Assert.Equal(ItemOutcome.Placed, result.Outcome);
        Assert.Equal(2, _launcher.Starts.Count);
        Assert.True(_time.Elapsed < TimeSpan.FromSeconds(1), $"run took {_time.Elapsed}");
    }

    [Fact]
    public async Task Cancel_before_the_run_starts_nothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var results = await Run(cts.Token, Item(Notepad));

        Assert.Empty(results);
        Assert.Empty(_launcher.Starts);
    }

    [Fact]
    public async Task Items_with_the_same_process_path_start_after_the_previous_window_is_claimed()
    {
        var nextHandle = 1;
        _launcher.OnStart = _ => _source.Add(Window(nextHandle++, Chrome), _time.Elapsed + TimeSpan.FromMilliseconds(300));

        var results = await Run(Item(Chrome), Item(Chrome));

        Assert.All(results, r => Assert.Equal(ItemOutcome.Placed, r.Outcome));
        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMilliseconds(300)], _launcher.Starts.Select(s => s.At));
        Assert.Equal([new IntPtr(1), new IntPtr(2)], _placer.Calls.Select(c => c.Hwnd));
    }

    [Fact]
    public async Task Item_without_placement_is_LaunchedNotPlaced()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(200));

        var results = await Run(Item(Notepad, withPlacement: false));

        Assert.Equal(ItemOutcome.LaunchedNotPlaced, Assert.Single(results).Outcome);
        Assert.Empty(_placer.Calls);
    }

    [Fact]
    public async Task Item_without_match_is_LaunchedNotPlaced_without_waiting()
    {
        var item = Item(Notepad);
        item.Match = null;

        var results = await Run(item);

        Assert.Equal(ItemOutcome.LaunchedNotPlaced, Assert.Single(results).Outcome);
        Assert.Single(_launcher.Starts);
        Assert.Equal(TimeSpan.Zero, _time.Elapsed);
    }

    [Fact]
    public async Task Launch_error_gives_Failed_and_the_run_continues()
    {
        _launcher.OnStart = item =>
        {
            if (item.Location == Chrome)
            {
                throw new Win32Exception(2, "The system cannot find the file specified.");
            }

            _source.Add(Window(1, Notepad), _time.Elapsed + TimeSpan.FromMilliseconds(100));
        };

        var results = await Run(Item(Chrome), Item(Notepad));

        Assert.Equal(ItemOutcome.Failed, results[0].Outcome);
        Assert.Equal("The system cannot find the file specified.", results[0].Message);
        Assert.Equal(ItemOutcome.Placed, results[1].Outcome);
    }

    [Fact]
    public async Task Placer_error_gives_Failed_with_its_reason()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        _placer.Result = new PlaceResult(PlaceStatus.AccessDenied, "window is elevated");

        var results = await Run(Item(Notepad));

        var result = Assert.Single(results);
        Assert.Equal(ItemOutcome.Failed, result.Outcome);
        Assert.Equal("window is elevated", result.Message);
    }

    private async Task<IReadOnlyList<IntPtr>> RunAndCollectFront(CancellationToken ct, params WorkspaceItem[] items)
    {
        var fronted = new List<IntPtr>();
        var runner = new WorkspaceRunner(_source, _launcher, _placer, _time, fronted.Add);
        await runner.RunAsync(new Workspace { Name = "Test", Items = [.. items] }, ct);
        return fronted;
    }

    [Fact]
    public async Task Focus_item_window_is_brought_to_front_once_after_the_run()
    {
        var nextHandle = 1;
        _launcher.OnStart = _ => _source.Add(Window(nextHandle++, Chrome), _time.Elapsed + TimeSpan.FromMilliseconds(100));
        var first = Item(Chrome);
        first.Focus = true;

        var fronted = await RunAndCollectFront(CancellationToken.None, first, Item(Chrome));

        Assert.Equal([new IntPtr(1)], fronted);
    }

    [Fact]
    public async Task No_focus_item_means_nothing_is_brought_to_front()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));

        var fronted = await RunAndCollectFront(CancellationToken.None, Item(Notepad));

        Assert.Empty(fronted);
    }

    [Fact]
    public async Task Focus_item_that_was_not_placed_is_not_brought_to_front()
    {
        var item = Item(Notepad, timeoutSeconds: 1);
        item.Focus = true;

        var fronted = await RunAndCollectFront(CancellationToken.None, item);

        Assert.Empty(fronted);
    }

    [Fact]
    public async Task Cancelled_run_does_not_bring_anything_to_front()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource();
        _source.OnCall = call =>
        {
            if (call == 5)
            {
                cts.Cancel();
            }
        };
        var first = Item(Notepad);
        first.Focus = true;

        var fronted = await RunAndCollectFront(cts.Token, first, Item(Chrome));

        Assert.Empty(fronted);
    }

    [Fact]
    public async Task Placed_result_carries_the_window_handle()
    {
        _source.Add(Window(7, Notepad), TimeSpan.FromMilliseconds(100));

        var results = await Run(Item(Notepad));

        Assert.Equal(new IntPtr(7), Assert.Single(results).Window);
    }

    [Fact]
    public async Task Placed_window_is_rechecked_at_1_s_and_2_s_after_its_placement()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(300));

        var results = await Run(Item(Notepad));

        Assert.Equal(
            [(new IntPtr(1), TimeSpan.FromMilliseconds(1300)), (new IntPtr(1), TimeSpan.FromMilliseconds(2300))],
            _placer.Rechecks);
        Assert.Null(Assert.Single(results).Message);
    }

    [Fact]
    public async Task Rechecks_run_after_the_last_item_and_do_not_delay_the_launches()
    {
        var nextHandle = 1;
        _launcher.OnStart = _ => _source.Add(Window(nextHandle++, Chrome), _time.Elapsed + TimeSpan.FromMilliseconds(300));

        await Run(Item(Chrome), Item(Chrome));

        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMilliseconds(300)], _launcher.Starts.Select(s => s.At));
        Assert.Equal(
            [
                (new IntPtr(1), TimeSpan.FromMilliseconds(1300)),
                (new IntPtr(2), TimeSpan.FromMilliseconds(1600)),
                (new IntPtr(1), TimeSpan.FromMilliseconds(2300)),
                (new IntPtr(2), TimeSpan.FromMilliseconds(2600))
            ],
            _placer.Rechecks);
    }

    [Fact]
    public async Task Window_that_was_moved_is_reapplied_once_and_the_message_says_so()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        _placer.OnRecheck = (_, attempt) => attempt == 1 ? RecheckStatus.Reapplied : RecheckStatus.InPlace;

        var results = await Run(Item(Notepad));

        var result = Assert.Single(results);
        Assert.Equal(ItemOutcome.Placed, result.Outcome);
        Assert.Equal(WorkspaceRunner.ReappliedMessage, result.Message);
        Assert.Single(_placer.Rechecks);
        Assert.Null(WorkspaceRunner.Summarize(results));
    }

    [Fact]
    public async Task Window_moved_only_at_the_second_check_is_reapplied_then()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        _placer.OnRecheck = (_, attempt) => attempt == 2 ? RecheckStatus.Reapplied : RecheckStatus.InPlace;

        var results = await Run(Item(Notepad));

        Assert.Equal(2, _placer.Rechecks.Count);
        Assert.Equal(WorkspaceRunner.ReappliedMessage, Assert.Single(results).Message);
    }

    [Theory]
    [InlineData(RecheckStatus.WindowGone)]
    [InlineData(RecheckStatus.Failed)]
    public async Task Window_that_is_gone_or_fails_is_checked_once_and_the_result_stays_Placed(RecheckStatus status)
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        _placer.OnRecheck = (_, _) => status;

        var results = await Run(Item(Notepad));

        var result = Assert.Single(results);
        Assert.Equal(ItemOutcome.Placed, result.Outcome);
        Assert.Null(result.Message);
        Assert.Single(_placer.Rechecks);
    }

    [Fact]
    public async Task Items_that_were_not_placed_are_not_rechecked()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        _placer.Result = new PlaceResult(PlaceStatus.Failed, "x");

        await Run(Item(Notepad), Item(Chrome, timeoutSeconds: 1));

        Assert.Empty(_placer.Rechecks);
    }

    [Fact]
    public async Task Cancel_during_the_rechecks_stops_them_and_keeps_the_results()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource();
        _placer.AfterRecheck = (_, _) => cts.Cancel();

        var results = await Run(cts.Token, Item(Notepad));

        Assert.Equal(ItemOutcome.Placed, Assert.Single(results).Outcome);
        Assert.Single(_placer.Rechecks);
    }

    [Fact]
    public async Task Cancel_before_the_rechecks_start_skips_them()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource();
        var runner = new WorkspaceRunner(_source, _launcher, _placer, _time, _ => cts.Cancel());
        var item = Item(Notepad);
        item.Focus = true;

        var results = await runner.RunAsync(new Workspace { Name = "Test", Items = [item] }, cts.Token);

        Assert.Single(results);
        Assert.Empty(_placer.Rechecks);
    }

    [Fact]
    public void Summarize_is_null_when_every_item_is_placed()
    {
        Assert.Null(WorkspaceRunner.Summarize([new ItemResult(Item(Notepad), ItemOutcome.Placed)]));
    }

    [Fact]
    public void Summarize_lists_the_items_that_were_not_placed()
    {
        var summary = WorkspaceRunner.Summarize(
        [
            new ItemResult(Item(Notepad), ItemOutcome.Placed),
            new ItemResult(Item(Chrome), ItemOutcome.NotFound, "no window appeared"),
            new ItemResult(Item(@"C:\Tools\app.exe"), ItemOutcome.Failed, "window is elevated")
        ]);

        Assert.Equal("chrome.exe: window not found (no window appeared)\napp.exe: failed (window is elevated)", summary);
    }

    [Fact]
    public async Task Progress_reports_starting_then_the_final_state_of_each_item()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        var events = new List<ItemProgress>();
        var items = new[] { Item(Notepad), Item(Chrome, timeoutSeconds: 1), Item(Notepad, withPlacement: false) };
        _launcher.OnStart = item => { if (item == items[2]) throw new InvalidOperationException("boom"); };

        await new WorkspaceRunner(_source, _launcher, _placer, _time)
            .RunAsync(new Workspace { Name = "Test", Items = [.. items] }, CancellationToken.None, events.Add);

        Assert.Equal(
            [
                (0, ItemState.Starting), (0, ItemState.Placed),
                (1, ItemState.Starting), (1, ItemState.NotFound),
                (2, ItemState.Starting), (2, ItemState.Failed)
            ],
            events.Select(e => (e.Index, e.State)).ToArray());
        Assert.Equal("boom", events[^1].Message);
        Assert.Same(items[1], events[3].Item);
    }

    [Fact]
    public async Task Progress_reports_started_not_placed_for_an_item_without_placement()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        var events = new List<ItemProgress>();

        await new WorkspaceRunner(_source, _launcher, _placer, _time)
            .RunAsync(new Workspace { Name = "Test", Items = [Item(Notepad, withPlacement: false)] },
                      CancellationToken.None, events.Add);

        Assert.Equal(ItemState.StartedNotPlaced, events[^1].State);
    }

    [Fact]
    public async Task Cancel_from_the_progress_callback_stops_before_the_next_item()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));
        using var cts = new CancellationTokenSource();
        var events = new List<ItemProgress>();

        var results = await new WorkspaceRunner(_source, _launcher, _placer, _time).RunAsync(
            new Workspace { Name = "Test", Items = [Item(Notepad), Item(Chrome)] }, cts.Token,
            p =>
            {
                events.Add(p);
                if (p.State == ItemState.Placed)
                {
                    cts.Cancel();
                }
            });

        Assert.Single(results);
        Assert.Single(_launcher.Starts);
        Assert.DoesNotContain(events, e => e.Index == 1);
    }

    [Fact]
    public async Task Run_without_a_progress_callback_still_works()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(100));

        var results = await Run(Item(Notepad));

        Assert.Equal(ItemOutcome.Placed, Assert.Single(results).Outcome);
    }

    [Fact]
    public async Task Reuse_places_the_open_window_and_does_not_launch()
    {
        _source.Add(Window(7, Notepad), TimeSpan.Zero);
        var item = Item(Notepad);
        item.IfRunning = IfRunning.Reuse;

        var results = await Run(item);

        var result = Assert.Single(results);
        Assert.Equal(ItemOutcome.Placed, result.Outcome);
        Assert.Equal(new IntPtr(7), result.Window);
        Assert.Empty(_launcher.Starts);
        Assert.Equal(new IntPtr(7), Assert.Single(_placer.Calls).Hwnd);
    }

    [Fact]
    public async Task Reuse_launches_as_usual_when_no_window_is_open()
    {
        _source.Add(Window(1, Notepad), TimeSpan.FromMilliseconds(300));
        var item = Item(Notepad);
        item.IfRunning = IfRunning.Reuse;

        var results = await Run(item);

        Assert.Equal(ItemOutcome.Placed, Assert.Single(results).Outcome);
        Assert.Single(_launcher.Starts);
    }

    [Fact]
    public async Task Reuse_without_saved_placement_is_started_not_placed()
    {
        _source.Add(Window(7, Notepad), TimeSpan.Zero);
        var item = Item(Notepad, withPlacement: false);
        item.IfRunning = IfRunning.Reuse;

        var results = await Run(item);

        Assert.Equal(ItemOutcome.LaunchedNotPlaced, Assert.Single(results).Outcome);
        Assert.Empty(_launcher.Starts);
        Assert.Empty(_placer.Calls);
    }

    [Fact]
    public async Task Reuse_gives_two_items_with_the_same_signature_two_different_windows()
    {
        _source.Add(Window(7, Notepad), TimeSpan.Zero);
        _source.Add(Window(8, Notepad), TimeSpan.Zero);
        var first = Item(Notepad);
        var second = Item(Notepad);
        first.IfRunning = second.IfRunning = IfRunning.Reuse;

        await Run(first, second);

        Assert.Equal([new IntPtr(7), new IntPtr(8)], _placer.Calls.Select(c => c.Hwnd).ToArray());
        Assert.Empty(_launcher.Starts);
    }

    [Fact]
    public async Task LaunchNew_ignores_the_open_window_and_launches()
    {
        _source.Add(Window(7, Notepad), TimeSpan.Zero);
        _source.Add(Window(8, Notepad), TimeSpan.FromMilliseconds(300));

        var results = await Run(Item(Notepad));

        Assert.Equal(new IntPtr(8), Assert.Single(results).Window);
        Assert.Single(_launcher.Starts);
    }

    [Fact]
    public async Task LaunchNew_folder_with_no_new_window_reports_already_open_hint()
    {
        var item = Item(Notepad, timeoutSeconds: 1);
        item.Type = TargetType.Folder;

        var result = Assert.Single(await Run(item));

        Assert.Equal(ItemOutcome.NotFound, result.Outcome);
        Assert.Equal(WorkspaceRunner.AlreadyOpenMessage, result.Message);
    }

    [Fact]
    public async Task LaunchNew_store_app_with_no_new_window_reports_already_open_hint()
    {
        var item = Item(Notepad, timeoutSeconds: 1);
        item.Match = new WindowMatch { AppUserModelId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" };

        var result = Assert.Single(await Run(item));

        Assert.Equal(ItemOutcome.NotFound, result.Outcome);
        Assert.Equal(WorkspaceRunner.AlreadyOpenMessage, result.Message);
    }

    [Fact]
    public async Task LaunchNew_plain_app_with_no_new_window_keeps_the_plain_message()
    {
        var result = Assert.Single(await Run(Item(Notepad, timeoutSeconds: 1)));

        Assert.Equal("no window appeared", result.Message);
    }

    [Fact]
    public void Old_workspace_json_without_IfRunning_loads_as_LaunchNew()
    {
        var dir = Path.Combine(Path.GetTempPath(), "launchy-ws23-json");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid() + ".json");
        File.WriteAllText(path, """{"name":"Old","items":[{"type":"Application","location":"a.exe","timeoutSeconds":5}]}""");

        try
        {
            var workspace = DataFiles.Read<Workspace>(path);

            Assert.Equal(IfRunning.LaunchNew, Assert.Single(workspace!.Items).IfRunning);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IfRunning_loads_from_text()
    {
        var dir = Path.Combine(Path.GetTempPath(), "launchy-ws23-json");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid() + ".json");
        File.WriteAllText(path, """{"name":"W","items":[{"type":"Application","location":"a.exe","ifRunning":"Reuse"}]}""");

        try
        {
            Assert.Equal(IfRunning.Reuse, Assert.Single(DataFiles.Read<Workspace>(path)!.Items).IfRunning);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
