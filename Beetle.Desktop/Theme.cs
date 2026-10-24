using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Beetle.Desktop;

/// <summary>
/// One named colour per job, in the same spirit as Airlift's palette: surfaces darkest first,
/// hairlines, type, then the accent and the dark ink that rides on it. Danger and the close
/// button mean the same thing in every theme, so they are fixed.
/// </summary>
public sealed record Palette
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Summary { get; init; }

    public required string Window { get; init; }
    public required string Panel { get; init; }
    public required string Card { get; init; }
    public required string CardHover { get; init; }
    public required string Field { get; init; }
    public required string Raised { get; init; }
    public required string Hover { get; init; }
    public required string Pressed { get; init; }
    public required string Active { get; init; }
    public required string ActiveHover { get; init; }
    public required string ButtonHover { get; init; }
    public required string ChromeHover { get; init; }
    public required string ChromePressed { get; init; }

    public required string Line { get; init; }
    public required string LineStrong { get; init; }
    public required string LineButton { get; init; }
    public required string LineButtonHover { get; init; }

    public required string Text { get; init; }
    public required string TextBright { get; init; }
    public required string TextButton { get; init; }
    public required string Prose { get; init; }
    public required string Muted { get; init; }
    public required string Subtle { get; init; }
    public required string Faint { get; init; }

    public required string Accent { get; init; }
    public required string AccentHover { get; init; }
    public required string AccentPressed { get; init; }
    public required string AccentOn { get; init; }
    public required string AccentMuted { get; init; }
    public required string AccentSoft { get; init; }
    public required string AccentIdle { get; init; }

    public required string HeroFrom { get; init; }
    public required string HeroTo { get; init; }

    public string DangerSurface => "#302420";
    public string DangerLine => "#634236";
    public string DangerText => "#EAB5A4";
    public string CloseHover => "#C0392B";
    public string ClosePressed => "#9A2C21";
}

public static class Themes
{
    public static readonly Palette Carapace = new()
    {
        Id = "carapace", Name = "Carapace", Summary = "Iridescent beetle-shell teal.",
        Window = "#0B0F0E", Panel = "#0F1413", Card = "#121917", CardHover = "#16201D", Field = "#141B19",
        Raised = "#182320", Hover = "#1B2522", Pressed = "#15201D", Active = "#16241F", ActiveHover = "#1C2D27",
        ButtonHover = "#203029", ChromeHover = "#182420", ChromePressed = "#22332C",
        Line = "#253029", LineStrong = "#3B4F44", LineButton = "#30423A", LineButtonHover = "#455D50",
        Text = "#DCEAE3", TextBright = "#EAF3EE", TextButton = "#C3D6CC", Prose = "#9FB0A8",
        Muted = "#8A9A92", Subtle = "#93A39B", Faint = "#879690",
        Accent = "#35E0B0", AccentHover = "#5CEBC2", AccentPressed = "#27BD93", AccentOn = "#04231B",
        AccentMuted = "#8FD9C4", AccentSoft = "#A6C7BD", AccentIdle = "#40554C",
        HeroFrom = "#123028", HeroTo = "#0C1A16",
    };

    public static readonly Palette Scarab = new()
    {
        Id = "scarab", Name = "Scarab", Summary = "Deep amethyst, like a rhino beetle.",
        Window = "#0D0B12", Panel = "#121019", Card = "#17141F", CardHover = "#1C1826", Field = "#181524",
        Raised = "#201B2E", Hover = "#241F30", Pressed = "#1D1829", Active = "#221B33", ActiveHover = "#2A2240",
        ButtonHover = "#2E2545", ChromeHover = "#1E1A28", ChromePressed = "#292338",
        Line = "#2B2639", LineStrong = "#443B5C", LineButton = "#38314E", LineButtonHover = "#4C4269",
        Text = "#E4DEF2", TextBright = "#EEE9F8", TextButton = "#D0C6E6", Prose = "#ABA2C0",
        Muted = "#938AA6", Subtle = "#9E95B2", Faint = "#8F87A0",
        Accent = "#B48BFF", AccentHover = "#C7A6FF", AccentPressed = "#9A72E6", AccentOn = "#180F2E",
        AccentMuted = "#BCA8E0", AccentSoft = "#C3B6DC", AccentIdle = "#4D4166",
        HeroFrom = "#1F1838", HeroTo = "#130F24",
    };

    public static IReadOnlyList<Palette> All { get; } = [Carapace, Scarab];
    public static Palette Default => All[0];
    public static Palette Current { get; private set; } = All[0];
    public static event Action? Changed;

    public static Palette Find(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Default;

    /// <summary>Publishes the palette as application resources that the styles bind to dynamically.</summary>
    public static void Apply(string? id, Application? application = null)
    {
        var palette = Find(id);
        var target = application ?? Application.Current;
        var changed = !ReferenceEquals(palette, Current);
        Current = palette;
        if (target != null)
        {
            foreach (var (key, hex) in Entries(palette)) target.Resources["Beetle." + key] = new ImmutableSolidColorBrush(Color.Parse(hex));
            target.Resources["SystemAccentColor"] = Color.Parse(palette.Accent);
            target.Resources["SystemAccentColorLight1"] = Color.Parse(palette.AccentHover);
            target.Resources["SystemAccentColorDark1"] = Color.Parse(palette.AccentPressed);
        }
        if (changed) Changed?.Invoke();
    }

    public static IEnumerable<(string Key, string Hex)> Entries(Palette palette)
    {
        foreach (var property in typeof(Palette).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(string)) continue;
            if (property.Name is nameof(Palette.Id) or nameof(Palette.Name) or nameof(Palette.Summary)) continue;
            yield return (property.Name, (string)property.GetValue(palette)!);
        }
    }

    private static readonly Dictionary<string, IBrush> Cache = [];
    public static string Hex(Func<Palette, string> role) => role(Current);
    public static IBrush Brush(Func<Palette, string> role)
    {
        var hex = role(Current);
        if (!Cache.TryGetValue(hex, out var brush)) Cache[hex] = brush = Avalonia.Media.Brush.Parse(hex);
        return brush;
    }
}
