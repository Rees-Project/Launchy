namespace Launchy.Services.Windows;

public interface IWindowSource
{
    IReadOnlyList<WindowInfo> GetTopLevelWindows();
    IReadOnlyList<MonitorInfo> GetMonitors();
    IntPtr GetForegroundWindow();
}
