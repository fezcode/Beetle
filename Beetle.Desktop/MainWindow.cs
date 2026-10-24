using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Beetle.Core;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace Beetle.Desktop;

public sealed class MainWindow : Window
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly BleStrip _ble = new();
    private readonly StripController _strip;
    private GdiScreenSampler? _sampler;
    private AmbientController? _ambient;

    private RgbColor _color;
    private IReadOnlyList<BleDeviceInfo> _devices = [];
    private BleDeviceInfo? _selected;
    private bool _scanning;
    private DateTime _lastColorSend, _lastBrightnessSend;

    // Rebuilt by Compose() on every theme change; the model above is the source of truth.
    private Ellipse _statusDot = new();
    private TextBlock _statusText = Ui.MutedText("");
    private ComboBox _deviceBox = new();
    private Button _scanButton = new();
    private Button _connectButton = new();
    private Button _onButton = new();
    private Button _offButton = new();
    private ColorField _field = new();
    private TextBox _hexBox = new();
    private StackPanel _colorControls = new();
    private Border _previewSwatch = new();
    private Slider _brightness = new();
    private TextBlock _brightnessLabel = Ui.MutedText("");
    private Button _ambientButton = new();
    private Border _ambientSwatch = new();
    private TextBlock _ambientNote = Ui.MutedText("");
    private WrapPanel _regionChips = new();
    private WrapPanel _modeChips = new();
    private Slider _boostSlider = new();
    private TextBlock _boostLabel = Ui.MutedText("");
    private TextBlock _notice = Ui.MutedText("");

    private static readonly RgbColor[] Presets =
    [
        new(255, 60, 60), new(255, 140, 40), new(255, 210, 60), new(120, 230, 70),
        new(50, 220, 170), new(60, 200, 255), new(60, 120, 255), new(150, 90, 255),
        new(230, 80, 220), new(255, 110, 170), new(255, 255, 255), new(255, 180, 110),
    ];

    public MainWindow(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        _strip = new StripController(_ble);
        _color = RgbColor.TryParseHex(settings.ColorHex, out var c) ? c : new RgbColor(102, 204, 255);

        Title = "Beetle";
        using (var icon = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Beetle/Assets/beetle.ico"))) Icon = new WindowIcon(icon);
        Width = 420; Height = 780; MinWidth = 380; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Chrome.Extend(this);

        _ble.ConnectionChanged += connected => Dispatcher.UIThread.Post(() =>
        {
            if (!connected && _ambient is { IsRunning: true }) StopAmbient();
            UpdateConnectionUi();
            Notice(connected ? $"Connected to {_ble.ConnectedName}." : "Disconnected.");
        });
        Themes.Changed += Rebuild;

        Rebuild();
        Opened += (_, _) => BringToFront();
        Opened += async (_, _) => await ScanAsync(autoConnect: true);
        // Beetle lives in the tray: closing the window hides it and the strip keeps going (ambient
        // included). Only Quit, or Windows/the app shutting down, really ends it.
        Closing += (_, e) =>
        {
            if (_quitting || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)
            {
                Shutdown();
                return;
            }
            e.Cancel = true;
            Save();
            Hide();
        };
    }

    // ---- tray ---------------------------------------------------------------

    private bool _quitting;
    private bool _shutDown;

    /// <summary>Raised on the UI thread whenever connection, power or ambient state changes.</summary>
    public event Action? StateChanged;
    public bool IsConnected => _ble.IsConnected;
    public bool IsStripOn => _strip.IsOn;
    public bool IsAmbientRunning => _ambient is { IsRunning: true };
    public string? StripName => _ble.ConnectedName;

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        BringToFront();
    }

    public void ToggleAmbientFromTray() => ToggleAmbient();

    public Task TogglePowerFromTray() => SetPower(!_strip.IsOn);

    /// <summary>Really exits: stops ambient, saves, releases the strip, then closes the window.</summary>
    public void Quit()
    {
        _quitting = true;
        Close();
    }

    // Launched over a fullscreen app (a game, say), the window otherwise opens behind it and looks
    // like nothing happened. Raise it above other windows briefly so it surfaces, then let it fall
    // back to normal stacking so it isn't permanently pinned on top.
    private async void BringToFront()
    {
        Activate();
        Topmost = true;
        await Task.Delay(500);
        Topmost = false;
        Activate();
    }

    // ---- composition -------------------------------------------------------

    private void Rebuild()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions($"{Chrome.Height},*") };
        root.Children.Add(Chrome.TitleBar(this, Brand.Wordmark(), resizable: true));
        var scroll = new ScrollViewer
        {
            Content = Compose(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        Content = root;
        Chrome.KeepInsideScreen(this, root);
    }

    private Control Compose()
    {
        var theme = Ui.Button($"Theme: {Themes.Current.Name}", CycleTheme, "link");
        ToolTip.SetTip(theme, "Switch theme");
        var header = Ui.Between(
            Ui.Stack(4, Ui.Text("Your strip", 22, p => p.TextBright, FontWeight.SemiBold), Ui.MutedText("ELK-BLEDOM over Bluetooth LE", 12)),
            theme);

        var body = Ui.Stack(15, header, ConnectionCard(), PowerCard(), ColorCard(), BrightnessCard(), AmbientCard(), Footer());
        body.Margin = new Thickness(22, 18, 22, 24);

        UpdateConnectionUi();
        UpdatePowerUi();
        UpdateAmbientUi();
        ShowColor(_color);
        return body;
    }

    private Control ConnectionCard()
    {
        _statusDot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
        _statusText = Ui.MutedText("Not connected", 11);
        _deviceBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "No strips found yet",
            ItemsSource = _devices,
            SelectedItem = _selected,
            ItemTemplate = new FuncDataTemplate<BleDeviceInfo>((d, _) =>
                // No recycling: the text is set at build time, so a recycled block would keep showing
                // whatever it was first built for (the empty "nothing selected" item).
                new TextBlock { Text = d is null ? "" : d.Paired ? d.Name : $"{d.Name}  (new)", VerticalAlignment = VerticalAlignment.Center }, false),
        };
        _deviceBox.SelectionChanged += (_, _) => { _selected = _deviceBox.SelectedItem as BleDeviceInfo; UpdateConnectionUi(); };
        _scanButton = Ui.AsyncButton("Scan", () => ScanAsync(autoConnect: false));
        _connectButton = Ui.AsyncButton("Connect", ToggleConnect, "primary");

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        row.Children.Add(_deviceBox);
        Grid.SetColumn(_scanButton, 1); row.Children.Add(_scanButton);
        Grid.SetColumn(_connectButton, 2); row.Children.Add(_connectButton);

        return Ui.Card(Ui.Stack(13,
            Ui.Between(Ui.Text("Connection", 14, weight: FontWeight.SemiBold), Ui.Row(7, _statusDot, _statusText)),
            row));
    }

    private Control PowerCard()
    {
        _onButton = Ui.AsyncButton("Turn on", () => SetPower(true));
        _offButton = Ui.AsyncButton("Turn off", () => SetPower(false));
        _onButton.HorizontalAlignment = _offButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        grid.Children.Add(_onButton);
        Grid.SetColumn(_offButton, 1); grid.Children.Add(_offButton);
        return Ui.Card(Ui.Stack(13, Ui.Text("Power", 14, weight: FontWeight.SemiBold), grid));
    }

    private Control ColorCard()
    {
        _previewSwatch = new Border { Height = 44, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = Ui.Line };
        _field = new ColorField { Color = _color };
        _field.ColorChanged += OnFieldColor;

        _hexBox = new TextBox { Width = 112, Text = _color.ToHex(), VerticalAlignment = VerticalAlignment.Center };
        _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitHex(); };
        _hexBox.LostFocus += (_, _) => CommitHex();

        var presets = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var preset in Presets)
        {
            var swatch = Ui.Button("", () => CommitColor(preset), "swatch");
            swatch.Background = new SolidColorBrush(Color.FromRgb(preset.R, preset.G, preset.B));
            swatch.Margin = new Thickness(0, 0, 7, 7);
            ToolTip.SetTip(swatch, preset.ToHex());
            presets.Children.Add(swatch);
        }

        _colorControls = Ui.Stack(13, _field.View, Ui.Row(10, Ui.MutedText("Hex", 11), _hexBox), presets);
        return Ui.Card(Ui.Stack(14, Ui.Text("Color", 14, weight: FontWeight.SemiBold), _previewSwatch, _colorControls));
    }

    private Control BrightnessCard()
    {
        _brightnessLabel = Ui.MutedText($"{_settings.Brightness}%", 12);
        _brightness = new Slider { Minimum = 0, Maximum = 100, Value = _settings.Brightness, SmallChange = 1, LargeChange = 10 };
        _brightness.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) OnBrightness(_brightness.Value); };
        return Ui.Card(Ui.Stack(10,
            Ui.Between(Ui.Text("Brightness", 14, weight: FontWeight.SemiBold), _brightnessLabel),
            _brightness));
    }

    private Control AmbientCard()
    {
        _ambientSwatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), BorderBrush = Ui.Line, Background = Themes.Brush(p => p.Field) };
        _ambientNote = Ui.MutedText("", 11);
        _ambientButton = Ui.Button("Start ambient", ToggleAmbient, "primary");
        _ambientButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _regionChips = new WrapPanel { Orientation = Orientation.Horizontal };
        BuildRegionChips();
        _modeChips = new WrapPanel { Orientation = Orientation.Horizontal };
        BuildModeChips();
        _boostLabel = Ui.MutedText(BoostText(_settings.AmbientBoost), 11);
        _boostSlider = new Slider { Minimum = 0, Maximum = 100, Value = _settings.AmbientBoost, SmallChange = 1, LargeChange = 10 };
        _boostSlider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) OnBoost(_boostSlider.Value); };
        ToolTip.SetTip(_boostSlider, "Brightens and saturates colourful parts of the screen. Dark greys stay dark.");
        return Ui.Card(Ui.Stack(13,
            Ui.Between(Ui.Text("Ambient (screen)", 14, weight: FontWeight.SemiBold), _ambientSwatch),
            _ambientNote,
            Ui.Stack(8, Ui.MutedText("C O L O R   M O D E", 9), _modeChips),
            Ui.Stack(8, Ui.MutedText("S A M P L E   A R E A", 9), _regionChips),
            Ui.Stack(2, Ui.Between(Ui.MutedText("C O L O R   B O O S T", 9), _boostLabel), _boostSlider),
            _ambientButton));
    }

    private static string BoostText(int percent) => percent == 0 ? "Off · raw screen colour" : $"{percent}%";

    // Takes effect on the next ambient sample; the strip's manual colour is never boosted.
    private void OnBoost(double value)
    {
        var percent = (int)Math.Round(value);
        _boostLabel.Text = BoostText(percent);
        _settings.AmbientBoost = percent;
        if (_ambient is not null) _ambient.Boost = percent / 100d;
    }

    // One chip per preset plus "Pick area…"; the chip for the area in use is highlighted.
    private void BuildRegionChips()
    {
        _regionChips.Children.Clear();
        var current = _settings.Region.PresetName;
        foreach (var (name, region) in SampleRegion.Presets)
            _regionChips.Children.Add(RegionChip(name, current == name, () => SetRegion(region)));
        var pick = Ui.AsyncButton(current is null ? "Custom area · change…" : "Pick area…", PickRegionAsync, "chip");
        pick.Classes.Set("active", current is null);
        pick.Margin = new Thickness(0, 0, 6, 6);
        ToolTip.SetTip(pick, "Drag a rectangle on your screen");
        _regionChips.Children.Add(pick);
    }

    // Average follows everything on screen; Selective sets black, white and greys aside first.
    private void BuildModeChips()
    {
        _modeChips.Children.Clear();
        var current = _settings.ColorMode;
        var average = RegionChip("Average", current == AmbientColorMode.Average, () => SetColorMode(AmbientColorMode.Average));
        ToolTip.SetTip(average, "Follow the average of everything in the sample area.");
        var selective = RegionChip("Selective", current == AmbientColorMode.Selective, () => SetColorMode(AmbientColorMode.Selective));
        ToolTip.SetTip(selective, "Ignore black, white and greys; follow only the colours that are left. With no colour on screen, the strip keeps its last colour.");
        _modeChips.Children.Add(average);
        _modeChips.Children.Add(selective);
    }

    private void SetColorMode(AmbientColorMode mode)
    {
        _settings.ColorMode = mode;
        Save();
        if (_sampler is not null) _sampler.Mode = mode; // takes effect on the next sample, even mid-run
        BuildModeChips();
        UpdateAmbientUi();
        Notice(mode == AmbientColorMode.Selective
            ? "Selective: following colours only; black, white and greys are ignored."
            : "Average: following everything in the sample area.");
    }

    private static Button RegionChip(string name, bool active, Action select)
    {
        var chip = Ui.Button(name, select, "chip");
        chip.Classes.Set("active", active);
        chip.Margin = new Thickness(0, 0, 6, 6);
        return chip;
    }

    private void SetRegion(SampleRegion region)
    {
        _settings.Region = region;
        Save();
        if (_sampler is not null) _sampler.Region = region; // takes effect on the next sample, even mid-run
        BuildRegionChips();
        UpdateAmbientUi();
        Notice($"Ambient follows: {region.PresetName ?? "your custom area"}.");
    }

    private async Task PickRegionAsync()
    {
        // Get Beetle out of the way so the area behind it can be picked too.
        var previous = WindowState;
        WindowState = WindowState.Minimized;
        try
        {
            var picked = await RegionPicker.PickAsync(_settings.Region);
            if (picked is { } region) SetRegion(region);
        }
        finally
        {
            WindowState = previous == WindowState.Minimized ? WindowState.Normal : previous;
            Activate();
        }
    }

    private Control Footer()
    {
        _notice = Ui.MutedText("Scan to find your strip, then connect.", 11);
        return Ui.Stack(8, Ui.Separator(), Ui.Between(_notice, Ui.MutedText($"v{AppVersion.Current}", 10)));
    }

    // ---- actions -----------------------------------------------------------

    private async Task ScanAsync(bool autoConnect)
    {
        if (_scanning || _ble.IsConnected) return;
        _scanning = true; UpdateConnectionUi(); Notice("Scanning for strips…");
        try
        {
            _devices = await BleStrip.ScanAsync(TimeSpan.FromSeconds(4));
            _deviceBox.ItemsSource = _devices;
            var lastName = _settings.LastDeviceName?.Trim();
            var remembered = _devices.FirstOrDefault(d => d.Id is not null && d.Id == _settings.LastDeviceId)
                             ?? _devices.FirstOrDefault(d => lastName is not null && d.Name == lastName);
            _selected = remembered ?? _devices.FirstOrDefault();
            _deviceBox.SelectedItem = _selected;
            Notice(_devices.Count == 0
                ? "No strips found. Make sure the strip is powered and not connected to its phone app (it hides while connected), then scan again."
                : $"Found {_devices.Count} strip{(_devices.Count == 1 ? "" : "s")}.");
            // Only reconnect on launch to the strip used last time, never to whichever one is nearby.
            if (autoConnect && remembered is not null)
                await ConnectAsync();
        }
        catch (Exception e) { Notice(e.Message, true); }
        finally { _scanning = false; UpdateConnectionUi(); }
    }

    private async Task ToggleConnect()
    {
        if (_ble.IsConnected) await _ble.DisconnectAsync();
        else await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (_selected is null) return;
        Notice($"Connecting to {_selected.Name}…");
        _connectButton.IsEnabled = false;
        try
        {
            await _ble.ConnectAsync(_selected);
            _settings.LastDeviceId = _selected.Id;
            _settings.LastDeviceName = _selected.Name;
            Save();
            // ConnectionChanged updates the rest of the UI.
        }
        catch (Exception e) { Notice(e.Message, true); UpdateConnectionUi(); }
    }

    private async Task SetPower(bool on)
    {
        if (!_ble.IsConnected) return;
        try { await _strip.PowerAsync(on); UpdatePowerUi(); Notice(on ? "Strip on." : "Strip off."); }
        catch (Exception e) { Notice(e.Message, true); }
    }

    private void OnFieldColor(RgbColor color, bool committed)
    {
        _color = color;
        ShowColor(color);
        if (committed) { _settings.ColorHex = color.ToHex(); Save(); }
        _ = SendColor(color, committed);
    }

    private void CommitColor(RgbColor color)
    {
        _field.Color = color;
        OnFieldColor(color, committed: true);
    }

    private void CommitHex()
    {
        if (RgbColor.TryParseHex(_hexBox.Text, out var color)) CommitColor(color);
        else _hexBox.Text = _color.ToHex();
    }

    private async Task SendColor(RgbColor color, bool committed)
    {
        if (!_ble.IsConnected) return;
        var now = DateTime.UtcNow;
        if (!committed && (now - _lastColorSend).TotalMilliseconds < 90) return;
        _lastColorSend = now;
        try { await _strip.SetColorAsync(color); UpdatePowerUi(); }
        catch (Exception e) { Notice(e.Message, true); }
    }

    private async void OnBrightness(double value)
    {
        var percent = (int)Math.Round(value);
        _brightnessLabel.Text = $"{percent}%";
        _settings.Brightness = percent;
        if (!_ble.IsConnected) return;
        var now = DateTime.UtcNow;
        if ((now - _lastBrightnessSend).TotalMilliseconds < 90) return;
        _lastBrightnessSend = now;
        try { await _strip.SetBrightnessAsync(percent); }
        catch (Exception e) { Notice(e.Message, true); }
    }

    private void ToggleAmbient()
    {
        if (_ambient is { IsRunning: true }) StopAmbient();
        else StartAmbient();
    }

    private void StartAmbient()
    {
        if (!_ble.IsConnected) return;
        _sampler ??= new GdiScreenSampler();
        _sampler.Region = _settings.Region;
        _sampler.Mode = _settings.ColorMode;
        _ambient = new AmbientController(_strip, _sampler) { Boost = _settings.AmbientBoost / 100d };
        _ambient.Sampled += color => Dispatcher.UIThread.Post(() =>
        {
            _ambientSwatch.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            ShowColor(color);
        });
        _ambient.Faulted += error => Dispatcher.UIThread.Post(() => { StopAmbient(); Notice(error.Message, true); });
        _ambient.Start();
        UpdatePowerUi();
        UpdateAmbientUi();
        Notice("Ambient mode on — matching your screen.");
    }

    private void StopAmbient()
    {
        _ambient?.Stop();
        _ambient = null;
        UpdateAmbientUi();
        Notice("Ambient mode off.");
    }

    private void CycleTheme()
    {
        var index = Themes.All.ToList().FindIndex(p => p.Id == Themes.Current.Id);
        var next = Themes.All[(index + 1) % Themes.All.Count];
        _settings.Theme = next.Id; Save();
        Themes.Apply(next.Id); // raises Changed -> Rebuild
    }

    // ---- view state --------------------------------------------------------

    private void ShowColor(RgbColor color)
    {
        _previewSwatch.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        if (!_hexBox.IsFocused) _hexBox.Text = color.ToHex();
    }

    private void UpdateConnectionUi()
    {
        var connected = _ble.IsConnected;
        _statusDot.Fill = Themes.Brush(connected ? p => p.Accent : p => p.AccentIdle);
        _statusText.Text = connected ? $"Connected · {_ble.ConnectedName}" : _scanning ? "Scanning…" : "Not connected";
        _connectButton.Content = connected ? "Disconnect" : "Connect";
        _connectButton.Classes.Set("primary", !connected);
        _connectButton.Classes.Set("danger", connected);
        _connectButton.IsEnabled = connected || (_selected is not null && !_scanning);
        _scanButton.IsEnabled = !_scanning && !connected;
        _deviceBox.IsEnabled = !_scanning && !connected;
        _onButton.IsEnabled = _offButton.IsEnabled = connected;
        _brightness.IsEnabled = connected;
        _ambientButton.IsEnabled = connected;
        _colorControls.IsEnabled = connected && _ambient is not { IsRunning: true };
        StateChanged?.Invoke();
    }

    private void UpdatePowerUi()
    {
        var on = _strip.IsOn;
        _onButton.Classes.Set("primary", on);
        _offButton.Classes.Set("primary", !on);
        StateChanged?.Invoke();
    }

    private void UpdateAmbientUi()
    {
        var running = _ambient is { IsRunning: true };
        _ambientButton.Content = running ? "Stop ambient" : "Start ambient";
        _ambientButton.Classes.Set("primary", !running);
        _ambientButton.Classes.Set("danger", running);
        var area = _settings.Region.PresetName switch
        {
            null => "your custom area",
            "Full screen" => "the whole screen",
            var preset => $"the {preset.ToLowerInvariant()} of the screen",
        };
        var selective = _settings.ColorMode == AmbientColorMode.Selective;
        _ambientNote.Text = (running, selective) switch
        {
            (true, false) => $"Matching {area}. The colour picker is paused while this runs.",
            (true, true) => $"Matching the colours in {area}, ignoring black, white and greys. The colour picker is paused while this runs.",
            (false, false) => $"Continuously matches the strip to the average colour of {area}.",
            (false, true) => $"Continuously matches the strip to the colours in {area}, ignoring black, white and greys.",
        };
        _colorControls.IsEnabled = _ble.IsConnected && !running;
        StateChanged?.Invoke();
    }

    private void Notice(string message, bool problem = false)
    {
        _notice.Text = message;
        _notice.Foreground = Themes.Brush(problem ? p => p.DangerText : p => p.Muted);
    }

    private void Save() => _store.Save(_settings);

    private void Shutdown()
    {
        if (_shutDown) return;
        _shutDown = true;
        _ambient?.Stop();
        _sampler?.Dispose();
        Themes.Changed -= Rebuild;
        _settings.ColorHex = _color.ToHex();
        Save();
        _ble.Dispose();
    }
}
