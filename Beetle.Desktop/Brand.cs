using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Line = Avalonia.Controls.Shapes.Line;

namespace Beetle.Desktop;

/// <summary>Beetle's mark, drawn from <c>Assets/beetle.svg</c>, the same file the Windows icon and
/// README banner are rendered from. Each shape's class names a palette role, so the mark follows the
/// theme; a shape without a known class keeps the colour written in the SVG.</summary>
internal static class Brand
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static XDocument? _document;

    public static Control Mark(double size)
    {
        if (_document is null)
        {
            using var stream = AssetLoader.Open(new Uri("avares://Beetle/Assets/beetle.svg"));
            _document = XDocument.Load(stream);
        }
        var canvas = new Canvas { Width = 64, Height = 64 };
        foreach (var shape in _document.Root!.Elements())
        {
            var role = (string?)shape.Attribute("class");
            if (shape.Name == Svg + "rect")
            {
                var tile = new Border
                {
                    Width = Num(shape, "width"), Height = Num(shape, "height"), CornerRadius = new CornerRadius(Num(shape, "rx")),
                    Background = Fill(shape, role), BorderBrush = Stroke(shape, role), BorderThickness = new Thickness(Num(shape, "stroke-width")),
                };
                Canvas.SetLeft(tile, Num(shape, "x")); Canvas.SetTop(tile, Num(shape, "y"));
                canvas.Children.Add(tile);
            }
            else if (shape.Name == Svg + "ellipse")
            {
                double rx = Num(shape, "rx"), ry = Num(shape, "ry");
                var ellipse = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = Fill(shape, role) };
                Canvas.SetLeft(ellipse, Num(shape, "cx") - rx); Canvas.SetTop(ellipse, Num(shape, "cy") - ry);
                canvas.Children.Add(ellipse);
            }
            else if (shape.Name == Svg + "line")
            {
                canvas.Children.Add(new Line
                {
                    StartPoint = new Point(Num(shape, "x1"), Num(shape, "y1")), EndPoint = new Point(Num(shape, "x2"), Num(shape, "y2")),
                    Stroke = Stroke(shape, role), StrokeThickness = Num(shape, "stroke-width"), StrokeLineCap = PenLineCap.Round,
                });
            }
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static double Num(XElement shape, string name) =>
        double.Parse((string?)shape.Attribute(name) ?? "0", CultureInfo.InvariantCulture);

    private static IBrush Fill(XElement shape, string? role) => role switch
    {
        "tile" => Themes.Brush(p => p.Raised),
        "accent" => Themes.Brush(p => p.Accent),
        "ink" => Themes.Brush(p => p.Window),
        _ => Brush.Parse((string?)shape.Attribute("fill") ?? "#00000000"),
    };

    private static IBrush Stroke(XElement shape, string? role) => role switch
    {
        "tile" => Themes.Brush(p => p.LineStrong),
        "legs" => Themes.Brush(p => p.AccentMuted),
        "ink" => Themes.Brush(p => p.Window),
        _ => Brush.Parse((string?)shape.Attribute("stroke") ?? "#00000000"),
    };

    /// <summary>The mark beside the "beetle / BY FEZCODE" wordmark, for the title bar.</summary>
    public static Control Wordmark(double markSize = 26)
    {
        var name = Ui.Text("beetle", 16, p => p.TextBright, FontWeight.Bold);
        name.LineHeight = 18;
        var byline = Ui.Text("B Y  F E Z C O D E", 7, p => p.Faint);
        byline.LineHeight = 9;
        var lockup = Ui.Stack(3, name, byline);
        lockup.VerticalAlignment = VerticalAlignment.Center;
        return Ui.Row(12, Mark(markSize), lockup);
    }
}
