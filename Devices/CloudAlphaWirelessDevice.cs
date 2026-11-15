namespace HyperXBatteryTray.Devices;

public sealed class CloudAlphaWirelessDevice : HyperXBatteryDeviceBase
{
    private const byte ProtocolReportId = 0x21;
    private const byte ProtocolMarker = 0xBB;
    private const byte BatteryResponseSelector = 0x0B;
    private const byte ChargeResponseSelector = 0x0C;
    private const byte MicrophoneMuteResponseSelector = 0x0A;
    private const byte BatteryNotificationSelector = 0x25;
    private const byte ChargeNotificationSelector = 0x26;
    private const byte MicrophoneMuteNotificationSelector = 0x23;

    private static readonly byte[] ChargeCommandBytes =
        { ProtocolReportId, ProtocolMarker, ChargeResponseSelector };

    private static readonly byte[] MicrophoneMuteCommandBytes =
        { ProtocolReportId, ProtocolMarker, MicrophoneMuteResponseSelector };

    internal static readonly HyperXDeviceDefinition DeviceDefinition = new()
    {
        Name = "HyperX Cloud Alpha",
        VendorId = 0x03F0,
        ProductId = 0x098D,
        InterfacePattern = "VID_03F0&PID_098D&MI_03&Col01",
        BatteryCommandBytes = new byte[]
        {
            ProtocolReportId, ProtocolMarker, BatteryResponseSelector
        },
        BatteryByteIndex = 3,
        SupportsMicrophoneMuteMonitoring = true,
        SupportsChargingMonitoring = true,
        NominalBatteryLifeHours = 300
    };

    public CloudAlphaWirelessDevice() : base(DeviceDefinition)
    {
    }

    protected override bool UsesContinuousReader => true;

    // The existing parser handles unsolicited battery reports independently
    // of replies to explicit queries. Experimental 60-second recovery in Beta.
    public override bool SupportsUnsolicitedBatteryNotifications => true;

    protected override byte[]? CreateChargeStatusCommand() =>
        CreateCommand(ChargeCommandBytes);

    protected override byte[]? CreateMicrophoneMuteCommand() =>
        CreateCommand(MicrophoneMuteCommandBytes);

    protected override bool TryParseBatteryReport(
        byte[] report,
        out int battery,
        out bool isResponse)
    {
        battery = -1;
        isResponse = false;

        if (!HasProtocolPrefix(report) || report.Length < 4)
            return false;

        if (report[2] != BatteryResponseSelector &&
            report[2] != BatteryNotificationSelector)
        {
            return false;
        }

        battery = report[3];

        if (battery > 100)
            return false;

        isResponse = report[2] == BatteryResponseSelector;
        return true;
    }

    protected override bool TryParseChargeStatusReport(
        byte[] report,
        out bool isCharging,
        out bool isResponse)
    {
        isCharging = false;
        isResponse = false;

        if (!HasProtocolPrefix(report) || report.Length < 4)
            return false;

        if (report[2] != ChargeResponseSelector &&
            report[2] != ChargeNotificationSelector)
        {
            return false;
        }

        if (report[3] is not 0 and not 1)
            return false;

        isCharging = report[3] == 1;
        isResponse = report[2] == ChargeResponseSelector;
        return true;
    }

    protected override bool TryParseMicrophoneMuteReport(
        byte[] report,
        out bool isMuted,
        out bool isResponse)
    {
        isMuted = false;
        isResponse = false;

        if (!HasProtocolPrefix(report) || report.Length < 4)
            return false;

        if (report[2] != MicrophoneMuteResponseSelector &&
            report[2] != MicrophoneMuteNotificationSelector)
        {
            return false;
        }

        isMuted = report[3] == 1;
        isResponse = report[2] == MicrophoneMuteResponseSelector;
        return true;
    }

    private static bool HasProtocolPrefix(byte[] report) =>
        report.Length >= 2 &&
        report[0] == ProtocolReportId &&
        report[1] == ProtocolMarker;
}
