namespace Beetle.Core;

/// <summary>
/// Turns intentions ("be this colour", "turn off") into frames on the transport. It serialises
/// writes with a one-slot lock, because a BLE controller handles one write-without-response at a
/// time and will drop or garble overlapping ones.
/// </summary>
public sealed class StripController(ILedTransport transport)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsOn { get; private set; }
    public RgbColor Color { get; private set; } = RgbColor.White;
    public int Brightness { get; private set; } = 100;

    public async Task PowerAsync(bool on, CancellationToken cancellationToken = default)
    {
        await SendAsync(LedCommands.Power(on), cancellationToken);
        IsOn = on;
    }

    /// <summary>Sends a colour, powering the strip on first if it is off so the colour is actually visible.</summary>
    public async Task SetColorAsync(RgbColor color, bool powerOnIfNeeded = true, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (powerOnIfNeeded && !IsOn)
            {
                await transport.WriteAsync(LedCommands.Power(true), cancellationToken);
                IsOn = true;
            }
            await transport.WriteAsync(LedCommands.Color(color), cancellationToken);
            Color = color;
        }
        finally { _gate.Release(); }
    }

    public async Task SetBrightnessAsync(int percent, CancellationToken cancellationToken = default)
    {
        percent = Math.Clamp(percent, 0, 100);
        await SendAsync(LedCommands.Brightness(percent), cancellationToken);
        Brightness = percent;
    }

    private async Task SendAsync(byte[] frame, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await transport.WriteAsync(frame, cancellationToken); }
        finally { _gate.Release(); }
    }
}
