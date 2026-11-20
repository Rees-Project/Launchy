using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Launchy.Models;
using Launchy.Services;
using Launchy.Services.Windows;

namespace Launchy;

/// <summary>
/// Creates, edits and test-runs workspaces. Edits stay in memory until Save writes workspaces.json.
/// </summary>
public partial class WorkspaceEditorWindow : Window
{
    private readonly WorkspaceStore _store;
    private readonly IReadOnlyList<LaunchCommand> _commands;
    private readonly ObservableCollection<Workspace> _workspaces;
    private readonly ObservableCollection<ItemRow> _rows = [];
    private readonly CancellationTokenSource _closeCts = new();
    private Workspace? _current;
    private bool _loading;
    private bool _running;
    private bool _dirty;

    public WorkspaceEditorWindow(WorkspaceStore store, IReadOnlyList<Workspace> workspaces, IReadOnlyList<LaunchCommand> commands)
    {
        InitializeComponent();
        _store = store;
        _commands = commands;

        TypeColumn.ItemsSource = Enum.GetValues<TargetType>().Where(t => t != TargetType.Script).ToList();
        IfRunningColumn.ItemsSource = Enum.GetValues<IfRunning>();
        ItemsGrid.ItemsSource = _rows;
        _rows.CollectionChanged += (_, _) => _dirty = true;

        _workspaces = new ObservableCollection<Workspace>(workspaces);
        WorkspaceList.ItemsSource = _workspaces;
        if (_workspaces.Count > 0)
        {
            WorkspaceList.SelectedIndex = 0;
        }
        else
        {
            ShowCurrent();
        }
    }

    private void WorkspaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitGrid();
        SyncItems();
        _current = WorkspaceList.SelectedItem as Workspace;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        var wasDirty = _dirty;
        _loading = true;
        _rows.Clear();
        if (_current is not null)
        {
            foreach (var item in _current.Items)
            {
                _rows.Add(new ItemRow(item));
            }
        }

