using Launchy.Models;
using Launchy.Services;
using Launchy.Services.Windows;
using Xunit;
using WindowState = Launchy.Models.WindowState;

namespace Launchy.Tests;

public class LayoutCaptureTests
{
    private const string NotepadPath = @"C:\Windows\notepad.exe";
    private const string ExplorerPath = @"C:\Windows\explorer.exe";
    private const string ChromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string CalculatorAumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    private static readonly MonitorInfo Primary =
        new(@"\\.\DISPLAY1", new IntRect(0, 0, 2560, 1440), new IntRect(0, 0, 2560, 1400), 96, true);

    private static readonly IReadOnlyList<MonitorInfo> Monitors = [Primary];
    private static readonly string[] NoBrowsers = [];

    private static WindowInfo Win(int handle, string path = NotepadPath, string cls = "Notepad",
        string title = "Untitled - Notepad", string? aumid = null, string? folder = null,
        IntRect? rect = null) =>
        new(new IntPtr(handle), 1, path, cls, title, aumid, folder, rect ?? new IntRect(100, 100, 900, 700),
            rect ?? new IntRect(100, 100, 900, 700), 1, false, false, false, true, Primary.Device);

    [Fact]
    public void CreateItem_Explorer_IsFolderWithFolderPathMatch()
    {
        var item = LayoutCapture.CreateItem(
            Win(1, ExplorerPath, LayoutCapture.ExplorerClass, "Projects", folder: @"C:\Projects"), Monitors, NoBrowsers)!;

        Assert.Equal(TargetType.Folder, item.Type);
        Assert.Equal(@"C:\Projects", item.Location);
        Assert.Equal(ExplorerPath, item.Match!.ProcessPath);
        Assert.Equal(LayoutCapture.ExplorerClass, item.Match.ClassName);
        Assert.Equal(@"C:\Projects", item.Match.FolderPath);
        Assert.Null(item.Parameters);
    }

    [Fact]
    public void CreateItem_ExplorerShellFolder_KeepsShellPath()
    {
        const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        var item = LayoutCapture.CreateItem(
            Win(1, ExplorerPath, LayoutCapture.ExplorerClass, "This PC", folder: ThisPc), Monitors, NoBrowsers)!;

        Assert.Equal(ThisPc, item.Location);
        Assert.Equal(ThisPc, item.Match!.FolderPath);
    }

    [Fact]
    public void CreateItem_KnownBrowser_IsWebsiteWithEmptyLocation()
    {
        var item = LayoutCapture.CreateItem(Win(1, ChromePath, "Chrome_WidgetWin_1", "Google - Chrome"),
                                            Monitors, NoBrowsers)!;

        Assert.Equal(TargetType.Website, item.Type);
        Assert.Equal("", item.Location);
        Assert.Equal(ChromePath, item.Match!.ProcessPath);
        Assert.Equal("Chrome_WidgetWin_1", item.Match.ClassName);
        Assert.Null(item.Match.FolderPath);
    }

    [Fact]
    public void CreateItem_DefaultBrowserNotInList_IsWebsite()
    {
        const string Other = @"C:\Apps\Floorp\floorp.exe";
        var item = LayoutCapture.CreateItem(Win(1, Other, "MozillaWindowClass"), Monitors, [Other])!;

        Assert.Equal(TargetType.Website, item.Type);
    }

    [Fact]
    public void CreateItem_ElectronAppWithChromeClass_IsApplication()
    {
        const string Code = @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe";
        var item = LayoutCapture.CreateItem(Win(1, Code, "Chrome_WidgetWin_1", "Visual Studio Code"),
                                            Monitors, NoBrowsers)!;

        Assert.Equal(TargetType.Application, item.Type);
        Assert.Equal(Code, item.Location);
    }

