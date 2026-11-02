using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Launchy.Models;
using Launchy.Services;

namespace Launchy;

/// <summary>
/// Lists every shortcut and lets the user record a new one. The caller must suspend global hotkeys
/// while this is open, or pressing the current shortcut would trigger it instead of being recorded.
/// </summary>
public partial class KeybindsWindow : Window
{
    private readonly HotkeyService _hotkeys;
    private readonly List<KeybindRow> _rows;
    private KeybindRow? _capturing;

    /// <summary>The new settings, or null when the user cancelled.</summary>
    public AppSettings? Result { get; private set; }

    public KeybindsWindow(AppSettings current, HotkeyService hotkeys)
    {
        InitializeComponent();
        _hotkeys = hotkeys;
        _rows = HotkeyActions.All.Select(info => new KeybindRow(info, current.HotkeyFor(info))).ToList();
        RowsList.ItemsSource = _rows;
    }

    private static KeybindRow RowFrom(object sender) => (KeybindRow)((FrameworkElement)sender).DataContext;

    private void Change_Click(object sender, RoutedEventArgs e)
    {
        StopCapture();
        _capturing = RowFrom(sender);
        _capturing.IsCapturing = true;
        StatusText.Text = "Press the new shortcut, or Esc to cancel.";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        StopCapture();
        var row = RowFrom(sender);
        row.Hotkey = row.Info.Default;
        StatusText.Text = "";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing is null)
        {
            return;
        }

        e.Handled = true;

        // With Alt held, WPF reports the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            StopCapture();
            StatusText.Text = "";
            return;
        }

        if (IsModifierKey(key))
        {
            return;
        }

        if (modifiers == ModifierKeys.None)
        {
            StatusText.Text = "Include Ctrl, Alt, Shift or Win in the shortcut.";
            return;
        }

        _capturing.Hotkey = new Hotkey(modifiers, key);
        StopCapture();
        StatusText.Text = "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        StopCapture();

        var duplicate = _rows.GroupBy(r => r.Hotkey).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            StatusText.Text = $"{duplicate.Key} is assigned to more than one action.";
            return;
        }

        // Try each shortcut for real so a combination owned by another app is caught here, not later.
        var taken = _rows.FirstOrDefault(r => !_hotkeys.Register(r.Info.Action, r.Hotkey));
        _hotkeys.UnregisterAll();
        if (taken is not null)
        {
            StatusText.Text = $"{taken.Hotkey} is already used by another app. Pick a different shortcut.";
            return;
        }

        Result = new AppSettings { Hotkeys = _rows.ToDictionary(r => r.Info.Action, r => r.Hotkey) };
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void StopCapture()
    {
        if (_capturing is not null)
        {
            _capturing.IsCapturing = false;
            _capturing = null;
        }
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}

public sealed class KeybindRow(HotkeyActionInfo info, Hotkey hotkey) : INotifyPropertyChanged
{
    private Hotkey _hotkey = hotkey;
    private bool _isCapturing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public HotkeyActionInfo Info { get; } = info;
    public string Name => Info.Name;
    public string Description => Info.Description;
    public string DisplayText => IsCapturing ? "Press keys…" : Hotkey.ToString();

    public Hotkey Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            _isCapturing = value;
            OnPropertyChanged(nameof(IsCapturing));
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
