namespace HyperXBatteryTray.Devices;

public sealed class CloudStinger2WirelessDevice : HyperXBatteryDeviceBase
{
    private const byte ProtocolReportId = 0x06;
    private const byte ProtocolMarker1 = 0xFF;
    private const byte ProtocolMarker2 = 0xBB;
    private const byte BatterySelector = 0x02;
    private const byte ChargeSelector = 0x03;
    private const byte NoChargingValue = 0x00;
    private const byte WireChargingValue = 0x01;
    private const byte FullChargedValue = 0x02;
    private const byte ChargeErrorValue = 0x03;
    private const int SelectorIndex = 3;
    private const int ChargeValueIndex = 4;
    private const int BatteryValueIndex = 7;

    private static readonly byte[] ChargeCommandBytes =
        { ProtocolReportId, ProtocolMarker1, ProtocolMarker2, ChargeSelector };

    internal static readonly HyperXDeviceDefinition DeviceDefinition = new()
    {
        Name = "HyperX Cloud Stinger 2",
        VendorId = 0x03F0,
        ProductId = 0x0D93,
        InterfacePattern = "VID_03F0&PID_0D93&MI_03&Col03",
        BatteryCommandBytes = new byte[]
        {
            ProtocolReportId, ProtocolMarker1, ProtocolMarker2, BatterySelector
        },
        BatteryByteIndex = BatteryValueIndex,
        SupportsChargingMonitoring = true,
        NominalBatteryLifeHours = 20
    };

    public CloudStinger2WirelessDevice() : base(DeviceDefinition)
    {
    }

    protected override bool UsesContinuousReader => true;

    public override bool SupportsAutoPowerOff => true;
    protected override byte[]? CreateAutoPowerOffQueryCommand() =>
        new byte[] { 0x06, 0xFF, 0xBB, 0x07 };
    protected override byte[]? CreateAutoPowerOffSetCommand(int minutes) =>
        new byte[] { 0x06, 0xFF, 0xBB, 0x22, (byte)minutes };

    protected override bool TryParseAutoPowerOffReport(byte[] report, out int minutes)
    {
        minutes = -1;
        if (report.Length < 5 || report[0] != 0x06 ||
            report[1] != 0xFF || report[2] != 0xBB || report[3] != 0x07)
            return false;
        minutes = report[4];
        return IsAutoPowerOffValue(minutes);
    }


    protected override byte[]? CreateChargeStatusCommand() =>
        CreateCommand(ChargeCommandBytes);

    protected override bool TryParseBatteryReport(
        byte[] report,
        out int battery,
        out bool isResponse)
    {
        battery = -1;
        isResponse = false;

        if (!HasProtocolPrefix(report) ||
            report.Length <= BatteryValueIndex ||
            report[SelectorIndex] != BatterySelector)
        {
            return false;
        }

        battery = report[BatteryValueIndex];

        if (battery > 100)
            return false;

        isResponse = true;
        return true;
    }

    protected override bool TryParseChargeStatusReport(
        byte[] report,
        out bool isCharging,
        out bool isResponse)
    {
        isCharging = false;
        isResponse = false;

        if (!HasProtocolPrefix(report) ||
            report.Length <= ChargeValueIndex ||
            report[SelectorIndex] != ChargeSelector)
        {
            return false;
        }

        byte chargeState = report[ChargeValueIndex];

        switch (chargeState)
        {
            case NoChargingValue:
            case FullChargedValue:
            case ChargeErrorValue:
                isCharging = false;
                break;

            case WireChargingValue:
                isCharging = true;
                break;

            default:
                return false;
        }

        isResponse = true;
        return true;
    }

    private static bool HasProtocolPrefix(byte[] report) =>
        report.Length > SelectorIndex &&
        report[0] == ProtocolReportId &&
        report[1] == ProtocolMarker1 &&
        report[2] == ProtocolMarker2;
}