    [Fact]
    public void CreateItem_StoreApp_UsesAppsFolderAndAumid()
    {
        var item = LayoutCapture.CreateItem(
            Win(1, @"C:\Program Files\WindowsApps\Calc\CalculatorApp.exe", LayoutCapture.StoreAppFrameClass,
                "Calculator", aumid: CalculatorAumid),
            Monitors, NoBrowsers)!;

        Assert.Equal(TargetType.Application, item.Type);
        Assert.Equal(@"shell:AppsFolder\" + CalculatorAumid, item.Location);
        Assert.Equal(CalculatorAumid, item.Match!.AppUserModelId);
        Assert.Null(item.Match.ProcessPath);
        Assert.Null(item.Match.ClassName);
    }

    [Fact]
    public void CreateItem_OtherApp_UsesProcessPathAndClass()
    {
        var item = LayoutCapture.CreateItem(Win(1), Monitors, NoBrowsers)!;

        Assert.Equal(TargetType.Application, item.Type);
        Assert.Equal(NotepadPath, item.Location);
        Assert.Equal(NotepadPath, item.Match!.ProcessPath);
        Assert.Equal("Notepad", item.Match.ClassName);
        Assert.Null(item.Match.AppUserModelId);
        Assert.Null(item.Match.FolderPath);
    }

    [Fact]
    public void CreateItem_StoresPlacementFromCalculator()
    {
        var window = Win(1, rect: new IntRect(200, 150, 1000, 750));

        var item = LayoutCapture.CreateItem(window, Monitors, NoBrowsers)!;

        Assert.Equal(WindowState.Normal, item.Placement!.State);
        Assert.Equal(200, item.Placement.X);
        Assert.Equal(150, item.Placement.Y);
        Assert.Equal(800, item.Placement.Width);
        Assert.Equal(Primary.Device, item.Placement.MonitorDevice);
    }

    [Theory]
    [InlineData("", "Notepad", null, null)]
    [InlineData(@"C:\Windows\System32\ApplicationFrameHost.exe", LayoutCapture.StoreAppFrameClass, null, null)]
    [InlineData(ExplorerPath, LayoutCapture.ExplorerClass, null, null)]
    public void CreateItem_ReturnsNull_WhenWindowIsNotCapturable(string path, string cls, string? aumid, string? folder)
    {
        var window = Win(1, path, cls, aumid: aumid, folder: folder);

        Assert.NotNull(LayoutCapture.WhyNotCapturable(window));
        Assert.Null(LayoutCapture.CreateItem(window, Monitors, NoBrowsers));
    }

    [Fact]
    public void Recapture_KeepsLocationParametersAndTimeout_UpdatesMatchAndPlacement()
    {
        var oldPlacement = new WindowPlacement { X = 5, Y = 5, Width = 10, Height = 10 };
        var existing = new WorkspaceItem
        {
            Type = TargetType.Application,
            Location = @"%WINDIR%\notepad.exe",
            Parameters = @"C:\notes\todo.txt",
            TimeoutSeconds = 20,
            Match = new WindowMatch { ProcessPath = NotepadPath, TitleContains = "todo" },
            Placement = oldPlacement
        };
        var window = Win(7, title: "todo.txt - Notepad", rect: new IntRect(300, 200, 1100, 800));

        var result = LayoutCapture.Apply([existing], [new CaptureSelection(window, 0)], Monitors, NoBrowsers);

        var item = Assert.Single(result);
        Assert.Equal(TargetType.Application, item.Type);
        Assert.Equal(@"%WINDIR%\notepad.exe", item.Location);
        Assert.Equal(@"C:\notes\todo.txt", item.Parameters);
        Assert.Equal(20, item.TimeoutSeconds);
        Assert.Equal(300, item.Placement!.X);
        Assert.Equal(200, item.Placement.Y);
        Assert.Equal("Notepad", item.Match!.ClassName);
        Assert.Equal("todo", item.Match.TitleContains);
        Assert.Same(oldPlacement, existing.Placement);
    }

    private const string ClaudeAumid = "Claude_pzs8sxrjxfjjc!Claude";
    private const string ClaudeOldPath = @"C:\Program Files\WindowsApps\Claude_1.0.0.0_x64__pzs8sxrjxfjjc\app\claude.exe";
    private const string ClaudeNewPath = @"C:\Program Files\WindowsApps\Claude_1.1.0.0_x64__pzs8sxrjxfjjc\app\claude.exe";

    [Fact]
    public void Recapture_PackagedDesktopApp_ReplacesVersionedPathWithAppsFolder()
    {
        var existing = new WorkspaceItem
        {
            Type = TargetType.Application,
            Location = ClaudeOldPath,
            Parameters = "--flag",
            TimeoutSeconds = 15,
            Match = new WindowMatch { ProcessPath = ClaudeOldPath, TitleContains = "chat" }
        };
        var window = Win(3, ClaudeNewPath, "Chrome_WidgetWin_1", aumid: ClaudeAumid);

        var item = Assert.Single(
            LayoutCapture.Apply([existing], [new CaptureSelection(window, 0)], Monitors, NoBrowsers));

        Assert.Equal(LayoutCapture.AppsFolderPrefix + ClaudeAumid, item.Location);
        Assert.Equal(TargetType.Application, item.Type);
        Assert.Equal("--flag", item.Parameters);
        Assert.Equal(15, item.TimeoutSeconds);
        Assert.Equal("chat", item.Match!.TitleContains);
        Assert.Equal(ClaudeAumid, item.Match.AppUserModelId);
        Assert.Equal(ClaudeOldPath, existing.Location);
    }

    [Fact]
    public void Recapture_PackagedDesktopApp_NormalExePathUnchanged()
    {
        var existing = new WorkspaceItem { Type = TargetType.Application, Location = NotepadPath };
        var window = Win(3, ClaudeNewPath, "Chrome_WidgetWin_1", aumid: ClaudeAumid);

        var item = Assert.Single(
            LayoutCapture.Apply([existing], [new CaptureSelection(window, 0)], Monitors, NoBrowsers));

        Assert.Equal(NotepadPath, item.Location);
    }

    [Fact]
    public void Recapture_NonPackagedWindow_VersionedPathUnchanged()
    {
        var existing = new WorkspaceItem { Type = TargetType.Application, Location = ClaudeOldPath };

        var item = Assert.Single(
            LayoutCapture.Apply([existing], [new CaptureSelection(Win(3), 0)], Monitors, NoBrowsers));

        Assert.Equal(ClaudeOldPath, item.Location);
    }

    [Fact]
    public void Recapture_WebsiteKeepsUrl()
    {
        var existing = new WorkspaceItem
        {
            Type = TargetType.Website,
            Location = "https://example.com",
            Match = new WindowMatch { ProcessPath = ChromePath }
        };

        var result = LayoutCapture.Apply([existing], [new CaptureSelection(Win(1, ChromePath, "Chrome_WidgetWin_1"), 0)],
                                         Monitors, NoBrowsers);

        Assert.Equal(TargetType.Website, result[0].Type);
        Assert.Equal("https://example.com", result[0].Location);
        Assert.NotNull(result[0].Placement);
    }

    [Fact]
    public void Apply_UntickedWindowsAreNotAdded_UnmatchedItemsKeepPlacement()
    {
        var keptPlacement = new WindowPlacement { X = 42, Y = 43, Width = 500, Height = 400 };
        var unmatched = new WorkspaceItem
        {
            Type = TargetType.Folder,
            Location = @"C:\Data",
            Match = new WindowMatch { FolderPath = @"C:\Data" },
            Placement = keptPlacement
        };
        var ticked = Win(2, ChromePath, "Chrome_WidgetWin_1");

        var result = LayoutCapture.Apply([unmatched], [new CaptureSelection(ticked, null)], Monitors, NoBrowsers);

        Assert.Equal(2, result.Count);
        Assert.Equal(@"C:\Data", result[0].Location);
        Assert.Equal(42, result[0].Placement!.X);
        Assert.Equal(43, result[0].Placement!.Y);
        Assert.Equal(TargetType.Website, result[1].Type);
    }

    [Fact]
    public void Apply_NoSelections_ReturnsCopiesOfExistingItems()
    {
        var existing = new WorkspaceItem { Type = TargetType.File, Location = @"C:\a.txt", Match = new WindowMatch() };

        var result = LayoutCapture.Apply([existing], [], Monitors, NoBrowsers);

        var item = Assert.Single(result);
        Assert.NotSame(existing, item);
        Assert.Equal(@"C:\a.txt", item.Location);
    }

    [Fact]
    public void Apply_SkipsNotCapturableSelections()
    {
        var elevated = Win(3, path: "");

        var result = LayoutCapture.Apply([], [new CaptureSelection(elevated, null)], Monitors, NoBrowsers);

        Assert.Empty(result);
    }

    [Fact]
    public void Apply_TwoWindowsForOneItem_Throws()
    {
        var existing = new WorkspaceItem { Location = NotepadPath, Match = new WindowMatch { ProcessPath = NotepadPath } };

        Assert.Throws<ArgumentException>(() => LayoutCapture.Apply(
            [existing], [new CaptureSelection(Win(1), 0), new CaptureSelection(Win(2), 0)], Monitors, NoBrowsers));
    }

    [Fact]
    public void FindCandidates_ItemWithoutMatch_HasNoCandidates()
    {
        var items = new[] { new WorkspaceItem { Location = NotepadPath } };

        var candidates = LayoutCapture.FindCandidates(items, [Win(1)]);

        Assert.Empty(candidates[0]);
    }

    [Fact]
    public void FindCandidates_ListsEveryMatchingWindow()
    {
        var items = new[] { new WorkspaceItem { Match = new WindowMatch { ProcessPath = NotepadPath } } };
        var other = Win(3, ChromePath, "Chrome_WidgetWin_1");

        var candidates = LayoutCapture.FindCandidates(items, [Win(1), other, Win(2)]);

        Assert.Equal(new[] { new IntPtr(1), new IntPtr(2) }, candidates[0].Select(w => w.Handle));
    }

    [Fact]
    public void AssignDefaults_TwoIdenticalItems_GetDifferentWindows()
    {
        var match = new WindowMatch { ProcessPath = NotepadPath };
        var items = new[] { new WorkspaceItem { Match = match }, new WorkspaceItem { Match = match } };
        var candidates = LayoutCapture.FindCandidates(items, [Win(1), Win(2)]);

        var picks = LayoutCapture.AssignDefaults(candidates);

        Assert.Equal(new IntPtr(1), picks[0]!.Handle);
        Assert.Equal(new IntPtr(2), picks[1]!.Handle);
    }

    [Fact]
    public void AssignDefaults_MoreItemsThanWindows_LeavesNull()
    {
        var match = new WindowMatch { ProcessPath = NotepadPath };
        var items = new[] { new WorkspaceItem { Match = match }, new WorkspaceItem { Match = match } };

        var picks = LayoutCapture.AssignDefaults(LayoutCapture.FindCandidates(items, [Win(1)]));

        Assert.NotNull(picks[0]);
        Assert.Null(picks[1]);
    }

    [Fact]
    public void Refresh_ReadsWindowsAndMonitorsFromSource()
    {
        var capture = new LayoutCapture(new FakeSource([Win(1)], Monitors));

        capture.Refresh();

        Assert.Single(capture.Windows);
        Assert.Equal(TargetType.Application, capture.CreateItem(capture.Windows[0])!.Type);
    }

    private sealed class FakeSource(IReadOnlyList<WindowInfo> windows, IReadOnlyList<MonitorInfo> monitors) : IWindowSource
    {
        public IReadOnlyList<WindowInfo> GetTopLevelWindows() => windows;
        public IReadOnlyList<MonitorInfo> GetMonitors() => monitors;
        public IntPtr GetForegroundWindow() => IntPtr.Zero;
    }
}
