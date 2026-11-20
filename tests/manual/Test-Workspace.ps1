<#
.SYNOPSIS
    Checks that a Launchy workspace opens its windows at the saved positions.

.DESCRIPTION
    Writes a temporary workspace into %APPDATA%\Launchy\workspaces.json, runs it through the launcher
    (Alt+Q, type the name, Enter), then reads each new window's rectangle with GetWindowRect and compares
    it with the saved value (tolerance 2 px). Prints PASS or FAIL per window.

    The script never touches commands.json. It copies the real workspaces.json to
    D:\Scratchpad\Claude\launchy-workspaces\manual-test\ first and puts it back (or deletes the test file
    if there was none) when it ends, even after an error. If the script is killed before it ends, copy
    workspaces.json.bak from that folder back by hand.

    Runs:
      A. Three windows at saved positions: Notepad (empty), an Explorer folder, Notepad with a file.
         (No browser: a URL may open as a tab in a window you already have, and the script must only
         close windows it opened itself.)
      B. The same workspace again while all three windows are open. Launchy always starts new
         windows, so two more Notepad windows must appear at the saved positions, and the first three
         must not move. Explorer shows an already open folder instead of opening a second window, so no
         new Explorer window is expected. Run C uses a second folder (wst-folder-c) for the same reason.
      C. Variant: Notepad maximized on DISPLAY2 (the primary screen when there is no second screen),
         Explorer minimized.

    The script closes only the windows it found as new. Existing Notepad and Explorer windows are left
    alone. Before it types anything it checks that the launcher is visible and in front. If it is not,
    the script stops without typing.

    The script injects key presses, so do not type while it runs (about 60 seconds).

    Requirements:
      - Windows PowerShell 5.1 (powershell.exe, not pwsh), 64-bit.
      - A Launchy build that has workspaces, running, with the "Open launcher" shortcut set to Alt+Q.
      - Classic Notepad (Windows 10).

    Exit code: 0 = all checks passed, 1 = at least one check failed, 2 = could not run.

.EXAMPLE
    powershell -NoProfile -STA -ExecutionPolicy Bypass -File tests\manual\Test-Workspace.ps1
#>
param(
    [string]$ProcessName = 'Launchy',
    [string]$ScratchRoot = 'D:\Scratchpad\Claude\launchy-workspaces\manual-test',

    # Print the workspace JSON and exit. Touches no Launchy file and presses no keys.
    [switch]$DumpJson
)

$ErrorActionPreference = 'Stop'
$Tolerance = 2

Add-Type -ReferencedAssemblies System.Windows.Forms -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public class WsWindow
{
    public IntPtr Handle;
    public string Class;
    public string Title;
    public uint Pid;
}

public static class WorkspaceTest
{
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    const uint InputKeyboard = 1, KeyUp = 2, KeyUnicode = 4, WmClose = 0x0010;
    const ushort VkAlt = 0x12, VkQ = 0x51, VkReturn = 0x0D;

    public static void Init() { SetProcessDPIAware(); }

