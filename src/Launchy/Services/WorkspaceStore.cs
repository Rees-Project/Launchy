using System.IO;
using Launchy.Models;

namespace Launchy.Services;

public sealed class WorkspaceVersionException(string path, int fileVersion) : Exception(
    $"{path} has version {fileVersion}, but this build supports up to version {WorkspaceFile.CurrentVersion}. " +
    "Update Launchy. The file was not changed.")
{
}

public sealed class WorkspaceStore(string? path = null)
{
    private bool _writeBlocked;

    public string WorkspacesPath { get; } = path ?? DataFiles.PathFor("workspaces.json");

    /// <summary>
    /// Loads the workspaces. A missing file gives an empty list.
    /// Throws <see cref="WorkspaceVersionException"/> for a newer file; after that <see cref="Save"/> refuses to write.
    /// </summary>
    public IReadOnlyList<Workspace> Load()
    {
        if (!File.Exists(WorkspacesPath))
        {
            return [];
        }

        var file = DataFiles.Read<WorkspaceFile>(WorkspacesPath);
        if (file is null)
        {
            return [];
        }

        if (file.Version > WorkspaceFile.CurrentVersion)
        {
            _writeBlocked = true;
            throw new WorkspaceVersionException(WorkspacesPath, file.Version);
        }

        return file.Workspaces;
    }

    public void Save(IEnumerable<Workspace> workspaces)
    {
        if (_writeBlocked)
        {
            throw new InvalidOperationException(
                $"{WorkspacesPath} was written by a newer Launchy. It is not saved to in this session.");
        }

        DataFiles.Write(WorkspacesPath, new WorkspaceFile { Workspaces = workspaces.ToList() });
    }

    /// <summary>
    /// Returns an error message when <paramref name="name"/> is empty, matches a command name,
    /// or matches another workspace name (case-insensitive). Returns null when the name is free.
    /// </summary>
    public static string? ValidateName(
        string name,
        IEnumerable<LaunchCommand> commands,
        IEnumerable<Workspace> existingWorkspaces)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return "A workspace needs a name.";
        }

        if (commands.Any(c => c.Command.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return $"'{name}' is already a command name. Choose another workspace name.";
        }

        if (existingWorkspaces.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return $"A workspace named '{name}' already exists.";
        }

        return null;
    }
}
