using System.IO;
using Launchy.Models;
using Launchy.Services;
using Xunit;

namespace Launchy.Tests;

public class WorkspaceCsvTests
{
    private static string ToCsv(params Workspace[] workspaces)
    {
        var writer = new StringWriter();
        WorkspaceCsv.Write(writer, workspaces);
        return writer.ToString();
    }

    private static WorkspaceCsvResult Read(string csv, IEnumerable<LaunchCommand>? commands = null, IEnumerable<Workspace>? existing = null) =>
        WorkspaceCsv.Read(new StringReader(csv), commands ?? [], existing ?? []);

    [Fact]
    public void WriteThenRead_RoundTripsWorkspaceWithTwoItems()
    {
        var original = new Workspace
        {
            Name = "morning, \"main\"",
            Items =
            [
                new WorkspaceItem
                {
                    Type = TargetType.Website, Location = "https://example.com/?q=a,b", Parameters = "say \"hi\"",
                    TimeoutSeconds = 20, Focus = true, IfRunning = IfRunning.Reuse,
                    Match = new WindowMatch { TitleContains = "Example" },
                    Placement = new WindowPlacement { State = WindowState.Snapped, X = 1, Y = 2, Width = 300, Height = 400, MonitorDevice = "DISPLAY1" }
                },
                new WorkspaceItem { Type = TargetType.Application, Location = @"C:\Windows\notepad.exe" }
            ]
        };

        var result = Read(ToCsv(original));

        Assert.Empty(result.Errors);
        var w = Assert.Single(result.Workspaces);
        Assert.Equal(original.Name, w.Name);
        Assert.Equal(2, w.Items.Count);
        Assert.Equal(DataFiles.ToCompactJson(original.Items), DataFiles.ToCompactJson(w.Items));
        Assert.Equal("https://example.com/?q=a,b", w.Items[0].Location);
        Assert.Equal(IfRunning.Reuse, w.Items[0].IfRunning);
        Assert.Equal(WindowState.Snapped, w.Items[0].Placement!.State);
        Assert.Equal(@"C:\Windows\notepad.exe", w.Items[1].Location);
    }

    [Fact]
    public void Read_BadJsonRow_IsSkippedWithErrorAndOthersImport()
    {
        var good = new Workspace { Name = "good", Items = [new WorkspaceItem { Location = "a.exe" }] };
        var csv = ToCsv(good) + "bad,\"[{not json\"\r\n";

        var result = Read(csv + ToCsv(new Workspace { Name = "other", Items = [new WorkspaceItem { Location = "b.exe" }] }).Split("\r\n")[1] + "\r\n");

        Assert.Equal(["good", "other"], result.Workspaces.Select(w => w.Name));
        var error = Assert.Single(result.Errors);
        Assert.Contains("Row 3", error);
        Assert.Contains("bad", error);
    }

    [Fact]
    public void Read_NameClashes_AreSkippedWithMessage()
    {
        var item = new WorkspaceItem { Location = "a.exe" };
        var csv = ToCsv(
            new Workspace { Name = "Notes", Items = [item] },
            new Workspace { Name = "work", Items = [item] },
            new Workspace { Name = "fresh", Items = [item] },
            new Workspace { Name = "FRESH", Items = [item] });

        var result = Read(csv,
            [new LaunchCommand { Command = "notes", Type = TargetType.Application, Location = "n.exe" }],
            [new Workspace { Name = "Work" }]);

        Assert.Equal(["fresh"], result.Workspaces.Select(w => w.Name));
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains("command name", result.Errors[0]);
        Assert.Contains("already exists", result.Errors[1]);
    }

    [Fact]
    public void Read_EmptyNameAndEmptyItems_AreSkipped()
    {
        var result = Read("Name,Items\r\n,\"[]\"\r\nnoitems,\r\n");

        Assert.Empty(result.Workspaces);
        var error = Assert.Single(result.Errors);
        Assert.Contains("noitems", error);
    }

    [Fact]
    public void Read_MissingHeaderColumn_Throws()
    {
        Assert.Throws<FormatException>(() => Read("Command,Type\r\nx,y\r\n"));
    }
}
