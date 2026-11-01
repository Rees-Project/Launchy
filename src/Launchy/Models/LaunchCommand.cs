namespace Launchy.Models;

public enum TargetType
{
    Application,
    Folder,
    File,
    Website,
    Script
}

public sealed class LaunchCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Command { get; set; } = "";
    public TargetType Type { get; set; }
    public string Location { get; set; } = "";
    public string? Parameters { get; set; }
    public string? Script { get; set; }
    public bool RunAsAdmin { get; set; }
}

public sealed class CommandFile
{
    public int Version { get; set; } = 1;
    public List<LaunchCommand> Commands { get; set; } = [];
}
