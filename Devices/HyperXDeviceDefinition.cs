namespace HyperXBatteryTray.Devices;

public sealed class HyperXDeviceDefinition
{
    public required string Name { get; init; }

    public required ushort VendorId { get; init; }

    public required ushort ProductId { get; init; }

    public required string InterfacePattern { get; init; }

    public IReadOnlyList<string> AlternateInterfacePatterns { get; init; } = Array.Empty<string>();

    // Set only when a documented HID control collection's Usage Page/Usage is known.
    // A null pair retains the existing exact/first-match behavior for legacy drivers.
    public ushort? ControlUsagePage { get; init; }
    public ushort? ControlUsage { get; init; }


    public required byte[] BatteryCommandBytes { get; init; }

    public required int BatteryByteIndex { get; init; }

    public bool SupportsMicrophoneMuteMonitoring { get; init; }

    public bool SupportsChargingMonitoring { get; init; }

    public bool RequiresDedicatedDongle { get; init; }

    public double? NominalBatteryLifeHours { get; init; }

    public bool Matches(string devicePath)
    {
        return devicePath.Contains(
            InterfacePattern,
            StringComparison.OrdinalIgnoreCase) ||
            AlternateInterfacePatterns.Any(pattern =>
                !string.IsNullOrWhiteSpace(pattern) &&
                devicePath.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
