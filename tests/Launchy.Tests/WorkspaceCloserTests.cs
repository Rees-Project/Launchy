using Launchy.Services;
using Launchy.Services.Windows;
using Xunit;

namespace Launchy.Tests;

public class WorkspaceCloserTests
{
    private sealed class FakeSource(params WindowInfo[] windows) : IWindowSource
    {
        public IReadOnlyList<WindowInfo> GetTopLevelWindows() => windows;
        public IReadOnlyList<MonitorInfo> GetMonitors() => [];
        public IntPtr GetForegroundWindow() => IntPtr.Zero;
    }

    private sealed class FakeCloser : IWindowCloser
    {
        public List<IntPtr> Closed { get; } = [];
        public bool Accepts { get; set; } = true;

        public bool RequestClose(IntPtr handle)
        {
            Closed.Add(handle);
            return Accepts;
        }
    }

    private static WindowInfo Window(int handle, uint pid = 10, string cls = "Notepad") =>
        new(new IntPtr(handle), pid, @"C:\x.exe", cls, "t", null, null,
            new IntRect(0, 0, 1, 1), new IntRect(0, 0, 1, 1), 1, false, false, false, true, "");

    private static OpenedWindow Opened(int handle, uint pid = 10, string cls = "Notepad", bool reused = false) =>
        new(new IntPtr(handle), pid, cls, reused);

    private static (WorkspaceCloser Closer, FakeCloser Sender) Create(
        IEnumerable<OpenedWindow>? recorded, params WindowInfo[] current)
    {
        var sessions = new WorkspaceSessions();
        if (recorded is not null)
        {
            sessions.Record("Dev", recorded);
        }

        var sender = new FakeCloser();
        return (new WorkspaceCloser(sessions, new FakeSource(current), sender), sender);
    }

    [Fact]
    public void Close_never_run_workspace_does_nothing()
    {
        var (closer, sender) = Create(null, Window(1));

        var result = closer.Close("Dev");

        Assert.False(result.HadRun);
        Assert.Empty(sender.Closed);
    }

    [Fact]
    public void Close_sends_request_to_matching_launched_window()
    {
        var (closer, sender) = Create([Opened(1), Opened(2)], Window(1), Window(2));

        var result = closer.Close("dev");

        Assert.Equal(2, result.Requested);
        Assert.Equal([new IntPtr(1), new IntPtr(2)], sender.Closed);
    }

    [Fact]
    public void Close_skips_reused_windows()
    {
        var (closer, sender) = Create([Opened(1), Opened(2, reused: true)], Window(1), Window(2));

        var result = closer.Close("Dev");

        Assert.Equal(1, result.Requested);
        Assert.Equal(1, result.SkippedReused);
        Assert.Equal([new IntPtr(1)], sender.Closed);
    }

    [Fact]
    public void Close_skips_window_that_is_gone()
    {
        var (closer, sender) = Create([Opened(1)]);

        var result = closer.Close("Dev");

        Assert.Equal(0, result.Requested);
        Assert.Equal(1, result.NotOpen);
        Assert.Empty(sender.Closed);
    }

    [Theory]
    [InlineData(99u, "Notepad")]
    [InlineData(10u, "Chrome_WidgetWin_1")]
    public void Close_skips_reused_handle_with_other_process_or_class(uint pid, string cls)
    {
        var (closer, sender) = Create([Opened(1)], Window(1, pid, cls));

        var result = closer.Close("Dev");

        Assert.Equal(0, result.Requested);
        Assert.Equal(1, result.NotOpen);
        Assert.Empty(sender.Closed);
    }

    [Fact]
    public void Close_does_not_count_a_refused_post()
    {
        var (closer, sender) = Create([Opened(1)], Window(1));
        sender.Accepts = false;

        Assert.Equal(0, closer.Close("Dev").Requested);
    }

    [Fact]
    public void Record_replaces_the_previous_run()
    {
        var sessions = new WorkspaceSessions();
        sessions.Record("Dev", [Opened(1)]);
        sessions.Record("Dev", [Opened(2)]);

        Assert.Equal([new IntPtr(2)], sessions.Get("Dev")!.Select(w => w.Handle));
    }

    [Fact]
    public void Describe_reports_counts()
    {
        var text = new CloseResult(true, 2, 1, 3).Describe("Dev");

        Assert.Contains("2 window(s)", text);
        Assert.Contains("1 already-open", text);
        Assert.Contains("3 no longer open", text);
    }

    [Theory]
    [InlineData(":close Dev", true, "Dev")]
    [InlineData("  :CLOSE   my space ", true, "my space")]
    [InlineData(":close", true, "")]
    [InlineData(":closed", false, "")]
    [InlineData("close Dev", false, "")]
    [InlineData("", false, "")]
    public void TryParse_reads_close_command(string text, bool expected, string name)
    {
        Assert.Equal(expected, CloseCommand.TryParse(text, out var parsed));
        Assert.Equal(name, parsed);
    }
}
