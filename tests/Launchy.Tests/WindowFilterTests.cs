using Launchy.Services.Windows;
using Xunit;

namespace Launchy.Tests;

public class WindowFilterTests
{
    private const uint OwnPid = 100;
    private const uint OtherPid = 200;
    private const long ToolWindow = 0x80;
    private const long AppWindow = 0x40000;

    private static bool Keep(
        bool visible = true, bool cloaked = false, long exStyle = 0, bool hasOwner = false,
        string title = "Notepad", uint pid = OtherPid) =>
        WindowFilter.IsAppWindow(visible, cloaked, exStyle, hasOwner, title, pid, OwnPid);

    [Fact]
    public void PlainVisibleWindow_IsKept() => Assert.True(Keep());

    [Fact]
    public void HiddenWindow_IsDropped() => Assert.False(Keep(visible: false));

    [Fact]
    public void CloakedWindow_IsDropped() => Assert.False(Keep(cloaked: true));

    [Fact]
    public void ToolWindow_IsDropped() => Assert.False(Keep(exStyle: ToolWindow));

    [Fact]
    public void ToolWindowWithAppWindowStyle_IsDropped() => Assert.False(Keep(exStyle: ToolWindow | AppWindow));

    [Fact]
    public void OwnedWindow_IsDropped() => Assert.False(Keep(hasOwner: true));

    [Fact]
    public void OwnedWindowWithAppWindowStyle_IsKept() => Assert.True(Keep(hasOwner: true, exStyle: AppWindow));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTitle_IsDropped(string title) => Assert.False(Keep(title: title));

    [Fact]
    public void OwnProcessWindow_IsDropped() => Assert.False(Keep(pid: OwnPid));
}

public class IntRectTests
{
    [Fact]
    public void WidthAndHeight_ComeFromEdges()
    {
        var rect = new IntRect(313, 100, 1313, 900);

        Assert.Equal(1000, rect.Width);
        Assert.Equal(800, rect.Height);
    }

    [Fact]
    public void NegativeTop_GivesCorrectHeight()
    {
        var portrait = new IntRect(2560, -330, 3640, 1590);

        Assert.Equal(1080, portrait.Width);
        Assert.Equal(1920, portrait.Height);
    }

    [Fact]
    public void EqualRects_AreEqual() => Assert.Equal(new IntRect(0, 0, 10, 10), new IntRect(0, 0, 10, 10));
}
