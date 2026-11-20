namespace Launchy.Models;

public sealed class Workspace
{
    public string Name { get; set; } = "";
    public List<WorkspaceItem> Items { get; set; } = [];
}

public sealed class WorkspaceFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<Workspace> Workspaces { get; set; } = [];
}
