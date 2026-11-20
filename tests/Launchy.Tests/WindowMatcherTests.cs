using System.IO;
using Launchy.Models;
using Launchy.Services.Windows;
using Xunit;

namespace Launchy.Tests;

public class WindowMatcherTests
{
    private const string NotepadPath = @"C:\Windows\notepad.exe";
    private static readonly IntRect Rect = new(0, 0, 800, 600);

    private static WindowInfo Win(int handle, string path = NotepadPath, string cls = "Notepad",
        string title = "Untitled", uint pid = 1, string? aumid = null, string? folder = null,
        bool resizable = true) =>
        new(new IntPtr(handle), pid, path, cls, title, aumid, folder, Rect, Rect,
            1, false, false, false, resizable, "");

    private static HashSet<IntPtr> None() => new();

    [Fact]
    public void FindNew_ReturnsNewWindow_IgnoresWindowInBefore()
    {
        var old = Win(1);
        var fresh = Win(2);
        var sig = new WindowMatch { ProcessPath = NotepadPath };

        var found = WindowMatcher.FindNew(sig, [old], [old, fresh], null, None());

        Assert.Equal(fresh, found);
    }

    [Fact]
    public void FindNew_ReturnsNull_WhenNothingNew()
    {
        var old = Win(1);
        var sig = new WindowMatch { ProcessPath = NotepadPath };

        Assert.Null(WindowMatcher.FindNew(sig, [old], [old], null, None()));
    }

    [Fact]
    public void FindNew_TwoWindowsSameTitleDifferentPids_NewOneWins()
    {
        var old = Win(1, title: "Same", pid: 10);
        var fresh = Win(2, title: "Same", pid: 20);
        var sig = new WindowMatch { ProcessPath = NotepadPath, TitleContains = "Same" };

        Assert.Equal(fresh, WindowMatcher.FindNew(sig, [old], [old, fresh], null, None()));
    }

    [Fact]
    public void FindNew_LauncherStub_PidDiffers_StillMatchesByPath()
    {
        var fresh = Win(2, pid: 500);
        var sig = new WindowMatch { ProcessPath = NotepadPath };

        Assert.Equal(fresh, WindowMatcher.FindNew(sig, [], [fresh], 42, None()));
    }

    [Fact]
    public void FindNew_PrefersLaunchedPid_ThenListOrder()
    {
        var a = Win(2, pid: 5);
        var b = Win(3, pid: 42);
        var c = Win(4, pid: 42);
        var sig = new WindowMatch { ProcessPath = NotepadPath };

        Assert.Equal(b, WindowMatcher.FindNew(sig, [], [a, b, c], 42, None()));
        Assert.Equal(a, WindowMatcher.FindNew(sig, [], [a, b, c], 99, None()));
        Assert.Equal(a, WindowMatcher.FindNew(sig, [], [a, b, c], null, None()));
    }

    [Fact]
    public void FindNew_RespectsAlreadyClaimed()
    {
        var a = Win(2);
        var b = Win(3);
        var sig = new WindowMatch { ProcessPath = NotepadPath };
        var claimed = new HashSet<IntPtr> { a.Handle };

        Assert.Equal(b, WindowMatcher.FindNew(sig, [], [a, b], null, claimed));
        claimed.Add(b.Handle);
        Assert.Null(WindowMatcher.FindNew(sig, [], [a, b], null, claimed));
    }

    [Fact]
    public void FindNew_SplashWithDifferentClass_IsSkipped()
    {
        var splash = Win(2, cls: "SplashClass", resizable: false);
        var main = Win(3, cls: "MainClass");
        var sig = new WindowMatch { ProcessPath = NotepadPath, ClassName = "MainClass" };

        Assert.Equal(main, WindowMatcher.FindNew(sig, [], [splash, main], null, None()));
        Assert.Null(WindowMatcher.FindNew(sig, [], [splash], null, None()));
    }

    [Fact]
    public void FindNew_NoClassInSignature_RequiresResizable()
    {
        var splash = Win(2, resizable: false);
        var main = Win(3);
        var sig = new WindowMatch { ProcessPath = NotepadPath };

        Assert.Equal(main, WindowMatcher.FindNew(sig, [], [splash, main], null, None()));
    }

    [Fact]
    public void FindNew_NonResizableCapturedWindow_MatchesWhenClassMatches()
    {
        var dialog = Win(2, cls: "Dialog", resizable: false);
        var sig = new WindowMatch { ProcessPath = NotepadPath, ClassName = "Dialog" };

        Assert.Equal(dialog, WindowMatcher.FindNew(sig, [], [dialog], null, None()));
    }

