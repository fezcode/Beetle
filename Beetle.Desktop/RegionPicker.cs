using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Beetle.Core;

namespace Beetle.Desktop;

/// <summary>
/// A dimmed, see-through overlay across the primary monitor for dragging out the area ambient mode
/// should sample. It reports the rectangle as fractions of the screen, the same space the sampler
/// uses, so DPI scaling cancels out. Escape or a right click cancels.
/// </summary>
internal sealed class RegionPicker : Window
{
    private readonly TaskCompletionSource<SampleRegion?> _result = new();
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Cross) };
    private readonly Border _selection;
    private Point? _start;

    private RegionPicker(SampleRegion current)
    {
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));

        _selection = new Border
        {
            BorderBrush = Themes.Brush(p => p.Accent), BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), IsVisible = false,
        };
        _canvas.Children.Add(_selection);

        var hint = new Border
        {
            Background = Themes.Brush(p => p.Card), BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 12),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 0, 0), IsHitTestVisible = false,
            Child = Ui.Stack(4,
                Ui.Text("Drag to choose what ambient mode follows", 15, p => p.TextBright, FontWeight.SemiBold),
                Ui.MutedText("Release to apply  ·  Esc or right-click to cancel", 11)),
        };

        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(hint);
        Content = root;

        Opened += (_, _) =>
        {
            CoverPrimaryScreen();
            ShowExisting(current);
            Activate();
            Focus();
        };
        _canvas.PointerPressed += OnPressed;
        _canvas.PointerMoved += OnMoved;
        _canvas.PointerReleased += OnReleased;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };
        Closed += (_, _) => _result.TrySetResult(null);
    }

    /// <summary>Shows the overlay and resolves with the dragged area, or null if cancelled.</summary>
    public static Task<SampleRegion?> PickAsync(SampleRegion current)
    {
        var picker = new RegionPicker(current);
        picker.Show();
        return picker._result.Task;
    }

    private void CoverPrimaryScreen()
    {
        var screen = Screens.Primary;
        if (screen is null) { WindowState = WindowState.FullScreen; return; }
        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;
    }

    // Outlines the area currently in use, so the starting point is visible before dragging.
    private void ShowExisting(SampleRegion region)
    {
        if (region == SampleRegion.Full) return;
        var size = new Size(Width, Height);
        Place(new Point(region.X * size.Width, region.Y * size.Height),
              new Point((region.X + region.Width) * size.Width, (region.Y + region.Height) * size.Height));
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas);
        if (point.Properties.IsRightButtonPressed) { Finish(null); return; }
        _start = point.Position;
        e.Pointer.Capture(_canvas);
        Place(point.Position, point.Position);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_start is { } start) Place(start, e.GetPosition(_canvas));
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_start is not { } start) return;
        var end = e.GetPosition(_canvas);
        var size = _canvas.Bounds.Size;
        _start = null;
        if (size.Width <= 0 || size.Height <= 0) { Finish(null); return; }
        Finish(SampleRegion.FromCorners(start.X / size.Width, start.Y / size.Height, end.X / size.Width, end.Y / size.Height));
    }

    private void Place(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X); var y = Math.Min(a.Y, b.Y);
        Canvas.SetLeft(_selection, x); Canvas.SetTop(_selection, y);
        _selection.Width = Math.Abs(b.X - a.X); _selection.Height = Math.Abs(b.Y - a.Y);
        _selection.IsVisible = true;
    }

    private void Finish(SampleRegion? region)
    {
        _result.TrySetResult(region);
        Close();
    }
}
