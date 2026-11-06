# Manual regression tests

These scripts check behaviour that unit tests can't reach: window focus and the notification-area
("tray") icon. They drive the real Windows desktop, so run them on a machine you're sitting at and
don't touch the mouse or keyboard while they run.

| Script | What it checks | Time |
|---|---|---|
| `Test-LauncherFocus.ps1` | Alt+Q shows the launcher in front; a click outside or Esc hides it | ~60 s |
| `Test-TrayIcon.ps1` | Exactly one tray icon, even after Launchy is killed and restarted; tray clicks and Exit work | ~45 s |

Both print a table and end with `RESULT: PASS` (exit code 0) or `RESULT: FAIL` (exit code 1).
Exit code 2 means the test couldn't run, for example because Launchy wasn't running.

## Bugs these tests cover

1. **Launcher flashes and disappears.** After Alt+Q, the launcher showed for an instant and hid
   again, so Alt+Q had to be pressed twice. It happened some of the time.
2. **Launcher won't hide on a click outside.** After a summon, clicking another window left the
   launcher on screen. It happened most often on the second summon in a row.
3. **Duplicate "L" tray icons.** Each time Launchy was stopped without a normal exit (the alpha
   update script's `Stop-Process -Force`, Task Manager, a crash), its icon stayed behind, and each
   restart added another one beside it.

Fixes (branch `fix-launcher-flash`):

- Bugs 1 and 2: `LauncherWindow.xaml` sets `ShowActivated="False"`. Before, `Show()` made the window
  active inside Launchy while another app still had the foreground, and `ForegroundHelper.ForceForeground`
  then pulled it to the front. Windows was left with conflicting ideas of which window was in front,
  so the click that should deactivate the launcher produced no deactivation message at all.
  Now `Show()` doesn't activate the window, and activation happens in one step.
- Bug 1 also: for 0.2 s after showing, and until Alt/Ctrl/Shift/Win have been released for 0.1 s,
  the launcher ignores focus loss. Then it checks with Windows which window is really in front and
  takes the foreground back if needed (`LauncherWindow.xaml.cs`, `GraceTimer_Tick`/`EndGracePeriod`).
- Bug 3: `Services/TrayIcon.cs` registers the icon with a fixed GUID through `Shell_NotifyIcon`
  instead of WinForms' `NotifyIcon`. On startup it deletes any icon with that GUID left by a dead
  process, then adds its own. Windows ties a tray GUID to one exe path, so the GUID is derived from
  the exe path; each install location (alpha, release, dev build) gets its own stable GUID.

## Requirements

- Windows 10. The tray test reads the Windows 10 taskbar's tray toolbars.
- Windows PowerShell 5.1, 64-bit (`powershell.exe`, not `pwsh`).
- Launchy running, with the "Open launcher" keybind set to **Alt+Q** (the default).
- Only one copy of Launchy running.

## How to run

Run these from the repo root.

1. Build and start the build you want to test. For the alpha:
   ```powershell
   & C:\Users\Rees1\Documents\Projects\Launchy-Alpha\Update-Alpha.ps1
   ```
   Wait about 5 seconds after it starts. The first launch after a publish is slower, because the
   single-file exe unpacks itself.
2. Run the focus test:
   ```powershell
   powershell -NoProfile -STA -ExecutionPolicy Bypass -File tests\manual\Test-LauncherFocus.ps1
   ```
   Expected: `Test 1: 16/16 passed`, `Test 2: 8/8 passed`, `RESULT: PASS`.
3. Run the tray test:
   ```powershell
   powershell -NoProfile -STA -ExecutionPolicy Bypass -File tests\manual\Test-TrayIcon.ps1
   ```
   Expected: `10/10 checks passed`, `RESULT: PASS`. The test stops and restarts Launchy several
   times and leaves it running at the end.

## What each test does

### Test-LauncherFocus.ps1

Setup: opens two windows in the top-left corner of the primary screen:

- **Previous app** (blue) stands in for the app you were using before Alt+Q. It runs in a separate
  `powershell.exe` process. This matters: bug 2 only happens when the window in front belongs to a
  different process from the one sending the key presses. With the window in the test's own
  process, the unfixed build passes.
- **Other window** (yellow) is a second place to click.

**Test 1** runs 16 times, with Alt held for 40, 60, 120, 150, 180, 200, 220, 250, 280, 320, 350,
400, 450, 500, 600 and 900 ms. The bugs depended on when Alt is released. Each run:

1. Clicks *Previous app* and checks it is the foreground window (`Setup ok`).
2. Presses Alt down, Q down, Q up, waits the hold time, releases Alt.
3. Waits 700 ms, then checks the launcher is visible (`Shown`) and is the foreground window (`In front`).
4. Clicks *Previous app* on odd runs and *Other window* on even runs. Waits 500 ms.
5. Checks the clicked window is now in front (`Click landed`) and the launcher is hidden (`Hidden by click`).
6. If the launcher is stuck open, presses Alt+Q to close it before the next run.

A run passes only if all five columns are `yes`.

**Test 2** runs 8 times, with Alt held for 150 to 570 ms. Each run clicks *Previous app*, presses
Alt+Q, waits 700 ms, checks the launcher is shown, presses Esc, and checks the launcher closed.
Esc is handled by the launcher's text box, so this proves keyboard focus lands in the text box.

### Test-TrayIcon.ps1

It reads every icon in the tray (visible area and the ^ overflow) straight from Explorer's tray
toolbars. For each icon it gets the tooltip and the window that owns it. An icon whose owner window
no longer exists is a *ghost*. Tray clicks are simulated by posting the same message Explorer sends
to Launchy's hidden `Launchy tray` window.

1. **One live icon:** exactly one live Launchy icon. Any ghosts already present (from older builds)
   are counted as the baseline. They don't fail the test, but they mustn't increase.
2. **Left click opens launcher:** simulated left click; the launcher must appear within 800 ms. Esc closes it.
3. **Right click opens menu:** simulated right click; the tray menu must appear, and Esc must close it.
4. **Explorer restart keeps one icon:** sends Launchy the `TaskbarCreated` message, which Explorer
   broadcasts when it restarts. Launchy re-adds its icon; there must still be exactly one, with no new ghost.
5. **Kill + restart, 5 times:** `Stop-Process -Force`, then starts the same exe and waits up to 10 s
   for its icon. After each restart there must be exactly one live icon and no new ghost.
6. **Exit removes icon:** opens the tray menu, presses Up (selects the last item, Exit) and Enter.
   Launchy must exit within 5 s, leaving no icon and no new ghost. The test then starts Launchy again.

A build from before the tray fix has no `Launchy tray` window, so checks 2, 3, 4 and 6 report it and
fail, while check 5 still runs and shows the duplicate icons.

## Checking the tests still catch the bugs

When changing these tests, run them against the code from before the fixes and confirm they fail.
Build the old code in a separate worktree so your working copy isn't touched:

```powershell
git worktree add --detach G:\Claude.Pad\launchy-focus\main-src 818216b
dotnet publish G:\Claude.Pad\launchy-focus\main-src\src\Launchy\Launchy.csproj -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o G:\Claude.Pad\launchy-focus\main-build
Get-Process Launchy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Process G:\Claude.Pad\launchy-focus\main-build\Launchy.exe
# wait ~5 s, then run both tests
```

`818216b` is `main` before these fixes. Afterwards, restart the alpha (`Update-Alpha.ps1`) and remove
the worktree with `git worktree remove --force G:\Claude.Pad\launchy-focus\main-src`. Killing the old
build leaves one ghost icon, which hovering the mouse over removes.

To check bug 2 on its own, apply the fix but delete the `ShowActivated="False"` line from
`LauncherWindow.xaml`, build that the same way, and run the focus test.

## Recorded results (2026-11-06)

Windows 10 Pro 22H2 (build 19045), Windows PowerShell 5.1.

| Build | Focus test 1 | Focus test 2 | Tray test |
|---|---|---|---|
| `818216b` (`main` before the fixes) | **0/16**: 15 runs never shown (the flash); run 9 shown but not in front and not hidden by the click | **0/8**: never shown | **1/10**: ghosts went 5 → 6 → 7 → 8 → 9 over the 5 kill + restart cycles |
| Fix without `ShowActivated="False"` | **13/16**: run 14 not hidden by the click (bug 2); in runs 5 and 6 a click didn't bring the clicked window to the front | 8/8 | not run |
| `fix-launcher-flash` (final) | **16/16** | **8/8** | **10/10**: one icon throughout, no new ghosts |

The flash on `818216b` was confirmed separately by recording the launcher window's show/hide events
for the Launchy process (`SetWinEventHook`, `EVENT_OBJECT_SHOW`/`EVENT_OBJECT_HIDE`). After Alt+Q the
window was shown and hidden again within the same millisecond, about 45 ms after the key press.

The final build's focus code also passed the focus test on two more full runs before the tray change.
Its tray code was also checked by reinstalling the alpha twice and plain-restarting it twice: one icon
and no ghosts each time.

## Things a person should still check

The scripts can't see what the screen looks like. After a change in this area, also check by hand:

- Press Alt+Q about 10 times from different apps (an editor, a browser, File Explorer, the desktop).
  The launcher comes up every time on the first press and doesn't flash away.
- Click outside it: it closes. Do this twice in a row.
- Press Esc: it closes. Type a command and press Enter: it runs and closes.
- Left-click the tray icon: the launcher opens. Right-click it: the menu opens next to the icon, and
  each menu item works. Exit closes Launchy and removes the icon.
- End Launchy in Task Manager and start it again: there is still only one "L" icon, including under
  the ^ arrow.
