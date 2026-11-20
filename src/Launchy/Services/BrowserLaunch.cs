using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Launchy.Services;

public sealed record BrowserCommand(string FileName, string Arguments, bool MayOpenAsTab);

/// <summary>
/// Browser knowledge for website items. A website item opens with the plain shell open unless its
/// "New window" setting is on; then <see cref="BuildNewWindowCommand"/> adds the browser's new-window flag.
/// </summary>
public static class BrowserLaunch
{
    private static readonly (string ExeName, string NewWindowFlag)[] NewWindowFlags =
    [
        ("chrome.exe", "--new-window"),
        ("msedge.exe", "--new-window"),
        ("brave.exe", "--new-window"),
        ("vivaldi.exe", "--new-window"),
        ("opera.exe", "--new-window"),
        ("firefox.exe", "-new-window")
    ];

    public static BrowserCommand BuildCommand(string browserExe, string url)
    {
        var exeName = Path.GetFileName(browserExe);
        foreach (var (name, flag) in NewWindowFlags)
        {
            if (string.Equals(exeName, name, StringComparison.OrdinalIgnoreCase))
            {
                return new BrowserCommand(browserExe, $"{flag} \"{url}\"", MayOpenAsTab: false);
            }
        }

        return new BrowserCommand(browserExe, $"\"{url}\"", MayOpenAsTab: true);
    }

    /// <summary>
    /// One command line that opens every URL in a new browser window. Null when the browser has no
    /// known new-window flag, so the caller can fall back to the plain open.
    /// </summary>
    public static BrowserCommand? BuildNewWindowCommand(string browserExe, IEnumerable<string> urls)
    {
        var exeName = Path.GetFileName(browserExe);
        foreach (var (name, flag) in NewWindowFlags)
        {
            if (string.Equals(exeName, name, StringComparison.OrdinalIgnoreCase))
            {
                var arguments = string.Join(" ", urls.Select(url => $"\"{url.Replace("\"", "%22")}\""));
                return new BrowserCommand(browserExe, $"{flag} {arguments}", MayOpenAsTab: false);
            }
        }

        return null;
    }

    /// <summary>One command line with every URL quoted and no window flag, so the browser opens them as tabs.</summary>
    public static BrowserCommand BuildTabsCommand(string browserExe, IEnumerable<string> urls)
    {
        var arguments = string.Join(" ", urls.Select(url => $"\"{url.Replace("\"", "%22")}\""));
        return new BrowserCommand(browserExe, arguments, MayOpenAsTab: true);
    }

    public static string? GetDefaultBrowserPath()
    {
        const uint AssocfNone = 0;
        const int AssocstrExecutable = 2;

        uint length = 0;
        AssocQueryString(AssocfNone, AssocstrExecutable, "https", null, null, ref length);
        if (length == 0)
        {
            return null;
        }

        var buffer = new StringBuilder((int)length);
        var hr = AssocQueryString(AssocfNone, AssocstrExecutable, "https", null, buffer, ref length);
        return hr == 0 ? buffer.ToString() : null;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(
        uint flags, int str, string pszAssoc, string? pszExtra, StringBuilder? pszOut, ref uint pcchOut);
}
