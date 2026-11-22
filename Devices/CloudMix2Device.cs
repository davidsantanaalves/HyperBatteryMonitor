namespace HyperXBatteryTray.Devices;

public sealed class CloudMix2Device : HyperXBatteryDeviceBase
{
    private const ushort DeviceVendorId = 0x03F0;
    private const ushort DeviceProductId = 0x0FAE;
    private const string ControlInterface = "VID_03F0&PID_0FAE&MI_03&Col06";
    private const byte ResponseReportId = 0x0C;
    private const byte NotificationReportId = 0x0D;
    private const byte ProtocolGroup = 0x02;
    private const byte ProtocolCategory = 0x03;
    private const byte QueryOperation = 0x01;
    private const byte ReservedValue = 0x00;
    private const byte ConnectionSelector = 0x02;
    private const byte BatterySelector = 0x06;
    private const byte MicrophoneSelector = 0x04;
    private const byte ConnectionNotification = 0x04;
    private const byte BatteryNotification = 0x01;
    private const byte MicrophoneNotification = 0x03;
    private const byte ActiveValue = 1;
    private const int ReportIdIndex = 0;
    private const int GroupIndex = 1;
    private const int CategoryIndex = 2;
    private const int OperationIndex = 3;
    private const int ResponseReservedIndex = 4;
    private const int ResponseSelectorIndex = 5;
    private const int ResponseValueIndex = 6;
    private const int NotificationSelectorIndex = 4;
    private const int NotificationValueIndex = 5;

    internal static readonly HyperXDeviceDefinition DeviceDefinition = new()
    {
        Name = "HyperX Cloud Mix 2",
        VendorId = DeviceVendorId,
        ProductId = DeviceProductId,
        InterfacePattern = ControlInterface,
        BatteryCommandBytes = Command(BatterySelector),
        BatteryByteIndex = ResponseValueIndex,
        SupportsMicrophoneMuteMonitoring = true,
        SupportsChargingMonitoring = false,
        NominalBatteryLifeHours = 72
    };

    public CloudMix2Device() : base(DeviceDefinition)
    {
    }

    protected override bool UsesContinuousReader => true;

    // The existing parser handles unsolicited battery reports independently
    // of replies to explicit queries. Experimental 60-second recovery in Beta.
    public override bool SupportsUnsolicitedBatteryNotifications => true;

    public override async Task<int?> QueryBatteryAsync(CancellationToken cancellationToken = default)
    {
        byte[]? connection = await QuerySelectorAsync(ConnectionSelector, cancellationToken).ConfigureAwait(false);
        if (connection == null || connection[ResponseValueIndex] != ActiveValue)
            return null;

        byte[]? report = await QuerySelectorAsync(BatterySelector, cancellationToken).ConfigureAwait(false);
        return report == null ? null : report[ResponseValueIndex];
    }

    // Charging is unknown for this model; the base implementation returns null.
    public override async Task<bool?> QueryMicrophoneMuteStatusAsync(CancellationToken cancellationToken = default)
    {
        byte[]? report = await QuerySelectorAsync(MicrophoneSelector, cancellationToken).ConfigureAwait(false);
        return report == null ? null : report[ResponseValueIndex] == ActiveValue;
    }

    private Task<byte[]?> QuerySelectorAsync(byte selector, CancellationToken cancellationToken) =>
        QueryReportAsync(Command(selector), r => IsResponse(r, selector) &&
            (selector != BatterySelector || r[ResponseValueIndex] <= 100), cancellationToken);

    protected override void ProcessInputReport(byte[] report)
    {
        if (IsResponse(report, BatterySelector) && report[ResponseValueIndex] <= 100)
            UpdateBattery(report[ResponseValueIndex]);
        else if (IsResponse(report, MicrophoneSelector))
            UpdateMicrophoneMuteState(report[ResponseValueIndex] == ActiveValue);
        else if (IsNotification(report, BatteryNotification) && report[NotificationValueIndex] <= 100)
            UpdateBattery(report[NotificationValueIndex]);
        else if (IsNotification(report, MicrophoneNotification))
            UpdateMicrophoneMuteState(report[NotificationValueIndex] == ActiveValue);
        else if (IsNotification(report, ConnectionNotification) && report[NotificationValueIndex] != ActiveValue)
        {
            // Let BatteryMonitor reconnect and initialize microphone state again.
            ReportPassiveDisconnect();
            return;
        }
        CompleteReportRequest(report);
    }

    private static byte[] Command(byte selector) =>
        new[] { ResponseReportId, ProtocolGroup, ProtocolCategory, QueryOperation, ReservedValue, selector };

    private static bool IsResponse(byte[] report, byte selector) =>
        report.Length > ResponseValueIndex && report[ReportIdIndex] == ResponseReportId &&
        report[GroupIndex] == ProtocolGroup && report[CategoryIndex] == ProtocolCategory &&
        report[OperationIndex] == QueryOperation && report[ResponseReservedIndex] == ReservedValue &&
        report[ResponseSelectorIndex] == selector;

    private static bool IsNotification(byte[] report, byte selector) =>
        report.Length > NotificationValueIndex && report[ReportIdIndex] == NotificationReportId &&
        report[GroupIndex] == ProtocolGroup && report[CategoryIndex] == ProtocolCategory &&
        report[OperationIndex] == ReservedValue && report[NotificationSelectorIndex] == selector;
}
