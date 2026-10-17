using HyperXBatteryTray.Hid;

namespace HyperXBatteryTray.Devices;

public sealed class HyperXDeviceManager : IDisposable
{
    private const string ThreeInOneReceiverInterfacePattern = "VID_03F0&PID_01BF";
    private const string ThreeInOneAlternateReceiverInterfacePattern = "VID_03F0&PID_01C5";
    private const int OriginalRegistrationCount = 10;

    private readonly List<IHyperXDevice> _devices = new();

    private static readonly IReadOnlyList<HyperXDeviceRegistration>
        SupportedDevices =
        new List<HyperXDeviceRegistration>
        {
            new()
            {
                Definition = Cloud3WirelessDevice.Definition,
                Factory = () => new Cloud3WirelessDevice()
            },
            new()
            {
                Definition = Cloud3SWirelessDevice.DeviceDefinition,
                Factory = () => new Cloud3SWirelessDevice()
            },
            new()
            {
                Definition = Cloud2CoreWirelessDevice.DeviceDefinition,
                Factory = () => new Cloud2CoreWirelessDevice()
            },
            new()
            {
                Definition = CloudAlphaWirelessDevice.DeviceDefinition,
                Factory = () => new CloudAlphaWirelessDevice()
            },
            new()
            {
                Definition = CloudStinger2WirelessDevice.DeviceDefinition,
                Factory = () => new CloudStinger2WirelessDevice()
            },
            new()
            {
                Definition = CloudFlightSDevice.DeviceDefinition,
                Factory = () => new CloudFlightSDevice()
            },
            new()
            {
                Definition = CloudFlightWirelessDevice.DeviceDefinition,
                Factory = () => new CloudFlightWirelessDevice()
            },
            new()
            {
                Definition = CloudStingerCoreWirelessDevice.DeviceDefinition,
                Factory = () => new CloudStingerCoreWirelessDevice()
            },
            new()
            {
                Definition = CloudFlight2Device.DeviceDefinition,
                Factory = () => new CloudFlight2Device()
            },
            new()
            {
                Definition = CloudMix2Device.DeviceDefinition,
                Factory = () => new CloudMix2Device()
            },
            new()
            {
                Definition = CloudStingerCoreWirelessDevice.PlayStationDefinition,
                Factory = () => new CloudStingerCoreWirelessDevice(
                    CloudStingerCoreWirelessDevice.PlayStationDefinition)
            },
            new()
            {
                Definition = Cloud3WirelessAlternateReceiverDevice.Definition,
                Factory = () => new Cloud3WirelessAlternateReceiverDevice()
            },
            new()
            {
                Definition = Cloud3SAlternateReceiverDevice.DeviceDefinition,
                Factory = () => new Cloud3SAlternateReceiverDevice()
            },
            new()
            {
                Definition = UniversalAdditionalWirelessDevice.Alpha2Definition,
                Factory = () => new UniversalAdditionalWirelessDevice(UniversalAdditionalWirelessDevice.Alpha2Definition)
            },
            new()
            {
                Definition = UniversalAdditionalWirelessDevice.Flight2XboxDefinition,
                Factory = () => new UniversalAdditionalWirelessDevice(UniversalAdditionalWirelessDevice.Flight2XboxDefinition)
            },
            new()
            {
                Definition = CloudStinger3WirelessDevice.PcDefinition,
                Factory = () => new CloudStinger3WirelessDevice(CloudStinger3WirelessDevice.PcDefinition)
            },
            new()
            {
                Definition = CloudStinger3WirelessDevice.PsDefinition,
                Factory = () => new CloudStinger3WirelessDevice(CloudStinger3WirelessDevice.PsDefinition)
            },
            new()
            {
                Definition = CloudStinger3WirelessDevice.XboxDefinition,
                Factory = () => new CloudStinger3WirelessDevice(CloudStinger3WirelessDevice.XboxDefinition)
            },
            new()
            {
                Definition = UniversalAdditionalWirelessDevice.Cloud3STioDefinition,
                Factory = () => new UniversalAdditionalWirelessDevice(UniversalAdditionalWirelessDevice.Cloud3STioDefinition, 0x05BE)
            },
            new()
            {
                Definition = UniversalAdditionalWirelessDevice.Flight2TioDefinition,
                Factory = () => new UniversalAdditionalWirelessDevice(UniversalAdditionalWirelessDevice.Flight2TioDefinition, 0x0BC1)
            }
        };

    public IReadOnlyList<IHyperXDevice> Devices => _devices;

    public IReadOnlyList<IHyperXDevice> DiscoverDevices()
    {
        DisposeDevices();

        foreach (HyperXDeviceRegistration registration in SupportedDevices)
        {
            string? devicePath = HidConnection.FindDevice(registration.Definition);

            if (string.IsNullOrWhiteSpace(devicePath))
                continue;

            _devices.Add(registration.Factory());
        }

        return _devices;
    }

