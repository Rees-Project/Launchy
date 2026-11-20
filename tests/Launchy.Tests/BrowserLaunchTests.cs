using Launchy.Services;
using Xunit;

namespace Launchy.Tests;

public class BrowserLaunchTests
{
    private const string Url = "https://example.com/a?b=1";

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")]
    [InlineData(@"C:\Brave\BRAVE.EXE")]
    public void Chromium_UsesDoubleDashNewWindow(string exe)
    {
        var command = BrowserLaunch.BuildCommand(exe, Url);

        Assert.Equal(exe, command.FileName);
        Assert.Equal($"--new-window \"{Url}\"", command.Arguments);
        Assert.False(command.MayOpenAsTab);
    }

    [Fact]
    public void Firefox_UsesSingleDashNewWindow()
    {
        var command = BrowserLaunch.BuildCommand(@"C:\Program Files\Mozilla Firefox\firefox.exe", Url);

        Assert.Equal($"-new-window \"{Url}\"", command.Arguments);
        Assert.False(command.MayOpenAsTab);
    }

    [Fact]
    public void UnknownBrowser_FallsBackAndMayOpenAsTab()
    {
        var command = BrowserLaunch.BuildCommand(@"C:\Tools\somebrowser.exe", Url);

        Assert.Equal($"\"{Url}\"", command.Arguments);
        Assert.True(command.MayOpenAsTab);
    }
}
