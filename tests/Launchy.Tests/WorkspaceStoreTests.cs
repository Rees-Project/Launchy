using System.IO;
using Launchy.Models;
using Launchy.Services;
using Xunit;

namespace Launchy.Tests;

public sealed class WorkspaceStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "launchy-ws-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public WorkspaceStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "workspaces.json");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Workspace SampleWorkspace() => new()
    {
        Name = "morning",
        Items =
        [
            new()
            {
                Type = TargetType.Application,
                Location = "notepad.exe",
                Parameters = "-x",
                TimeoutSeconds = 20,
                Match = new() { ProcessPath = @"C:\Windows\notepad.exe", ClassName = "Notepad", TitleContains = "Untitled" },
                Placement = new()
                {
                    State = WindowState.Snapped, X = 1, Y = 2, Width = 800, Height = 600,
                    NormalX = 3, NormalY = 4, NormalWidth = 700, NormalHeight = 500,
                    MonitorDevice = @"\\.\DISPLAY1", MonX = 0, MonY = 0, MonWidth = 1920, MonHeight = 1080,
                    WorkX = 0, WorkY = 0, WorkWidth = 1920, WorkHeight = 1040, Dpi = 144
                }
            },
            new()
            {
                Type = TargetType.Folder,
                Location = @"C:\Temp",
                Match = new() { FolderPath = @"C:\Temp" }
            }
        ]
    };

    [Fact]
    public void RoundTrip_WorkspaceWithTwoItems_KeepsValues()
    {
        var store = new WorkspaceStore(_path);
        store.Save([SampleWorkspace()]);

        var loaded = Assert.Single(new WorkspaceStore(_path).Load());

        Assert.Equal("morning", loaded.Name);
        Assert.Equal(2, loaded.Items.Count);
        var first = loaded.Items[0];
        Assert.Equal(TargetType.Application, first.Type);
        Assert.Equal("-x", first.Parameters);
        Assert.Equal(20, first.TimeoutSeconds);
        Assert.Equal("Notepad", first.Match?.ClassName);
        Assert.Equal(WindowState.Snapped, first.Placement?.State);
        Assert.Equal(144, first.Placement?.Dpi);
        Assert.Equal(@"\\.\DISPLAY1", first.Placement?.MonitorDevice);
        Assert.Equal(1040, first.Placement?.WorkHeight);
        Assert.Equal(TargetType.Folder, loaded.Items[1].Type);
        Assert.Null(loaded.Items[1].TimeoutSeconds);
        Assert.Equal(@"C:\Temp", loaded.Items[1].Match?.FolderPath);
    }

    [Fact]
    public void RoundTrip_FocusFlag_IsKept()
    {
        var workspace = SampleWorkspace();
        workspace.Items[1].Focus = true;
        new WorkspaceStore(_path).Save([workspace]);

        var loaded = Assert.Single(new WorkspaceStore(_path).Load());

        Assert.False(loaded.Items[0].Focus);
        Assert.True(loaded.Items[1].Focus);
    }

    [Fact]
    public void Load_FileWithoutFocusField_DefaultsToNoFocus()
    {
        File.WriteAllText(_path,
            "{ \"version\": 1, \"workspaces\": [ { \"name\": \"old\", \"items\": [ { \"type\": \"Folder\", \"location\": \"C:\\\\Temp\" } ] } ] }");

        var loaded = Assert.Single(new WorkspaceStore(_path).Load());

        Assert.False(Assert.Single(loaded.Items).Focus);
    }

    [Fact]
    public void Load_PlacementWithoutMonitorId_KeepsDeviceAndNullId()
    {
        File.WriteAllText(_path,
            "{ \"version\": 1, \"workspaces\": [ { \"name\": \"old\", \"items\": [ { \"type\": \"Application\", " +
            "\"location\": \"notepad.exe\", \"placement\": { \"state\": \"Normal\", \"monitorDevice\": \"\\\\\\\\.\\\\DISPLAY2\" } } ] } ] }");

        var placement = Assert.Single(Assert.Single(new WorkspaceStore(_path).Load()).Items).Placement;

        Assert.Equal(@"\\.\DISPLAY2", placement?.MonitorDevice);
        Assert.Null(placement?.MonitorId);
    }

    [Fact]
    public void RoundTrip_MonitorId_IsKept()
    {
        const string id = @"\\?\DISPLAY#AAA0001#5&1a2b3c4&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        var workspace = SampleWorkspace();
        workspace.Items[0].Placement!.MonitorId = id;
        new WorkspaceStore(_path).Save([workspace]);

        var loaded = Assert.Single(new WorkspaceStore(_path).Load());

        Assert.Equal(id, loaded.Items[0].Placement?.MonitorId);
        Assert.Equal(1, new WorkspaceFile().Version);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyAndCreatesNothing()
    {
        var store = new WorkspaceStore(_path);

        Assert.Empty(store.Load());
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Load_NewerVersion_ThrowsAndLeavesBytesUnchanged()
    {
        const string json = "{ \"version\": 2, \"workspaces\": [ { \"name\": \"future\", \"unknownField\": 1 } ] }";
        File.WriteAllText(_path, json);
        var before = File.ReadAllBytes(_path);
        var store = new WorkspaceStore(_path);

        var ex = Assert.Throws<WorkspaceVersionException>(() => store.Load());
        Assert.Contains("version 2", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(_path));
    }

    [Fact]
    public void Save_AfterNewerVersionRefused_DoesNotWrite()
    {
        File.WriteAllText(_path, "{ \"version\": 3, \"workspaces\": [] }");
        var before = File.ReadAllBytes(_path);
        var store = new WorkspaceStore(_path);
        Assert.Throws<WorkspaceVersionException>(() => store.Load());

        Assert.Throws<InvalidOperationException>(() => store.Save([SampleWorkspace()]));
        Assert.Equal(before, File.ReadAllBytes(_path));
    }

    [Fact]
    public void Commands_V1File_StillReadsAndVersionStaysOne()
    {
        var commandsPath = Path.Combine(_dir, "commands.json");
        File.WriteAllText(commandsPath,
            "{ \"version\": 1, \"commands\": [ { \"id\": \"2f1e3f0e-0000-4000-8000-000000000001\", " +
            "\"command\": \"notepad\", \"type\": \"Application\", \"location\": \"notepad.exe\", \"runAsAdmin\": false } ] }");

        var file = DataFiles.Read<CommandFile>(commandsPath);

        Assert.Equal(1, file?.Version);
        Assert.Equal(1, new CommandFile().Version);
        var command = Assert.Single(file!.Commands);
        Assert.Equal("notepad", command.Command);
        Assert.Equal(TargetType.Application, command.Type);
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("NOTEPAD")]
    [InlineData("  Notepad  ")]
    public void ValidateName_ClashWithCommand_ReturnsMessage(string name)
    {
        List<LaunchCommand> commands = [new() { Command = "notepad" }];

        var error = WorkspaceStore.ValidateName(name, commands, []);

        Assert.NotNull(error);
        Assert.Contains("command name", error);
    }

    [Fact]
    public void ValidateName_DuplicateWorkspace_ReturnsMessage()
    {
        var error = WorkspaceStore.ValidateName("MORNING", [], [SampleWorkspace()]);

        Assert.Contains("already exists", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateName_Empty_ReturnsMessage(string name)
    {
        Assert.NotNull(WorkspaceStore.ValidateName(name, [], []));
    }

    [Fact]
    public void ValidateName_FreeName_ReturnsNull()
    {
        List<LaunchCommand> commands = [new() { Command = "notepad" }];

        Assert.Null(WorkspaceStore.ValidateName("evening", commands, [SampleWorkspace()]));
    }
}
