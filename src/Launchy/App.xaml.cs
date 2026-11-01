using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
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
    private readonly CommandStore _store = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\Launchy.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _launcher = new LauncherWindow(_store);
        _launcher.EditRequested += (_, _) => OpenEditor();

        _hotkey = new HotkeyService(_launcher);
        _hotkey.Pressed += (_, _) => ToggleLauncher();
        if (!_hotkey.Register(ModifierKeys.Alt, Key.Q))
        {
            MessageBox.Show(
                "Alt+Q is already used by another app. Launchy is still running; open it from the tray icon.",
                "Launchy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _tray = new TrayIcon(_store);
        _tray.OpenRequested += (_, _) => _launcher.ShowLauncher();
        _tray.EditRequested += (_, _) => OpenEditor();
        _tray.ExitRequested += (_, _) => Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
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
        _editor.Show();
        ForegroundHelper.ForceForeground(new WindowInteropHelper(_editor).Handle);
        _editor.Activate();
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
