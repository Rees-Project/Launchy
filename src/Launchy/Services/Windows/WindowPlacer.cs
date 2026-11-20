using Launchy.Models;
using WindowState = Launchy.Models.WindowState;

namespace Launchy.Services.Windows;

public enum PlaceStatus
{
    Success,
    AccessDenied,
    WindowGone,
    Mismatch,
    Failed
}

public sealed record PlaceResult(PlaceStatus Status, string? Reason = null)
{
    public bool Success => Status == PlaceStatus.Success;

    public static PlaceResult Ok { get; } = new(PlaceStatus.Success);
}

public enum RecheckStatus
{
    InPlace,
    Reapplied,
    WindowGone,
    Failed
}

public interface IWindowPlacer
{
    PlaceResult Place(IntPtr hwnd, PlacementTarget target);

    /// <summary>Reads the window rect again and applies the target once more if the app moved the window itself.</summary>
    RecheckStatus Recheck(IntPtr hwnd, PlacementTarget target);
}

/// <summary>The window calls the placer needs. Each call returns 0 on success or the Win32 error code.</summary>
public interface IWindowOperations
{
    int SetNormalPlacement(IntPtr hwnd, IntRect normalRect);

    int SetPosition(IntPtr hwnd, IntRect rect);

    int Show(IntPtr hwnd, int showCommand);

    int GetRect(IntPtr hwnd, out IntRect rect);
}

/// <summary>Moves one window to its target without activating it.</summary>
public sealed class WindowPlacer : IWindowPlacer
{
    public const int TolerancePx = 2;
    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidWindowHandle = 1400;

    private readonly IWindowOperations _ops;

    public WindowPlacer() : this(new Win32WindowOperations())
    {
    }

    public WindowPlacer(IWindowOperations operations)
    {
        _ops = operations;
    }

    public PlaceResult Place(IntPtr hwnd, PlacementTarget target)
    {
        var error = Apply(hwnd, target);
        if (error != 0)
        {
            return Fail(error);
        }

        if (target.State is not (WindowState.Normal or WindowState.Snapped))
        {
            return PlaceResult.Ok;
        }

        var rectError = _ops.GetRect(hwnd, out var actual);
        if (rectError != 0)
        {
            return Fail(rectError);
        }

        if (IsWithinTolerance(actual, target.Rect))
        {
            return PlaceResult.Ok;
        }

        error = Apply(hwnd, target);
        if (error != 0)
        {
            return Fail(error);
        }

        rectError = _ops.GetRect(hwnd, out actual);
        if (rectError != 0)
        {
            return Fail(rectError);
        }

        return IsWithinTolerance(actual, target.Rect)
            ? PlaceResult.Ok
            : new PlaceResult(PlaceStatus.Mismatch, $"window is at {actual}, expected {target.Rect}, after retry");
    }

    public RecheckStatus Recheck(IntPtr hwnd, PlacementTarget target)
    {
        if (target.State is not (WindowState.Normal or WindowState.Snapped))
        {
            return RecheckStatus.InPlace;
        }

        var rectError = _ops.GetRect(hwnd, out var actual);
        if (rectError != 0)
        {
            return ToRecheckStatus(rectError);
        }

        if (IsWithinTolerance(actual, target.Rect))
        {
            return RecheckStatus.InPlace;
        }

        var error = Apply(hwnd, target);
        return error == 0 ? RecheckStatus.Reapplied : ToRecheckStatus(error);
    }

    private static RecheckStatus ToRecheckStatus(int error) =>
        error == ErrorInvalidWindowHandle ? RecheckStatus.WindowGone : RecheckStatus.Failed;

    private int Apply(IntPtr hwnd, PlacementTarget target)
    {
        var error = _ops.SetNormalPlacement(hwnd, target.NormalRect);
        if (error != 0)
        {
            return error;
        }

        switch (target.State)
        {
            case WindowState.Snapped:
                return _ops.SetPosition(hwnd, target.Rect);
            case WindowState.Maximized:
                return _ops.Show(hwnd, NativeMethods.SW_MAXIMIZE);
            case WindowState.Minimized:
                return _ops.Show(hwnd, NativeMethods.SW_SHOWMINNOACTIVE);
            default:
                return 0;
        }
    }

    private static bool IsWithinTolerance(IntRect actual, IntRect expected) =>
        Math.Abs(actual.Left - expected.Left) <= TolerancePx
        && Math.Abs(actual.Top - expected.Top) <= TolerancePx
        && Math.Abs(actual.Right - expected.Right) <= TolerancePx
        && Math.Abs(actual.Bottom - expected.Bottom) <= TolerancePx;

    private static PlaceResult Fail(int error) => error switch
    {
        ErrorAccessDenied => new PlaceResult(PlaceStatus.AccessDenied, "window is elevated"),
        ErrorInvalidWindowHandle => new PlaceResult(PlaceStatus.WindowGone, "window is gone"),
        _ => new PlaceResult(PlaceStatus.Failed, $"Win32 error {error}")
    };
}

internal sealed class Win32WindowOperations : IWindowOperations
{
    public int SetNormalPlacement(IntPtr hwnd, IntRect normalRect)
    {
        var placement = new NativeMethods.WINDOWPLACEMENT
        {
            length = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>()
        };
        if (!NativeMethods.GetWindowPlacement(hwnd, ref placement))
        {
            return LastError();
        }

        placement.showCmd = NativeMethods.SW_SHOWNOACTIVATE;
        placement.rcNormalPosition = new NativeMethods.RECT
        {
            Left = normalRect.Left,
            Top = normalRect.Top,
            Right = normalRect.Right,
            Bottom = normalRect.Bottom
        };

        return NativeMethods.SetWindowPlacement(hwnd, ref placement) ? 0 : LastError();
    }

    public int SetPosition(IntPtr hwnd, IntRect rect) =>
        NativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE)
            ? 0
            : LastError();

    public int Show(IntPtr hwnd, int showCommand)
    {
        NativeMethods.ShowWindow(hwnd, showCommand);
        return 0;
    }

    public int GetRect(IntPtr hwnd, out IntRect rect)
    {
        if (NativeMethods.GetWindowRect(hwnd, out var native))
        {
            rect = native.ToIntRect();
            return 0;
        }

        rect = new IntRect(0, 0, 0, 0);
        return LastError();
    }

    private static int LastError()
    {
        var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        return error == 0 ? -1 : error;
    }
}
