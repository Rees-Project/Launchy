using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Launchy.Models;
using Launchy.Services;
using static Launchy.Services.Windows.NativeMethods;

namespace Launchy;

/// <summary>
/// Small status window for a workspace run. It never takes focus: WS_EX_NOACTIVATE keeps the foreground
/// window where it is, even when the Cancel button is clicked. It belongs to Launchy, so the window source skips it.
/// </summary>
public partial class WorkspaceProgressWindow : Window
{
    public static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(1.5);
    public static readonly TimeSpan CloseDelayWithProblems = TimeSpan.FromSeconds(6);

    private readonly CancellationTokenSource _cts;
    private readonly List<ProgressRow> _rows;
    private readonly DispatcherTimer _closeTimer = new();

    public WorkspaceProgressWindow(Workspace workspace, CancellationTokenSource cts)
    {
        InitializeComponent();
        _cts = cts;
        _rows = workspace.Items.Select(item => new ProgressRow(WorkspaceRunner.ItemName(item), item.Location)).ToList();
        RowsList.ItemsSource = _rows;
        TitleText.Text = workspace.Name;
        SubtitleText.Text = "Starting…";
        _closeTimer.Tick += (_, _) => Close();

        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Bottom - 16 - 80;
        Loaded += (_, _) => Top = area.Bottom - ActualHeight - 16;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    protected override void OnClosed(EventArgs e)
    {
        _closeTimer.Stop();
        base.OnClosed(e);
    }

    public void Apply(ItemProgress progress)
    {
        if (progress.Index >= 0 && progress.Index < _rows.Count)
        {
            _rows[progress.Index].Set(progress.State, progress.Message);
        }
    }

    /// <summary>Shows the end state, then closes after a short delay (longer when something went wrong).</summary>
    public void Finish(bool cancelled, bool hadProblems)
    {
        CancelButton.IsEnabled = false;
        SubtitleText.Text = cancelled ? "Cancelled" : hadProblems ? "Done, with problems" : "Done";
        _closeTimer.Interval = hadProblems && !cancelled ? CloseDelayWithProblems : CloseDelay;
        _closeTimer.Start();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        SubtitleText.Text = "Cancelling…";
        _cts.Cancel();
    }

    private sealed class ProgressRow(string name, string detail) : INotifyPropertyChanged
    {
        private static readonly Brush Muted = Frozen("#9AA0A6");
        private static readonly Brush Busy = Frozen("#4F8BFF");
        private static readonly Brush Good = Frozen("#4CC38A");
        private static readonly Brush Warn = Frozen("#E8A33D");
        private static readonly Brush Bad = Frozen("#F25F5C");

        private ItemState _state = ItemState.Waiting;
        private string? _message;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; } = name;
        public string Detail => string.IsNullOrEmpty(_message) ? detail : $"{detail}\n{_message}";

        public string StateText => _state switch
        {
            ItemState.Waiting => "waiting",
            ItemState.Starting => "starting",
            ItemState.Placed => "placed",
            ItemState.StartedNotPlaced => "started, not placed",
            ItemState.NotFound => "not found",
            _ => "failed"
        };

        // A glyph per state, so the state does not rest on colour alone.
        public string Glyph => _state switch
        {
            ItemState.Waiting => "·",
            ItemState.Starting => "…",
            ItemState.Placed => "✓",
            ItemState.StartedNotPlaced => "!",
            _ => "✗"
        };

        public Brush Brush => _state switch
        {
            ItemState.Waiting => Muted,
            ItemState.Starting => Busy,
            ItemState.Placed => Good,
            ItemState.StartedNotPlaced => Warn,
            _ => Bad
        };

        public void Set(ItemState state, string? message)
        {
            _state = state;
            _message = message;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        private static Brush Frozen(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }
}
