using Launchy.Models;
using Launchy.Services.Windows;
using Xunit;
using WindowState = Launchy.Models.WindowState;

namespace Launchy.Tests;

public class WindowPlacerTests
{
    private static readonly IntPtr Hwnd = new(42);
    private static readonly IntRect Rect = new(100, 100, 1100, 800);
    private static readonly IntRect NormalRect = new(100, 100, 1100, 800);

    private sealed class FakeOperations : IWindowOperations
    {
        public List<string> Calls { get; } = [];
        public Queue<IntRect> RectsAfterPlacement { get; } = new();
        public IntRect CurrentRect { get; set; } = Rect;
        public int PlacementError { get; set; }
        public int PositionError { get; set; }
        public int GetRectError { get; set; }

        public int SetNormalPlacement(IntPtr hwnd, IntRect normalRect)
        {
            Calls.Add("placement");
            return PlacementError;
        }

        public int SetPosition(IntPtr hwnd, IntRect rect)
        {
            Calls.Add("pos");
            return PositionError;
        }

        public int Show(IntPtr hwnd, int showCommand)
        {
            Calls.Add($"show{showCommand}");
            return 0;
        }

        public int GetRect(IntPtr hwnd, out IntRect rect)
        {
            Calls.Add("getrect");
            rect = RectsAfterPlacement.Count > 0 ? RectsAfterPlacement.Dequeue() : CurrentRect;
            return GetRectError;
        }
    }

    private static PlaceResult Place(FakeOperations ops, WindowState state) =>
        new WindowPlacer(ops).Place(Hwnd, new PlacementTarget(state, Rect, NormalRect));

    [Fact]
    public void Normal_sets_placement_and_checks_rect_once()
    {
        var ops = new FakeOperations();

        var result = Place(ops, WindowState.Normal);

        Assert.True(result.Success);
        Assert.Equal(["placement", "getrect"], ops.Calls);
    }

    [Fact]
    public void Snapped_sets_placement_then_position()
    {
        var ops = new FakeOperations();

        var result = Place(ops, WindowState.Snapped);

        Assert.True(result.Success);
        Assert.Equal(["placement", "pos", "getrect"], ops.Calls);
    }

    [Fact]
    public void Maximized_shows_maximized_and_skips_rect_check()
    {
        var ops = new FakeOperations();

        var result = Place(ops, WindowState.Maximized);

        Assert.True(result.Success);
        Assert.Equal(["placement", "show3"], ops.Calls);
    }

    [Fact]
    public void Minimized_shows_min_no_active_and_skips_rect_check()
    {
        var ops = new FakeOperations();

        var result = Place(ops, WindowState.Minimized);

        Assert.True(result.Success);
        Assert.Equal(["placement", "show7"], ops.Calls);
    }

    [Fact]
    public void Rect_within_two_pixels_is_accepted_without_retry()
    {
        var ops = new FakeOperations { CurrentRect = new IntRect(102, 98, 1098, 802) };

        var result = Place(ops, WindowState.Normal);

        Assert.True(result.Success);
        Assert.Equal(["placement", "getrect"], ops.Calls);
    }

    [Fact]
    public void Rect_off_by_three_pixels_retries_once_and_succeeds()
    {
        var ops = new FakeOperations();
        ops.RectsAfterPlacement.Enqueue(new IntRect(103, 100, 1100, 800));

        var result = Place(ops, WindowState.Snapped);

        Assert.True(result.Success);
        Assert.Equal(["placement", "pos", "getrect", "placement", "pos", "getrect"], ops.Calls);
    }

    [Fact]
    public void Mismatch_after_retry_fails_without_third_attempt()
    {
        var ops = new FakeOperations { CurrentRect = new IntRect(0, 0, 500, 500) };

        var result = Place(ops, WindowState.Normal);

        Assert.Equal(PlaceStatus.Mismatch, result.Status);
        Assert.Equal(2, ops.Calls.Count(c => c == "placement"));
    }

    [Fact]
    public void Access_denied_fails_as_elevated_without_retry()
    {
        var ops = new FakeOperations { PlacementError = WindowPlacer.ErrorAccessDenied };

        var result = Place(ops, WindowState.Normal);

        Assert.Equal(PlaceStatus.AccessDenied, result.Status);
        Assert.Equal("window is elevated", result.Reason);
        Assert.Equal(["placement"], ops.Calls);
    }

    [Fact]
    public void Access_denied_from_set_position_fails_without_retry()
    {
        var ops = new FakeOperations { PositionError = WindowPlacer.ErrorAccessDenied };

        var result = Place(ops, WindowState.Snapped);

        Assert.Equal(PlaceStatus.AccessDenied, result.Status);
        Assert.Equal(["placement", "pos"], ops.Calls);
    }

    [Fact]
    public void Invalid_handle_reports_window_gone()
    {
        var ops = new FakeOperations { PlacementError = WindowPlacer.ErrorInvalidWindowHandle };

        var result = Place(ops, WindowState.Maximized);

        Assert.Equal(PlaceStatus.WindowGone, result.Status);
    }

    [Fact]
    public void Other_errors_report_failed_with_code()
    {
        var ops = new FakeOperations { GetRectError = 87 };

        var result = Place(ops, WindowState.Normal);

        Assert.Equal(PlaceStatus.Failed, result.Status);
        Assert.Contains("87", result.Reason);
    }

    private static RecheckStatus Recheck(FakeOperations ops, WindowState state) =>
        new WindowPlacer(ops).Recheck(Hwnd, new PlacementTarget(state, Rect, NormalRect));

    [Fact]
    public void Recheck_in_place_only_reads_the_rect()
    {
        var ops = new FakeOperations();

        Assert.Equal(RecheckStatus.InPlace, Recheck(ops, WindowState.Normal));
        Assert.Equal(["getrect"], ops.Calls);
    }

    [Fact]
    public void Recheck_moved_window_is_applied_once_without_a_second_read()
    {
        var ops = new FakeOperations { CurrentRect = new IntRect(0, 0, 500, 500) };

        Assert.Equal(RecheckStatus.Reapplied, Recheck(ops, WindowState.Snapped));
        Assert.Equal(["getrect", "placement", "pos"], ops.Calls);
    }

    [Theory]
    [InlineData(WindowState.Maximized)]
    [InlineData(WindowState.Minimized)]
    public void Recheck_skips_maximized_and_minimized_windows(WindowState state)
    {
        var ops = new FakeOperations { CurrentRect = new IntRect(0, 0, 500, 500) };

        Assert.Equal(RecheckStatus.InPlace, Recheck(ops, state));
        Assert.Empty(ops.Calls);
    }

    [Fact]
    public void Recheck_invalid_handle_reports_window_gone()
    {
        var ops = new FakeOperations { GetRectError = WindowPlacer.ErrorInvalidWindowHandle };

        Assert.Equal(RecheckStatus.WindowGone, Recheck(ops, WindowState.Normal));
    }

    [Fact]
    public void Recheck_apply_error_reports_failed()
    {
        var ops = new FakeOperations
        {
            CurrentRect = new IntRect(0, 0, 500, 500),
            PlacementError = WindowPlacer.ErrorAccessDenied
        };

        Assert.Equal(RecheckStatus.Failed, Recheck(ops, WindowState.Normal));
    }
}
