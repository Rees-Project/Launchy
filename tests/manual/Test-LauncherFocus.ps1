<#
.SYNOPSIS
    Checks that the launcher comes up in front on Alt+Q and hides again on a click outside or Esc.

.DESCRIPTION
    Regression test for two launcher focus bugs:
      1. The launcher flashed on screen and disappeared straight away after Alt+Q.
      2. After a summon, clicking outside the launcher did not hide it.

    The script drives the real desktop with injected key presses and mouse clicks, so do not touch
    the mouse or keyboard while it runs (about 60 seconds). It opens two small test windows in the
    top-left corner of the primary screen:
      - "Previous app" plays the app you were in before pressing Alt+Q. It runs in a separate
        powershell.exe process, because bug 2 only happens when the app in front belongs to a
        different process from the one sending the key presses.
      - "Other window" is a second place to click outside the launcher.

    Requirements:
      - Windows PowerShell 5.1 (powershell.exe, not pwsh).
      - Launchy running, with the "Open launcher" shortcut set to Alt+Q (the default).

    Exit code: 0 = all checks passed, 1 = at least one check failed, 2 = could not run.

.EXAMPLE
    powershell -NoProfile -STA -ExecutionPolicy Bypass -File tests\manual\Test-LauncherFocus.ps1
#>
param(
    # Process that owns the launcher window. The alpha and release builds are both Launchy.exe.
    [string]$ProcessName = 'Launchy',

    # Internal: run as the separate "Previous app" window process.
    [switch]$PreviousAppWindow
)

$ErrorActionPreference = 'Stop'
$previousAppTitle = 'Launchy test - Previous app'

