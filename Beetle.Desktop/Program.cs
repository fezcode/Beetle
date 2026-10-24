using Avalonia;
using Beetle.Core;

namespace Beetle.Desktop;

internal static class Program
{
    private const string InstanceName = @"Local\Fezcode.Beetle.Instance";
    public const string ShowSignalName = @"Local\Fezcode.Beetle.Show";

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version") || args.Contains("-v")) { Console.WriteLine(AppVersion.Display); return; }
        if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(HelpText); return; }

        // One Beetle per user session: the strip accepts a single Bluetooth connection. If Beetle is
        // already running (usually hidden in the tray), ask it to show its window and stop here.
        using var instance = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            try { using var show = EventWaitHandle.OpenExisting(ShowSignalName); show.Set(); }
            catch (WaitHandleCannotBeOpenedException) { /* the other copy is still starting up */ }
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    private const string HelpText = """
        Beetle - control an ELK-BLEDOM Bluetooth RGB LED strip from Windows.

        Usage:
          Beetle                 Launch the app.
          Beetle -h, --help      Show this help.
          Beetle -v, --version   Show the version.

        In the app:
          Connect       Scan for and connect to a paired ELK-BLEDOM strip.
          Power         Turn the strip on or off.
          Color         Pick a colour; it is sent to the strip as you choose it.
          Brightness    Set the strip's hardware brightness, 0-100%.
          Ambient       Continuously match the strip to your screen's colour.

        Closing the window keeps Beetle running in the system tray, ambient mode included.
        Use the tray icon to show it again, or its menu to quit. Launching Beetle again shows
        the copy that is already running.
        """;
}
