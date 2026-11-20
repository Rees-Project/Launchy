using System.IO;
using Launchy.Models;
using Launchy.Services.Windows;

namespace Launchy.Services;

public enum ItemOutcome
{
    Placed,
    LaunchedNotPlaced,
    NotFound,
    Failed
}

public sealed record ItemResult(WorkspaceItem Item, ItemOutcome Outcome, string? Message = null, IntPtr Window = default);

public enum ItemState
{
    Waiting,
    Starting,
    Placed,
    StartedNotPlaced,
    NotFound,
    Failed
}

/// <summary>One state change of one item. Index is the position in the workspace's item list.</summary>
public sealed record ItemProgress(int Index, WorkspaceItem Item, ItemState State, string? Message = null);

/// <summary>
/// Starts every item of a workspace, waits for its new window and moves it to the saved placement.
/// Items run one after the other, so items with the same process path never race for a window.
/// The runner has no thread logic of its own: call it from the UI thread. It never uses
/// ConfigureAwait(false), so each poll returns to the STA thread that the window source's
/// Shell.Application COM calls need.
/// </summary>
public sealed class WorkspaceRunner
{
    public const int DefaultTimeoutSeconds = 15;
    public const string ReappliedMessage = "re-applied after the app moved the window";
    public const string AlreadyOpenMessage =
        "no new window appeared; it may already be open (set 'If running' to Reuse)";
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan[] RecheckDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    private sealed record PlacedWindow(IntPtr Handle, PlacementTarget Target, DateTimeOffset PlacedAt);

    private readonly IWindowSource _source;
    private readonly IItemLauncher _launcher;
    private readonly IWindowPlacer _placer;
    private readonly TimeProvider _time;
    private readonly Action<IntPtr> _bringToFront;
    private readonly IWindowShowEvents? _showEvents;
    private readonly WakeSignal _wake = new();
    private bool _wakeActive;
    private readonly List<OpenedWindow> _opened = [];

    /// <summary>The windows the last run found for its items, marked as launched or reused. Used by WS-33 close.</summary>
    public IReadOnlyList<OpenedWindow> OpenedWindows => _opened;

    public WorkspaceRunner(IWindowSource source, IItemLauncher launcher, IWindowPlacer placer, TimeProvider time,
                           Action<IntPtr>? bringToFront = null, IWindowShowEvents? showEvents = null)
    {
        _source = source;
        _launcher = launcher;
        _placer = placer;
        _time = time;
        _bringToFront = bringToFront ?? ForegroundHelper.ForceForeground;
        _showEvents = showEvents;
    }

    /// <summary>
    /// Runs the items in order. On cancellation, returns the results of the items that finished.
    /// The optional progress callback runs on the calling thread: Starting before each item, then its final state.
    /// </summary>
    public async Task<IReadOnlyList<ItemResult>> RunAsync(Workspace workspace, CancellationToken ct,
                                                          Action<ItemProgress>? progress = null)
    {
        var results = new List<ItemResult>();
        var alreadyClaimed = new HashSet<IntPtr>();
        var placed = new List<PlacedWindow>();
        _opened.Clear();

        var showSubscription = _showEvents?.Subscribe(_wake.Set);
        _wakeActive = showSubscription is not null;
        try
        {
            for (var index = 0; index < workspace.Items.Count; index++)
            {
                var item = workspace.Items[index];
                ct.ThrowIfCancellationRequested();
                progress?.Invoke(new ItemProgress(index, item, ItemState.Starting));

                var result = await RunItemAsync(item, alreadyClaimed, placed, ct);
                results.Add(result);
                progress?.Invoke(new ItemProgress(index, item, ToState(result.Outcome), result.Message));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return results;
        }
        finally
        {
            _wakeActive = false;
            showSubscription?.Dispose();
        }

        var focused = results.LastOrDefault(r => r.Item.Focus && r.Outcome == ItemOutcome.Placed);
        if (focused is not null)
        {
            _bringToFront(focused.Window);
        }

        try
        {
            await RecheckLateMovesAsync(placed, results, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }

        return results;
    }

    /// <summary>
    /// Some apps move their own window a moment after it appears. Once every item ran, check each placed
    /// window about 1 s and 2 s after its placement and re-apply the target once if the app moved it.
    /// </summary>
    private async Task RecheckLateMovesAsync(List<PlacedWindow> placed, List<ItemResult> results, CancellationToken ct)
    {
        var done = new HashSet<IntPtr>();

        foreach (var delay in RecheckDelays)
        {
            foreach (var window in placed)
            {
                if (done.Contains(window.Handle))
                {
                    continue;
                }

                var wait = window.PlacedAt + delay - _time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _time, ct);
                }

                ct.ThrowIfCancellationRequested();

                switch (_placer.Recheck(window.Handle, window.Target))
                {
                    case RecheckStatus.Reapplied:
                        done.Add(window.Handle);
                        MarkReapplied(results, window.Handle);
                        break;
                    case RecheckStatus.WindowGone:
                    case RecheckStatus.Failed:
                        done.Add(window.Handle);
                        break;
                }
            }
        }
    }

