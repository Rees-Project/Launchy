<#
.SYNOPSIS
    Checks Launchy's notification-area ("tray") icon: one icon only, even after Launchy is killed and restarted.

.DESCRIPTION
    Regression test for duplicate "L" tray icons. Before the fix, every time Launchy was stopped without a
    normal exit (for example the alpha update script's Stop-Process -Force, or a crash), Windows kept its
    icon, and each restart added another one beside it.

    The script reads Explorer's tray toolbars (the visible area and the ^ overflow) to see every Launchy
    icon and whether the program behind it is still running. An icon whose window no longer exists is a
    "ghost". It also simulates tray clicks by posting the same messages Explorer sends.

    Checks:
      1. Exactly one live Launchy icon.
      2. Left-clicking the icon opens the launcher.
      3. Right-clicking the icon opens the menu.
      4. After an Explorer restart notification, there is still exactly one icon.
      5. Hard-killing and restarting Launchy 5 times adds no icons.
      6. Exit from the tray menu closes Launchy and removes its icon. Launchy is started again afterwards.

    Ghosts left over from builds before the fix can't be replaced by the new code. They don't fail the
    test; the script only checks that their number doesn't grow. Hovering the mouse over them removes them.

    The script presses keys (Esc, Up, Enter) and stops/starts Launchy, so don't type while it runs
    (about 45 seconds).

    Requirements:
      - Windows PowerShell 5.1 (powershell.exe, not pwsh), 64-bit.
      - Windows 10 taskbar (reads the classic tray toolbars).
      - Launchy running, with the "Open launcher" shortcut set to Alt+Q (the default).

    Exit code: 0 = all checks passed, 1 = at least one check failed, 2 = could not run.

.EXAMPLE
    powershell -NoProfile -STA -ExecutionPolicy Bypass -File tests\manual\Test-TrayIcon.ps1
#>
param(
    [string]$ProcessName = 'Launchy'
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public class TrayEntry
{
    public string Area;
    public string Text;
    public IntPtr Owner;
    public uint OwnerPid;
    public bool Ghost;
}

public static class TrayTest
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr addr, IntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p, IntPtr addr, IntPtr size, uint type);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; public uint pad1, pad2; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public KEYBDINPUT ki; }
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);

    const uint TB_BUTTONCOUNT = 0x418, TB_GETBUTTON = 0x417, TB_GETBUTTONTEXTW = 0x44B;
    const uint TrayCallback = 0x8000 + 1;   // must match TrayIcon.CallbackMessage
    const int WmLButtonUp = 0x0202, WmRButtonUp = 0x0205;

    static IntPtr Toolbar(string area)
    {
        if (area == "visible")
        {
            var tray = FindWindow("Shell_TrayWnd", null);
            var notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            var pager = FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
            return FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null);
        }
        var overflow = FindWindow("NotifyIconOverflowWindow", null);
        return FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null);
    }

    /// <summary>Every tray icon whose tooltip starts with textPrefix, read from Explorer's tray toolbars.</summary>
    public static List<TrayEntry> Icons(string textPrefix)
    {
        var result = new List<TrayEntry>();
        foreach (var area in new[] { "visible", "overflow" })
        {
            var tb = Toolbar(area);
            if (tb == IntPtr.Zero) continue;
            uint explorerPid; GetWindowThreadProcessId(tb, out explorerPid);
            var proc = OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, explorerPid);
            var remote = VirtualAllocEx(proc, IntPtr.Zero, (IntPtr)4096, 0x1000, 0x04);
            int count = (int)SendMessage(tb, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
            var buf = new byte[1024]; IntPtr read;
            for (int i = 0; i < count; i++)
            {
                SendMessage(tb, TB_GETBUTTON, (IntPtr)i, remote);
                ReadProcessMemory(proc, remote, buf, (IntPtr)32, out read);   // 64-bit TBBUTTON
                int idCommand = BitConverter.ToInt32(buf, 4);
                var trayData = (IntPtr)BitConverter.ToInt64(buf, 16);
                ReadProcessMemory(proc, trayData, buf, (IntPtr)16, out read);
                var owner = (IntPtr)BitConverter.ToInt64(buf, 0);
                int len = (int)SendMessage(tb, TB_GETBUTTONTEXTW, (IntPtr)idCommand, remote);
                string text = "";
                if (len > 0)
                {
                    int bytes = Math.Min(len * 2, 1000);
                    ReadProcessMemory(proc, remote, buf, (IntPtr)bytes, out read);
                    text = Encoding.Unicode.GetString(buf, 0, bytes);
                }
                if (!text.StartsWith(textPrefix)) continue;
                var entry = new TrayEntry { Area = area, Text = text, Owner = owner, Ghost = !IsWindow(owner) };
                if (!entry.Ghost) GetWindowThreadProcessId(owner, out entry.OwnerPid);
                result.Add(entry);
            }
            VirtualFreeEx(proc, remote, IntPtr.Zero, 0x8000);
            CloseHandle(proc);
        }
        return result;
    }

    /// <summary>First top-level window of the process whose title or class starts with the given text.</summary>
    public static IntPtr FindProcessWindow(int pid, string titlePrefix, string classPrefix, bool mustBeVisible)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
        {
            uint wpid; GetWindowThreadProcessId(hwnd, out wpid);
            if (found != IntPtr.Zero || wpid != (uint)pid) return true;
            if (mustBeVisible && !IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(128); GetWindowText(hwnd, title, title.Capacity);
            var cls = new StringBuilder(256); GetClassName(hwnd, cls, cls.Capacity);
            if ((titlePrefix == null || title.ToString().StartsWith(titlePrefix)) &&
                (classPrefix == null || cls.ToString().StartsWith(classPrefix)))
                found = hwnd;
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindLauncher(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
        {
            uint wpid; GetWindowThreadProcessId(hwnd, out wpid);
            var title = new StringBuilder(64); GetWindowText(hwnd, title, title.Capacity);
            if (found == IntPtr.Zero && wpid == (uint)pid && title.ToString() == "Launchy" && IsWindowVisible(hwnd)) found = hwnd;
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);

    /// <summary>
    /// Explorer lets the clicked icon's program take the foreground. Do the same: a no-op input event
    /// gives this process that right, and it passes it on to Launchy.
    /// </summary>
    static void GrantForeground(IntPtr trayWindow)
    {
        var noop = new INPUT { type = 0 };   // all-zero mouse input: no movement, no buttons
        SendInput(1, new[] { noop }, Marshal.SizeOf(typeof(INPUT)));
        uint pid; GetWindowThreadProcessId(trayWindow, out pid);
        AllowSetForegroundWindow(pid);
    }

    public static void LeftClick(IntPtr trayWindow) { GrantForeground(trayWindow); PostMessage(trayWindow, TrayCallback, IntPtr.Zero, (IntPtr)WmLButtonUp); }
    public static void RightClick(IntPtr trayWindow) { GrantForeground(trayWindow); PostMessage(trayWindow, TrayCallback, IntPtr.Zero, (IntPtr)WmRButtonUp); }
    public static void ExplorerRestarted(IntPtr trayWindow) { PostMessage(trayWindow, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero); }

    public static void Key(ushort vk)
    {
        var down = new INPUT { type = 1 }; down.ki.wVk = vk;
        var up = new INPUT { type = 1 }; up.ki.wVk = vk; up.ki.dwFlags = 2;
        SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
    }
}
'@

$T = [TrayTest]
$VkEscape = 0x1B; $VkUp = 0x26; $VkReturn = 0x0D

$process = Get-Process $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) {
    Write-Host "Launchy isn't running (no '$ProcessName' process). Start it and run this script again." -ForegroundColor Yellow
    exit 2
}
$exePath = $process.Path

function Get-State {
    $icons = $T::Icons('Launchy')
    $proc = Get-Process $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    [pscustomobject]@{
        Live    = @($icons | Where-Object { -not $_.Ghost }).Count
        Ghosts  = @($icons | Where-Object { $_.Ghost }).Count
        Process = $proc
        Tray    = if ($proc) { $T::FindProcessWindow($proc.Id, 'Launchy tray', $null, $false) } else { [IntPtr]::Zero }
    }
}

# Explorer adds an icon a moment after Launchy registers it, so wait (up to 10 s) for a live one.
function Wait-LiveIcon {
    for ($i = 0; $i -lt 20; $i++) {
        $proc = Get-Process $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($proc -and @($T::Icons('Launchy') | Where-Object { $_.OwnerPid -eq $proc.Id }).Count -gt 0) { break }
        Start-Sleep -Milliseconds 500
    }
    Start-Sleep -Milliseconds 500
}

function Start-Launchy {
    Start-Process $exePath
    Wait-LiveIcon
}

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$check, [bool]$pass, [string]$detail) {
    $results.Add([pscustomobject]@{ Check = $check; Pass = $pass; Detail = $detail })
}