        NameBox.Text = _current?.Name ?? "";
        EditPanel.IsEnabled = _current is not null;
        _loading = false;
        _dirty = wasDirty;
        ResultsText.Text = "Test run results appear here.";
    }

    private void CommitGrid()
    {
        ItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void SyncItems()
    {
        if (_current is not null)
        {
            _current.Items = _rows.Select(r => r.Item).ToList();
        }
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current is null)
        {
            return;
        }

        _current.Name = NameBox.Text;
        _dirty = true;
        WorkspaceList.Items.Refresh();
    }

    private void ItemsGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            _dirty = true;
        }
    }

    private void NewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var workspace = new Workspace { Name = NextFreeName() };
        _workspaces.Add(workspace);
        _dirty = true;
        WorkspaceList.SelectedItem = workspace;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private string NextFreeName()
    {
        for (var i = 1; ; i++)
        {
            var name = $"workspace{i}";
            if (WorkspaceStore.ValidateName(name, _commands, _workspaces) is null)
            {
                return name;
            }
        }
    }

    private void DeleteWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        var answer = MessageBox.Show(this, $"Delete the workspace \"{_current.Name}\"?\nThis takes effect when you press Save.",
            "Launchy", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var index = _workspaces.IndexOf(_current);
        _current = null;
        _workspaces.RemoveAt(index);
        _dirty = true;
        if (_workspaces.Count > 0)
        {
            WorkspaceList.SelectedIndex = Math.Min(index, _workspaces.Count - 1);
        }
        else
        {
            ShowCurrent();
        }
    }

    private const string CsvFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

    private void ImportWorkspaces_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = CsvFilter };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            CommitGrid();
            SyncItems();
            var result = WorkspaceCsv.Import(dialog.FileName, _commands, _workspaces);
            foreach (var workspace in result.Workspaces)
            {
                _workspaces.Add(workspace);
            }

            if (result.Workspaces.Count > 0)
            {
                _dirty = true;
            }

            StatusText.Text = $"Imported {result.Workspaces.Count} workspace(s)" +
                (result.Errors.Count > 0 ? $", skipped {result.Errors.Count}: {string.Join(" ", result.Errors)}" : "") +
                ". Press Save to keep them.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            StatusText.Text = $"Couldn't import: {ex.Message}";
        }
    }

    private void ExportWorkspaces_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = CsvFilter, FileName = "launchy-workspaces.csv" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            CommitGrid();
            SyncItems();
            WorkspaceCsv.Export(dialog.FileName, _workspaces);
            StatusText.Text = $"Exported {_workspaces.Count} workspace(s).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Couldn't export: {ex.Message}";
        }
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var row = new ItemRow(new WorkspaceItem { Type = TargetType.Application });
        _rows.Add(row);

        ItemsGrid.SelectedItem = row;
        ItemsGrid.ScrollIntoView(row);
        ItemsGrid.CurrentCell = new DataGridCellInfo(row, ItemsGrid.Columns[1]);
        ItemsGrid.Focus();
        ItemsGrid.BeginEdit();
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        foreach (var row in ItemsGrid.SelectedItems.Cast<ItemRow>().ToList())
        {
            _rows.Remove(row);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int step)
    {
        CommitGrid();
        if (ItemsGrid.SelectedItem is not ItemRow row)
        {
            return;
        }

        var from = _rows.IndexOf(row);
        var to = from + step;
        if (to < 0 || to >= _rows.Count)
        {
            return;
        }

        _rows.Move(from, to);
        ItemsGrid.SelectedItem = row;
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        CommitGrid();
        TrimItems();
        SyncItems();

        try
        {
            var picker = new WindowPickerWindow(_current.Items) { Owner = this };
            if (picker.ShowDialog() == true && picker.Result is { } items)
            {
                _current.Items = items;
                _dirty = true;
                ShowCurrent();
                StatusText.Text = "Layout captured. Press Save to keep it.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        {
            StatusText.Text = $"Couldn't read the open windows: {ex.Message}";
        }
    }

    private async void TestRun_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _running)
        {
            return;
        }

        CommitGrid();
        TrimItems();
        SyncItems();

        var error = ValidateItems(_current);
        if (error is not null)
        {
            StatusText.Text = error;
            return;
        }

        StatusText.Text = "";
        _running = true;
        TestRunButton.IsEnabled = false;
        ResultsText.Text = "Running…";
        try
        {
            var runner = new WorkspaceRunner(new Win32WindowSource(), new ItemLauncher(), new WindowPlacer(), TimeProvider.System,
                                             showEvents: new Win32WindowShowEvents());
            var results = await runner.RunAsync(_current, _closeCts.Token);
            ResultsText.Text = results.Count == 0
                ? "Nothing ran."
                : string.Join("\n", results.Select((r, i) => WorkspaceDisplay.ResultLine(i + 1, r)));
        }
        catch (Exception ex)
        {
            ResultsText.Text = $"Test run failed: {ex.Message}";
        }
        finally
        {
            _running = false;
            TestRunButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TrySave())
        {
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _dirty = false;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_dirty)
        {
            var answer = MessageBox.Show(this, "Save changes to your workspaces?", "Launchy",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !TrySave()))
            {
                e.Cancel = true;
                return;
            }
        }

        _closeCts.Cancel();
    }

    private void TrimItems()
    {
        foreach (var row in _rows)
        {
            if (row.Item.Type == TargetType.Website)
            {
                row.Item.SetLocationText(row.Item.LocationText());
            }
            else
            {
                row.Item.Urls = null;
            }

            row.Item.Location = row.Item.Location.Trim();
            row.Item.Parameters = string.IsNullOrWhiteSpace(row.Item.Parameters) ? null : row.Item.Parameters.Trim();
        }
    }

    private bool TrySave()
    {
        CommitGrid();
        TrimItems();
        SyncItems();

        foreach (var workspace in _workspaces)
        {
            workspace.Name = workspace.Name.Trim();
        }

        var error = Validate();
        if (error is not null)
        {
            StatusText.Text = error;
            return false;
        }

        try
        {
            _store.Save(_workspaces);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText.Text = $"Couldn't save workspaces.json: {ex.Message}";
            return false;
        }

        _dirty = false;
        return true;
    }

    private string? Validate()
    {
        foreach (var workspace in _workspaces)
        {
            var others = _workspaces.Where(w => !ReferenceEquals(w, workspace));
            var nameError = WorkspaceStore.ValidateName(workspace.Name, _commands, others);
            if (nameError is not null)
            {
                return nameError;
            }

            var itemError = ValidateItems(workspace);
            if (itemError is not null)
            {
                return $"\"{workspace.Name}\": {itemError}";
            }
        }

        return null;
    }

    private static string? ValidateItems(Workspace workspace)
    {
        if (workspace.Items.Count == 0)
        {
            return "A workspace needs at least one item.";
        }

        for (var i = 0; i < workspace.Items.Count; i++)
        {
            var item = workspace.Items[i];
            if (item.Location.Length == 0)
            {
                return $"Item {i + 1}: Location is required.";
            }

            if (item.TimeoutSeconds is <= 0)
            {
                return $"Item {i + 1}: Timeout must be more than 0 seconds, or empty for the default.";
            }
        }

        return null;
    }

    /// <summary>Grid row over a <see cref="WorkspaceItem"/>; adds the read-only placement text.</summary>
    private sealed class ItemRow(WorkspaceItem item)
    {
        public WorkspaceItem Item { get; } = item;

        public TargetType Type
        {
            get => Item.Type;
            set => Item.Type = value;
        }

        public string Location
        {
            get => Item.LocationText();
            set => Item.SetLocationText(value);
        }

        public string? Parameters
        {
            get => Item.Parameters;
            set => Item.Parameters = value;
        }

        public int? TimeoutSeconds
        {
            get => Item.TimeoutSeconds;
            set => Item.TimeoutSeconds = value;
        }

        public IfRunning IfRunning
        {
            get => Item.IfRunning;
            set => Item.IfRunning = value;
        }

        public bool Focus
        {
            get => Item.Focus;
            set => Item.Focus = value;
        }

        public string PlacementSummary => WorkspaceDisplay.PlacementSummary(Item.Placement);
    }
}