    public static List<WsWindow> VisibleWindows()
    {
        var list = new List<WsWindow>();
        EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
        {
            if (!IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(256); GetWindowText(hwnd, title, title.Capacity);
            var cls = new StringBuilder(256); GetClassName(hwnd, cls, cls.Capacity);
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            list.Add(new WsWindow { Handle = hwnd, Class = cls.ToString(), Title = title.ToString(), Pid = pid });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static int[] Rect(IntPtr h)
    {
        RECT r; GetWindowRect(h, out r);
        return new[] { r.Left, r.Top, r.Right, r.Bottom };
    }

    public static bool Maximized(IntPtr h) { return IsZoomed(h); }
    public static bool Minimized(IntPtr h) { return IsIconic(h); }
    public static bool Exists(IntPtr h) { return IsWindow(h); }
    public static bool IsForeground(IntPtr h) { return GetForegroundWindow() == h; }
    public static void Close(IntPtr h) { PostMessage(h, WmClose, IntPtr.Zero, IntPtr.Zero); }

    public static string ScreenOf(IntPtr h)
    {
        return Screen.FromHandle(h).DeviceName;
    }

    static void Key(ushort vk, bool up)
    {
        var input = new INPUT { type = InputKeyboard };
        input.u.ki.wVk = vk;
        input.u.ki.dwFlags = up ? KeyUp : 0;
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void AltQ()
    {
        Key(VkAlt, false); Key(VkQ, false); Key(VkQ, true);
        Thread.Sleep(300);
        Key(VkAlt, true);
    }

    public static void Enter() { Key(VkReturn, false); Key(VkReturn, true); }

    public static void TypeText(string text)
    {
        foreach (var ch in text)
        {
            var down = new INPUT { type = InputKeyboard };
            down.u.ki.wScan = ch; down.u.ki.dwFlags = KeyUnicode;
            var up = new INPUT { type = InputKeyboard };
            up.u.ki.wScan = ch; up.u.ki.dwFlags = KeyUnicode | KeyUp;
            SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
            Thread.Sleep(30);
        }
    }
}
'@

[WorkspaceTest]::Init()
$T = [WorkspaceTest]

$workspacesPath = Join-Path $env:APPDATA 'Launchy\workspaces.json'
$backupPath = Join-Path $ScratchRoot 'workspaces.json.bak'
$folderName = 'wst-folder'
$folderPath = Join-Path $ScratchRoot $folderName
$folderNameC = 'wst-folder-c'
$folderPathC = Join-Path $ScratchRoot $folderNameC
$noteName = 'wst-note.txt'
$notePath = Join-Path $ScratchRoot $noteName
$workspaceA = 'wst-run-a'
$workspaceC = 'wst-run-c'

$process = Get-Process $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) {
    Write-Host "Launchy isn't running (no '$ProcessName' process). Start it and run this script again." -ForegroundColor Yellow
    exit 2
}

New-Item -ItemType Directory -Force -Path $ScratchRoot, $folderPath, $folderPathC | Out-Null
if (-not (Test-Path $notePath)) { New-Item -ItemType File -Path $notePath | Out-Null }

$commandsPath = Join-Path $env:APPDATA 'Launchy\commands.json'
if (Test-Path $commandsPath) {
    $names = (Get-Content $commandsPath -Raw | ConvertFrom-Json).commands | ForEach-Object { $_.command }
    if ($names -contains $workspaceA -or $names -contains $workspaceC) {
        Write-Host "commands.json already has a command named '$workspaceA' or '$workspaceC'. Rename it and run again." -ForegroundColor Yellow
        exit 2
    }
}

Add-Type -AssemblyName System.Windows.Forms
$screens = [System.Windows.Forms.Screen]::AllScreens
$primary = $screens | Where-Object Primary | Select-Object -First 1
$display2 = $screens | Where-Object { $_.DeviceName -eq '\\.\DISPLAY2' } | Select-Object -First 1
$hasDisplay2 = $null -ne $display2
if (-not $hasDisplay2) { $display2 = $primary }

function New-Placement($screen, [string]$state, [int]$x, [int]$y, [int]$w, [int]$h) {
    # Normal* values are relative to the primary work area, the same as rcNormalPosition.
    $wa = $primary.WorkingArea
    [ordered]@{
        state = $state; x = $x; y = $y; width = $w; height = $h
        normalX = $x - $wa.X; normalY = $y - $wa.Y; normalWidth = $w; normalHeight = $h
        monitorDevice = $screen.DeviceName
        monX = $screen.Bounds.X; monY = $screen.Bounds.Y; monWidth = $screen.Bounds.Width; monHeight = $screen.Bounds.Height
        workX = $screen.WorkingArea.X; workY = $screen.WorkingArea.Y
        workWidth = $screen.WorkingArea.Width; workHeight = $screen.WorkingArea.Height
        dpi = 96
    }
}

function New-Item-Notepad($placement, [string]$parameters, [string]$title) {
    $item = [ordered]@{
        type = 'Application'; location = 'notepad.exe'
        match = [ordered]@{ className = 'Notepad'; titleContains = $title }
        placement = $placement; timeoutSeconds = 20
    }
    if ($parameters) { $item.parameters = $parameters }
    $item
}

function New-Item-Folder($placement, [string]$path) {
    [ordered]@{
        type = 'Folder'; location = $path
        match = [ordered]@{ folderPath = $path }
        placement = $placement; timeoutSeconds = 20
    }
}

$wa = $primary.WorkingArea
$placeNotepad = New-Placement $primary 'Normal' ($wa.X + 100) ($wa.Y + 100) 700 450
$placeFolder = New-Placement $primary 'Normal' ($wa.X + 900) ($wa.Y + 150) 800 500
$placeNote = New-Placement $primary 'Normal' ($wa.X + 200) ($wa.Y + 700) 600 400
$d2 = $display2.WorkingArea
$placeMax = New-Placement $display2 'Maximized' ($d2.X + 100) ($d2.Y + 100) 800 600
$placeMin = New-Placement $primary 'Minimized' ($wa.X + 300) ($wa.Y + 300) 800 500

$workspaces = @(
    [ordered]@{ name = $workspaceA; items = @(
        (New-Item-Notepad $placeNotepad $null 'Untitled'),
        (New-Item-Folder $placeFolder $folderPath),
        (New-Item-Notepad $placeNote "`"$notePath`"" $noteName)) },
    [ordered]@{ name = $workspaceC; items = @(
        (New-Item-Notepad $placeMax $null 'Untitled'),
        (New-Item-Folder $placeMin $folderPathC)) }
)

$workspaceJson = ConvertTo-Json -InputObject ([ordered]@{ version = 1; workspaces = $workspaces }) -Depth 8
if ($DumpJson) {
    $workspaceJson
    exit 0
}

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$check, [bool]$pass, [string]$detail) {
    $results.Add([pscustomobject]@{ Check = $check; Pass = $pass; Detail = $detail })
}

# Test windows: Notepad windows ('Notepad' class) and Explorer windows for the test folder.
function Get-TestWindows {
    $T::VisibleWindows() | Where-Object {
        $_.Class -eq 'Notepad' -or ($_.Class -eq 'CabinetWClass' -and ($_.Title -eq $folderName -or $_.Title -eq $folderNameC))
    }
}

function Get-NewWindows($before, [string]$kind) {
    $known = @($before | ForEach-Object { $_.Handle })
    # No switch inside the filter: switch rebinds $_ to its own input.
    Get-TestWindows | Where-Object { $known -notcontains $_.Handle } | Where-Object {
        if ($kind -eq 'blank') { $_.Class -eq 'Notepad' -and $_.Title -like 'Untitled*' }
        elseif ($kind -eq 'note') { $_.Class -eq 'Notepad' -and $_.Title -like "$noteName*" }
        else { $_.Class -eq 'CabinetWClass' }
    } | Sort-Object { $_.Handle.ToInt64() }
}

function Test-NearRect($window, $placement) {
    $r = $T::Rect($window.Handle)
    $expected = @($placement.x, $placement.y, ($placement.x + $placement.width), ($placement.y + $placement.height))
    for ($i = 0; $i -lt 4; $i++) {
        if ([math]::Abs($r[$i] - $expected[$i]) -gt $Tolerance) { return $false }
    }
    $true
}

function Format-Rect($window) { '(' + (($T::Rect($window.Handle)) -join ', ') + ')' }

function Show-LauncherAndRun([string]$name) {
    $T::AltQ()
    $launcher = $null
    for ($i = 0; $i -lt 20 -and -not $launcher; $i++) {
        Start-Sleep -Milliseconds 100
        $launcher = $T::VisibleWindows() | Where-Object { $_.Pid -eq $process.Id -and $_.Title -eq 'Launchy' } | Select-Object -First 1
    }
    Start-Sleep -Milliseconds 400
    if (-not $launcher -or -not $T::IsForeground($launcher.Handle)) {
        throw 'The launcher is not visible and in front after Alt+Q. Nothing was typed.'
    }
    $T::TypeText($name)
    Start-Sleep -Milliseconds 300
    $T::Enter()
}

# Waits until every expected window is found and placed, or the timeout runs out.
function Wait-Windows([scriptblock]$collect, [int]$timeoutSeconds = 30) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $found = & $collect
        if ($found.Done) { break }
    } while ((Get-Date) -lt $deadline)
    Start-Sleep -Milliseconds 1000
    & $collect
}

$opened = New-Object System.Collections.Generic.List[IntPtr]
function Remember($windows) { foreach ($w in @($windows)) { if ($w) { $opened.Add($w.Handle) } } }

$hadBackup = Test-Path $workspacesPath
try {
    if ($hadBackup) { Copy-Item $workspacesPath $backupPath -Force }
    [System.IO.File]::WriteAllText($workspacesPath, $workspaceJson,(New-Object System.Text.UTF8Encoding $false))

    # A. Three windows at saved positions ------------------------------------------------------------
    $before = @(Get-TestWindows)
    Show-LauncherAndRun $workspaceA
    $collectA = {
        $blank = @(Get-NewWindows $before 'blank'); $folder = @(Get-NewWindows $before 'folder'); $note = @(Get-NewWindows $before 'note')
        [pscustomobject]@{ Blank = $blank; Folder = $folder; Note = $note
            Done = ($blank.Count -ge 1 -and $folder.Count -ge 1 -and $note.Count -ge 1 -and
                    (Test-NearRect $blank[0] $placeNotepad) -and (Test-NearRect $folder[0] $placeFolder) -and (Test-NearRect $note[0] $placeNote)) }
    }
    $a = Wait-Windows $collectA
    Remember ($a.Blank + $a.Folder + $a.Note)
    foreach ($case in @(
            @('A1. Notepad', $a.Blank, $placeNotepad), @('A2. Explorer folder', $a.Folder, $placeFolder), @('A3. Notepad with file', $a.Note, $placeNote))) {
        $w = @($case[1]) | Select-Object -First 1
        if (-not $w) { Add-Result $case[0] $false 'window did not appear'; continue }
        Add-Result $case[0] (Test-NearRect $w $case[2]) ("at $(Format-Rect $w), saved ($($case[2].x), $($case[2].y), $($case[2].x + $case[2].width), $($case[2].y + $case[2].height))")
    }
    $firstRects = @($a.Blank[0], $a.Folder[0], $a.Note[0]) | ForEach-Object { if ($_) { Format-Rect $_ } }

    # B. Run again with all three open ---------------------------------------------------------------
    $before = @(Get-TestWindows)
    Show-LauncherAndRun $workspaceA
    $collectB = {
        $blank = @(Get-NewWindows $before 'blank'); $note = @(Get-NewWindows $before 'note')
        [pscustomobject]@{ Blank = $blank; Note = $note
            Done = ($blank.Count -ge 1 -and $note.Count -ge 1 -and
                    (Test-NearRect $blank[0] $placeNotepad) -and (Test-NearRect $note[0] $placeNote)) }
    }
    $b = Wait-Windows $collectB
    Remember ($b.Blank + $b.Note)
    $newCount = @($b.Blank).Count + @($b.Note).Count
    Add-Result 'B1. Second run starts 2 new Notepad windows' ($newCount -eq 2) "new windows: $newCount"
    Add-Result 'B2. New windows at saved positions' $b.Done "blank: $(@($b.Blank).Count), note: $(@($b.Note).Count)"
    $nowRects = @($a.Blank[0], $a.Folder[0], $a.Note[0]) | ForEach-Object { if ($_ -and $T::Exists($_.Handle)) { Format-Rect $_ } }
    Add-Result 'B3. First three windows did not move' (($firstRects -join ';') -eq ($nowRects -join ';')) "before: $($firstRects -join ' ') after: $($nowRects -join ' ')"

    # C. Maximized on DISPLAY2, minimized -------------------------------------------------------------
    $before = @(Get-TestWindows)
    Show-LauncherAndRun $workspaceC
    $collectC = {
        $blank = @(Get-NewWindows $before 'blank'); $folder = @(Get-NewWindows $before 'folder')
        [pscustomobject]@{ Blank = $blank; Folder = $folder
            Done = ($blank.Count -ge 1 -and $folder.Count -ge 1 -and $T::Maximized($blank[0].Handle) -and $T::Minimized($folder[0].Handle)) }
    }
    $c = Wait-Windows $collectC
    Remember ($c.Blank + $c.Folder)
    $where = if ($hasDisplay2) { 'DISPLAY2' } else { 'DISPLAY1 (no second screen found)' }
    $max = @($c.Blank) | Select-Object -First 1
    if ($max) {
        $screenName = $T::ScreenOf($max.Handle)
        Add-Result "C1. Notepad maximized on $where" ($T::Maximized($max.Handle) -and $screenName -eq $display2.DeviceName) "maximized: $($T::Maximized($max.Handle)), screen: $screenName"
    } else { Add-Result "C1. Notepad maximized on $where" $false 'window did not appear' }
    $min = @($c.Folder) | Select-Object -First 1
    if ($min) { Add-Result 'C2. Explorer minimized' ($T::Minimized($min.Handle)) "minimized: $($T::Minimized($min.Handle))" }
    else { Add-Result 'C2. Explorer minimized' $false 'window did not appear' }
}
catch {
    Add-Result 'Test run' $false $_.Exception.Message
}
finally {
    foreach ($handle in $opened) { if ($T::Exists($handle)) { $T::Close($handle) } }
    Start-Sleep -Milliseconds 800
    if ($hadBackup) { Copy-Item $backupPath $workspacesPath -Force }
    elseif (Test-Path $workspacesPath) { Remove-Item $workspacesPath -Force }
    $left = @($opened | Where-Object { $T::Exists($_) }).Count
    if ($left -gt 0) { Write-Host "$left test window(s) did not close. Close them by hand." -ForegroundColor Yellow }
}

$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$passed = @($results | Where-Object Pass).Count
Write-Host ("{0}/{1} checks passed" -f $passed, $results.Count)
if ($passed -eq $results.Count -and $results.Count -gt 0) {
    Write-Host 'RESULT: PASS' -ForegroundColor Green
    exit 0
}
Write-Host 'RESULT: FAIL' -ForegroundColor Red
exit 1
