using System.Diagnostics;
using Launchy.Models;
using Launchy.Services;
using Launchy.Services.Windows;
using Xunit;

namespace Launchy.Tests;

/// <summary>WS-30: window show events end a poll wait early; polling stays the fallback.</summary>
public class WorkspaceRunnerWakeTests
{
    private const string Notepad = @"C:\Windows\System32\notepad.exe";
    private const string Display1 = @"\\.\DISPLAY1";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private static readonly MonitorInfo Monitor =
        new(Display1, new IntRect(0, 0, 1920, 1080), new IntRect(0, 0, 1920, 1040), 96, true);

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private abstract class TestTime : TimeProvider
    {
        protected DateTimeOffset Now = Start;

        public TimeSpan Elapsed => Now - Start;

        public override DateTimeOffset GetUtcNow() => Now;

        protected sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Timers never fire, so a poll wait can only end by a show event or by cancellation.</summary>
    private sealed class FrozenTime : TestTime
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new NoTimer();
    }

    /// <summary>Jumps ahead by each timer's due time and fires it at once.</summary>
    private sealed class AutoAdvanceTime : TestTime
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Now += dueTime;
            callback(state);
            return new NoTimer();
        }
    }

    private sealed class FakeShowEvents(bool available = true) : IWindowShowEvents
    {
        private Action? _onShown;

        public int Subscriptions { get; private set; }
        public bool IsSubscribed => _onShown is not null;

        public IDisposable? Subscribe(Action onShown)
        {
            Subscriptions++;
            if (!available)
            {
                return null;
            }

            _onShown = onShown;
            return new Subscription(this);
        }

        public void Raise() => _onShown?.Invoke();

        private sealed class Subscription(FakeShowEvents owner) : IDisposable
        {
            public void Dispose() => owner._onShown = null;
        }
    }

    private sealed class FakeSource(TestTime time) : IWindowSource
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

    private sealed class FakeLauncher : IItemLauncher
    {
        public Action<WorkspaceItem>? OnStart { get; set; }

        public Process? Start(WorkspaceItem item)
        {
            OnStart?.Invoke(item);
            return null;
        }
    }

    private sealed class FakePlacer : IWindowPlacer
    {
        public PlaceResult Place(IntPtr hwnd, PlacementTarget target) => PlaceResult.Ok;

        public RecheckStatus Recheck(IntPtr hwnd, PlacementTarget target) => RecheckStatus.InPlace;
    }

    private readonly FakeLauncher _launcher = new();
    private readonly FakePlacer _placer = new();

    private WorkspaceRunner Runner(IWindowSource source, TimeProvider time, IWindowShowEvents? showEvents) =>
        new(source, _launcher, _placer, time, _ => { }, showEvents);

    private static Workspace Workspace(params WorkspaceItem[] items) => new() { Name = "Test", Items = [.. items] };

    /// <summary>No placement, so the run ends without the late-move rechecks that wait on the clock.</summary>
    private static WorkspaceItem Item(int? timeoutSeconds = null) => new()
    {
        Type = TargetType.Application,
        Location = Notepad,
        Match = new WindowMatch { ProcessPath = Notepad },
        TimeoutSeconds = timeoutSeconds
    };

    private static WindowInfo Window(int handle) => new(
        new IntPtr(handle), 1000, Notepad, "TestClass", $"Window {handle}", null, null,
        new IntRect(0, 0, 640, 480), new IntRect(0, 0, 640, 480), 1,
        false, false, false, true, Display1);

    [Fact]
    public async Task ShowEventDuringLaunch_FindsWindowWithoutWaitingForAPoll()
    {
        var time = new FrozenTime();
        var source = new FakeSource(time);
        var events = new FakeShowEvents();
        _launcher.OnStart = _ =>
        {
            source.Add(Window(1), TimeSpan.Zero);
            events.Raise();
        };

        var results = await Runner(source, time, events).RunAsync(Workspace(Item()), CancellationToken.None)
            .WaitAsync(Guard);

        var result = Assert.Single(results);
        Assert.Equal(ItemOutcome.LaunchedNotPlaced, result.Outcome);
        Assert.Equal("no saved placement", result.Message);
        Assert.Equal(TimeSpan.Zero, time.Elapsed);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task ShowEventDuringWait_EndsTheWaitEarly()
    {
        var time = new FrozenTime();
        var source = new FakeSource(time);
        var events = new FakeShowEvents();

        var run = Runner(source, time, events).RunAsync(Workspace(Item()), CancellationToken.None);
        Assert.False(run.IsCompleted);
        Assert.Equal(1, source.Calls);

        source.Add(Window(1), TimeSpan.Zero);
        events.Raise();
        var results = await run.WaitAsync(Guard);

        Assert.Equal(ItemOutcome.LaunchedNotPlaced, Assert.Single(results).Outcome);
        Assert.Equal(2, source.Calls);
        Assert.False(events.IsSubscribed);
    }

    [Fact]
    public async Task ShowEventWithoutMatch_FallsBackToPollingUntilTheDeadline()
    {
        var time = new AutoAdvanceTime();
        var source = new FakeSource(time);
        var events = new FakeShowEvents();
        _launcher.OnStart = _ => events.Raise();

        var results = await Runner(source, time, events).RunAsync(Workspace(Item(timeoutSeconds: 1)),
                                                                  CancellationToken.None).WaitAsync(Guard);

        Assert.Equal(ItemOutcome.NotFound, Assert.Single(results).Outcome);
        Assert.Equal(TimeSpan.FromSeconds(1), time.Elapsed);
        // The snapshot before launch, one early snapshot at 0 ms, then polls at 100 ms .. 1000 ms.
        Assert.Equal(12, source.Calls);
    }

    [Fact]
    public async Task HookNotAvailable_BehavesLikeNoHook()
    {
        var (withoutHookResult, withoutHookElapsed, withoutHookCalls) = await RunWindowAt300Ms(null);
        var unavailable = new FakeShowEvents(available: false);
        var (result, elapsed, calls) = await RunWindowAt300Ms(unavailable);

        Assert.Equal(1, unavailable.Subscriptions);
        Assert.Equal(withoutHookResult.Outcome, result.Outcome);
        Assert.Equal(withoutHookResult.Message, result.Message);
        Assert.Equal(withoutHookElapsed, elapsed);
        Assert.Equal(withoutHookCalls, calls);
        Assert.Equal(TimeSpan.FromMilliseconds(300), elapsed);
    }

    private async Task<(ItemResult Result, TimeSpan Elapsed, int Calls)> RunWindowAt300Ms(IWindowShowEvents? events)
    {
        var time = new AutoAdvanceTime();
        var source = new FakeSource(time);
        source.Add(Window(1), TimeSpan.FromMilliseconds(300));

        var results = await Runner(source, time, events).RunAsync(Workspace(Item()), CancellationToken.None)
            .WaitAsync(Guard);
        return (Assert.Single(results), time.Elapsed, source.Calls);
    }

    [Fact]
    public async Task Run_UnsubscribesWhenItemsAreDone()
    {
        var time = new AutoAdvanceTime();
        var source = new FakeSource(time);
        source.Add(Window(1), TimeSpan.FromMilliseconds(200));
        var events = new FakeShowEvents();
        var subscribedDuringRun = false;
        source.OnCall = _ => subscribedDuringRun |= events.IsSubscribed;

        await Runner(source, time, events).RunAsync(Workspace(Item()), CancellationToken.None).WaitAsync(Guard);

        Assert.True(subscribedDuringRun);
        Assert.Equal(1, events.Subscriptions);
        Assert.False(events.IsSubscribed);
    }

    [Fact]
    public async Task Cancel_DuringWait_UnsubscribesAndReturns()
    {
        var time = new FrozenTime();
        var source = new FakeSource(time);
        var events = new FakeShowEvents();
        using var cts = new CancellationTokenSource();

        var run = Runner(source, time, events).RunAsync(Workspace(Item()), cts.Token);
        Assert.True(events.IsSubscribed);

        cts.Cancel();
        var results = await run.WaitAsync(Guard);

        Assert.Empty(results);
        Assert.False(events.IsSubscribed);
    }
}