# 1. One live icon --------------------------------------------------------------------------------------
Wait-LiveIcon
$state = Get-State
$baselineGhosts = $state.Ghosts
Add-Result '1. One live icon' ($state.Live -eq 1) "live icons: $($state.Live), ghosts from older builds: $baselineGhosts"
# Builds before the fix used WinForms' NotifyIcon and have no "Launchy tray" window, so the click checks
# can't run against them. Check 5 still runs, which is the one that shows the old bug.
$hasTrayWindow = $state.Tray -ne [IntPtr]::Zero
$noTrayWindow = 'no "Launchy tray" window (build from before the fix)'

if ($hasTrayWindow) {
    # 2. Left click opens the launcher ------------------------------------------------------------------
    $T::LeftClick($state.Tray); Start-Sleep -Milliseconds 800
    $opened = $T::FindLauncher($state.Process.Id) -ne [IntPtr]::Zero
    $T::Key($VkEscape); Start-Sleep -Milliseconds 400
    Add-Result '2. Left click opens launcher' $opened "launcher shown: $opened"

    # 3. Right click opens the menu ----------------------------------------------------------------------
    $T::RightClick($state.Tray); Start-Sleep -Milliseconds 800
    $menuShown = $T::FindProcessWindow($state.Process.Id, $null, 'WindowsForms10.Window', $true) -ne [IntPtr]::Zero
    $T::Key($VkEscape); Start-Sleep -Milliseconds 400
    $menuClosed = $T::FindProcessWindow($state.Process.Id, $null, 'WindowsForms10.Window', $true) -eq [IntPtr]::Zero
    Add-Result '3. Right click opens menu' ($menuShown -and $menuClosed) "menu shown: $menuShown, closed by Esc: $menuClosed"

    # 4. Explorer restart notification keeps one icon ----------------------------------------------------
    $T::ExplorerRestarted($state.Tray); Start-Sleep -Seconds 2
    $state = Get-State
    Add-Result '4. Explorer restart keeps one icon' ($state.Live -eq 1 -and $state.Ghosts -eq $baselineGhosts) "live: $($state.Live), ghosts: $($state.Ghosts)"
} else {
    Add-Result '2. Left click opens launcher' $false $noTrayWindow
    Add-Result '3. Right click opens menu' $false $noTrayWindow
    Add-Result '4. Explorer restart keeps one icon' $false $noTrayWindow
}

