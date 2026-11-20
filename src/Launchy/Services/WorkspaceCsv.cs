using System.IO;
using System.Text;
using System.Text.Json;
using Launchy.Models;

namespace Launchy.Services;

public sealed record WorkspaceCsvResult(List<Workspace> Workspaces, List<string> Errors);

/// <summary>
/// Imports and exports workspaces as CSV with the columns Name and Items. Items is the item list as compact JSON.
/// </summary>
public static class WorkspaceCsv
{
    private static readonly string[] Header = ["Name", "Items"];

    public static void Export(string path, IEnumerable<Workspace> workspaces)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        Write(writer, workspaces);
    }

    public static WorkspaceCsvResult Import(string path, IEnumerable<LaunchCommand> commands, IEnumerable<Workspace> existing)
    {
        using var reader = new StreamReader(path);
        return Read(reader, commands, existing);
    }

    public static void Write(TextWriter writer, IEnumerable<Workspace> workspaces)
    {
        CommandCsv.WriteRow(writer, Header);
        foreach (var w in workspaces)
        {
            CommandCsv.WriteRow(writer, [w.Name, DataFiles.ToCompactJson(w.Items)]);
        }
    }

    /// <summary>
    /// Reads workspaces from CSV. A row with an empty name, bad JSON, or a name that clashes with a command,
    /// an existing workspace or an earlier row is skipped and reported in <see cref="WorkspaceCsvResult.Errors"/>.
    /// </summary>
    public static WorkspaceCsvResult Read(TextReader reader, IEnumerable<LaunchCommand> commands, IEnumerable<Workspace> existing)
    {
        var rows = CommandCsv.ParseRows(reader.ReadToEnd());
        var result = new WorkspaceCsvResult([], []);
        if (rows.Count == 0)
        {
            return result;
        }

        var columns = rows[0]
            .Select((name, index) => (name: name.Trim(), index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        if (!columns.TryGetValue("Name", out var nameIndex) || !columns.TryGetValue("Items", out var itemsIndex))
        {
            throw new FormatException("The CSV file needs \"Name\" and \"Items\" header columns.");
        }

        var commandList = commands.ToList();
        var taken = existing.ToList();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var name = nameIndex < row.Count ? row[nameIndex].Trim() : "";
            if (name.Length == 0)
            {
                continue;
            }

            var nameError = WorkspaceStore.ValidateName(name, commandList, taken);
            if (nameError is not null)
            {
                result.Errors.Add($"Row {r + 1}: skipped. {nameError}");
                continue;
            }

            var json = itemsIndex < row.Count ? row[itemsIndex].Trim() : "";
            List<WorkspaceItem>? items;
            try
            {
                items = json.Length == 0 ? null : DataFiles.FromJson<List<WorkspaceItem>>(json);
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"Row {r + 1}: skipped \"{name}\". Items is not valid JSON ({ex.Message})");
                continue;
            }

            if (items is null)
            {
                result.Errors.Add($"Row {r + 1}: skipped \"{name}\". Items is empty.");
                continue;
            }

            var workspace = new Workspace { Name = name, Items = items };
            result.Workspaces.Add(workspace);
            taken.Add(workspace);
        }

        return result;
    }
}
