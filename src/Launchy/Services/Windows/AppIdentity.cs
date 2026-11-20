namespace Launchy.Services.Windows;

/// <summary>Decides which AppUserModelID (AUMID) a window gets. Pure: the readers are passed in.</summary>
public static class AppIdentity
{
    public const string StoreAppFrameClass = "ApplicationFrameWindow";

    /// <summary>True for an exe inside a package install folder, for example the Claude desktop app.</summary>
    public static bool IsPackagedPath(string processPath) =>
        processPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Store app frame: the AUMID of the hosted CoreWindow's process, else the frame's own AUMID property
    /// (the only source while the app is minimized or suspended, because the CoreWindow child is gone then).
    /// Packaged desktop app: the process AUMID. Other windows: null.
    /// The readers are only called when needed, because each one opens a process or a property store.
    /// </summary>
    public static string? Resolve(string className, string processPath, Func<string?> coreWindowProcessAumid,
                                  Func<string?> windowPropertyAumid, Func<string?> processAumid)
    {
        if (className == StoreAppFrameClass)
        {
            return NullIfEmpty(coreWindowProcessAumid()) ?? NullIfEmpty(windowPropertyAumid());
        }

        return IsPackagedPath(processPath) ? NullIfEmpty(processAumid()) : null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
