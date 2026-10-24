using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;

namespace Beetle.Core;

/// <summary>
/// The real Bluetooth LE strip. It finds ELK-BLEDOM controllers, opens the GATT write
/// characteristic and sends frames with no response, which is how the strip expects them.
/// One instance holds at most one live connection.
/// </summary>
public sealed class BleStrip : ILedTransport, IDisposable
{
    private BluetoothLEDevice? _device;
    // Held so it can be disposed: an open GattDeviceService keeps Windows' GATT session alive
    // even after the device object is disposed, and a connected strip stops advertising.
    private GattDeviceService? _service;
    private GattCharacteristic? _characteristic;

    public bool IsConnected { get; private set; }
    public string? ConnectedName { get; private set; }

    /// <summary>Raised on the thread pool whenever the live connection comes or goes.</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>
    /// Looks for strips two ways and merges the results: Windows' list of already-paired BLE
    /// devices, and a short live scan for anything currently advertising an ELK-BLEDOM name.
    /// </summary>
    public static async Task<IReadOnlyList<BleDeviceInfo>> ScanAsync(TimeSpan advertisingWindow, CancellationToken cancellationToken = default)
    {
        var byKey = new Dictionary<string, BleDeviceInfo>(StringComparer.OrdinalIgnoreCase);

        var paired = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
        foreach (var info in paired)
            if (Matches(info.Name))
                byKey[info.Name.Trim()] = new BleDeviceInfo(info.Name.Trim(), info.Id, 0);

        var seen = new Dictionary<ulong, string>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, args) =>
        {
            var name = args.Advertisement.LocalName;
            // The strip pads its advertised name with spaces ("ELK-BLEDOM      ").
            if (!string.IsNullOrEmpty(name) && Matches(name)) lock (seen) seen[args.BluetoothAddress] = name.Trim();
        };
        watcher.Start();
        try { await Task.Delay(advertisingWindow, cancellationToken); }
        catch (OperationCanceledException) { }
        finally { watcher.Stop(); }

        foreach (var (address, name) in seen)
            if (!byKey.ContainsKey(name)) // a paired entry already carries a device id, which connects more reliably
                byKey[name] = new BleDeviceInfo(name, null, address);

        return byKey.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool Matches(string? name) =>
        name is not null && (name.Contains("ELK", StringComparison.OrdinalIgnoreCase) || name.Contains("BLEDOM", StringComparison.OrdinalIgnoreCase));

    /// <summary>Opens a connection to one strip and resolves its write characteristic.
    /// Throws with a readable reason if the strip can't be reached.</summary>
    public async Task ConnectAsync(BleDeviceInfo device, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();

        var ble = device.Id is not null
            ? await BluetoothLEDevice.FromIdAsync(device.Id)
            : await BluetoothLEDevice.FromBluetoothAddressAsync(device.Address);
        if (ble is null)
            throw new InvalidOperationException("Windows could not open that Bluetooth device. Make sure the strip is powered and in range.");

        cancellationToken.ThrowIfCancellationRequested();

        var services = await ble.GetGattServicesForUuidAsync(LedCommands.ServiceUuid, BluetoothCacheMode.Uncached);
        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
        {
            foreach (var s in services.Services ?? []) s.Dispose();
            ble.Dispose();
            throw new InvalidOperationException($"Connected, but the strip's control service wasn't found ({services.Status}). It may be out of range or busy with another app.");
        }
        var service = services.Services[0];
        foreach (var extra in services.Services.Skip(1)) extra.Dispose();

        var characteristics = await service.GetCharacteristicsForUuidAsync(LedCommands.CharacteristicUuid, BluetoothCacheMode.Uncached);
        if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
        {
            service.Dispose();
            ble.Dispose();
            throw new InvalidOperationException($"The strip's control service has no writable channel ({characteristics.Status}).");
        }

        _device = ble;
        _service = service;
        _characteristic = characteristics.Characteristics[0];
        ConnectedName = device.Name;
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;
        SetConnected(true);
    }

    public Task DisconnectAsync()
    {
        // Drop the characteristic and service before the device, so nothing keeps the link open.
        _characteristic = null;
        _service?.Dispose();
        _service = null;
        if (_device is not null)
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _device.Dispose();
        }
        _device = null;
        ConnectedName = null;
        SetConnected(false);
        return Task.CompletedTask;
    }

    public async Task WriteAsync(byte[] frame, CancellationToken cancellationToken = default)
    {
        var characteristic = _characteristic ?? throw new InvalidOperationException("Not connected to a strip.");
        cancellationToken.ThrowIfCancellationRequested();
        var status = await characteristic.WriteValueAsync(CryptographicBuffer.CreateFromByteArray(frame), GattWriteOption.WriteWithoutResponse);
        if (status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"The strip rejected a command ({status}).");
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args) =>
        SetConnected(sender.ConnectionStatus == BluetoothConnectionStatus.Connected);

    private void SetConnected(bool connected)
    {
        if (connected == IsConnected) return;
        IsConnected = connected;
        ConnectionChanged?.Invoke(connected);
    }

    public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
}
