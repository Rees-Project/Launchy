namespace Launchy.Services.Windows;

/// <summary>
/// Tells when a top-level window of another process was shown or uncloaked.
/// Only a hint to look again sooner; it carries no window data.
/// </summary>
public interface IWindowShowEvents
{
    /// <summary>
    /// Calls onShown on the subscribing thread for each event until the result is disposed.
    /// Null when the events are not available; callers then poll only.
    /// </summary>
    IDisposable? Subscribe(Action onShown);
}