    private static ItemState ToState(ItemOutcome outcome) => outcome switch
    {
        ItemOutcome.Placed => ItemState.Placed,
        ItemOutcome.LaunchedNotPlaced => ItemState.StartedNotPlaced,
        ItemOutcome.NotFound => ItemState.NotFound,
        _ => ItemState.Failed
    };

    private static void MarkReapplied(List<ItemResult> results, IntPtr handle)
    {
        var index = results.FindIndex(r => r.Outcome == ItemOutcome.Placed && r.Window == handle);
        if (index >= 0)
        {
            results[index] = results[index] with { Message = ReappliedMessage };
        }
    }

    /// <summary>A short list of the items that were not placed, for the tray balloon; null when all were placed.</summary>
    public static string? Summarize(IReadOnlyList<ItemResult> results)
    {
        var lines = results
            .Where(r => r.Outcome != ItemOutcome.Placed)
            .Select(r => $"{ItemName(r.Item)}: {Describe(r)}")
            .ToList();

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private async Task<ItemResult> RunItemAsync(WorkspaceItem item, ISet<IntPtr> alreadyClaimed,
                                               List<PlacedWindow> placed, CancellationToken ct)
    {
        var before = _source.GetTopLevelWindows();

        if (item.IfRunning == IfRunning.Reuse && item.Match is not null && HasCriteria(item.Match))
        {
            var existing = WindowMatcher.FindExisting(item.Match, before, _source.GetForegroundWindow(), alreadyClaimed);
            if (existing is not null)
            {
                alreadyClaimed.Add(existing.Handle);
                _opened.Add(OpenedWindow.From(existing, reused: true));
                return item.Placement is null
                    ? new ItemResult(item, ItemOutcome.LaunchedNotPlaced, "reused open window, no saved placement")
                    : Place(item, existing, placed);
            }
        }

        int? launchedPid;
        try
        {
            using var process = _launcher.Start(item);
            launchedPid = TryGetId(process);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ItemResult(item, ItemOutcome.Failed, ex.Message);
        }

        if (item.Match is null || !HasCriteria(item.Match))
        {
            return new ItemResult(item, ItemOutcome.LaunchedNotPlaced, "no window signature");
        }

        var window = await WaitForNewWindowAsync(item, before, launchedPid, alreadyClaimed, ct);
        if (window is null)
        {
            return new ItemResult(item, ItemOutcome.NotFound, IsSingleInstance(item)
                ? AlreadyOpenMessage
                : "no window appeared");
        }

        alreadyClaimed.Add(window.Handle);
        _opened.Add(OpenedWindow.From(window, reused: false));

        if (item.Placement is null)
        {
            return new ItemResult(item, ItemOutcome.LaunchedNotPlaced, "no saved placement");
        }

        return Place(item, window, placed);
    }

    private async Task<WindowInfo?> WaitForNewWindowAsync(WorkspaceItem item, IReadOnlyList<WindowInfo> before,
                                                          int? launchedPid, ISet<IntPtr> alreadyClaimed,
                                                          CancellationToken ct)
    {
        var deadline = _time.GetUtcNow() + TimeSpan.FromSeconds(item.TimeoutSeconds ?? DefaultTimeoutSeconds);

        while (true)
        {
            await WaitForNextPollAsync(ct);

            var after = _source.GetTopLevelWindows();
            var window = WindowMatcher.FindNew(item.Match!, before, after, launchedPid, alreadyClaimed);
            if (window is not null)
            {
                return window;
            }

            if (_time.GetUtcNow() >= deadline)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Waits one poll interval. A window show event ends the wait early; the caller then runs the same
    /// snapshot compare as a normal poll. Without a show event subscription this is the plain poll delay.
    /// </summary>
    private async Task WaitForNextPollAsync(CancellationToken ct)
    {
        if (!_wakeActive)
        {
            await Task.Delay(PollInterval, _time, ct);
            return;
        }

        if (!_wake.IsSet)
        {
            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await Task.WhenAny(Task.Delay(PollInterval, _time, pollCts.Token), _wake.NextAsync());
            pollCts.Cancel();
        }

        ct.ThrowIfCancellationRequested();
        _wake.Reset();
    }

    /// <summary>
    /// Remembers a show event until the next snapshot, so an event that arrives during a snapshot or a
    /// launch is not lost. Set and the waits all run on the runner's thread; no locking is needed.
    /// </summary>
    private sealed class WakeSignal
    {
        private TaskCompletionSource? _waiter;

        public bool IsSet { get; private set; }

        public void Set()
        {
            IsSet = true;
            _waiter?.TrySetResult();
        }

        public Task NextAsync()
        {
            _waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _waiter.Task;
        }

        public void Reset()
        {
            IsSet = false;
            _waiter = null;
        }
    }

    private ItemResult Place(WorkspaceItem item, WindowInfo window, List<PlacedWindow> placed)
    {
        PlacementTarget target;
        try
        {
            target = PlacementCalculator.ComputeTarget(item.Placement!, _source.GetMonitors());
        }
        catch (ArgumentException ex)
        {
            return new ItemResult(item, ItemOutcome.Failed, ex.Message);
        }

        var result = _placer.Place(window.Handle, target);
        if (!result.Success)
        {
            return new ItemResult(item, ItemOutcome.Failed, result.Reason ?? result.Status.ToString());
        }

        placed.Add(new PlacedWindow(window.Handle, target, _time.GetUtcNow()));
        return new ItemResult(item, ItemOutcome.Placed, Window: window.Handle);
    }

    private static int? TryGetId(System.Diagnostics.Process? process)
    {
        if (process is null)
        {
            return null;
        }

        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            // The process exited, or ShellExecute handed the launch to a process we have no handle for.
            return null;
        }
    }

    /// <summary>Folders and Store apps reuse an open window instead of making a new one.</summary>
    private static bool IsSingleInstance(WorkspaceItem item) =>
        item.Type == TargetType.Folder || !string.IsNullOrEmpty(item.Match?.AppUserModelId);

    private static bool HasCriteria(WindowMatch match) =>
        !string.IsNullOrWhiteSpace(match.ProcessPath)
        || !string.IsNullOrEmpty(match.ClassName)
        || !string.IsNullOrEmpty(match.AppUserModelId)
        || !string.IsNullOrWhiteSpace(match.FolderPath)
        || !string.IsNullOrEmpty(match.TitleContains);

    public static string ItemName(WorkspaceItem item)
    {
        var name = Path.GetFileName(item.Location.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? item.Location : name;
    }

    private static string Describe(ItemResult result)
    {
        var outcome = result.Outcome switch
        {
            ItemOutcome.LaunchedNotPlaced => "started, not placed",
            ItemOutcome.NotFound => "window not found",
            _ => "failed"
        };

        return string.IsNullOrEmpty(result.Message) ? outcome : $"{outcome} ({result.Message})";
    }
}
