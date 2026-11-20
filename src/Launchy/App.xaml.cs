using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Launchy.Models;
using Launchy.Services;
using Launchy.Services.Windows;

namespace Launchy;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private HotkeyService? _hotkey;
    private TrayIcon? _tray;
    private LauncherWindow? _launcher;
    private CommandEditorWindow? _editor;
    private KeybindsWindow? _keybinds;
    private WorkspaceEditorWindow? _workspaceEditor;
    private readonly CancellationTokenSource _exitCts = new();
    private bool _workspaceRunning;
    private readonly WorkspaceSessions _sessions = new();
    private readonly CommandStore _store = new();
    private readonly WorkspaceStore _workspaceStore = new();
    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Runs before the single-instance check so it works while Launchy is open.
        if (e.Args.Contains(WindowDump.Argument))
        {
            DumpWindows();
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, @"Local\Launchy.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _settings = LoadSettings();

        _launcher = new LauncherWindow(_store, _workspaceStore);
        _launcher.EditRequested += (_, _) => OpenEditor();
        _launcher.WorkspaceRequested += (_, workspace) => _ = RunWorkspaceSafeAsync(workspace);
        _launcher.CloseWorkspaceRequested += (_, workspace) => CloseWorkspace(workspace);
        _launcher.KeybindsRequested += (_, _) => OpenKeybinds();

        _tray = new TrayIcon(_store);
        _tray.OpenRequested += (_, _) => _launcher.ShowLauncher();
        _tray.EditRequested += (_, _) => OpenEditor();
        _tray.KeybindsRequested += (_, _) => OpenKeybinds();
        _tray.ExitRequested += (_, _) => Shutdown();

        _hotkey = new HotkeyService(_launcher);
        _hotkey.Pressed += (_, action) => OnHotkey(action);
        ApplyHotkeys();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitCts.Cancel();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void DumpWindows()
    {
        // A WinExe has no console; attach to the parent's unless output is redirected to a file or pipe.
        if (!Console.IsOutputRedirected && Win32WindowSource.AttachToParentConsole())
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }

        WindowDump.Write(new Win32WindowSource(), Console.Out);
        Console.Out.Flush();
    }

    private AppSettings LoadSettings()
    {
        try
        {
            return _settingsStore.Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"settings.json couldn't be read, so Launchy is using the default keybinds.\n\n{ex.Message}",
                "Launchy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return new AppSettings();
        }
    }

    private void ApplyHotkeys()
    {
        _hotkey!.UnregisterAll();

        var failed = new List<string>();
        foreach (var info in HotkeyActions.All)
        {
            var hotkey = _settings.HotkeyFor(info);
            if (!_hotkey.Register(info.Action, hotkey))
            {
                failed.Add($"{info.Name} ({hotkey})");
            }
        }

        _tray!.SetShortcutText(_settings.HotkeyFor(HotkeyActions.All[0]).ToString());

        if (failed.Count > 0)
        {
            MessageBox.Show(
                $"These shortcuts are already used by another app:\n\n{string.Join("\n", failed)}\n\nOpen Keybinds from the tray icon to choose different ones.",
                "Launchy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.OpenLauncher:
                ToggleLauncher();
                break;
        }
    }

    private void OpenEditor()
    {
        if (_editor is not null)
        {
            _editor.Activate();
            return;
        }

        IReadOnlyList<LaunchCommand> commands;
        try
        {
            commands = _store.Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Opening an empty editor here would let a Save wipe the user's commands.
            MessageBox.Show(
                $"commands.json couldn't be read, so the editor won't open.\n\n{ex.Message}\n\nFix the file with \"Open commands file (JSON)\" in the tray menu.",
                "Launchy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _editor = new CommandEditorWindow(_store, commands);
        _editor.WorkspacesRequested += (_, _) => OpenWorkspaceEditor();
        _editor.Closed += (_, _) => _editor = null;
        ShowInFront(_editor);
    }

    private void OpenWorkspaceEditor()
    {
        if (_workspaceEditor is not null)
        {
            _workspaceEditor.Activate();
            return;
        }

        IReadOnlyList<Workspace> workspaces;
        IReadOnlyList<LaunchCommand> commands;
        try
        {
            workspaces = _workspaceStore.Load();
            commands = _store.Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or WorkspaceVersionException)
        {
            // Opening an empty editor here would let a Save wipe the user's workspaces.
            MessageBox.Show(
                $"A data file couldn't be read, so the workspace editor won't open.\n\n{ex.Message}",
                "Launchy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _workspaceEditor = new WorkspaceEditorWindow(_workspaceStore, workspaces, commands);
        _workspaceEditor.Closed += (_, _) => _workspaceEditor = null;
        ShowInFront(_workspaceEditor);
    }

    /// <summary>Starts a workspace from the launcher. A run never throws out of here, and only one runs at a time.</summary>
    private async Task RunWorkspaceSafeAsync(Workspace workspace)
    {
        if (_workspaceRunning)
        {
            _tray?.ShowWarning($"Workspace \"{workspace.Name}\"", "Another workspace is still starting.");
            return;
        }

        _workspaceRunning = true;
        try
        {
            await RunWorkspaceAsync(workspace, _exitCts.Token);
        }
        catch (Exception ex)
        {
            _tray?.ShowWarning($"Workspace \"{workspace.Name}\"", $"Couldn't run: {ex.Message}");
        }
        finally
        {
            _workspaceRunning = false;
        }
    }

    private void CloseWorkspace(Workspace workspace)
    {
        var closer = new WorkspaceCloser(_sessions, new Win32WindowSource(), new Win32WindowCloser());
        _tray?.ShowWarning($"Workspace \"{workspace.Name}\"", closer.Close(workspace.Name).Describe(workspace.Name));
    }

    private void OpenKeybinds()
    {
        if (_keybinds is not null)
        {
            _keybinds.Activate();
            return;
        }

        // Suspend shortcuts so pressing the current one gets recorded instead of firing.
        _hotkey!.UnregisterAll();

        _keybinds = new KeybindsWindow(_settings, _hotkey);
        _keybinds.Closed += (_, _) =>
        {
            if (_keybinds.Result is { } updated)
            {
                try
                {
                    _settingsStore.Save(updated);
                    _settings = updated;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show($"Couldn't save settings.json: {ex.Message}", "Launchy",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            _keybinds = null;
            ApplyHotkeys();
        };
        ShowInFront(_keybinds);
    }

    /// <summary>
    /// Runs a workspace and reports the items that were not placed in a tray balloon.
    /// Call it on the UI thread after the launcher hides: the window source needs the STA thread.
    /// </summary>
    internal async Task RunWorkspaceAsync(Workspace workspace, CancellationToken exitToken = default)
    {
        // The progress window's Cancel button cancels this source; app exit cancels it through the link.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(exitToken);
        var window = new WorkspaceProgressWindow(workspace, cts);
        window.Show();

        var summary = default(string);
        try
        {
            var runner = new WorkspaceRunner(new Win32WindowSource(), new ItemLauncher(), new WindowPlacer(), TimeProvider.System,
                                             showEvents: new Win32WindowShowEvents());
            var results = await runner.RunAsync(workspace, cts.Token, window.Apply);
            _sessions.Record(workspace.Name, runner.OpenedWindows);
            summary = WorkspaceRunner.Summarize(results);
            window.Finish(cts.IsCancellationRequested, summary is not null);
        }
        catch
        {
            window.Close();
            throw;
        }

        if (summary is not null)
        {
            _tray?.ShowWarning($"Workspace \"{workspace.Name}\"", summary);
        }
    }

    private static void ShowInFront(Window window)
    {
        window.Show();
        ForegroundHelper.ForceForeground(new WindowInteropHelper(window).Handle);
        window.Activate();
    }

    private void ToggleLauncher()
    {
        if (_launcher!.IsVisible)
        {
            _launcher.Hide();
        }
        else
        {
            _launcher.ShowLauncher();
        }
    }
}
