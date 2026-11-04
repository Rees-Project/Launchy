using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Launchy.Models;
using Launchy.Services;

namespace Launchy;

public partial class LauncherWindow : Window
{
    private const string EditKeyword = ":edit";

    private readonly CommandStore _store;
    private IReadOnlyList<LaunchCommand> _commands = [];
    private string? _loadError;
    private LaunchCommand? _match;
    private IReadOnlyList<LaunchCommand> _matches = [];
    private int _matchIndex = -1;

    public event EventHandler? EditRequested;
    public event EventHandler? KeybindsRequested;

    private bool IsEditKeyword => CommandBox.Text.Trim().Equals(EditKeyword, StringComparison.OrdinalIgnoreCase);

    public LauncherWindow(CommandStore store)
    {
        InitializeComponent();
        _store = store;
        Deactivated += (_, _) => Hide();
    }

    public void ShowLauncher()
    {
        // Reload on every summon so edits to commands.json apply without a restart.
        LoadCommands();
        CommandBox.Clear();
        UpdateMatch();

        var area = SystemParameters.WorkArea;
        Width = Math.Clamp(area.Width * 0.2, 480, 900);
        Left = area.Left + (area.Width - Width) / 2;

        Show();
        UpdateLayout();
        Top = area.Top + (area.Height - ActualHeight) / 2;

        ForegroundHelper.ForceForeground(new WindowInteropHelper(this).Handle);
        Activate();
        CommandBox.Focus();
        Keyboard.Focus(CommandBox);
    }

    private void LoadCommands()
    {
        try
        {
            _commands = _store.Load();
            _loadError = null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _commands = [];
            _loadError = $"Couldn't read commands.json: {ex.Message}";
        }
    }

    private void UpdateMatch()
    {
        var text = CommandBox.Text;
        Placeholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _matches = CommandStore.Matches(_commands, text);
        _match = CommandStore.Find(_commands, text);
        _matchIndex = _match is null ? -1 : 0;
        UpdateStatus();
    }

    private void CycleMatch(int step)
    {
        if (_matches.Count == 0)
        {
            return;
        }

        _matchIndex = _matchIndex < 0
            ? (step > 0 ? 0 : _matches.Count - 1)
            : (_matchIndex + step + _matches.Count) % _matches.Count;
        _match = _matches[_matchIndex];
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var text = CommandBox.Text;
        if (IsEditKeyword)
        {
            StatusText.Text = "Open the command editor";
        }
        else if (_loadError is not null)
        {
            StatusText.Text = _loadError;
        }
        else if (text.Trim().Length == 0)
        {
            StatusText.Text = $"{_commands.Count} commands · Enter to run · Tab to switch match · Esc to close";
        }
        else if (_match is null)
        {
            StatusText.Text = _matches.Count > 1 ? $"{_matches.Count} matches · Tab to choose" : "No matching command";
        }
        else
        {
            var parameters = string.IsNullOrWhiteSpace(_match.Parameters) ? "" : $" {_match.Parameters}";
            var position = _matches.Count > 1 ? $"  ({_matchIndex + 1} of {_matches.Count} · Tab for next)" : "";
            StatusText.Text = $"{_match.Command}  →  {_match.Type}: {_match.Location}{parameters}{position}";
        }
    }

    private void CommandBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateMatch();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        EditRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Keybinds_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        KeybindsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void CommandBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;

            case Key.Tab:
                e.Handled = true;
                CycleMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                break;

            case Key.Enter:
                e.Handled = true;
                if (IsEditKeyword)
                {
                    Hide();
                    EditRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (_match is null)
                {
                    return;
                }

                try
                {
                    LaunchService.Run(_match);
                    Hide();
                }
                catch (Exception ex) when (ex is Win32Exception or NotSupportedException or InvalidOperationException)
                {
                    StatusText.Text = $"Couldn't open {_match.Location}: {ex.Message}";
                }
                break;
        }
    }
}
