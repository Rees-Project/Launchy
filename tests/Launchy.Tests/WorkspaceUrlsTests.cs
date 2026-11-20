using System.IO;
using Launchy.Models;
using Launchy.Services;
using Xunit;

namespace Launchy.Tests;

public sealed class WorkspaceUrlsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "launchy-ws-url-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public WorkspaceUrlsTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "workspaces.json");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")]
    [InlineData(@"C:\Program Files\Mozilla Firefox\firefox.exe")]
    [InlineData(@"C:\Tools\somebrowser.exe")]
    public void BuildTabsCommand_AnyBrowser_QuotesEveryUrlWithoutWindowFlag(string exe)
    {
        var command = BrowserLaunch.BuildTabsCommand(exe, ["https://a.com", "https://b.com/x"]);

        Assert.Equal(exe, command.FileName);
        Assert.Equal("\"https://a.com\" \"https://b.com/x\"", command.Arguments);
        Assert.DoesNotContain("new-window", command.Arguments);
        Assert.True(command.MayOpenAsTab);
    }

    [Fact]
    public void BuildTabsCommand_SpacesAmpersandsAndQuotes_StayInsideOneQuotedArgument()
    {
        var command = BrowserLaunch.BuildTabsCommand("chrome.exe",
            ["https://a.com/?q=one two&x=1", "https://b.com/\"x\""]);

        Assert.Equal("\"https://a.com/?q=one two&x=1\" \"https://b.com/%22x%22\"", command.Arguments);
    }

    [Fact]
    public void SetLocationText_SeveralUrls_SplitsIntoLocationAndUrls()
    {
        var item = new WorkspaceItem { Type = TargetType.Website };

        item.SetLocationText("https://a.com ; https://b.com;; https://c.com ");

        Assert.Equal("https://a.com", item.Location);
        Assert.Equal(["https://a.com", "https://b.com", "https://c.com"], item.Urls);
        Assert.Equal("https://a.com ; https://b.com ; https://c.com", item.LocationText());
    }

    [Fact]
    public void SetLocationText_SingleUrl_LeavesUrlsEmpty()
    {
        var item = new WorkspaceItem { Type = TargetType.Website, Urls = ["x", "y"] };

        item.SetLocationText("https://a.com");

        Assert.Equal("https://a.com", item.Location);
        Assert.Null(item.Urls);
        Assert.Equal(["https://a.com"], item.AllUrls());
    }

    [Fact]
    public void SetLocationText_NotWebsite_KeepsSemicolonsInLocation()
    {
        var item = new WorkspaceItem { Type = TargetType.Folder };

        item.SetLocationText(@"C:\a;b");

        Assert.Equal(@"C:\a;b", item.Location);
        Assert.Null(item.Urls);
    }

    [Fact]
    public void AllUrls_EmptyLocation_IsEmpty()
    {
        Assert.Empty(new WorkspaceItem().AllUrls());
    }

    [Fact]
    public void RoundTrip_Urls_AreKept()
    {
        var item = new WorkspaceItem
        {
            Type = TargetType.Website, Location = "https://a.com", Urls = ["https://a.com", "https://b.com"]
        };
        new WorkspaceStore(_path).Save([new Workspace { Name = "w", Items = [item] }]);

        var loaded = Assert.Single(Assert.Single(new WorkspaceStore(_path).Load()).Items);

        Assert.Equal("https://a.com", loaded.Location);
        Assert.Equal(["https://a.com", "https://b.com"], loaded.Urls);
    }

    [Fact]
    public void Load_FileWithoutUrls_LoadsUnchanged()
    {
        File.WriteAllText(_path,
            "{ \"version\": 1, \"workspaces\": [ { \"name\": \"old\", \"items\": [ { \"type\": \"Website\", \"location\": \"https://a.com\" } ] } ] }");

        var loaded = Assert.Single(Assert.Single(new WorkspaceStore(_path).Load()).Items);

        Assert.Null(loaded.Urls);
        Assert.Equal(["https://a.com"], loaded.AllUrls());
    }
}
