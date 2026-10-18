using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace HyperXBatteryTray.Devices;

/// <summary>
/// Read-only Windows Bluetooth battery source. Only matched, paired and currently
/// connected Windows Bluetooth endpoints are eligible. No discovery advertising,
/// pairing, firmware operations or vendor-specific writes are performed.
/// </summary>
internal static class BluetoothBatteryProbe
{
    private const string ClassicSelector =
        "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";
    private const string LowEnergySelector =
        "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";
    private const string ConnectedKey = "System.Devices.Aep.IsConnected";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    private const string BatteryKey = "System.Devices.BatteryLife";
    private static readonly string[] EndpointProperties = { ConnectedKey, ContainerKey, BatteryKey };
    private static readonly string[] ContainerProperties = { BatteryKey };

    private static readonly IReadOnlyDictionary<string, string[]> DeviceAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["HyperX Cloud III S"] = new[] { "HyperX Cloud III S", "HyperX Cloud III S Wireless" },
            ["HyperX Cloud Alpha 2 Wireless"] = new[] { "HyperX Cloud Alpha 2", "HyperX Cloud Alpha 2 Wireless" },
            ["HyperX Cloud Flight 2"] = new[] { "HyperX Cloud Flight 2", "HyperX Cloud Flight 2 Wireless" },
            ["HyperX Cloud Stinger 3 Wireless"] = new[] { "HyperX Cloud Stinger 3", "HyperX Cloud Stinger 3 Wireless" },
            ["HyperX Cloud Mix 2"] = new[] { "HyperX Cloud MIX 2", "HyperX Cloud Mix 2" }
        };

    public static bool SupportsModel(string modelName) => DeviceAliases.ContainsKey(modelName);

    public static async Task<int?> ReadBatteryAsync(string modelName, CancellationToken token)
    {
        if (!DeviceAliases.TryGetValue(modelName, out string[]? aliases))
            return null;

        token.ThrowIfCancellationRequested();
        // AssociationEndpoints are filtered to Bluetooth, unlike generic sound
        // endpoints that can share a friendly name with an attached USB dongle.
        List<(DeviceInformation Device, bool LowEnergy, string Container)> connected = new();
        await CollectAsync(ClassicSelector, false).ConfigureAwait(false);
        await CollectAsync(LowEnergySelector, true).ConfigureAwait(false);

        // Two different physical devices of the same model must not be silently
        // merged. Classic+LE endpoints are equivalent only with a shared ContainerId.
        if (connected.Count == 0)
            return null;

        string[] distinctContainers = connected.Select(item => item.Container)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinctContainers.Length != 1 || string.IsNullOrWhiteSpace(distinctContainers[0]))
        {
            if (connected.Count != 1)
            {
                Debug.WriteLine($"Bluetooth battery match for '{modelName}' is ambiguous; no battery will be reported.");
                return null;
            }
        }

        foreach (var item in connected)
        {
            token.ThrowIfCancellationRequested();
            int? percent = GetPercent(item.Device.Properties, BatteryKey);
            if (percent.HasValue)
                return percent;
        }

        // Bluetooth properties can be present only on the Windows device
        // container, rather than the endpoint itself.
        if (Guid.TryParse(connected[0].Container, out Guid containerGuid))
        {
            try
            {
                DeviceInformation container = await DeviceInformation.CreateFromIdAsync(
                    containerGuid.ToString("B"), ContainerProperties, DeviceInformationKind.DeviceContainer);
                token.ThrowIfCancellationRequested();
                if (container != null)
                {
                    int? percent = GetPercent(container.Properties, BatteryKey);
                    if (percent.HasValue)
                        return percent;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Bluetooth container battery read unavailable: {ex.Message}");
            }
        }

        // BLE-only fallback. Battery Service (0x180F) is optional in headsets;
        // never guess an unsupported proprietary service or connect to an unpaired device.
        foreach (var item in connected.Where(item => item.LowEnergy))
        {
            token.ThrowIfCancellationRequested();
            int? percent = await ReadStandardGattBatteryAsync(item.Device.Id, token).ConfigureAwait(false);
            if (percent.HasValue)
                return percent;
        }
        return null;

        async Task CollectAsync(string selector, bool isLowEnergy)
        {
            try
            {
                DeviceInformationCollection endpoints = await DeviceInformation.FindAllAsync(
                    selector, EndpointProperties, DeviceInformationKind.AssociationEndpoint);
                token.ThrowIfCancellationRequested();
                foreach (DeviceInformation endpoint in endpoints)
                {
                    string normalizedName = endpoint.Name?.Trim() ?? string.Empty;
                    if (normalizedName.StartsWith("LE-", StringComparison.OrdinalIgnoreCase))
                        normalizedName = normalizedName[3..];
                    if (!aliases.Any(alias => string.Equals(alias, normalizedName, StringComparison.OrdinalIgnoreCase)) ||
                        endpoint.Pairing.IsPaired != true ||
                        !GetBoolean(endpoint.Properties, ConnectedKey))
                        continue;

                    endpoint.Properties.TryGetValue(ContainerKey, out object? id);
                    connected.Add((endpoint, isLowEnergy, id?.ToString() ?? string.Empty));
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"Windows Bluetooth endpoint enumeration unavailable: {ex.Message}");
            }
        }
    }

    private static bool GetBoolean(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out object? value) && value is bool connected && connected;

    private static int? GetPercent(IReadOnlyDictionary<string, object> properties, string key)
    {
        if (!properties.TryGetValue(key, out object? value) || value == null)
            return null;
        try
        {
            int percent = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            // Windows uses 101 as an unknown battery sentinel; do not clamp it to 100.
            return percent is >= 0 and <= 100 ? percent : null;
        }
        catch (Exception) { return null; }
    }

    private static async Task<int?> ReadStandardGattBatteryAsync(string endpointId, CancellationToken token)
    {
        try
        {
            using BluetoothLEDevice? device = await BluetoothLEDevice.FromIdAsync(endpointId);
            token.ThrowIfCancellationRequested();
            if (device?.ConnectionStatus != BluetoothConnectionStatus.Connected)
                return null;

            GattDeviceServicesResult services = await device.GetGattServicesForUuidAsync(
                GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
            token.ThrowIfCancellationRequested();
            if (services.Status != GattCommunicationStatus.Success)
                return null;

            foreach (GattDeviceService service in services.Services)
            {
                using (service)
                {
                    GattCharacteristicsResult characteristics = await service.GetCharacteristicsForUuidAsync(
                        GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
                    token.ThrowIfCancellationRequested();
                    if (characteristics.Status != GattCommunicationStatus.Success)
                        continue;
                    foreach (GattCharacteristic characteristic in characteristics.Characteristics)
                    {
                        GattReadResult response = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                        token.ThrowIfCancellationRequested();
                        if (response.Status != GattCommunicationStatus.Success || response.Value.Length != 1)
                            continue;
                        DataReader reader = DataReader.FromBuffer(response.Value);
                        int level = reader.ReadByte();
                        if (level <= 100)
                            return level;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"Bluetooth GATT battery read unavailable: {ex.Message}");
        }
        return null;
    }
}