    public IHyperXDevice? GetFirstAvailableDevice()
    {
        DiscoverDevices();

        return _devices.Count > 0
            ? _devices[0]
            : null;
    }

    public IHyperXDevice? GetAvailableDevice(string? selectedDeviceName) =>
        CreateDevice(selectedDeviceName);

    public static IHyperXDevice? CreateDevice(string? selectedDeviceName)
    {
        if (string.IsNullOrWhiteSpace(selectedDeviceName))
            return null;

        // Explicit historical variants still resolve to their original driver.
        // A model selected in the current UI gets a receiver-aware dispatcher.
        if (TryGetReceiverFamily(selectedDeviceName, out string? modelName))
        {
            IHyperXDevice receiverDevice = new ReceiverAwareDevice(modelName!, GetReceiverCandidates(modelName!));
            return BluetoothBatteryProbe.SupportsModel(modelName!)
                ? new BluetoothFallbackDevice(receiverDevice, modelName!)
                : receiverDevice;
        }

        HyperXDeviceRegistration? registration = FindRegistration(selectedDeviceName);
        if (registration == null)
            return null;
        IHyperXDevice device = new ReceiverAwareDevice(registration.Definition.Name,
            new[] { registration });
        // Keep the same parser and HID command per registration; only endpoint
        // resolution and battery validation are shared across all models.
        string name = registration.Definition.Name;
        return string.Equals(selectedDeviceName, name, StringComparison.OrdinalIgnoreCase) &&
               BluetoothBatteryProbe.SupportsModel(name)
            ? new BluetoothFallbackDevice(device, name)
            : device;
    }

    public static bool IsSameDevice(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);

