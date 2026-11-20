using System.Diagnostics;
using Launchy.Models;

namespace Launchy.Services;

public static class LaunchService
{
    public static void Run(LaunchCommand command)
    {
        Process.Start(BuildStartInfo(command.Type, command.Location, command.Parameters, command.RunAsAdmin));
    }

    public static Process? Start(WorkspaceItem item)
    {
        if (item.Type == TargetType.Website && item.AllUrls().Count > 1)
        {
            return StartWebsiteTabs(item.AllUrls());
        }

        return Process.Start(BuildStartInfo(item.Type, item.Location, item.Parameters, runAsAdmin: false));
    }

    private static Process? StartWebsiteTabs(IReadOnlyList<string> urls)
    {
        if (BrowserLaunch.GetDefaultBrowserPath() is { } browser)
        {
            var command = BrowserLaunch.BuildTabsCommand(browser, urls);
            return Process.Start(new ProcessStartInfo(command.FileName, command.Arguments) { UseShellExecute = true });
        }

        Process? first = null;
        foreach (var url in urls)
        {
            var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            first ??= process;
        }

        return first;
    }

    private static ProcessStartInfo BuildStartInfo(TargetType type, string location, string? parameters, bool runAsAdmin)
    {
        if (type == TargetType.Script)
        {
            throw new NotSupportedException("Script commands are not supported yet.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(location),
            Arguments = parameters ?? "",
            UseShellExecute = true
        };

        if (runAsAdmin)
        {
            startInfo.Verb = "runas";
        }

        return startInfo;
    }
}

public interface IItemLauncher
{
    Process? Start(WorkspaceItem item);
}

public sealed class ItemLauncher : IItemLauncher
{
    public Process? Start(WorkspaceItem item) => LaunchService.Start(item);
}
