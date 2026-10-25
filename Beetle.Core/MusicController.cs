using System.Diagnostics;

namespace Beetle.Core;

/// <summary>
/// Drives the strip from sound. About 30 times a second it reads the <see cref="MusicAnalyzer"/>:
/// loudness sets brightness (flaring instantly, fading over a quarter of a second, never quite dark),
/// and each beat jumps the hue round the wheel, with a slow drift between beats so a beatless passage
/// still moves. <see cref="Sensitivity"/> scales the loudness before it reaches the strip.
///
/// Events fire from the background loop; a UI subscriber must marshal them to its own thread.
/// </summary>
public sealed class MusicController(StripController strip, IAudioSource source)
{
    /// <summary>Hue degrees each beat moves the colour on.</summary>
    public const double BeatHueStep = 55;
    /// <summary>Hue degrees per second the colour drifts between beats.</summary>
    public const double DriftPerSecond = 8;
    /// <summary>Brightness floor, so silence leaves a dim glow rather than an apparently dead strip.</summary>
    public const double Floor = 0.06;
    private const double FadeSeconds = 0.25;

    private double _sensitivity = 0.5;
    private RgbColor[] _palette = [];
    private CancellationTokenSource? _cts;

    /// <summary>
    /// The colours each beat steps through, in order. Empty means the whole colour wheel (each beat
    /// moves the hue on, drifting between beats). With colours picked, a beat jumps straight to the
    /// next one and the colour holds until the following beat, which reads as far more dramatic.
    /// Settable while running.
    /// </summary>
    public IReadOnlyList<RgbColor> Palette
    {
        get => Volatile.Read(ref _palette);
        set => Volatile.Write(ref _palette, value.ToArray());
    }

    /// <summary>A palette colour shown at a brightness: its hue and saturation, at the given value.
    /// White stays white (saturation 0), so it can be part of a palette too.</summary>
    public static RgbColor PaletteColor(RgbColor color, double brightness)
    {
        var (h, s, _) = color.ToHsv();
        return RgbColor.FromHsv(h, s, brightness);
    }

    /// <summary>0 (subtle) to 1 (strong); maps to a 0.5x-2.5x gain on loudness. Settable while running.</summary>
    public double Sensitivity
    {
        get => Volatile.Read(ref _sensitivity);
        set => Volatile.Write(ref _sensitivity, Math.Clamp(value, 0, 1));
    }

    /// <summary>Loop period; 33 ms is about 30 updates a second, quick enough to feel on the beat.</summary>
    public int IntervalMs { get; init; } = 33;
    /// <summary>Smallest per-channel change that earns a frame.</summary>
    public int SendThreshold { get; init; } = 3;

    public bool IsRunning => _cts is not null;

    public event Action<RgbColor>? Sampled;
    public event Action<Exception>? Faulted;

    /// <summary>The brightness music mode shows for a loudness: the floor plus a gentle curve above it.</summary>
    public static double BrightnessFor(double level) => Floor + (1 - Floor) * Math.Pow(Math.Clamp(level, 0, 1), 0.8);

    /// <summary>Gain the sensitivity applies to loudness: 0.5x at 0, 1.5x at 0.5, 2.5x at 1.</summary>
    public static double GainFor(double sensitivity) => 0.5 + 2 * Math.Clamp(sensitivity, 0, 1);

    public void Start()
    {
        if (IsRunning) return;
        source.Start(); // throws if there is no output device to listen to; the caller reports it
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = RunAsync(cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        source.Stop();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var hue = strip.Color.ToHsv().Hue;
        var paletteIndex = 0;
        var shown = 0.0;
        var beats = source.Analyzer.Snapshot().Beats;
        var lastSent = RgbColor.Black;
        var first = true;
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = clock.Elapsed;
                var dt = (now - last).TotalSeconds;
                last = now;

                var snapshot = source.Analyzer.Snapshot();
                var loudness = Math.Clamp(snapshot.Level * GainFor(Sensitivity), 0, 1);
                shown = Math.Max(loudness, shown * Math.Exp(-dt / FadeSeconds));   // instant attack, quick fade
                var newBeats = (int)Math.Min(snapshot.Beats - beats, 3);
                beats = snapshot.Beats;
                var palette = Palette;
                RgbColor color;
                if (palette.Count == 0)
                {
                    hue = (hue + newBeats * BeatHueStep + DriftPerSecond * dt) % 360;
                    color = RgbColor.FromHsv(hue, 1, BrightnessFor(shown));
                }
                else
                {
                    paletteIndex = (paletteIndex + newBeats) % palette.Count;
                    color = PaletteColor(palette[paletteIndex], BrightnessFor(shown));
                }
                Sampled?.Invoke(color);
                if (first || color.MaxChannelDelta(lastSent) >= SendThreshold)
                {
                    await strip.SetColorAsync(color, powerOnIfNeeded: true, cancellationToken);
                    lastSent = color;
                    first = false;
                }
                await Task.Delay(IntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException) { /* Stop() was called */ }
        catch (Exception error)
        {
            _cts = null;
            source.Stop();
            Faulted?.Invoke(error);
        }
    }
}
