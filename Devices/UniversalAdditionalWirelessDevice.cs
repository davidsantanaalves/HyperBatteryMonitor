namespace HyperXBatteryTray.Devices;

/// <summary>
/// Additional NGENUITY 3.1 Universal headsets. This is independent of the established
/// CloudFlight2Device driver to avoid changing any already-supported receiver.
/// For TIO, only the requested headset child is addressed; the shared receiver itself
/// never supplies a headset battery value.
/// </summary>
public sealed class UniversalAdditionalWirelessDevice : HyperXBatteryDeviceBase
{
    private const byte BatteryQueryId = 0x50;
    private const byte BatteryResponseId = 0x51;
    private const byte BatterySubreport = 0x02;
    private const byte ConnectionQueryId = 0x52;
    private const byte ConnectionResponseId = 0x53;
    private const byte ConnectionSubreport = 0x01;
    private const byte AudioQueryId = 0x60;
    private const byte AudioResponseId = 0x61;
    private const byte AudioSubreport = 0x02;
    private const byte ModeQueryId = 0x70;
    private const byte ModeResponseId = 0x71;
    private const byte ModeSubreport = 0x02;
    private const byte NotificationId = 0xFB;
    private const byte ChargeNotification = 0x09;
    private const byte ConnectionNotification = 0x0A;
    private const byte MicrophoneNotification = 0x0D;
    private const byte ModeChangedNotification = 0x14;
    private const byte SubreportMask = 0x3F;
    private const ushort HyperXVendorId = 0x03F0;
    private const int ModeSlotCount = 3;
    private const int ModeRecordSize = 8;
    private const int ModeRecordFirstIndex = 3;
    private const int ModeRecordStateIndex = 1;
    private const int ModeRecordVendorIndex = 2;
    private const int ModeRecordProductIndex = 4;
    private const byte ConnectedValue = 1; // NGENUITY 3.1 Universal parser (NOT the existing HBM Flight 2 parser).

    internal static readonly HyperXDeviceDefinition Alpha2Definition = Dedicated(
        "HyperX Cloud Alpha 2 Wireless", 0x08BE);
    internal static readonly HyperXDeviceDefinition Flight2XboxDefinition = Dedicated(
        "HyperX Cloud Flight 2 (Xbox)", 0x0CC1);
    internal static readonly HyperXDeviceDefinition Cloud3STioDefinition = Tio(
        "HyperX Cloud III S (TIO - Three-In-One)", 120);
    internal static readonly HyperXDeviceDefinition Flight2TioDefinition = Tio(
        "HyperX Cloud Flight 2 (TIO - Three-In-One)", 100);

    private readonly ushort? _tioChildPid;
    private volatile byte _tioSlot;
    private volatile bool _routingInvalidated;
    private volatile bool _chargeKnown;

    public UniversalAdditionalWirelessDevice(HyperXDeviceDefinition definition, ushort? tioChildPid = null)
        : base(definition)
    {
        _tioChildPid = tioChildPid;
    }

    protected override bool UsesContinuousReader => true;

    public override async Task<int?> QueryBatteryAsync(CancellationToken cancellationToken = default)
    {
        if (_tioChildPid.HasValue)
        {
            // A mode-change notification invalidates all outstanding child routing.
            // Let BatteryMonitor disconnect/reset its UI state before rediscovery.
            if (_routingInvalidated ||
                !await LocateTioChildAsync(cancellationToken).ConfigureAwait(false))
                return null;
        }
        else
        {
            byte[]? connection = await QueryReportAsync(
                Command(ConnectionQueryId, ConnectionSubreport),
                r => IsReport(r, ConnectionResponseId, ConnectionSubreport) && r[2] == ConnectedValue,
                cancellationToken).ConfigureAwait(false);
            if (connection == null)
                return null;
        }

        byte expectedSlot = _tioSlot;
        byte[]? report = await QueryReportAsync(
            Command(BatteryQueryId, BatterySubreport),
            r => !_routingInvalidated && _tioSlot == expectedSlot &&
                 IsReport(r, BatteryResponseId, BatterySubreport) && r[2] <= 100,
            cancellationToken).ConfigureAwait(false);
        if (report == null || _routingInvalidated || _tioSlot != expectedSlot)
            return null;

        // For TIO, commit status only after the child slot was verified for
        // this query. Unsolicited reports are not trusted for child identity.
        if (_tioChildPid.HasValue)
        {
            UpdateBattery(report[2]);
            if (report.Length > 3)
                UpdateCharge(report[3]);
        }
        return report[2];
    }

