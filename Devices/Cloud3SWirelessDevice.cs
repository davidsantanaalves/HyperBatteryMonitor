namespace HyperXBatteryTray.Devices;

public sealed class Cloud3SWirelessDevice : HyperXBatteryDeviceBase
{
    private const byte CommandReportId = 0x0C;
    private const byte NotificationReportId = 0x0D;
    private const byte BatteryResponseSelector = 0x06;
    private const byte ChargeResponseSelector = 0x48;
    private const byte MicrophoneMuteResponseSelector = 0x04;
    private const byte BatteryNotificationSelector = 0x01;
    private const byte ChargeNotificationSelector = 0x0A;
    private const byte MicrophoneMuteNotificationSelector = 0x03;
    private const byte InvalidResponseValue = 0xFF;

    private static readonly byte[] ChargeCommandBytes =
        { CommandReportId, 0x02, 0x03, 0x01, 0x00, ChargeResponseSelector };

    private static readonly byte[] MicrophoneMuteCommandBytes =
        { CommandReportId, 0x02, 0x03, 0x01, 0x00, MicrophoneMuteResponseSelector };

    internal static readonly HyperXDeviceDefinition DeviceDefinition = new()
    {
        Name = "HyperX Cloud III S",
        VendorId = 0x03F0,
        ProductId = 0x06BE,
        InterfacePattern = "VID_03F0&PID_06BE&MI_03&Col05",
        BatteryCommandBytes = new byte[]
        {
            CommandReportId, 0x02, 0x03, 0x01, 0x00, BatteryResponseSelector
        },
        BatteryByteIndex = 6,
        SupportsMicrophoneMuteMonitoring = true,
        SupportsChargingMonitoring = true,
        RequiresDedicatedDongle = true,
        NominalBatteryLifeHours = 120
    };

    public Cloud3SWirelessDevice() : base(DeviceDefinition)
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

        if (report.Length >= 7 &&
            report[0] == CommandReportId &&
            report[5] == BatteryResponseSelector)
        {
            if (report[6] == InvalidResponseValue)
                return false;

            battery = report[6];
            isResponse = true;
            return battery <= 100;
        }

        if (report.Length >= 6 &&
            report[0] == NotificationReportId &&
            report[4] == BatteryNotificationSelector)
        {
            battery = report[5];
            return battery <= 100;
        }

        return false;
    }

    protected override bool TryParseChargeStatusReport(
        byte[] report,
        out bool isCharging,
        out bool isResponse)
    {
        isCharging = false;
        isResponse = false;

        if (report.Length >= 7 &&
            report[0] == CommandReportId &&
            report[5] == ChargeResponseSelector)
        {
            if (report[6] == InvalidResponseValue)
                return false;

            isCharging = report[6] == 1;
            isResponse = true;
            return true;
        }

        if (report.Length >= 6 &&
            report[0] == NotificationReportId &&
            report[4] == ChargeNotificationSelector)
        {
            isCharging = report[5] == 1;
            return true;
        }

        return false;
    }

    protected override bool TryParseMicrophoneMuteReport(
        byte[] report,
        out bool isMuted,
        out bool isResponse)
    {
        isMuted = false;
        isResponse = false;

        if (report.Length >= 7 &&
            report[0] == CommandReportId &&
            report[5] == MicrophoneMuteResponseSelector)
        {
            if (report[6] == InvalidResponseValue)
                return false;

            isMuted = report[6] == 1;
            isResponse = true;
            return true;
        }

        if (report.Length >= 6 &&
            report[0] == NotificationReportId &&
            report[4] == MicrophoneMuteNotificationSelector)
        {
            isMuted = report[5] == 1;
            return true;
        }

        return false;
    }
}
