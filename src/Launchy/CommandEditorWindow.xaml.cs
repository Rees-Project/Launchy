using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Launchy.Models;
using Launchy.Services;

namespace Launchy;

public partial class CommandEditorWindow : Window
{
    private readonly CommandStore _store;
    private readonly ObservableCollection<LaunchCommand> _rows;
    private bool _dirty;

    public CommandEditorWindow(CommandStore store, IReadOnlyList<LaunchCommand> commands)
    {
        InitializeComponent();
        _store = store;

        TypeColumn.ItemsSource = Enum.GetValues<TargetType>();
        _rows = new ObservableCollection<LaunchCommand>(commands);
        _rows.CollectionChanged += (_, _) => _dirty = true;
        CommandsGrid.ItemsSource = _rows;
    }

    private void CommandsGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            _dirty = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var row = new LaunchCommand { Type = TargetType.Application };
        _rows.Add(row);

        CommandsGrid.SelectedItem = row;
        CommandsGrid.ScrollIntoView(row);
        CommandsGrid.CurrentCell = new DataGridCellInfo(row, CommandsGrid.Columns[0]);
        CommandsGrid.Focus();
        CommandsGrid.BeginEdit();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in CommandsGrid.SelectedItems.Cast<LaunchCommand>().ToList())
        {
            _rows.Remove(row);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = CsvFilter };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            CommandsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var imported = CommandCsv.Import(dialog.FileName);
            var added = CommandCsv.NewCommands(_rows, imported);
            foreach (var row in added)
            {
                _rows.Add(row);
            }

            var skipped = imported.Count - added.Count;
            StatusText.Text = $"Imported {added.Count} command(s)" +
                (skipped > 0 ? $", skipped {skipped} duplicate(s)" : "") + ". Press Save to keep them.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            StatusText.Text = $"Couldn't import: {ex.Message}";
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = CsvFilter, FileName = "launchy-commands.csv" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            CommandsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            CommandCsv.Export(dialog.FileName, _rows);
            StatusText.Text = $"Exported {_rows.Count} command(s).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Couldn't export: {ex.Message}";
        }
    }

    private const string CsvFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

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
        if (!_dirty)
        {
            return;
        }

        var answer = MessageBox.Show(this, "Save changes to your commands?", "Launchy",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !TrySave()))
        {
            e.Cancel = true;
        }
    }

    private bool TrySave()
    {
        CommandsGrid.CommitEdit(DataGridEditingUnit.Row, true);

        foreach (var row in _rows)
        {
            row.Command = row.Command.Trim();
            row.Location = row.Location.Trim();
            row.Parameters = string.IsNullOrWhiteSpace(row.Parameters) ? null : row.Parameters.Trim();
        }

        var error = Validate();
        if (error is not null)
        {
            StatusText.Text = error;
            return false;
        }

        try
        {
            _store.Save(_rows);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Couldn't save commands.json: {ex.Message}";
            return false;
        }

        _dirty = false;
        return true;
    }

    private string? Validate()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (row.Command.Length == 0)
            {
                return $"Row {i + 1}: Command is required.";
            }

            if (row.Location.Length == 0 && row.Type != TargetType.Script)
            {
                return $"\"{row.Command}\": Location is required.";
            }
        }

        var duplicate = _rows
            .GroupBy(r => r.Command, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        return duplicate is null ? null : $"\"{duplicate.Key}\" is used by more than one command.";
    }
}