    [Fact]
    public void Matches_ElectronApps_SeparatedByProcessPath()
    {
        var code = Win(1, path: @"C:\Apps\Code.exe", cls: "Chrome_WidgetWin_1");
        var slack = Win(2, path: @"C:\Apps\Slack.exe", cls: "Chrome_WidgetWin_1");
        var sig = new WindowMatch { ProcessPath = @"C:\Apps\Code.exe", ClassName = "Chrome_WidgetWin_1" };

        Assert.True(WindowMatcher.Matches(sig, code));
        Assert.False(WindowMatcher.Matches(sig, slack));
    }

    [Fact]
    public void Matches_ProcessPath_IsCaseInsensitive_AndExpandsVariables()
    {
        Environment.SetEnvironmentVariable("LAUNCHY_TEST_DIR", @"C:\Apps");
        try
        {
            var sig = new WindowMatch { ProcessPath = @"%LAUNCHY_TEST_DIR%\CODE.exe" };

            Assert.True(WindowMatcher.Matches(sig, Win(1, path: @"c:\apps\code.EXE")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAUNCHY_TEST_DIR", null);
        }
    }

    [Fact]
    public void Matches_ExplorerFolder_ExpandsVariablesAndIgnoresTrailingSlash()
    {
        var profile = Environment.GetEnvironmentVariable("USERPROFILE")!;
        var window = Win(1, cls: "CabinetWClass", folder: Path.Combine(profile, "Documents"));
        var sig = new WindowMatch { FolderPath = @"%USERPROFILE%\Documents\" };

        Assert.True(WindowMatcher.Matches(sig, window));
    }

    [Fact]
    public void Matches_ExplorerFolder_DifferentFolder_DoesNotMatch()
    {
        var window = Win(1, cls: "CabinetWClass", folder: @"C:\Users\X\Downloads");
        var sig = new WindowMatch { FolderPath = @"C:\Users\X\Documents" };

        Assert.False(WindowMatcher.Matches(sig, window));
    }

    [Fact]
    public void Matches_ShellGuidFolder_ComparesAsPlainString()
    {
        const string guid = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        var sig = new WindowMatch { FolderPath = guid };

        Assert.True(WindowMatcher.Matches(sig, Win(1, folder: guid.ToLowerInvariant())));
        Assert.False(WindowMatcher.Matches(sig, Win(2, folder: @"C:\Temp")));
    }

    [Fact]
    public void Matches_WindowWithoutFolder_DoesNotMatchFolderSignature()
    {
        var sig = new WindowMatch { FolderPath = @"C:\Temp" };

        Assert.False(WindowMatcher.Matches(sig, Win(1, folder: null)));
    }

    [Fact]
    public void FindNew_StoreAppByAumid()
    {
        var other = Win(2, cls: "ApplicationFrameWindow", aumid: "Other.App!App");
        var calc = Win(3, cls: "ApplicationFrameWindow", aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        var sig = new WindowMatch { AppUserModelId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" };

        Assert.Equal(calc, WindowMatcher.FindNew(sig, [], [other, calc], null, None()));
    }

    [Fact]
    public void Matches_TitleContains_IsCaseInsensitive()
    {
        var sig = new WindowMatch { ProcessPath = NotepadPath, TitleContains = "report" };

        Assert.True(WindowMatcher.Matches(sig, Win(1, title: "Quarterly REPORT.txt")));
        Assert.False(WindowMatcher.Matches(sig, Win(2, title: "Other")));
    }

    [Fact]
    public void Matches_EmptySignature_MatchesNothing()
    {
        Assert.False(WindowMatcher.Matches(new WindowMatch(), Win(1)));
    }

    [Fact]
    public void FindExisting_PrefersForegroundWindow()
    {
        var a = Win(1);
        var b = Win(2);
        var sig = new WindowMatch { ProcessPath = NotepadPath };

        Assert.Equal(b, WindowMatcher.FindExisting(sig, [a, b], b.Handle, None()));
        Assert.Equal(a, WindowMatcher.FindExisting(sig, [a, b], IntPtr.Zero, None()));
    }

    [Fact]
    public void FindExisting_ForegroundNotMatching_FallsBackToFirst_AndRespectsClaimed()
    {
        var a = Win(1);
        var b = Win(2);
        var other = Win(3, path: @"C:\Other.exe");
        var sig = new WindowMatch { ProcessPath = NotepadPath };

        Assert.Equal(a, WindowMatcher.FindExisting(sig, [a, b, other], other.Handle, None()));
        Assert.Equal(b, WindowMatcher.FindExisting(sig, [a, b, other], a.Handle, new HashSet<IntPtr> { a.Handle }));
    }
}
