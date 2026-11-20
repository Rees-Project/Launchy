using System.IO;
using Launchy.Models;
using Launchy.Services;
using Xunit;

namespace Launchy.Tests;

public sealed class WebsiteNewWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "launchy-ws-newwin-tests-" + Guid.NewGuid().ToString("N"));

    public WebsiteNewWindowTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe", "--new-window")]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", "--new-window")]
    [InlineData(@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe", "--new-window")]
    [InlineData(@"C:\Program Files\Mozilla Firefox\firefox.exe", "-new-window")]
    public void BuildNewWindowCommand_KnownBrowser_PutsFlagBeforeQuotedUrls(string exe, string flag)
    {
        var command = BrowserLaunch.BuildNewWindowCommand(exe, ["https://a.com", "https://b.com/x"]);

        Assert.NotNull(command);
        Assert.Equal(exe, command!.FileName);
        Assert.Equal($"{flag} \"https://a.com\" \"https://b.com/x\"", command.Arguments);
        Assert.False(command.MayOpenAsTab);
    }

    [Fact]
    public void BuildNewWindowCommand_UnknownBrowser_ReturnsNull()
    {
        Assert.Null(BrowserLaunch.BuildNewWindowCommand(@"C:\Tools\somebrowser.exe", ["https://a.com"]));
    }

    [Fact]
    public void BuildNewWindowCommand_QuoteInUrl_IsEscaped()
    {
        var command = BrowserLaunch.BuildNewWindowCommand("chrome.exe", ["https://a.com/\"x\""]);

        Assert.Equal("--new-window \"https://a.com/%22x%22\"", command!.Arguments);
    }

    [Fact]
    public void NewWindow_DefaultsToFalse_AndOldFilesLoad()
    {
        var path = Path.Combine(_dir, "workspaces.json");
        File.WriteAllText(path,
            """{"version":1,"workspaces":[{"name":"w","items":[{"type":"Website","location":"https://a.com"}]}]}""");

        var item = new WorkspaceStore(path).Load().Single().Items.Single();

        Assert.False(item.NewWindow);
    }

    [Fact]
    public void NewWindow_RoundTripsThroughSaveAndLoad()
    {
        var path = Path.Combine(_dir, "workspaces.json");
        var store = new WorkspaceStore(path);
        store.Save([new Workspace
        {
            Name = "w",
            Items = [new WorkspaceItem { Type = TargetType.Website, Location = "https://a.com", NewWindow = true }]
        }]);

        Assert.True(new WorkspaceStore(path).Load().Single().Items.Single().NewWindow);
    }
}
