using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Beetle.Desktop;

/// <summary>Small code-first building blocks, the same approach Airlift uses: resolve colours per
/// call so a theme change is picked up without rebuilding, and keep the call sites terse.</summary>
internal static class Ui
{
    public static IBrush Accent => Themes.Brush(p => p.Accent);
    public static IBrush Muted => Themes.Brush(p => p.Muted);
    public static IBrush Line => Themes.Brush(p => p.Line);

    public static TextBlock Text(string text, double size = 13, Func<Palette, string>? role = null, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = Themes.Brush(role ?? (p => p.Text)), FontWeight = weight ?? FontWeight.Normal,
        FontFamily = new FontFamily(size >= 18 ? "avares://Beetle/Assets/Fonts#Manrope" : "avares://Beetle/Assets/Fonts#DM Sans"),
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock MutedText(string text, double size = 12) => Text(text, size, p => p.Muted);

    public static StackPanel Stack(double spacing = 12, params Control[] children)
    {
        var panel = new StackPanel { Spacing = spacing };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static StackPanel Row(double spacing = 10, params Control[] children)
    {
        var panel = Stack(spacing, children);
        panel.Orientation = Orientation.Horizontal;
        panel.VerticalAlignment = VerticalAlignment.Center;
        return panel;
    }

    public static Border Card(Control child, double padding = 20, Func<Palette, string>? background = null) => new()
    {
        Child = child, Padding = new Thickness(padding), CornerRadius = new CornerRadius(12),
        BorderThickness = new Thickness(1), BorderBrush = Line, Background = Themes.Brush(background ?? (p => p.Card)),
    };

    public static Button Button(string text, Action action, string style = "", bool enabled = true)
    {
        var button = new Button { Content = text, IsEnabled = enabled };
        if (style.Length > 0) foreach (var cls in style.Split(' ', StringSplitOptions.RemoveEmptyEntries)) button.Classes.Add(cls);
        button.Click += (_, _) => action();
        return button;
    }

    public static Button AsyncButton(string text, Func<Task> action, string style = "", bool enabled = true) => Button(text, async () =>
    {
        try { await action(); } catch (Exception e) { System.Diagnostics.Trace.WriteLine(e); }
    }, style, enabled);

    public static Grid Between(Control left, Control right)
    {
        right.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(left); grid.Children.Add(right); Grid.SetColumn(right, 1);
        return grid;
    }

    // A stroked vector glyph; callers bind its Stroke so it follows the host control's Foreground.
    public static Path Glyph(string data, double size = 10, double thickness = 1.1) => new()
    {
        Data = Geometry.Parse(data), Width = size, Height = size, StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    public static Control Separator() => new Border { Height = 1, Background = Line, Margin = new Thickness(0, 4) };
}
