using System.Diagnostics;
using Beetle.Core;
using Xunit;

namespace Beetle.Tests;

file sealed class FakeAudio : IAudioSource
{
    public MusicAnalyzer Analyzer { get; } = new();
    public bool Started { get; private set; }
    public void Start() => Started = true;
    public void Stop() => Started = false;
    public void Dispose() { }
}

file sealed class CountingTransport : ILedTransport
{
    public int Frames;
    public bool IsConnected => true;
    public Task WriteAsync(byte[] frame, CancellationToken cancellationToken = default) { Interlocked.Increment(ref Frames); return Task.CompletedTask; }
}

public class MusicAnalyzerTests
{
    private const int Rate = 48000;
    private const int Chunk = 480; // 10 ms, about what WASAPI delivers

    // Feeds `seconds` of audio produced by `sample(t)` in 10 ms chunks; returns the highest level seen.
    private static double Play(MusicAnalyzer analyzer, double seconds, Func<double, double> sample, double start = 0)
    {
        var buffer = new float[Chunk];
        var max = 0.0;
        var chunks = (int)(seconds * Rate / Chunk);
        for (var c = 0; c < chunks; c++)
        {
            for (var i = 0; i < Chunk; i++) buffer[i] = (float)sample(start + (c * Chunk + i) / (double)Rate);
            analyzer.Feed(buffer, Rate);
            max = Math.Max(max, analyzer.Snapshot().Level);
        }
        return max;
    }

    // A kick drum: a 60 Hz burst of 100 ms at the start of every half second (120 BPM).
    private static Func<double, double> Kicks(double amplitude) => t => (t % 0.5) < 0.1 ? amplitude * Math.Sin(2 * Math.PI * 60 * t) : 0;

    [Fact]
    public void Silence_has_no_level_and_no_beats()
    {
        var analyzer = new MusicAnalyzer();
        Assert.Equal(0, Play(analyzer, 2, _ => 0));
        Assert.Equal(0, analyzer.Snapshot().Beats);
    }

    [Fact]
    public void A_steady_tone_is_loud_but_not_a_stream_of_beats()
    {
        var analyzer = new MusicAnalyzer();
        var max = Play(analyzer, 3, t => 0.5 * Math.Sin(2 * Math.PI * 1000 * t));
        Assert.True(max > 0.9, $"level {max}");
        Assert.True(analyzer.Snapshot().Beats <= 1, $"beats {analyzer.Snapshot().Beats}"); // at most the onset
    }

    [Fact]
    public void A_kick_drum_pattern_is_counted_beat_by_beat()
    {
        var analyzer = new MusicAnalyzer();
        Play(analyzer, 4, Kicks(0.6));          // eight kicks
        Assert.InRange(analyzer.Snapshot().Beats, 7, 8);
    }

    [Fact]
    public void Quiet_music_still_swings_through_the_range()
    {
        var analyzer = new MusicAnalyzer();
        var max = Play(analyzer, 4, Kicks(0.03)); // 20x quieter
        Assert.True(max > 0.8, $"auto-gain should lift quiet audio; level {max}");
        Assert.InRange(analyzer.Snapshot().Beats, 7, 8);
    }

    [Fact]
    public void When_audio_stops_arriving_the_level_drops_to_zero()
    {
        var analyzer = new MusicAnalyzer();
        Play(analyzer, 1, t => 0.5 * Math.Sin(2 * Math.PI * 440 * t));
        var later = Stopwatch.GetTimestamp() + Stopwatch.Frequency; // one second from now
        var snapshot = analyzer.Snapshot(later);
        Assert.Equal(0, snapshot.Level);
        Assert.Equal(analyzer.Snapshot().Beats, snapshot.Beats); // beats are history, they don't vanish
    }
}