# 5. Hard kill + restart, 5 times -----------------------------------------------------------------------
for ($cycle = 1; $cycle -le 5; $cycle++) {
    Stop-Process -Id $state.Process.Id -Force
    Start-Sleep -Milliseconds 700
    Start-Launchy
    $state = Get-State
    Add-Result "5. Kill + restart #$cycle" ($state.Live -eq 1 -and $state.Ghosts -eq $baselineGhosts) "live: $($state.Live), ghosts: $($state.Ghosts)"
}

# 6. Exit from the tray menu removes the icon ------------------------------------------------------------
if ($hasTrayWindow) {
    $T::RightClick($state.Tray); Start-Sleep -Milliseconds 800
    $T::Key($VkUp); Start-Sleep -Milliseconds 200      # Up from no selection wraps to the last item, Exit
    $T::Key($VkReturn)
    $exited = $state.Process.WaitForExit(5000)
    Start-Sleep -Seconds 2
    $after = Get-State
    Add-Result '6. Exit removes icon' ($exited -and $after.Live -eq 0 -and $after.Ghosts -eq $baselineGhosts) "exited: $exited, live: $($after.Live), ghosts: $($after.Ghosts)"
    if (-not $after.Process) { Start-Launchy }
} else {
    Add-Result '6. Exit removes icon' $false $noTrayWindow
}

$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$passed = @($results | Where-Object Pass).Count
Write-Host ("{0}/{1} checks passed" -f $passed, $results.Count)
if ($passed -eq $results.Count) {
    Write-Host 'RESULT: PASS' -ForegroundColor Green
    exit 0
}
Write-Host 'RESULT: FAIL' -ForegroundColor Red
exit 1
