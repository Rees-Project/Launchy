using System.IO;
using System.Text;
using Launchy.Models;

namespace Launchy.Services;

/// <summary>
/// Imports and exports commands as RFC 4180 CSV.
/// </summary>
public static class CommandCsv
{
    private static readonly string[] Header = ["Command", "Type", "Location", "Parameters", "Script", "RunAsAdmin"];

    public static void Export(string path, IEnumerable<LaunchCommand> commands)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        Write(writer, commands);
    }

    public static List<LaunchCommand> Import(string path)
    {
        using var reader = new StreamReader(path);
        return Read(reader);
    }

    public static void Write(TextWriter writer, IEnumerable<LaunchCommand> commands)
    {
        WriteRow(writer, Header);
        foreach (var c in commands)
        {
            WriteRow(writer, [c.Command, c.Type.ToString(), c.Location, c.Parameters ?? "", c.Script ?? "", c.RunAsAdmin.ToString()]);
        }
    }

    /// <summary>
    /// Reads commands from CSV, skipping blank rows and duplicate command names within the file.
    /// </summary>
    public static List<LaunchCommand> Read(TextReader reader)
    {
        var rows = ParseRows(reader.ReadToEnd());
        if (rows.Count == 0)
        {
            return [];
        }

        var columns = rows[0]
            .Select((name, index) => (name: name.Trim(), index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        if (!columns.ContainsKey("Command"))
        {
            throw new FormatException("The CSV file needs a \"Command\" header column.");
        }

        string Field(List<string> row, string name) =>
            columns.TryGetValue(name, out var i) && i < row.Count ? row[i].Trim() : "";

        var commands = new List<LaunchCommand>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var name = Field(row, "Command");
            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            var typeText = Field(row, "Type");
            if (!Enum.TryParse<TargetType>(typeText, true, out var type))
            {
                throw new FormatException($"Row {r + 1}: \"{typeText}\" is not a valid type.");
            }

            commands.Add(new LaunchCommand
            {
                Command = name,
                Type = type,
                Location = Field(row, "Location"),
                Parameters = NullIfEmpty(Field(row, "Parameters")),
                Script = NullIfEmpty(Field(row, "Script")),
                RunAsAdmin = bool.TryParse(Field(row, "RunAsAdmin"), out var admin) && admin
            });
        }

        return commands;
    }

    /// <summary>
    /// Returns the imported commands whose names aren't already in <paramref name="existing"/>.
    /// </summary>
    public static List<LaunchCommand> NewCommands(IEnumerable<LaunchCommand> existing, IEnumerable<LaunchCommand> imported)
    {
        var names = new HashSet<string>(existing.Select(c => c.Command.Trim()), StringComparer.OrdinalIgnoreCase);
        return imported.Where(c => names.Add(c.Command.Trim())).ToList();
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static void WriteRow(TextWriter writer, IEnumerable<string> fields)
    {
        writer.Write(string.Join(",", fields.Select(Escape)));
        writer.Write("\r\n");
    }

    private static string Escape(string field) =>
        field.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;

    private static List<List<string>> ParseRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        void EndRow()
        {
            row.Add(field.ToString());
            field.Clear();
            if (row.Count > 1 || row[0].Length > 0)
            {
                rows.Add(row);
            }
            row = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                EndRow();
            }
            else
            {
                field.Append(ch);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            EndRow();
        }

        return rows;
    }
}
