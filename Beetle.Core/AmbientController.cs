namespace Beetle.Core;

/// <summary>
/// Drives the strip from the screen. On a fixed cadence it samples the average screen colour,
/// eases the current colour toward it so the strip glides instead of strobing, and sends a frame
/// only when the colour has drifted far enough to be worth a Bluetooth write.
///
/// Events fire from the background loop; a UI subscriber must marshal them to its own thread.
/// </summary>
public sealed class AmbientController(StripController strip, IScreenSampler sampler)
{
    /// <summary>Colour boost applied to each sample, 0 (raw screen average) to 1 (strongest); see
    /// <see cref="RgbColor.Boost"/>. Settable while running, so a UI slider takes effect live.</summary>
    public double Boost { get; set; } = 0.4;
    /// <summary>Fraction of the way the strip moves toward each new sample per tick (0-1).
    /// 0.6 reaches ~90% of a change in about three ticks: quick, but still eased rather than strobing.</summary>
    public double Smoothing { get; init; } = 0.6;
    /// <summary>Loop period; 40 ms is 25 updates a second, which the strip's write-without-response channel keeps up with.</summary>
    public int IntervalMs { get; init; } = 40;
    /// <summary>Smallest per-channel change that earns a frame, so tiny jitter stays quiet.</summary>
    public int SendThreshold { get; init; } = 4;

    private CancellationTokenSource? _cts;

    public bool IsRunning => _cts is not null;

    /// <summary>The colour the loop just settled on, for a live preview.</summary>
    public event Action<RgbColor>? Sampled;
    /// <summary>The loop stopped because a frame could not be sent (usually a dropped connection).</summary>
    public event Action<Exception>? Faulted;

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
        var current = strip.Color;
        var lastSent = RgbColor.Black;
        var first = true;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var target = sampler.Sample().Boost(Boost);
                current = current.LerpTo(target, Smoothing);
                Sampled?.Invoke(current);

                if (first || current.MaxChannelDelta(lastSent) >= SendThreshold)
                {
                    await strip.SetColorAsync(current, powerOnIfNeeded: true, cancellationToken);
                    lastSent = current;
                    first = false;
                }

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
