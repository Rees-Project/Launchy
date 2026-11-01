using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launchy.Models;

namespace Launchy.Services;

public sealed class CommandStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launchy");

    public string CommandsPath { get; } = Path.Combine(DataDirectory, "commands.json");

    public IReadOnlyList<LaunchCommand> Load()
    {
        if (!File.Exists(CommandsPath))
        {
            Save(DefaultCommands());
        }

        var file = JsonSerializer.Deserialize<CommandFile>(File.ReadAllText(CommandsPath), JsonOptions);
        return file?.Commands ?? [];
    }

    public void Save(IEnumerable<LaunchCommand> commands)
    {
        Directory.CreateDirectory(DataDirectory);
        var file = new CommandFile { Commands = commands.ToList() };

        // Write to a temp file first so a crash never leaves a half-written commands.json.
        var tempPath = CommandsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(file, JsonOptions));
        File.Move(tempPath, CommandsPath, overwrite: true);
    }

    /// <summary>
    /// Returns the exact match for <paramref name="text"/>, or the only command starting with it.
    /// </summary>
    public static LaunchCommand? Find(IReadOnlyList<LaunchCommand> commands, string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var exact = commands.FirstOrDefault(c => c.Command.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var prefixMatches = commands
            .Where(c => c.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return prefixMatches.Count == 1 ? prefixMatches[0] : null;
    }

    private static List<LaunchCommand> DefaultCommands() =>
    [
        new() { Command = "notepad", Type = TargetType.Application, Location = "notepad.exe" },
        new() { Command = "appdata", Type = TargetType.Folder, Location = "%APPDATA%" },
        new() { Command = "launchy", Type = TargetType.Website, Location = "https://github.com/Rees-Project/Launchy" },
        new() { Command = "commands", Type = TargetType.File, Location = @"%APPDATA%\Launchy\commands.json" }
    ];
}