    public override Task<bool?> QueryChargeStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<bool?>(IsConnected && _chargeKnown ? IsCharging : null);

    public override async Task<bool?> QueryMicrophoneMuteStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_tioChildPid.HasValue && (_tioSlot == 0 || _routingInvalidated))
            return null;

        byte expectedSlot = _tioSlot;
        byte[]? report = await QueryReportAsync(
            Command(AudioQueryId, AudioSubreport),
            r => !_routingInvalidated && _tioSlot == expectedSlot &&
                 IsReport(r, AudioResponseId, AudioSubreport) && r.Length > 4 && r[4] <= 1,
            cancellationToken).ConfigureAwait(false);
        if (report == null || _routingInvalidated || expectedSlot != _tioSlot)
            return null;
        bool muted = report[4] == 1;
        if (_tioChildPid.HasValue)
            UpdateMicrophoneMuteState(muted);
        return muted;
    }

    private async Task<bool> LocateTioChildAsync(CancellationToken cancellationToken)
    {
        // The mode table, not the shared receiver PID, establishes headset identity.
        byte[]? mode = await QueryReportAsync(
            new byte[] { ModeQueryId, ModeSubreport },
            r => r.Length >= ModeRecordFirstIndex + ModeSlotCount * ModeRecordSize &&
                 r[0] == ModeResponseId && (r[1] & SubreportMask) == ModeSubreport &&
                 (r[1] >> 6) == 0,
            cancellationToken).ConfigureAwait(false);

        if (mode == null)
        {
            _tioSlot = 0;
            return false;
        }

        byte selectedSlot = 0;
        int matches = 0;
        for (int i = 0; i < ModeSlotCount; i++)
        {
            int start = ModeRecordFirstIndex + i * ModeRecordSize;
            ushort vendor = (ushort)(mode[start + ModeRecordVendorIndex] |
                (mode[start + ModeRecordVendorIndex + 1] << 8));
            ushort product = (ushort)(mode[start + ModeRecordProductIndex] |
                (mode[start + ModeRecordProductIndex + 1] << 8));
            if (mode[start + ModeRecordStateIndex] == ConnectedValue &&
                vendor == HyperXVendorId && product == _tioChildPid)
            {
                selectedSlot = (byte)(i + 1);
                matches++;
            }
        }

        // Two children of the same model cannot be distinguished by this
        // model-level selector. Refuse to assign the wrong headset battery.
        if (matches != 1)
        {
            _tioSlot = 0;
            _chargeKnown = false;
            return false;
        }

        // A child moving between slots must reset the BatteryMonitor state
        // before queries resume under its new routing bits.
        byte previousSlot = _tioSlot;
        if (previousSlot != 0 && previousSlot != selectedSlot)
        {
            _routingInvalidated = true;
            _tioSlot = 0;
            _chargeKnown = false;
            return false;
        }

        if (selectedSlot != previousSlot)
            _chargeKnown = false;
        _tioSlot = selectedSlot;
        return !_routingInvalidated;
    }

    protected override void ProcessInputReport(byte[] report)
    {
        // Invalidate the prior routing immediately on parent topology changes.
        // Never deliver unsolicited battery/mute data for an unverified child.
        if (_tioChildPid.HasValue && report.Length > 1 &&
            report[0] == NotificationId && (report[1] & SubreportMask) == ModeChangedNotification &&
            (report[1] >> 6) == 0)
        {
            _routingInvalidated = true;
            _tioSlot = 0;
            _chargeKnown = false;
            CompleteReportRequest(report);
            return;
        }

        if (_tioChildPid.HasValue)
        {
            if (_tioSlot != 0 && !_routingInvalidated &&
                IsReport(report, NotificationId, ConnectionNotification) &&
                report.Length > 2 && report[2] != ConnectedValue)
            {
                _routingInvalidated = true;
                _tioSlot = 0;
                _chargeKnown = false;
            }
            // Correlate all other child reports with explicit requests only;
            // do not publish stale notifications from a previously used slot.
            CompleteReportRequest(report); // Also handles parent mode-table response.
            return;
        }

        if (IsReport(report, BatteryResponseId, BatterySubreport) && report.Length > 3 && report[2] <= 100)
        {
            UpdateBattery(report[2]);
            UpdateCharge(report[3]);
        }
        else if (IsReport(report, AudioResponseId, AudioSubreport) && report.Length > 4 && report[4] <= 1)
            UpdateMicrophoneMuteState(report[4] == 1);
        else if (IsReport(report, NotificationId, ChargeNotification) && report.Length > 2)
            UpdateCharge(report[2]);
        else if (IsReport(report, NotificationId, MicrophoneNotification) && report.Length > 2 && report[2] <= 1)
            UpdateMicrophoneMuteState(report[2] == 1);
        else if (IsReport(report, NotificationId, ConnectionNotification) && report.Length > 2 &&
            report[2] != ConnectedValue)
        {
            // Let BatteryMonitor requery/reselect on the next monitoring cycle.
            _routingInvalidated = true;
            _chargeKnown = false;
            _tioSlot = 0;
        }

        CompleteReportRequest(report);
    }

    private void UpdateCharge(byte code)
    {
        // 0/1/2 are explicitly known. 3 (fault), FF (unknown) and other codes
        // must not be silently reported as physically not charging.
        if (code > 2)
        {
            _chargeKnown = false;
            return;
        }
        _chargeKnown = true;
        UpdateChargingState(code == 1);
    }

    private byte[] Command(byte reportId, byte subreport) =>
        new[] { reportId, (byte)((subreport & SubreportMask) | (_tioSlot << 6)) };

    private bool IsReport(byte[] report, byte reportId, byte subreport) =>
        report.Length > 2 && report[0] == reportId &&
        (report[1] & SubreportMask) == subreport &&
        (report[1] >> 6) == _tioSlot;

    public override void Disconnect()
    {
        _routingInvalidated = false;
        _tioSlot = 0;
        _chargeKnown = false;
        base.Disconnect();
    }

    private static HyperXDeviceDefinition Dedicated(string name, ushort pid) => new()
    {
        Name = name,
        VendorId = HyperXVendorId,
        ProductId = pid,
        InterfacePattern = $"VID_03F0&PID_{pid:X4}",
        // Windows NGENUITY3 primary HID usages for Alpha2 and Flight2 Xbox.
        ControlUsagePage = pid == 0x08BE ? (ushort)0xFF13 : (ushort)0xFF90,
        ControlUsage = pid == 0x08BE ? (ushort)0xFF00 : (ushort)0xFF00,
        BatteryCommandBytes = new byte[] { BatteryQueryId, BatterySubreport },
        BatteryByteIndex = 2,
        SupportsMicrophoneMuteMonitoring = true,
        SupportsChargingMonitoring = true,
        RequiresDedicatedDongle = true
    };

    private static HyperXDeviceDefinition Tio(string name, double nominalBatteryLifeHours) => new()
    {
        Name = name,
        VendorId = HyperXVendorId,
        ProductId = 0x01BF, // shared parent receiver; child is resolved by the mode table
        InterfacePattern = "VID_03F0&PID_01BF",
        AlternateInterfacePatterns = new[] { "VID_03F0&PID_01C5" },
        BatteryCommandBytes = new byte[] { BatteryQueryId, BatterySubreport },
        BatteryByteIndex = 2,
        SupportsMicrophoneMuteMonitoring = true,
        SupportsChargingMonitoring = true,
        NominalBatteryLifeHours = nominalBatteryLifeHours
    };
}
