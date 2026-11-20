using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Launchy.Services;

public sealed record BrowserCommand(string FileName, string Arguments, bool MayOpenAsTab);

/// <summary>
/// Browser knowledge for website items. Owner answer Q3 = No: launching does not use the new-window
/// flags. The table and <see cref="BuildCommand"/> stay available for a later switch.
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
