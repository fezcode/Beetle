using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Beetle.Desktop;

// Beetle draws its own window frame, the same way Airlift does: the client area is extended over
// the system decorations so the platform still owns the invisible resize border, drop shadow and
// snap gestures, while everything visible above the content belongs to the app.
internal static class Chrome
{
    // Taller than a bare caption strip, because Beetle's wordmark lives in it rather than in a sidebar.
    public const double Height = 50;
    private const string MinimizeGlyph = "M0 5 L10 5";
    private const string MaximizeGlyph = "M0.5 0.5 L9.5 0.5 L9.5 9.5 L0.5 9.5 Z";
    private const string RestoreGlyph = "M2.5 2.5 L2.5 0.5 L9.5 0.5 L9.5 7.5 L7.5 7.5 M0.5 2.5 L7.5 2.5 L7.5 9.5 L0.5 9.5 Z";
    public const string CloseGlyph = "M0.5 0.5 L9.5 9.5 M9.5 0.5 L0.5 9.5";

    public static void Extend(Window window)
    {
        window.WindowDecorations = WindowDecorations.BorderOnly;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = -1;
    }

    // Windows hangs a maximized window's resize border off the screen; content steps inside it.
    public static void KeepInsideScreen(Window window, Layoutable content)
    {
        void Apply() => content.Margin = window.OffScreenMargin;
        window.PropertyChanged += (_, e) => { if (e.Property == Window.WindowStateProperty) Apply(); };
        window.Resized += (_, _) => Apply();
        Apply();
    }

    public static void Draggable(Control control) =>
        WindowDecorationProperties.SetElementRole(control, WindowDecorationsElementRole.TitleBar);

    public static void ToggleMaximized(Window window) =>
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    public static Border TitleBar(Window window, Control? lead, bool resizable)
    {
        var strip = new Grid();
        if (lead != null)
        {
            lead.HorizontalAlignment = HorizontalAlignment.Left; lead.VerticalAlignment = VerticalAlignment.Center;
            lead.Margin = new Thickness(22, 4, 0, 0); strip.Children.Add(lead);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (resizable)
        {
            buttons.Children.Add(Caption(MinimizeGlyph, "Minimize", WindowDecorationsElementRole.MinimizeButton, () => window.WindowState = WindowState.Minimized));
            var maximize = Caption(MaximizeGlyph, "Maximize", WindowDecorationsElementRole.MaximizeButton, () => ToggleMaximized(window));
            var glyph = (Path)maximize.Content!;
            void Sync()
            {
                var maximized = window.WindowState == WindowState.Maximized;
                glyph.Data = Geometry.Parse(maximized ? RestoreGlyph : MaximizeGlyph);
                var name = maximized ? "Restore" : "Maximize";
                ToolTip.SetTip(maximize, name); Avalonia.Automation.AutomationProperties.SetName(maximize, name);
            }
            window.PropertyChanged += (_, e) => { if (e.Property == Window.WindowStateProperty) Sync(); };
            Sync(); buttons.Children.Add(maximize);
        }
        buttons.Children.Add(Caption(CloseGlyph, "Close", WindowDecorationsElementRole.CloseButton, window.Close, "close"));
        strip.Children.Add(buttons);
        var border = new Border { Height = Height, Background = Brushes.Transparent, Child = strip };
        Draggable(border);
        if (resizable) border.DoubleTapped += (_, _) => ToggleMaximized(window);
        return border;
    }

    private static Button Caption(string glyph, string name, WindowDecorationsElementRole role, Action action, string style = "")
    {
        var button = Ui.Button("", action);
        button.Classes.Add("chrome"); if (style.Length > 0) button.Classes.Add(style);
        var path = Ui.Glyph(glyph);
        path.Bind(Shape.StrokeProperty, new Avalonia.Data.Binding("Foreground") { Source = button });
        button.Content = path;
        WindowDecorationProperties.SetElementRole(button, role);
        ToolTip.SetTip(button, name); Avalonia.Automation.AutomationProperties.SetName(button, name);
        return button;
    }
}
