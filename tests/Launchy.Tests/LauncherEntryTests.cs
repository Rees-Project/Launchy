using Launchy.Models;
using Launchy.Services;
using Xunit;

namespace Launchy.Tests;

public sealed class LauncherEntryTests
{
    private static LaunchCommand Cmd(string name) => new() { Command = name, Type = TargetType.Application, Location = "x.exe" };

    private static Workspace Ws(string name, int items = 2) => new()
    {
        Name = name,
        Items = Enumerable.Range(0, items).Select(_ => new WorkspaceItem { Location = "x" }).ToList()
    };

    [Fact]
    public void Build_PutsCommandsFirstAndHidesClashingWorkspace()
    {
        var entries = LauncherEntry.Build([Cmd("notepad")], [Ws("Notepad"), Ws("morning")]);

        Assert.Equal(["notepad", "morning"], entries.Select(e => e.Name));
        Assert.NotNull(entries[0].Command);
        Assert.NotNull(entries[1].Workspace);
    }

    [Fact]
    public void Find_ResolvesWorkspaceByExactAndPrefix()
    {
        var entries = LauncherEntry.Build([Cmd("notepad")], [Ws("morning")]);

        Assert.Equal("morning", LauncherEntry.Find(entries, "morning")?.Workspace?.Name);
        Assert.Equal("morning", LauncherEntry.Find(entries, "mor")?.Workspace?.Name);
        Assert.Null(LauncherEntry.Find(entries, "zzz"));
    }

    [Fact]
    public void Matches_PutsExactMatchFirst()
    {
        var entries = LauncherEntry.Build([Cmd("work-app")], [Ws("work")]);

        Assert.Equal(["work", "work-app"], LauncherEntry.Matches(entries, "work").Select(e => e.Name));
    }

    [Fact]
    public void Find_SingleLetterWithTwoMatches_ReturnsNull()
    {
        var entries = LauncherEntry.Build([Cmd("mail")], [Ws("morning")]);

        Assert.Null(LauncherEntry.Find(entries, "m"));
    }
}

public sealed class WorkspaceDisplayTests
{
    [Theory]
    [InlineData(0, "Workspace: 0 items")]
    [InlineData(1, "Workspace: 1 item")]
    [InlineData(3, "Workspace: 3 items")]
    public void ItemCountText(int count, string expected) => Assert.Equal(expected, WorkspaceDisplay.ItemCountText(count));

    [Fact]
    public void PlacementSummary_NullIsNotCaptured() => Assert.Equal("Not captured", WorkspaceDisplay.PlacementSummary(null));

    [Fact]
    public void PlacementSummary_NormalShowsMonitorAndSize()
    {
        var placement = new WindowPlacement { State = Models.WindowState.Normal, MonitorDevice = @"\\.\DISPLAY2", Width = 800, Height = 600 };

        Assert.Equal("DISPLAY2 · Normal · 800×600", WorkspaceDisplay.PlacementSummary(placement));
    }

    [Fact]
    public void PlacementSummary_MaximizedHasNoSize()
    {
        var placement = new WindowPlacement { State = Models.WindowState.Maximized, MonitorDevice = @"\\.\DISPLAY1" };

        Assert.Equal("DISPLAY1 · Maximized", WorkspaceDisplay.PlacementSummary(placement));
    }

    [Fact]
    public void ResultLine_NamesTheItemAndOutcome()
    {
        var item = new WorkspaceItem { Location = @"C:\Windows\notepad.exe" };

        Assert.Equal("1. notepad.exe: placed", WorkspaceDisplay.ResultLine(1, new ItemResult(item, ItemOutcome.Placed)));
        Assert.Equal("2. notepad.exe: window not found (no window appeared)",
            WorkspaceDisplay.ResultLine(2, new ItemResult(item, ItemOutcome.NotFound, "no window appeared")));
    }
}
