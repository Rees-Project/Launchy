using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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

    // After the launcher is shown, focus loss is ignored until at least MinGracePeriod has
    // passed and the hotkey's modifiers have been released for ModifierSettleTime. Releasing
    // Alt can make the previously focused app take the foreground back, so judging focus
    // before then would hide the launcher the moment it appears.
    private static readonly TimeSpan MinGracePeriod = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ModifierSettleTime = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaxGracePeriod = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _graceTimer;
    private readonly Stopwatch _graceClock = new();
    private TimeSpan? _modifiersReleasedAt;
    private bool _inGracePeriod;

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    public event EventHandler? EditRequested;
    public event EventHandler? KeybindsRequested;

    private bool IsEditKeyword => CommandBox.Text.Trim().Equals(EditKeyword, StringComparison.OrdinalIgnoreCase);

    public LauncherWindow(CommandStore store)
    {
        InitializeComponent();
        _store = store;
        Deactivated += (_, _) => OnFocusLost();

        _graceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _graceTimer.Tick += GraceTimer_Tick;
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

        _inGracePeriod = true;
        _modifiersReleasedAt = null;
        _graceClock.Restart();
        _graceTimer.Start();

        // ShowActivated is off in the XAML, so Show() doesn't activate the window and
        // BringToFront activates it in one step. If Show() activated it first while another
        // app had the foreground, ForceForeground would leave Windows unsure which window is
        // in front, and a click outside would then never deactivate the launcher.
        Show();
        UpdateLayout();
        Top = area.Top + (area.Height - ActualHeight) / 2;
        BringToFront();
    }

    private void BringToFront()
    {
        ForegroundHelper.ForceForeground(Handle);
        Activate();
        CommandBox.Focus();
        Keyboard.Focus(CommandBox);
    }

    private void GraceTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = _graceClock.Elapsed;
        if (ForegroundHelper.AreModifierKeysDown())
        {
            _modifiersReleasedAt = null;
        }
        else
        {
            _modifiersReleasedAt ??= elapsed;
        }

        var settled = _modifiersReleasedAt is { } releasedAt
            && elapsed >= MinGracePeriod
            && elapsed - releasedAt >= ModifierSettleTime;

        if (settled || elapsed >= MaxGracePeriod)
        {
            EndGracePeriod();
        }
    }

    private void EndGracePeriod()
    {
        _graceTimer.Stop();
        _inGracePeriod = false;

        // WPF's IsActive can be stale here, so ask Windows. If another app took the
        // foreground during the show, take it back so a later click outside is noticed.
        if (IsVisible && !ForegroundHelper.IsForeground(Handle))
        {
            BringToFront();
        }
    }

    private void OnFocusLost()
    {
        if (IsVisible && !_inGracePeriod && !ForegroundHelper.IsForeground(Handle))
        {
            Hide();
        }
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
