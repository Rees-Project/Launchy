using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Launchy.Models;
using Launchy.Services;

namespace Launchy;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private HotkeyService? _hotkey;
    private TrayIcon? _tray;
    private LauncherWindow? _launcher;
    private CommandEditorWindow? _editor;
    private KeybindsWindow? _keybinds;
    private readonly CommandStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\Launchy.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _settings = LoadSettings();

        _launcher = new LauncherWindow(_store);
        _launcher.EditRequested += (_, _) => OpenEditor();
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
        _hotkey?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
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
        _editor.Closed += (_, _) => _editor = null;
        ShowInFront(_editor);
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
