namespace Beetle.Core;

/// <summary>
/// The one thing the rest of Beetle needs from a strip: push a raw frame at it. Keeping this
/// behind an interface lets the controllers and their tests run without any Bluetooth hardware.
/// </summary>
public interface ILedTransport
{
    bool IsConnected { get; }

    /// <summary>Writes one <see cref="LedCommands"/> frame to the strip.</summary>
    Task WriteAsync(byte[] frame, CancellationToken cancellationToken = default);
}

/// <summary>A strip discovered over Bluetooth. <see cref="Id"/> is a Windows device id when the
/// strip is already paired; otherwise only the live advertising <see cref="Address"/> is known.</summary>
public sealed record BleDeviceInfo(string Name, string? Id, ulong Address)
{
    public bool Paired => Id is not null;
}