public class MusicControllerTests
{
    [Fact]
    public void Brightness_has_a_dim_floor_and_reaches_full()
    {
        Assert.Equal(MusicController.Floor, MusicController.BrightnessFor(0), 6);
        Assert.Equal(1, MusicController.BrightnessFor(1), 6);
        Assert.True(MusicController.BrightnessFor(0.5) > MusicController.BrightnessFor(0.2));
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(0.5, 1.5)]
    [InlineData(1, 2.5)]
    public void Sensitivity_maps_to_gain(double sensitivity, double gain)
    {
        Assert.Equal(gain, MusicController.GainFor(sensitivity), 6);
    }

    [Fact]
    public void A_palette_colour_keeps_its_hue_at_any_brightness_and_white_stays_white()
    {
        Assert.Equal(new RgbColor(128, 0, 0), MusicController.PaletteColor(new RgbColor(255, 0, 0), 0.5));
        Assert.Equal(new RgbColor(64, 64, 64), MusicController.PaletteColor(RgbColor.White, 0.25));
    }

    [Fact]
    public async Task With_colours_picked_each_beat_steps_to_the_next_and_nothing_else_appears()
    {
        var red = new RgbColor(255, 0, 0);
        var blue = new RgbColor(0, 0, 255);
        var audio = new FakeAudio();
        var music = new MusicController(new StripController(new CountingTransport()), audio) { IntervalMs = 5, Palette = [red, blue] };
        var hues = new List<double>();
        music.Sampled += c => { var (h, s, v) = c.ToHsv(); if (v > 0.2) lock (hues) hues.Add(h); };

        music.Start();
        // Two seconds of kicks (one every half second), delivered in 10 ms chunks close to real time.
        var chunk = new float[480];
        for (var c = 0; c < 200; c++)
        {
            for (var i = 0; i < chunk.Length; i++)
            {
                var t = (c * chunk.Length + i) / 48000.0;
                chunk[i] = (t % 0.5) < 0.1 ? (float)(0.6 * Math.Sin(2 * Math.PI * 60 * t)) : 0f;
            }
            audio.Analyzer.Feed(chunk, 48000);
            await Task.Delay(10);
        }
        music.Stop();

        List<double> seen;
        lock (hues) seen = [.. hues];
        Assert.NotEmpty(seen);
        Assert.All(seen, h => Assert.True(h < 1 || Math.Abs(h - 240) < 1, $"hue {h} is not one of the picked colours"));
        Assert.Contains(seen, h => h < 1);                  // red showed
        Assert.Contains(seen, h => Math.Abs(h - 240) < 1);  // and a beat moved it to blue
    }

    [Fact]
    public void The_saved_palette_reads_back_in_order_and_skips_anything_unreadable()
    {
        var settings = new AppSettings { MusicPalette = ["#FF0000", "not a colour", "#0000FF"] };
        Assert.Equal([new RgbColor(255, 0, 0), new RgbColor(0, 0, 255)], settings.MusicColors);
        Assert.Empty(new AppSettings().MusicColors);
    }

    [Fact]
    public async Task Loud_audio_lights_the_strip_and_silence_dims_it()
    {
        var audio = new FakeAudio();
        var transport = new CountingTransport();
        var music = new MusicController(new StripController(transport), audio) { IntervalMs = 10 };
        var brightness = new List<double>();
        music.Sampled += c => { lock (brightness) brightness.Add(c.ToHsv().Value); };

        music.Start();
        Assert.True(audio.Started);
        var tone = Enumerable.Range(0, 480).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / 48000.0))).ToArray();
        for (var i = 0; i < 20; i++) { audio.Analyzer.Feed(tone, 48000); await Task.Delay(10); }
        double loud;
        lock (brightness) loud = brightness.Max();
        await Task.Delay(1500);                 // audio stops: stale after 0.2 s, then a quarter-second fade
        double quiet;
        lock (brightness) quiet = brightness[^1];
        music.Stop();

        Assert.False(audio.Started);
        Assert.True(loud > 0.9, $"loud brightness {loud}");
        Assert.True(quiet < 0.15, $"brightness after silence {quiet}");
        Assert.True(transport.Frames > 0);
    }
}