        return string.Equals(
            GetSelectionModelName(left),
            GetSelectionModelName(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool SupportsMicrophoneMuteMonitoring(
        string? selectedDeviceName)
    {
        HyperXDeviceRegistration? registration =
            FindRegistration(selectedDeviceName);

        return registration?.Definition.SupportsMicrophoneMuteMonitoring == true ||
            (TryGetReceiverFamily(selectedDeviceName, out string? group) &&
                GetReceiverCandidates(group!).Any(candidate => candidate.Definition.SupportsMicrophoneMuteMonitoring));
    }

    public static bool SupportsChargingMonitoring(
        string? selectedDeviceName)
    {
        HyperXDeviceRegistration? registration =
            FindRegistration(selectedDeviceName);

        return registration?.Definition.SupportsChargingMonitoring == true ||
            (TryGetReceiverFamily(selectedDeviceName, out string? group) &&
                GetReceiverCandidates(group!).Any(candidate => candidate.Definition.SupportsChargingMonitoring));
    }

    internal static bool RequiresDedicatedDongle(
        string? selectedDeviceName)
    {
        HyperXDeviceRegistration? registration =
            FindRegistration(selectedDeviceName);

        // Model-level selection can also use a shared Three-In-One receiver.
        if (TryGetReceiverFamily(selectedDeviceName, out _))
            return false;
        return registration?.Definition.RequiresDedicatedDongle == true;
    }

    public static async Task<IReadOnlyList<string>> ProbeResponsiveDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        List<string> detected = new();
        // Probe model families through the exact same receiver resolver that
        // normal selection uses. Avoid 19 independent uncoordinated probes.
        List<(string Model, IReadOnlyList<HyperXDeviceRegistration> Registrations)> searches = new();
        foreach (string family in ReceiverFamilies.Keys)
            searches.Add((family, GetReceiverCandidates(family)));

        foreach (HyperXDeviceRegistration registration in SupportedDevices)
        {
            if (ReceiverFamilies.Values.Any(family => family.Contains(
                    registration.Definition.Name, StringComparer.OrdinalIgnoreCase)))
                continue;
            searches.Add((registration.Definition.Name, new[] { registration }));
        }

        foreach (var (model, registrations) in searches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using IHyperXDevice device = new ReceiverAwareDevice(model, registrations);
            try
            {
                if (!device.Connect()) continue;
                int? battery = await device.QueryBatteryAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (battery is >= 0 and <= 100)
                    detected.Add(model);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Automatic device detection could not probe '{model}': {ex.Message}");
            }
        }
        return detected;
    }

    internal static double? GetNominalBatteryLifeHours(
        string? selectedDeviceName)
    {
        HyperXDeviceRegistration? registration =
            FindRegistration(selectedDeviceName);

        if (registration != null)
            return registration.Definition.NominalBatteryLifeHours;
        return TryGetReceiverFamily(selectedDeviceName, out string? group)
            ? GetReceiverCandidates(group!).Select(c => c.Definition.NominalBatteryLifeHours).FirstOrDefault(x => x.HasValue)
            : null;
    }

    internal static bool IsSupportedDeviceName(string? selectedDeviceName) =>
        FindRegistration(selectedDeviceName) != null ||
        TryGetReceiverFamily(selectedDeviceName, out _);

    internal static bool IsThreeInOneReceiverPresent() =>
        !string.IsNullOrWhiteSpace(
            HidConnection.FindDeviceByInterfacePattern(
                ThreeInOneReceiverInterfacePattern)) ||
        !string.IsNullOrWhiteSpace(
            HidConnection.FindDeviceByInterfacePattern(
                ThreeInOneAlternateReceiverInterfacePattern));

    internal static string NormalizeSupportedDeviceName(string value) =>
        NormalizeDeviceName(value);

    /// <summary>UI identity only; never rewrites historical persisted variant IDs.</summary>
    public static string GetSelectionModelName(string? selectedName)
    {
        if (string.IsNullOrWhiteSpace(selectedName))
            return string.Empty;
        string normalized = NormalizeDeviceName(selectedName);
        foreach ((string model, string[] variants) in ReceiverFamilies)
        {
            if (string.Equals(normalized, model, StringComparison.OrdinalIgnoreCase) ||
                variants.Any(variant => string.Equals(variant, normalized, StringComparison.OrdinalIgnoreCase)))
                return model;
        }
        return normalized;
    }

    private static bool TryGetReceiverFamily(string? name, out string? group)
    {
        group = null;
        if (string.IsNullOrWhiteSpace(name)) return false;
        string normalized = NormalizeDeviceName(name);
        foreach (string model in ReceiverFamilies.Keys)
        {
            if (!string.Equals(model, normalized, StringComparison.OrdinalIgnoreCase)) continue;
            group = model;
            return true;
        }
        return false;
    }

    private static IReadOnlyList<HyperXDeviceRegistration> GetReceiverCandidates(string model) =>
        SupportedDevices.Where(item =>
            ReceiverFamilies[model].Contains(item.Definition.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

    // Only models with documented receiver variants are consolidated.
    // Entries in SupportedDevices retain their exact original registrations/factories.
    private static readonly IReadOnlyDictionary<string, string[]> ReceiverFamilies =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["HyperX Cloud III"] = new[]
            {
                "HyperX Cloud III Wireless", "HyperX Cloud III (Receiver 0FCC)"
            },
            ["HyperX Cloud III S"] = new[]
            {
                "HyperX Cloud III S", "HyperX Cloud III S (Receiver 02CC)",
                "HyperX Cloud III S (TIO - Three-In-One)"
            },
            ["HyperX Cloud Flight 2"] = new[]
            {
                "HyperX Cloud Flight 2", "HyperX Cloud Flight 2 (Xbox)",
                "HyperX Cloud Flight 2 (TIO - Three-In-One)"
            },
            ["HyperX Cloud Stinger 3 Wireless"] = new[]
            {
                "HyperX Cloud Stinger 3 Wireless (PC)",
                "HyperX Cloud Stinger 3 Wireless (PlayStation)",
                "HyperX Cloud Stinger 3 Wireless (Xbox)"
            },
            ["HyperX Cloud Stinger Core Wireless + 7.1"] = new[]
            {
                "HyperX Cloud Stinger Core Wireless + 7.1",
                "HyperX Cloud Stinger Core Wireless + 7.1 (PlayStation)"
            }
        };

    private static HyperXDeviceRegistration? FindRegistration(
        string? selectedDeviceName)
    {
        if (string.IsNullOrWhiteSpace(selectedDeviceName))
            return null;

        string normalized = NormalizeDeviceName(selectedDeviceName);

        return SupportedDevices.FirstOrDefault(item =>
            string.Equals(
                NormalizeDeviceName(item.Definition.Name),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeDeviceName(string value)
    {
        string normalized = value.Trim();

        // These labels are distinct registered devices, not legacy display-name suffixes.
        // In particular, TIO uses the shared receiver and must never select the dedicated driver.
        if (SupportedDevices.Skip(OriginalRegistrationCount).Any(item => string.Equals(item.Definition.Name, normalized,
                StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        if (string.Equals(
                normalized,
                "HyperX Cloud III Wireless",
                StringComparison.OrdinalIgnoreCase))
        {
            return "HyperX Cloud III";
        }

        if (string.Equals(
                normalized,
                Cloud3SWirelessDevice.DeviceDefinition.Name,
                StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(
                Cloud3SWirelessDevice.DeviceDefinition.Name + " (",
                StringComparison.OrdinalIgnoreCase))
        {
            return Cloud3SWirelessDevice.DeviceDefinition.Name;
        }

        if (string.Equals(
                normalized,
                CloudFlight2Device.DeviceDefinition.Name,
                StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(
                CloudFlight2Device.DeviceDefinition.Name + " (",
                StringComparison.OrdinalIgnoreCase))
        {
            return CloudFlight2Device.DeviceDefinition.Name;
        }

        return normalized;
    }

    private void DisposeDevices()
    {
        foreach (IHyperXDevice device in _devices)
            device.Dispose();

        _devices.Clear();
    }

    public void Dispose()
    {
        DisposeDevices();
    }
}
