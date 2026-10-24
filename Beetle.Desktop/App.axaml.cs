using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Beetle.Core;

namespace Beetle.Desktop;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Styles resolve colours dynamically, so a palette must exist before any control is styled.
        Themes.Apply(Themes.Default.Id, this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new SettingsStore();
            var settings = store.Load();
            Themes.Apply(settings.Theme, this);
            // Closing the window only hides it; Beetle keeps running in the tray until Quit.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow(store, settings);
            desktop.MainWindow = window;
            window.Closed += (_, _) => desktop.Shutdown();
            TrayIcon.SetIcons(this, [CreateTray(window)]);
            ListenForSecondLaunch(window);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static TrayIcon CreateTray(MainWindow window)
    {
        var show = new NativeMenuItem("Show Beetle");
        show.Click += (_, _) => window.ShowFromTray();
        var power = new NativeMenuItem("Turn on");
        power.Click += async (_, _) => await window.TogglePowerFromTray();
        var ambient = new NativeMenuItem("Start ambient");
        ambient.Click += (_, _) => window.ToggleAmbientFromTray();
        var quit = new NativeMenuItem("Quit Beetle");
        quit.Click += (_, _) => window.Quit();

        var menu = new NativeMenu();
        foreach (var item in new NativeMenuItemBase[] { show, new NativeMenuItemSeparator(), power, ambient, new NativeMenuItemSeparator(), quit })
            menu.Items.Add(item);

        using var icon = AssetLoader.Open(new Uri("avares://Beetle/Assets/beetle.ico"));
        var tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "Beetle", Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => window.ShowFromTray();

        // Menu labels and the tooltip follow the window's state, so the tray never offers a stale action.
        void Sync()
        {
            var connected = window.IsConnected;
            power.Header = window.IsStripOn ? "Turn off" : "Turn on";
            power.IsEnabled = connected;
            ambient.Header = window.IsAmbientRunning ? "Stop ambient" : "Start ambient";
            ambient.IsEnabled = connected;
            tray.ToolTipText = !connected ? "Beetle · not connected"
                : window.IsAmbientRunning ? $"Beetle · {window.StripName} · ambient on"
                : $"Beetle · {window.StripName}";
        }
        window.StateChanged += Sync;
        Sync();
        return tray;
    }

    // A second launch (see Program) signals this event instead of starting another copy, which would
    // fight this one for the strip's single Bluetooth connection. Show the window that is already here.
    private static void ListenForSecondLaunch(MainWindow window)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSignalName);
        var listener = new Thread(() =>
        {
            while (signal.WaitOne()) Dispatcher.UIThread.Post(window.ShowFromTray);
        }) { IsBackground = true, Name = "Beetle second-launch listener" };
        listener.Start();
    }
}