if ($PreviousAppWindow) {
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    $form = New-Object System.Windows.Forms.Form
    $form.Text = $previousAppTitle
    $form.StartPosition = 'Manual'
    $form.Location = New-Object System.Drawing.Point(40, 40)
    $form.Size = New-Object System.Drawing.Size(380, 220)
    $form.BackColor = [System.Drawing.Color]::LightSteelBlue
    $form.TopMost = $true
    # This process is started hidden, and Windows applies that to the first window shown. Spend it here.
    $form.Show(); $form.Hide()
    [System.Windows.Forms.Application]::Run($form)
    exit 0
}

Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public static class LauncherFocusTest
{
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    const uint InputMouse = 0, InputKeyboard = 1, KeyUp = 2, LeftDown = 2, LeftUp = 4;
    const ushort VkAlt = 0x12, VkQ = 0x51, VkEscape = 0x1B;

    static POINT savedCursor;
    public static string ProcessName = "Launchy";

    public static void Init()
    {
        // Work in physical pixels so window rectangles and cursor positions agree on high-DPI screens.
        SetProcessDPIAware();
        GetCursorPos(out savedCursor);
    }

    public static void RestoreCursor() { SetCursorPos(savedCursor.X, savedCursor.Y); }

    public static Form MakeWindow(string title, int x, int y, Color color)
    {
        var form = new Form();
        form.Text = title;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(x, y);
        form.Size = new Size(380, 220);
        form.BackColor = color;
        form.TopMost = true;
        form.ShowInTaskbar = false;
        form.Show();
        return form;
    }

    /// <summary>Waits while keeping this process's test window responsive.</summary>
    public static void Pump(int ms)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); }
    }

    public static bool IsForeground(IntPtr hwnd) { return GetForegroundWindow() == hwnd; }

    /// <summary>A visible top-level window with this exact title in one of the given processes, or IntPtr.Zero.</summary>
    public static IntPtr FindWindow(string processName, string title)
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(processName)) pids.Add((uint)p.Id);
        return FindWindowIn(pids, title);
    }

    public static IntPtr FindWindowInProcess(int pid, string title)
    {
        var pids = new HashSet<uint>();
        pids.Add((uint)pid);
        return FindWindowIn(pids, title);
    }

    static IntPtr FindWindowIn(HashSet<uint> pids, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            var text = new StringBuilder(128);
            GetWindowText(hwnd, text, text.Capacity);
            if (found == IntPtr.Zero && pids.Contains(pid) && text.ToString() == title && IsWindowVisible(hwnd))
                found = hwnd;
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>The visible launcher window, or IntPtr.Zero when the launcher is hidden.</summary>
    public static IntPtr FindLauncher() { return FindWindow(ProcessName, "Launchy"); }

    static void Send(INPUT input) { SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))); }

    static void Key(ushort vk, bool up)
    {
        var input = new INPUT { type = InputKeyboard };
        input.u.ki.wVk = vk;
        input.u.ki.dwFlags = up ? KeyUp : 0;
        Send(input);
    }

    static void Mouse(uint flags)
    {
        var input = new INPUT { type = InputMouse };
        input.u.mi.dwFlags = flags;
        Send(input);
    }

    /// <summary>Alt down, Q down, Q up, then Alt held for altHoldMs before release, like a person pressing Alt+Q.</summary>
    public static void PressHotkey(int altHoldMs)
    {
        Key(VkAlt, false); Pump(20);
        Key(VkQ, false); Pump(30);
        Key(VkQ, true);
        Pump(altHoldMs);
        Key(VkAlt, true);
    }

    public static void PressEscape() { Key(VkEscape, false); Key(VkEscape, true); }

    /// <summary>Left-clicks the middle of a window.</summary>
    public static void Click(IntPtr hwnd)
    {
        RECT r;
        GetWindowRect(hwnd, out r);
        SetCursorPos((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        Pump(30);
        Mouse(LeftDown); Pump(20); Mouse(LeftUp);
    }
}
'@

$T = [LauncherFocusTest]
$T::ProcessName = $ProcessName

if (-not (Get-Process $ProcessName -ErrorAction SilentlyContinue)) {
    Write-Host "Launchy isn't running (no '$ProcessName' process). Start it and run this script again." -ForegroundColor Yellow
    exit 2
}

function Close-LauncherIfOpen {
    if ($T::FindLauncher() -eq [IntPtr]::Zero) { return $true }
    $T::PressHotkey(50); $T::Pump(400)
    return ($T::FindLauncher() -eq [IntPtr]::Zero)
}

function Format-Check([bool]$ok) { if ($ok) { 'yes' } else { 'NO' } }

$T::Init()

$helper = Start-Process powershell.exe -PassThru -WindowStyle Hidden -ArgumentList @(
    '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-PreviousAppWindow')
$otherWindowForm = $T::MakeWindow('Launchy test - Other window', 40, 300, [System.Drawing.Color]::Khaki)

$exitCode = 0
try {
    $previousApp = [IntPtr]::Zero
    for ($wait = 0; $wait -lt 100 -and $previousApp -eq [IntPtr]::Zero; $wait++) {
        $T::Pump(100)
        $previousApp = $T::FindWindowInProcess($helper.Id, $previousAppTitle)
    }
    if ($previousApp -eq [IntPtr]::Zero) {
        Write-Host 'The "Previous app" test window did not open.' -ForegroundColor Yellow
        exit 2
    }
    $otherWindow = $otherWindowForm.Handle
    $targets = @{ $previousApp = 'Previous app'; $otherWindow = 'Other window' }

    if (-not (Close-LauncherIfOpen)) {
        Write-Host 'The launcher is open and could not be closed with Alt+Q. Close it and run again.' -ForegroundColor Yellow
        exit 2
    }

    # --- Test 1: Alt+Q shows the launcher in front, a click outside hides it -----------------------------
    # Alt hold times cover a quick tap through a slow release; the original bugs depended on this timing.
    # Even runs click back into the previous app, odd runs click a different window.
    $altHolds = 40, 60, 120, 150, 180, 200, 220, 250, 280, 320, 350, 400, 450, 500, 600, 900
    $test1 = for ($i = 0; $i -lt $altHolds.Count; $i++) {
        $clickTarget = if ($i % 2 -eq 0) { $previousApp } else { $otherWindow }

        $T::Click($previousApp); $T::Pump(300)
        $setupOk = $T::IsForeground($previousApp)

        $T::PressHotkey($altHolds[$i]); $T::Pump(700)
        $launcher = $T::FindLauncher()
        $shown = $launcher -ne [IntPtr]::Zero
        $inFront = $shown -and $T::IsForeground($launcher)

        $T::Click($clickTarget); $T::Pump(500)
        $clickLanded = $T::IsForeground($clickTarget)
        $hidden = $T::FindLauncher() -eq [IntPtr]::Zero

        if (-not $hidden -and -not (Close-LauncherIfOpen)) {
            Write-Host 'The launcher is stuck open; stopping.' -ForegroundColor Red
            exit 1
        }

        [pscustomobject]@{
            Run               = $i + 1
            'Alt held (ms)'   = $altHolds[$i]
            'Clicked'         = $targets[$clickTarget]
            'Setup ok'        = Format-Check $setupOk
            'Shown'           = Format-Check $shown
            'In front'        = Format-Check $inFront
            'Click landed'    = Format-Check $clickLanded
            'Hidden by click' = Format-Check $hidden
            Pass              = $setupOk -and $shown -and $inFront -and $clickLanded -and $hidden
        }
    }

    # --- Test 2: keyboard focus is in the text box, so Esc closes the launcher ------------------------------
    $test2 = for ($i = 0; $i -lt 8; $i++) {
        $T::Click($previousApp); $T::Pump(300)
        $T::PressHotkey(150 + 60 * $i); $T::Pump(700)
        $shown = $T::FindLauncher() -ne [IntPtr]::Zero
        $T::PressEscape(); $T::Pump(300)
        $closed = $T::FindLauncher() -eq [IntPtr]::Zero
        if (-not $closed) { [void](Close-LauncherIfOpen) }

        [pscustomobject]@{
            Run             = $i + 1
            'Alt held (ms)' = 150 + 60 * $i
            'Shown'         = Format-Check $shown
            'Closed by Esc' = Format-Check $closed
            Pass            = $shown -and $closed
        }
    }

    Write-Host "`nTest 1 - Alt+Q shows the launcher in front; a click outside hides it" -ForegroundColor Cyan
    $test1 | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    Write-Host "Test 2 - Esc closes the launcher (keyboard focus is in the text box)" -ForegroundColor Cyan
    $test2 | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

    $passed1 = @($test1 | Where-Object Pass).Count
    $passed2 = @($test2 | Where-Object Pass).Count
    Write-Host ("Test 1: {0}/{1} passed" -f $passed1, $test1.Count)
    Write-Host ("Test 2: {0}/{1} passed" -f $passed2, $test2.Count)

    if ($passed1 -eq $test1.Count -and $passed2 -eq $test2.Count) {
        Write-Host 'RESULT: PASS' -ForegroundColor Green
    } else {
        Write-Host 'RESULT: FAIL' -ForegroundColor Red
        $exitCode = 1
    }
}
finally {
    $otherWindowForm.Close()
    if (-not $helper.HasExited) { $helper.Kill() }
    $T::RestoreCursor()
}
exit $exitCode
