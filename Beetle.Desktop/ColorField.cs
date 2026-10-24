using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Beetle.Core;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using MediaColor = Avalonia.Media.Color;

namespace Beetle.Desktop;

/// <summary>
/// A hand-built HSV picker: a saturation/value square over a hue strip. Dragging reports colour
/// continuously (committed = false) and once more on release (committed = true), so the caller can
/// stream updates gently while dragging and send a definite value when the pointer lifts.
/// </summary>
internal sealed class ColorField
{
    private const double BoxHeight = 170;
    private const double HueHeight = 20;

    private double _hue;
    private double _sat = 1;
    private double _val = 1;

    private readonly Grid _box;
    private readonly Rectangle _boxHue;      // white -> current hue, left to right
    private readonly Canvas _boxThumbLayer;
    private readonly Border _boxThumb;
    private readonly Grid _hueBar;
    private readonly Canvas _hueThumbLayer;
    private readonly Border _hueThumb;

    public Control View { get; }

    /// <summary>Fires on every change. The bool is true only on pointer release (a committed pick).</summary>
    public event Action<RgbColor, bool>? ColorChanged;

    public RgbColor Color
    {
        get => RgbColor.FromHsv(_hue, _sat, _val);
        set
        {
            var (h, s, v) = value.ToHsv();
            _hue = h; _sat = s; _val = v;
            RefreshHueFill();
            PositionThumbs();
        }
    }

    public ColorField()
    {
        _boxHue = new Rectangle { RadiusX = 10, RadiusY = 10 };
        var boxShade = new Rectangle
        {
            RadiusX = 10, RadiusY = 10,
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 1)],
            },
        };
        _boxThumb = Ring();
        _boxThumbLayer = new Canvas();
        _box = new Grid { Height = BoxHeight, Cursor = new Cursor(StandardCursorType.Cross) };
        _box.Children.Add(_boxHue);
        _box.Children.Add(boxShade);
        _box.Children.Add(_boxThumbLayer);
        _boxThumbLayer.Children.Add(_boxThumb);

        var rainbow = new Rectangle
        {
            RadiusX = 7, RadiusY = 7,
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(MediaColor.FromRgb(255, 0, 0), 0.0),
                    new GradientStop(MediaColor.FromRgb(255, 255, 0), 1.0 / 6),
                    new GradientStop(MediaColor.FromRgb(0, 255, 0), 2.0 / 6),
                    new GradientStop(MediaColor.FromRgb(0, 255, 255), 3.0 / 6),
                    new GradientStop(MediaColor.FromRgb(0, 0, 255), 4.0 / 6),
                    new GradientStop(MediaColor.FromRgb(255, 0, 255), 5.0 / 6),
                    new GradientStop(MediaColor.FromRgb(255, 0, 0), 1.0),
                ],
            },
        };
        _hueThumb = new Border
        {
            Width = 10, Height = HueHeight + 6, CornerRadius = new CornerRadius(5),
            Background = Brushes.White, BorderBrush = new SolidColorBrush(MediaColor.FromArgb(120, 0, 0, 0)), BorderThickness = new Thickness(1),
        };
        _hueThumbLayer = new Canvas();
        _hueBar = new Grid { Height = HueHeight, Cursor = new Cursor(StandardCursorType.Hand) };
        _hueBar.Children.Add(rainbow);
        _hueBar.Children.Add(_hueThumbLayer);
        _hueThumbLayer.Children.Add(_hueThumb);

        _box.PointerPressed += (_, e) => DragBox(e, committed: false, capture: true);
        _box.PointerMoved += (_, e) => { if (e.GetCurrentPoint(_box).Properties.IsLeftButtonPressed) DragBox(e, committed: false, capture: false); };
        _box.PointerReleased += (_, e) => DragBox(e, committed: true, capture: false);
        _hueBar.PointerPressed += (_, e) => DragHue(e, committed: false);
        _hueBar.PointerMoved += (_, e) => { if (e.GetCurrentPoint(_hueBar).Properties.IsLeftButtonPressed) DragHue(e, committed: false); };
        _hueBar.PointerReleased += (_, e) => DragHue(e, committed: true);

        _box.SizeChanged += (_, _) => PositionThumbs();
        _hueBar.SizeChanged += (_, _) => PositionThumbs();

        RefreshHueFill();
        View = Ui.Stack(12, _box, _hueBar);
    }

    private static Border Ring() => new()
    {
        Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
        Background = Brushes.Transparent, BorderBrush = Brushes.White, BorderThickness = new Thickness(2.5),
        BoxShadow = new BoxShadows(new BoxShadow { Blur = 2, Color = MediaColor.FromArgb(140, 0, 0, 0) }),
    };

    private void DragBox(PointerEventArgs e, bool committed, bool capture)
    {
        var size = _box.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        if (capture) e.Pointer.Capture(_box);
        var p = e.GetPosition(_box);
        _sat = Math.Clamp(p.X / size.Width, 0, 1);
        _val = Math.Clamp(1 - p.Y / size.Height, 0, 1);
        PositionThumbs();
        ColorChanged?.Invoke(Color, committed);
    }

    private void DragHue(PointerEventArgs e, bool committed)
    {
        var width = _hueBar.Bounds.Width;
        if (width <= 0) return;
        if (!committed) e.Pointer.Capture(_hueBar);
        var x = Math.Clamp(e.GetPosition(_hueBar).X / width, 0, 1);
        _hue = x * 360;
        RefreshHueFill();
        PositionThumbs();
        ColorChanged?.Invoke(Color, committed);
    }

    private void RefreshHueFill()
    {
        var hue = RgbColor.FromHsv(_hue, 1, 1);
        _boxHue.Fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Colors.White, 0), new GradientStop(MediaColor.FromRgb(hue.R, hue.G, hue.B), 1)],
        };
    }

    private void PositionThumbs()
    {
        var box = _box.Bounds.Size;
        if (box.Width > 0 && box.Height > 0)
        {
            Canvas.SetLeft(_boxThumb, _sat * box.Width - _boxThumb.Width / 2);
            Canvas.SetTop(_boxThumb, (1 - _val) * box.Height - _boxThumb.Height / 2);
        }
        var hueWidth = _hueBar.Bounds.Width;
        if (hueWidth > 0)
        {
            Canvas.SetLeft(_hueThumb, _hue / 360 * hueWidth - _hueThumb.Width / 2);
            Canvas.SetTop(_hueThumb, (HueHeight - _hueThumb.Height) / 2);
        }
    }
}
