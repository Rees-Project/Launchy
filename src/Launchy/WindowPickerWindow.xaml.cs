using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launchy.Models;
using Launchy.Services;
using Launchy.Services.Windows;

namespace Launchy;

/// <summary>
/// Lists the open windows so the owner can save their positions into a workspace.
/// Does not write any file: the caller saves <see cref="Result"/>. Open it on the UI thread.
/// </summary>
public partial class WindowPickerWindow : Window
{
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> AppNameCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyList<WorkspaceItem> _existing;
    private readonly LayoutCapture _capture;
    private List<ItemRow> _itemRows = [];
    private List<WindowRow> _windowRows = [];
    private readonly HashSet<IntPtr> _tickedNewWindows = [];

    /// <summary>The new item list, or null when the owner cancelled.</summary>
    public List<WorkspaceItem>? Result { get; private set; }

    public WindowPickerWindow(IReadOnlyList<WorkspaceItem> existingItems, IWindowSource? source = null)
    {
        InitializeComponent();
        _existing = existingItems;

        string[] browserPaths = BrowserLaunch.GetDefaultBrowserPath() is { } defaultBrowser ? [defaultBrowser] : [];
        _capture = new LayoutCapture(source ?? new Win32WindowSource(), browserPaths);

        ItemsSection.Visibility = _existing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Reload();
    }

    private void Reload()
    {
        RememberTicks();
        _capture.Refresh();

        var candidates = LayoutCapture.FindCandidates(_existing, _capture.Windows);
        var defaults = LayoutCapture.AssignDefaults(candidates);
        var previous = _itemRows;

        _itemRows = _existing.Select((item, i) =>
        {
            var options = candidates[i].Select(w => new WindowOption(w, OptionText(w))).ToList();
            var keepHandle = i < previous.Count ? previous[i].Selected?.Window.Handle : null;
            var selected = options.FirstOrDefault(o => o.Window.Handle == keepHandle)
                           ?? options.FirstOrDefault(o => o.Window.Handle == defaults[i]?.Handle);
            var isChecked = i < previous.Count && previous[i].HasWindow ? previous[i].IsChecked : selected is not null;
            return new ItemRow(i, item, options, selected, isChecked && selected is not null);
        }).ToList();

        ItemRowsList.ItemsSource = _itemRows;
        RebuildWindowRows();
        StatusText.Text = "";
    }

    private void RememberTicks()
    {
        foreach (var row in _windowRows)
        {
            if (row.IsChecked)
            {
                _tickedNewWindows.Add(row.Window.Handle);
            }
            else
            {
                _tickedNewWindows.Remove(row.Window.Handle);
            }
        }
    }

