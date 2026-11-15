namespace HyperXBatteryTray.Devices;

public sealed class Cloud2CoreWirelessDevice : HyperXBatteryDeviceBase
{
    private const byte ProtocolReportId = 0x66;
    private const byte BatteryResponseSelector = 0x89;
    private const byte ChargeResponseSelector = 0x8A;
    private const byte MicrophoneMuteResponseSelector = 0x86;
    private const byte BatteryNotificationSelector = 0x0D;
    private const byte ChargeNotificationSelector = 0x0C;
    private const byte MicrophoneMuteNotificationSelector = 0x0A;

    private static readonly byte[] ChargeCommandBytes =
        { ProtocolReportId, ChargeResponseSelector };

    private static readonly byte[] MicrophoneMuteCommandBytes =
        { ProtocolReportId, MicrophoneMuteResponseSelector };

    internal static readonly HyperXDeviceDefinition DeviceDefinition = new()
    {
        Name = "HyperX Cloud 2 Core",
        VendorId = 0x03F0,
        ProductId = 0x0995,
        InterfacePattern = "VID_03F0&PID_0995&MI_03&Col02",
        BatteryCommandBytes = new byte[] { ProtocolReportId, BatteryResponseSelector },
        BatteryByteIndex = 4,
        SupportsMicrophoneMuteMonitoring = true,
        SupportsChargingMonitoring = true,
        NominalBatteryLifeHours = 80
    };

    public Cloud2CoreWirelessDevice() : base(DeviceDefinition)
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

        if (report.Length < 5 || report[0] != ProtocolReportId)
            return false;

        if (report[1] != BatteryResponseSelector &&
            report[1] != BatteryNotificationSelector)
        {
            return false;
        }

        if (report[2] == 0 && report[3] == 0)
            return false;

        battery = report[4];

        if (battery > 100)
            return false;

        isResponse = report[1] == BatteryResponseSelector;
        return true;
    }

    protected override bool TryParseChargeStatusReport(
        byte[] report,
        out bool isCharging,
        out bool isResponse)
    {
        isCharging = false;
        isResponse = false;

        if (report.Length < 3 ||
            report[0] != ProtocolReportId ||
            (report[1] != ChargeResponseSelector &&
             report[1] != ChargeNotificationSelector))
        {
            return false;
        }

        isCharging = report[2] == 1;
        isResponse = report[1] == ChargeResponseSelector;
        return true;
    }

    protected override bool TryParseMicrophoneMuteReport(
        byte[] report,
        out bool isMuted,
        out bool isResponse)
    {
        isMuted = false;
        isResponse = false;

        if (report.Length < 3 ||
            report[0] != ProtocolReportId ||
            (report[1] != MicrophoneMuteResponseSelector &&
             report[1] != MicrophoneMuteNotificationSelector))
        {
            return false;
        }

        isMuted = report[2] == 1;
        isResponse = report[1] == MicrophoneMuteResponseSelector;
        return true;
    }
}
