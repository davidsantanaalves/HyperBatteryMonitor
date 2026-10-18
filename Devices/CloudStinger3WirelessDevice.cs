namespace HyperXBatteryTray.Devices;

/// <summary>
/// NGENUITY 3.1 Stinger 3 protocol (PC, PS and Xbox share this Windows dispatcher).
/// The charge state is not inferred from a battery-query response (which reports Unknown).
/// </summary>
public sealed class CloudStinger3WirelessDevice : HyperXBatteryDeviceBase
{
    private const byte ResponseId = 0x0C;
    private const byte NotificationId = 0x0D;
    private const byte BatterySelector = 0x06;
    private const byte ConnectionSelector = 0x02;
    private const byte MicrophoneSelector = 0x04;
    private const byte BatteryNotification = 0x0B;
    private const byte ChargeNotification = 0x0A;
    private const byte MicrophoneNotification = 0x03;
    private const byte ConnectionNotification = 0x0C;

    internal static readonly HyperXDeviceDefinition PcDefinition = CreateDefinition(
        "HyperX Cloud Stinger 3 Wireless (PC)", 0x01C6);
    internal static readonly HyperXDeviceDefinition PsDefinition = CreateDefinition(
        "HyperX Cloud Stinger 3 Wireless (PlayStation)", 0x03C6);
    // Physical CloudX Stinger 3 Xbox (BS1U0AA) on Windows 11 exposes several
    // HID collections under 03F0:0DC5. The NGENUITY control collection is
    // MI_03&Col05 (UsagePage 01C0 / Usage 0001), confirmed by a beta tester.
    // Restrict ONLY Xbox to this observed endpoint; do not guess MI/Col for PC/PS.
    internal static readonly HyperXDeviceDefinition XboxDefinition = CreateDefinition(
        "HyperX Cloud Stinger 3 Wireless (Xbox)", 0x0DC5,
        "VID_03F0&PID_0DC5&MI_03&Col05");

    private bool _chargeKnown;

    public CloudStinger3WirelessDevice(HyperXDeviceDefinition definition) : base(definition) { }

    protected override bool UsesContinuousReader => true;

    public override async Task<int?> QueryBatteryAsync(CancellationToken cancellationToken = default)
    {
        byte[]? connection = await QuerySelectorAsync(ConnectionSelector, cancellationToken)
            .ConfigureAwait(false);
        if (connection == null || connection[6] != 2)
            return null;

        byte[]? report = await QuerySelectorAsync(BatterySelector, cancellationToken)
            .ConfigureAwait(false);
        return report == null ? null : report[6];
    }

    public override Task<bool?> QueryChargeStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<bool?>(IsConnected && _chargeKnown ? IsCharging : null);

    public override async Task<bool?> QueryMicrophoneMuteStatusAsync(
        CancellationToken cancellationToken = default)
    {
        byte[]? report = await QuerySelectorAsync(MicrophoneSelector, cancellationToken)
            .ConfigureAwait(false);
        return report == null ? null : report[6] == 1;
    }

    private Task<byte[]?> QuerySelectorAsync(byte selector, CancellationToken cancellationToken) =>
        QueryReportAsync(Command(selector), r => IsResponse(r, selector) &&
            (selector != BatterySelector || r[6] <= 100) &&
            (selector != MicrophoneSelector || r[6] <= 1), cancellationToken);

    protected override void ProcessInputReport(byte[] report)
    {
        if (IsResponse(report, BatterySelector) && report[6] <= 100)
            UpdateBattery(report[6]);
        else if (IsResponse(report, MicrophoneSelector) && report[6] <= 1)
            UpdateMicrophoneMuteState(report[6] == 1);
        else if (IsNotification(report, BatteryNotification) && report[5] <= 100)
            UpdateBattery(report[5]);
        else if (IsNotification(report, ChargeNotification) && report[5] <= 1)
        {
            _chargeKnown = true;
            UpdateChargingState(report[5] == 1);
        }
        else if (IsNotification(report, MicrophoneNotification) && report[5] <= 1)
            UpdateMicrophoneMuteState(report[5] == 0); // Stinger 3 notification polarity differs from query.
        else if (IsNotification(report, ConnectionNotification) && report[5] != 1)
        {
            Disconnect();
            return;
        }

        CompleteReportRequest(report);
    }

    public override void Disconnect()
    {
        _chargeKnown = false;
        base.Disconnect();
    }

    private static HyperXDeviceDefinition CreateDefinition(
        string name, ushort pid, string? observedControlInterface = null) => new()
    {
        Name = name,
        VendorId = 0x03F0,
        ProductId = pid,
        // MI/Col is physically confirmed only for the Xbox variant; keep
        // the pre-existing PC/PlayStation matching rules unchanged.
        InterfacePattern = observedControlInterface ?? $"VID_03F0&PID_{pid:X4}",
        ControlUsagePage = 0x01C0,
        ControlUsage = 0x0001,
        BatteryCommandBytes = Command(BatterySelector),
        BatteryByteIndex = 6,
        SupportsChargingMonitoring = true,
        SupportsMicrophoneMuteMonitoring = true
    };

    private static byte[] Command(byte selector) =>
        new byte[] { ResponseId, 0x02, 0x03, 0x01, 0x00, selector };

    // NGENUITY 3.1.0 Windows input dispatcher validates the report ID plus
    // bytes [1]=02/[2]=03, then dispatches responses by [5] and notifications
    // by [4]. Bytes [3]/[4] of a response and [3] of a notification are NOT
    // part of that dispatcher's proven inbound gate (unlike outbound framing).
    // Do not reject a valid device response based on those unproven checks.
    private static bool IsResponse(byte[] r, byte selector) =>
        r.Length >= 7 && r[0] == ResponseId && r[1] == 0x02 && r[2] == 0x03 &&
        r[5] == selector && r[6] != 0xFF;

    private static bool IsNotification(byte[] r, byte selector) =>
        r.Length >= 6 && r[0] == NotificationId && r[1] == 0x02 && r[2] == 0x03 &&
        r[4] == selector;
}
