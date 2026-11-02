using System.IO;
using Launchy.Models;

namespace Launchy.Services;

public sealed class CommandStore
{
    public string CommandsPath { get; } = DataFiles.PathFor("commands.json");

    public IReadOnlyList<LaunchCommand> Load()
    {
        if (!File.Exists(CommandsPath))
        {
            Save(DefaultCommands());
        }

        return DataFiles.Read<CommandFile>(CommandsPath)?.Commands ?? [];
    }

    public void Save(IEnumerable<LaunchCommand> commands) =>
        DataFiles.Write(CommandsPath, new CommandFile { Commands = commands.ToList() });

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
