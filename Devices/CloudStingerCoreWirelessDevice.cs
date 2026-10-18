using System.Buffers.Binary;

namespace HyperXBatteryTray.Devices;

public sealed class CloudStingerCoreWirelessDevice : HyperXBatteryDeviceBase
{
    private const ushort DeviceVendorId = 0x0951;
    private const ushort DeviceProductId = 0x170B;
    private const string ControlInterface = "VID_0951&PID_170B&MI_03&Col03";
    // Verified by PR #6 contributor on the original 4P5J1AA PlayStation dongle.
    private const ushort PlayStationVendorId = 0x03F0;
    private const ushort PlayStationProductId = 0x0A93;
    private const string PlayStationControlInterface = "VID_03F0&PID_0A93&MI_03&Col03";
    private const byte PlayStationResponseType = 0x12;
    private const byte PlayStationResponseCommand = 0x05;
    private const int PlayStationBatteryResponseAttempts = 10;
    private const byte FeatureReportId = 0xFF;
    private const byte ConnectedValue = 1;
    private const byte NoChargingValue = 3;
    private const byte WireChargingValue = 5;
    private const byte FullChargedValue = 6;
    private const int ConnectionValueIndex = 11;
    private const int ChargeValueIndex = 9;
    private const int VoltageIndex = 11;
    private const int VoltageByteCount = 2;
    private const int FeatureResponseDelayMilliseconds = 20;
    private const int StabilizationRepeatCount = 3;
    private const int PercentPerInterval = 10;
    private const float MillivoltsPerVolt = 1000f;

    private static readonly byte[] ConnectionCommand =
        { FeatureReportId, 0x07, 0x00, 0xFD, 0x04, 0x00, 0x00, 0x02,
          0x82, 0x03, 0x05, 0x29, 0x0F, 0x00, 0x00, 0x03, 0x68, 0x10, 0x00, 0x00 };
    private static readonly byte[] BatteryCommand =
        { FeatureReportId, 0x07, 0x00, 0xFD, 0x04, 0x0C, 0xF1, 0x02,
          0x01, 0x04, 0xF0, 0x0C, 0x00, 0x00, 0x00, 0x00,
          0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
          0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    private static readonly float[] ChargingVoltageKnots =
        { 3.4000f, 3.8309f, 3.8707f, 3.9162f, 3.9299f, 3.9456f,
          3.9674f, 4.0000f, 4.0497f, 4.0900f, 4.1300f };
    private static readonly float[] DischargingVoltageKnots =
        { 3.5174f, 3.6508f, 3.6998f, 3.7381f, 3.7633f, 3.7847f,
          3.8381f, 3.9052f, 3.9542f, 4.0423f, 4.1205f };

    internal static readonly HyperXDeviceDefinition DeviceDefinition = new()
    {
        Name = "HyperX Cloud Stinger Core Wireless + 7.1",
        VendorId = DeviceVendorId,
        ProductId = DeviceProductId,
        InterfacePattern = ControlInterface,
        BatteryCommandBytes = BatteryCommand,
        BatteryByteIndex = VoltageIndex,
        SupportsMicrophoneMuteMonitoring = false,
        SupportsChargingMonitoring = true,
        NominalBatteryLifeHours = 17
    };

    // Distinct receiver definition: never impose the PlayStation header/retry
    // requirements on the original Kingston 0951:170B feature-report driver.
    internal static readonly HyperXDeviceDefinition PlayStationDefinition = new()
    {
        Name = "HyperX Cloud Stinger Core Wireless + 7.1 (PlayStation)",
        VendorId = PlayStationVendorId,
        ProductId = PlayStationProductId,
        InterfacePattern = PlayStationControlInterface,
        ControlUsagePage = 0xFF00,
        ControlUsage = 0x0001,
        BatteryCommandBytes = BatteryCommand,
        BatteryByteIndex = VoltageIndex,
        SupportsMicrophoneMuteMonitoring = false,
        SupportsChargingMonitoring = true,
        NominalBatteryLifeHours = 17
    };

    private readonly bool _isPlayStationReceiver;
    private readonly object _featureLock = new();
    private bool _requestPending;
    private int _session;
    private byte? _chargeCode;
    private byte _lastChargeCode = NoChargingValue;
    private int _chargeRepeatCount;

    public CloudStingerCoreWirelessDevice() : this(DeviceDefinition)
    {
    }

    internal CloudStingerCoreWirelessDevice(HyperXDeviceDefinition definition) : base(definition)
    {
        // Only this documented receiver revision uses FF 12 05 battery
        // confirmation and multi-read handling of the stale FF 01 00 report.
        _isPlayStationReceiver = definition.VendorId == PlayStationVendorId &&
            definition.ProductId == PlayStationProductId;
    }

    public override async Task<int?> QueryBatteryAsync(CancellationToken cancellationToken = default)
    {
        int session;
        lock (_featureLock)
        {
            if (!IsConnected || _requestPending)
                return null;
            _requestPending = true;
            session = _session;
        }

        try
        {
            byte[]? connection = await ExchangeFeatureAsync(ConnectionCommand, session, cancellationToken)
                .ConfigureAwait(false);
            if (connection == null || connection.Length <= ConnectionValueIndex ||
                connection[ConnectionValueIndex] != ConnectedValue)
                return null;

            byte[]? report = await ExchangeFeatureAsync(
                    BatteryCommand, session, cancellationToken,
                    _isPlayStationReceiver ? IsPlayStationBatteryResponse : null)
                .ConfigureAwait(false);
            if (report == null || report.Length < VoltageIndex + VoltageByteCount)
                return null;

            byte charge = report[ChargeValueIndex];
            if (charge is not (NoChargingValue or WireChargingValue or FullChargedValue))
                return null;

            short millivolts = BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(VoltageIndex, VoltageByteCount));
            int battery = ConvertBattery(millivolts, charge);
            lock (_featureLock)
            {
                if (session != _session || !IsConnected)
                    return null;
                ApplySample(battery, charge);
                return Battery;
            }
        }
        finally
        {
            lock (_featureLock)
                _requestPending = false;
        }
    }