    private void RebuildWindowRows()
    {
        RememberTicks();
        var usedByItems = _itemRows
            .Where(r => r.Selected is not null)
            .Select(r => r.Selected!.Window.Handle)
            .ToHashSet();

        _windowRows = _capture.Windows
            .Where(w => !usedByItems.Contains(w.Handle))
            .Select(w => new WindowRow(
                w,
                LoadIcon(w.ProcessPath),
                AppName(w),
                MonitorLabel(w.MonitorDevice),
                LayoutCapture.WhyNotCapturable(w) ?? WebsiteHint(w),
                LayoutCapture.WhyNotCapturable(w) is null,
                _tickedNewWindows.Contains(w.Handle)))
            .ToList();

        WindowRowsList.ItemsSource = _windowRows;
        NoWindowsText.Visibility = _windowRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string? WebsiteHint(WindowInfo window) =>
        _capture.IsBrowser(window) ? "Saved as a website. Type the URL in the editor." : null;

    private string OptionText(WindowInfo window) =>
        $"{window.Title} — {MonitorLabel(window.MonitorDevice)} at ({window.Rect.Left},{window.Rect.Top})";

    private string MonitorLabel(string device)
    {
        var name = device.StartsWith(@"\\.\", StringComparison.Ordinal) ? device[4..] : device;
        var isPrimary = _capture.Monitors.Any(m => m.Device == device && m.IsPrimary);
        return isPrimary ? $"{name} (primary)" : name;
    }

    private void ItemWindow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RebuildWindowRows();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var tickedItems = _itemRows.Where(r => r.IsChecked && r.Selected is not null).ToList();
        var duplicate = tickedItems
            .GroupBy(r => r.Selected!.Window.Handle)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            var numbers = string.Join(" and ", duplicate.Select(r => r.Index + 1));
            StatusText.Text = $"Items {numbers} use the same window. Choose a different window for one of them.";
            return;
        }

        var picked = tickedItems
            .Select(r => (r.Selected!.Window, ItemIndex: (int?)r.Index))
            .Concat(_windowRows.Where(r => r.IsChecked && r.IsCapturable).Select(r => (r.Window, ItemIndex: (int?)null)))
            .ToList();

        // Read the windows again so a window moved while the picker was open saves its current position.
        _capture.Refresh();
        var fresh = _capture.Windows.ToDictionary(w => w.Handle);
        var selections = picked.Select(p => new CaptureSelection(fresh.GetValueOrDefault(p.Window.Handle, p.Window), p.ItemIndex));

        Result = _capture.Apply(_existing, selections);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private static string AppName(WindowInfo window)
    {
        if (string.IsNullOrEmpty(window.ProcessPath))
        {
            return window.AppUserModelId ?? "Unknown program";
        }

        if (!AppNameCache.TryGetValue(window.ProcessPath, out var name))
        {
            try
            {
                name = FileVersionInfo.GetVersionInfo(window.ProcessPath).FileDescription;
            }
            catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException)
            {
                name = null;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(window.ProcessPath);
            }

            AppNameCache[window.ProcessPath] = name;
        }

        return name;
    }

    private static ImageSource? LoadIcon(string processPath)
    {
        if (string.IsNullOrEmpty(processPath))
        {
            return null;
        }

        if (IconCache.TryGetValue(processPath, out var cached))
        {
            return cached;
        }

        ImageSource? image = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
            if (icon is not null)
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                                                                 BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                image = bitmap;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                       or System.Runtime.InteropServices.COMException)
        {
        }

        IconCache[processPath] = image;
        return image;
    }

    private sealed record WindowOption(WindowInfo Window, string Text);

    private sealed class ItemRow(int index, WorkspaceItem item, List<WindowOption> candidates,
                                 WindowOption? selected, bool isChecked) : INotifyPropertyChanged
    {
        private WindowOption? _selected = selected;
        private bool _isChecked = isChecked;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; } = index;
        public List<WindowOption> Candidates { get; } = candidates;
        public bool HasWindow => Candidates.Count > 0;
        public bool HasChoice => Candidates.Count > 1;
        public bool ShowSingleWindowText => Candidates.Count <= 1;
        public string SingleWindowText => Candidates.Count == 1 ? Candidates[0].Text : "No open window. The saved position is kept.";

        public string Description { get; } =
            $"{index + 1}. {item.Type}: {(item.Location.Length > 0 ? item.Location : "(no location)")}";

        public string? Hint { get; } = item.Type == TargetType.Website && item.Location.Length == 0
            ? "The URL is empty. Type it in the editor."
            : null;

        public bool HasHint => Hint is not null;

        public WindowOption? Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                OnPropertyChanged();
            }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class WindowRow(WindowInfo window, ImageSource? icon, string appName, string monitor,
                                   string? hint, bool isCapturable, bool isChecked)
    {
        public WindowInfo Window { get; } = window;
        public ImageSource? Icon { get; } = icon;
        public string Title => Window.Title;
        public string AppName { get; } = appName;
        public string Monitor { get; } = monitor;
        public string? Hint { get; } = hint;
        public bool HasHint => Hint is not null;
        public bool IsCapturable { get; } = isCapturable;
        public bool IsChecked { get; set; } = isChecked && isCapturable;
    }
}
