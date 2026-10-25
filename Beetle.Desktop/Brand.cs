using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Beetle.Desktop;

/// <summary>The approved transparent mark shared by the app, tray and Windows icon.</summary>
internal static class Brand
{
    private static Bitmap? _mark;

    public static Control Mark(double size)
    {
        if (_mark is null)
        {
            using var stream = AssetLoader.Open(new Uri("avares://Beetle/Assets/beetle.png"));
            _mark = new Bitmap(stream);
        }
        return new Image { Width = size, Height = size, Source = _mark, Stretch = Stretch.Uniform };
    }

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