    public override Task<bool?> QueryChargeStatusAsync(CancellationToken cancellationToken = default)
    {
        lock (_featureLock)
            return Task.FromResult<bool?>(IsConnected && _chargeCode.HasValue ? IsCharging : null);
    }

    private async Task<byte[]?> ExchangeFeatureAsync(
        byte[] command, int session, CancellationToken cancellationToken,
        Func<byte[], bool>? isExpectedResponse = null)
    {
        lock (_featureLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session != _session || !IsConnected)
                return null;
            Connection.SetFeatureReport(command);
        }

        // The original receiver performs exactly one read as before.
        // On the PlayStation receiver, the first read may be a stale FF 01 00
        // report; the contributor observed FF 12 05 on the second attempt
        // in all 52 queries. Bound retries to 10 reads / 200 ms maximum.
        int attempts = isExpectedResponse == null ? 1 : PlayStationBatteryResponseAttempts;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            await Task.Delay(FeatureResponseDelayMilliseconds, cancellationToken)
                .ConfigureAwait(false);
            byte[]? response;
            lock (_featureLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                response = session == _session && IsConnected
                    ? Connection.GetFeatureReport(FeatureReportId) : null;
            }
            if (response == null || isExpectedResponse == null || isExpectedResponse(response))
                return response;
        }
        return null;
    }

    private static bool IsPlayStationBatteryResponse(byte[] response) =>
        response.Length >= VoltageIndex + VoltageByteCount &&
        response[0] == FeatureReportId &&
        response[1] == PlayStationResponseType &&
        response[2] == PlayStationResponseCommand;

    private void ApplySample(int battery, byte charge)
    {
        // Match the model's own transition filter; do not apply voltage-curve
        // changes to the displayed sample until its charge state stabilizes.
        if (!_chargeCode.HasValue)
        {
            _chargeCode = charge;
        }
        else if (Battery == 0 && battery != 0)
        {
            _chargeRepeatCount = 0;
        }
        else if ((_chargeCode is WireChargingValue or FullChargedValue) && charge != _chargeCode)
        {
            if (charge != _lastChargeCode)
            {
                _lastChargeCode = charge;
                _chargeRepeatCount = 0;
                return;
            }
            if (++_chargeRepeatCount < StabilizationRepeatCount)
                return;
        }
        else if (charge != _chargeCode)
        {
            _lastChargeCode = charge;
            _chargeRepeatCount = 0;
        }

        _chargeCode = charge;
        UpdateChargingState(charge == WireChargingValue);
        UpdateBattery(battery);
    }

    internal static int ConvertBattery(short millivolts, byte charge)
    {
        if (charge == FullChargedValue)
            return 100;
        float voltage = millivolts / MillivoltsPerVolt;
        float[] knots = charge == WireChargingValue ? ChargingVoltageKnots : DischargingVoltageKnots;
        if (voltage >= knots[^1])
            return 100;
        for (int i = 0; i < knots.Length - 1; i++)
        {
            if (voltage >= knots[i] && voltage <= knots[i + 1])
                return i * PercentPerInterval + (int)MathF.Floor(
                    (voltage - knots[i]) / (knots[i + 1] - knots[i]) * PercentPerInterval);
        }
        return 0;
    }

    public override void Disconnect()
    {
        lock (_featureLock)
        {
            _session++;
            _chargeCode = null;
            _lastChargeCode = NoChargingValue;
            _chargeRepeatCount = 0;
            base.Disconnect();
        }
    }
}
