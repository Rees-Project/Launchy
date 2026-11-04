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

    public const int FirstMatchMinLength = 2;

    /// <summary>
    /// Returns the exact match for <paramref name="text"/>. Otherwise, once at least
    /// <see cref="FirstMatchMinLength"/> characters are typed, the first command starting with it;
    /// for shorter text, only a command that is the sole prefix match.
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
        if (text.Length >= FirstMatchMinLength)
        {
            return prefixMatches.FirstOrDefault();
        }

        return prefixMatches.Count == 1 ? prefixMatches[0] : null;
    }

    public static int CountPrefixMatches(IReadOnlyList<LaunchCommand> commands, string text)
    {
        text = text.Trim();
        return text.Length == 0
            ? 0
            : commands.Count(c => c.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase));
    }

    private static List<LaunchCommand> DefaultCommands() =>
    [
        new() { Command = "notepad", Type = TargetType.Application, Location = "notepad.exe" },
        new() { Command = "appdata", Type = TargetType.Folder, Location = "%APPDATA%" },
        new() { Command = "launchy", Type = TargetType.Website, Location = "https://github.com/Rees-Project/Launchy" },
        new() { Command = "commands", Type = TargetType.File, Location = @"%APPDATA%\Launchy\commands.json" }
    ];
}
