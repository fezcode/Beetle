using System.Diagnostics;

namespace Beetle.Core;

/// <summary>
/// Cycles the strip through every hue at full saturation and brightness. Beetle drives each step
/// itself, the same way ambient mode does, rather than relying on the controller's built-in effect
/// codes, which are poorly documented and differ between ELK-BLEDOM units.
///
/// Events fire from the background loop; a UI subscriber must marshal them to its own thread.
/// </summary>
public sealed class RainbowController(StripController strip)
{
    public const double MinCycleSeconds = 3;
    public const double MaxCycleSeconds = 60;

    private double _cycleSeconds = 10;
    private CancellationTokenSource? _cts;

    /// <summary>Seconds for one full trip around the colour wheel. Settable while running: the hue
    /// carries on from where it is, so changing speed never makes the strip jump.</summary>
    public double CycleSeconds
    {
        get => Volatile.Read(ref _cycleSeconds);
        set => Volatile.Write(ref _cycleSeconds, Math.Clamp(value, MinCycleSeconds, MaxCycleSeconds));
    }

    /// <summary>Loop period; 40 ms is 25 steps a second, smooth to the eye and gentle on the BLE link.</summary>
    public int IntervalMs { get; init; } = 40;

    public bool IsRunning => _cts is not null;

    /// <summary>The colour just sent, for a live preview.</summary>
    public event Action<RgbColor>? Sampled;
    /// <summary>The loop stopped because a frame could not be sent (usually a dropped connection).</summary>
    public event Action<Exception>? Faulted;

    /// <summary>Advances a hue by the share of a cycle that <paramref name="elapsedSeconds"/> covers, wrapped to 0-360.</summary>
    public static double Advance(double hue, double elapsedSeconds, double cycleSeconds)
    {
        var next = hue + elapsedSeconds / cycleSeconds * 360;
        return ((next % 360) + 360) % 360;
    }

    public void Start()
    {
        if (IsRunning) return;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = RunAsync(cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // Start from the hue already on the strip, so switching to rainbow continues from it.
        var hue = strip.Color.ToHsv().Hue;
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = clock.Elapsed;
                hue = Advance(hue, (now - last).TotalSeconds, CycleSeconds);
                last = now;
                var color = RgbColor.FromHsv(hue, 1, 1);
                await strip.SetColorAsync(color, powerOnIfNeeded: true, cancellationToken);
                Sampled?.Invoke(color);
                await Task.Delay(IntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException) { /* Stop() was called */ }
        catch (Exception error)
        {
            _cts = null;
            Faulted?.Invoke(error);
        }
    }
}
