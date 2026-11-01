using System.Diagnostics;
using Launchy.Models;

namespace Launchy.Services;

public static class LaunchService
{
    public static void Run(LaunchCommand command)
    {
        if (command.Type == TargetType.Script)
        {
            throw new NotSupportedException("Script commands are not supported yet.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(command.Location),
            Arguments = command.Parameters ?? "",
            UseShellExecute = true
        };

        if (command.RunAsAdmin)
        {
            startInfo.Verb = "runas";
        }

        Process.Start(startInfo);
    }
}
