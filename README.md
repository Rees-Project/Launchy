## Workspaces

A workspace opens several apps, folders, files and websites with one launcher command, and puts each
window back where you saved it (monitor, position, size, maximized or minimized).

**Create.** Open the Command editor and press **Workspaces...**. In the workspace editor, add a
workspace (**New**), choose a name, and add items with **Add item** or **Capture layout...** (see
below). **Test run** runs the workspace without leaving the editor. A workspace name can't match a
command name or another workspace name. Workspaces are saved in `%APPDATA%\Launchy\workspaces.json`.
`commands.json` is not changed.

**Capture.** In the workspace editor, press **Capture layout...** and pick the open windows to add.
Launchy saves each window's program, folder or file, position, size and state, and the monitor it is on.
Arrange the windows first, then capture. A browser window is added as a website item with an empty
URL: type the URL in. The editor is the only place to save a layout; the launcher has no save command.

**Run.** Press Alt+Q, type the workspace name and press Enter. Workspaces appear in the launcher list
after the commands. Launchy starts each item in order, waits for its window (15 seconds by default),
and moves the window to the saved place. A tray message lists any item that was not placed.

Limits:

- Administrator windows cannot be moved.
- Windows open first and then move, so you see a jump.
- Snapped windows get the snapped size only. Windows does not treat them as snapped.
- Running a workspace always starts new windows. It does not move windows that are already open.
- Websites open the plain way, so a URL may open as a tab in an existing browser window. Browser tabs
  are not restored; only the saved URLs open. Websites are not placed.
- Tray-only apps, other virtual desktops, and apps that set their own position late can fail.
- Store apps (for example Calculator) start with `shell:AppsFolder\<app ID>` and are placed. A Store app
  that allows only one window and is already open is not placed ("window not found").
- Apps installed from a package (under `WindowsApps`, for example the Claude desktop app) are captured by
  app ID, so an app update does not break the item. Items saved before this change keep the old path.
- Older Launchy builds cannot read a `workspaces.json` with a newer version number. They show an error
  and don't change the file. Update all copies of Launchy together.

---

Windows Desktop Application
alt+Q to launch, should also be changed.
Gamemode, where it won't launch while in a game
start on startup
tool to open other programs/folders/documents/websites/scripts
allow for in app powershell scripts. This may be used to open multiple items (example: VSCode, folder, website, git)
Should have table based input, Open command, location (file,folder,website,application), Param (if app can be open with extra --Verbose or something similiar)
Backup Import/Export option for links
AutoComplete on Open command.
This webapp visually should be small and open in the middle of the screen, think ~20% of screen.
once command is ran and enter is hit, UI should close.
On edit (where you add your own data to the table, have 'smart options' as an assist on what might be helpfull, such as %appdata%, similiar websites, or folder struction from previous used data.
Beatify the UI, have a dark/light mode.
Add a splash screen on launch.
