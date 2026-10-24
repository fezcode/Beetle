using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beetle.Core;

/// <summary>What Beetle remembers between runs. Plain, nullable-friendly values so a missing or
/// older file still deserialises.</summary>
public sealed class AppSettings
{
    public string? LastDeviceName { get; set; }
    public string? LastDeviceId { get; set; }
    public string ColorHex { get; set; } = "#66CCFF";
    public int Brightness { get; set; } = 100;
    public string Theme { get; set; } = "carapace";
    /// <summary>Ambient colour boost, 0-100%.</summary>
    public int AmbientBoost { get; set; } = 40;
    /// <summary>Ambient colour mode, stored by name ("Average" or "Selective") so the file stays readable.</summary>
    public string AmbientMode { get; set; } = nameof(AmbientColorMode.Average);

    [JsonIgnore]
    public AmbientColorMode ColorMode
    {
        get => Enum.TryParse<AmbientColorMode>(AmbientMode, ignoreCase: true, out var mode) ? mode : AmbientColorMode.Average;
        set => AmbientMode = value.ToString();
    }
    // Ambient sampling area, as fractions of the primary monitor.
    public double RegionX { get; set; }
    public double RegionY { get; set; }
    public double RegionWidth { get; set; } = 1;
    public double RegionHeight { get; set; } = 1;

    [JsonIgnore]
    public SampleRegion Region
    {
        get => new SampleRegion(RegionX, RegionY, RegionWidth, RegionHeight).Normalize();
        set { var r = value.Normalize(); RegionX = r.X; RegionY = r.Y; RegionWidth = r.Width; RegionHeight = r.Height; }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON under
/// <c>%LOCALAPPDATA%\Fezcode\Beetle</c>, the Fezcode per-app data home. Any read error falls
/// back to defaults rather than throwing, because preferences are never worth a crash.</summary>
public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fezcode", "Beetle");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize(File.ReadAllText(_path), SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
