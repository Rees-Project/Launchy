namespace Launchy.Services.Windows;

/// <param name="Device">GDI name such as <c>\\.\DISPLAY1</c>. It can change after a driver or cable change.</param>
/// <param name="Id">Monitor device interface path, stable per physical monitor and connection. Empty when Windows gives none.</param>
public sealed record MonitorInfo(string Device, IntRect Bounds, IntRect WorkArea, int Dpi, bool IsPrimary, string Id = "");
