using Launchy.Models;

namespace Launchy.Services;

/// <summary>One thing the launcher can run: a command or a workspace. Exactly one of the two is set.</summary>
public sealed record LauncherEntry(string Name, LaunchCommand? Command, Workspace? Workspace)
{
    /// <summary>Commands first, then workspaces. A workspace whose name clashes with a command is hidden; the command wins.</summary>
    public static IReadOnlyList<LauncherEntry> Build(IEnumerable<LaunchCommand> commands, IEnumerable<Workspace> workspaces)
    {
        var entries = commands.Select(c => new LauncherEntry(c.Command, c, null)).ToList();
        var taken = entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var workspace in workspaces)
        {
            if (taken.Add(workspace.Name))
            {
                entries.Add(new LauncherEntry(workspace.Name, null, workspace));
            }
        }

        return entries;
    }

    /// <summary>Same rules as <see cref="CommandStore.Matches"/>, over commands and workspaces.</summary>
    public static IReadOnlyList<LauncherEntry> Matches(IReadOnlyList<LauncherEntry> entries, string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        return entries
            .Where(e => e.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Name.Equals(text, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Same rules as <see cref="CommandStore.Find"/>, over commands and workspaces.</summary>
    public static LauncherEntry? Find(IReadOnlyList<LauncherEntry> entries, string text)
    {
        var matches = Matches(entries, text);
        if (matches.Count == 0)
        {
            return null;
        }

        var isExact = matches[0].Name.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase);
        return isExact || matches.Count == 1 || text.Trim().Length >= CommandStore.FirstMatchMinLength ? matches[0] : null;
    }
}
