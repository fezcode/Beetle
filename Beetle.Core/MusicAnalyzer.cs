using System.Diagnostics;

namespace Beetle.Core;

/// <summary>What the music is doing right now, as music mode needs it.</summary>
/// <param name="Level">Loudness relative to the recent peak, 0-1; 0 when no audio is arriving.</param>
/// <param name="Beats">Running count of detected beats; compare with a previous snapshot to see new ones.</param>
public readonly record struct MusicSnapshot(double Level, long Beats);

/// <summary>
/// Turns a stream of mono samples into loudness and beats. The capture thread calls <see cref="Feed"/>
/// with each chunk the audio device delivers; music mode reads <see cref="Snapshot"/> on its own cadence.
///
/// Loudness is the chunk's RMS divided by a slowly fading peak (an automatic gain), so a quiet track and
/// a loud one both swing the strip through its range. A beat is a jump in bass energy (a one-pole
/// low-pass around <see cref="BassCutoffHz"/>) well above its own recent average, at most one every
/// <see cref="MinBeatGapSeconds"/>. Time comes from the samples themselves, not a wall clock, so the
/// behaviour is the same however the chunks arrive, and tests can drive it with synthetic audio.
/// </summary>
public sealed class MusicAnalyzer
{
    public const double BassCutoffHz = 150;
    public const double MinBeatGapSeconds = 0.2;
    /// <summary>How far above its recent average bass must jump to count as a beat.</summary>
    public const double BeatRatio = 1.5;
    /// <summary>Below this RMS (full scale = 1) the audio is treated as silence: no beats, no level.</summary>
    public const double NoiseFloor = 0.003;
    /// <summary>No samples for this long and the music is considered stopped.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(200);

    private const double PeakFadeSeconds = 6;     // auto-gain: how long a loud moment keeps the ceiling up
    // The beat detector's sense of "recent" bass. It rises fast, so a sound that starts and then holds
    // counts once rather than again every MinBeatGapSeconds while a slow average catches up; it falls
    // slowly, so between kicks it keeps enough memory for the next kick to stand clear of it.
    private const double BassRiseSeconds = 0.1;
    private const double BassFallSeconds = 0.5;

    private readonly Lock _gate = new();
    private double _lowPass;
    private double _peak = NoiseFloor * 4;
    private double _bassAverage;
    private double _sinceBeat = MinBeatGapSeconds;
    private double _level;
    private long _beats;
    private long _lastFeed;   // Stopwatch timestamp of the most recent chunk; 0 = never

    /// <summary>Consumes one chunk of mono samples in -1..1 at <paramref name="sampleRate"/> Hz.</summary>
    public void Feed(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.IsEmpty || sampleRate <= 0) return;
        lock (_gate)
        {
            var alpha = 1 - Math.Exp(-2 * Math.PI * BassCutoffHz / sampleRate);
            double sum = 0, bassSum = 0;
            foreach (var x in samples)
            {
                _lowPass += alpha * (x - _lowPass);
                sum += x * x;
                bassSum += _lowPass * _lowPass;
            }
            var rms = Math.Sqrt(sum / samples.Length);
            var bass = Math.Sqrt(bassSum / samples.Length);
            var dt = (double)samples.Length / sampleRate;

            _peak = Math.Max(rms, Math.Max(_peak * Math.Exp(-dt / PeakFadeSeconds), NoiseFloor * 4));
            _level = rms < NoiseFloor ? 0 : Math.Clamp(rms / _peak, 0, 1);

            _sinceBeat += dt;
            if (rms >= NoiseFloor && _sinceBeat >= MinBeatGapSeconds && bass > _bassAverage * BeatRatio && bass > NoiseFloor)
            {
                _beats++;
                _sinceBeat = 0;
            }
            var tau = bass > _bassAverage ? BassRiseSeconds : BassFallSeconds;
            _bassAverage += (bass - _bassAverage) * (1 - Math.Exp(-dt / tau));
            _lastFeed = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>The current state. Level drops to 0 once audio has stopped arriving for
    /// <see cref="StaleAfter"/>: Windows sends no loopback data at all while nothing plays.</summary>
    public MusicSnapshot Snapshot(long? nowTimestamp = null)
    {
        lock (_gate)
        {
            var now = nowTimestamp ?? Stopwatch.GetTimestamp();
            var stale = _lastFeed == 0 || Stopwatch.GetElapsedTime(_lastFeed, now) > StaleAfter;
            return new MusicSnapshot(stale ? 0 : _level, _beats);
        }
    }
}
