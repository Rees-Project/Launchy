using System.IO;

namespace Launchy.Services.Windows;

/// <summary>Debug output for the hidden <c>--dump-windows</c> argument.</summary>
public static class WindowDump
{
    public const string Argument = "--dump-windows";

    public static void Write(IWindowSource source, TextWriter output)
    {
        output.WriteLine("Monitors:");
        foreach (var m in source.GetMonitors())
        {
            output.WriteLine($"  {m.Device}{(m.IsPrimary ? " primary" : "")} bounds={m.Bounds} work={m.WorkArea} dpi={m.Dpi} id={(m.Id.Length == 0 ? "<none>" : m.Id)}");
        }

        var foreground = source.GetForegroundWindow();
        var windows = source.GetTopLevelWindows();
        output.WriteLine();
        output.WriteLine($"Windows ({windows.Count}):");
        foreach (var w in windows)
        {
            var state = w.IsMinimized ? "min" : w.IsMaximized ? "max" : w.IsArranged ? "arranged" : "normal";
            var frame = Win32WindowSource.GetExtendedFrameBounds(w.Handle);
            output.WriteLine($"  0x{w.Handle.ToInt64():X} {(w.Handle == foreground ? "*" : " ")} \"{Trim(w.Title, 60)}\"");
            output.WriteLine($"      class={w.ClassName} pid={w.ProcessId} path={(w.ProcessPath.Length == 0 ? "<none>" : w.ProcessPath)}");
            output.WriteLine($"      rect={w.Rect} frame={frame?.ToString() ?? "<none>"} normal={w.NormalRect} showCmd={w.ShowCmd} state={state} resizable={w.IsResizable} monitor={w.MonitorDevice}");
            if (w.FolderPath is not null)
            {
                output.WriteLine($"      folder={w.FolderPath}");
            }

            if (w.AppUserModelId is not null)
            {
                output.WriteLine($"      aumid={w.AppUserModelId}");
            }
        }
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";
}
