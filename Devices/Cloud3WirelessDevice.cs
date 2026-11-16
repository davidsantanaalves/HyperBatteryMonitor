using HyperXBatteryTray.Hid;

namespace HyperXBatteryTray.Devices;

public sealed class Cloud3WirelessDevice : IHyperXDevice, IEventDrivenHidDevice, IAutoPowerOffDevice
{
    private const byte ProtocolReportId = 0x66;
    private const byte BatteryResponseSelector = 0x89;
    private const byte ChargeResponseSelector = 0x8A;
    private const byte MicrophoneMuteResponseSelector = 0x86;
    private const byte BatteryNotificationSelector = 0x0D;
    private const byte ChargeNotificationSelector = 0x0C;
    private const byte MicrophoneMuteNotificationSelector = 0x0A;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);

    internal static readonly HyperXDeviceDefinition Definition = new()
    {
        Name = "HyperX Cloud III Wireless",
        VendorId = 0x03F0,
        ProductId = 0x05B7,
        InterfacePattern = "VID_03F0&PID_05B7&MI_03&Col01",
        BatteryCommandBytes = new byte[] { ProtocolReportId, BatteryResponseSelector },
        BatteryByteIndex = 4,
        SupportsMicrophoneMuteMonitoring = true,
        SupportsChargingMonitoring = true,
        NominalBatteryLifeHours = 120
    };

    private readonly HidConnection _connection = new();
    private readonly object _batteryRequestLock = new();
    private readonly object _chargeStatusRequestLock = new();
    private readonly object _microphoneMuteRequestLock = new();
    private readonly object _autoPowerOffRequestLock = new();
    private readonly SemaphoreSlim _autoPowerOffGate = new(1, 1);

    private CancellationTokenSource? _readerCancellation;
    private Task? _readerTask;
    private TaskCompletionSource<int>? _batteryRequest;
    private TaskCompletionSource<bool>? _chargeStatusRequest;
    private TaskCompletionSource<bool>? _microphoneMuteRequest;
    private TaskCompletionSource<int>? _autoPowerOffRequest;

    private int _battery = -1;
    private bool _isConnected;
    private bool _isCharging;
    private bool _isMicrophoneMuted;
    private bool _isMicrophoneMuteStateKnown;

    public string Name => Definition.Name;

    public ushort VendorId => Definition.VendorId;

    public ushort ProductId => Definition.ProductId;

    public bool IsConnected => _isConnected;

    public int Battery => _battery;

    public bool IsCharging => _isCharging;

    public bool IsMicrophoneMuted => _isMicrophoneMuted;

    public bool IsMicrophoneMuteStateKnown => _isMicrophoneMuteStateKnown;

    public bool SupportsAutoPowerOff => true;

    public async Task<int?> QueryAutoPowerOffAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return null;
        await _autoPowerOffGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await QueryAutoPowerOffCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _autoPowerOffGate.Release(); }
    }

    public async Task<bool> SetAutoPowerOffAsync(int minutes, CancellationToken cancellationToken = default)
    {
        if (minutes is not (0 or 10 or 20 or 30) || !IsConnected)
            return false;
        await _autoPowerOffGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected) return false;
            HyperXBatteryTray.Diagnostics.BetaDiagnostics.Write("HID.AutoPowerOff.WriteRequested", new
            { Model = Name, Minutes = minutes });
            _connection.WriteConfigurationReport(new byte[] { 0x66, 0x02, (byte)minutes });
            // Firmware may apply the device-resident setting after the output write.
            // Re-read for confirmation; never replay a successful write blindly.
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                await Task.Delay(attempt == 1 ? 250 : 350, cancellationToken).ConfigureAwait(false);
                int? actual = await QueryAutoPowerOffCoreAsync(cancellationToken).ConfigureAwait(false);
                HyperXBatteryTray.Diagnostics.BetaDiagnostics.Write("HID.AutoPowerOff.WriteVerification", new
                { Model = Name, RequestedMinutes = minutes, ActualMinutes = actual, Attempt = attempt });
                if (actual == minutes) return true;
            }
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            HyperXBatteryTray.Diagnostics.BetaDiagnostics.WriteError("HID.AutoPowerOff.WriteException", ex);
            return false;
        }
        finally { _autoPowerOffGate.Release(); }
    }

    private async Task<int?> QueryAutoPowerOffCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected) return null;
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_autoPowerOffRequestLock) _autoPowerOffRequest = pending;
        try
        {
            _connection.Write(new byte[] { 0x66, 0x85 });
            int? value = await WaitForResponseAsync(pending, cancellationToken).ConfigureAwait(false);
            HyperXBatteryTray.Diagnostics.BetaDiagnostics.Write("HID.AutoPowerOff.Read", new
            { Model = Name, Minutes = value, Responded = value.HasValue });
            return value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            HyperXBatteryTray.Diagnostics.BetaDiagnostics.WriteError("HID.AutoPowerOff.ReadException", ex);
            return null;
        }
        finally
        {
            lock (_autoPowerOffRequestLock)
                if (ReferenceEquals(_autoPowerOffRequest, pending)) _autoPowerOffRequest = null;
        }
    }

    public bool SupportsMicrophoneMuteMonitoring =>
        Definition.SupportsMicrophoneMuteMonitoring;

    public bool SupportsEventDrivenBattery => true;
    public event EventHandler<bool>? PhysicalConnectionChanged;
    public event EventHandler<int>? BatteryChanged;

    public event EventHandler<bool>? ChargingChanged;

    public event EventHandler<bool>? MicrophoneMuteChanged;

    public bool Connect()
    {
        if (_isConnected)
            return true;

        string? devicePath = HidConnection.FindDevice(Definition);

        if (string.IsNullOrWhiteSpace(devicePath))
            return false;

        try
        {
            if (!_connection.Open(devicePath))
                return false;

            _readerCancellation = new CancellationTokenSource();
            _isConnected = true;
            _isMicrophoneMuted = false;
            _isMicrophoneMuteStateKnown = false;

            CancellationToken token = _readerCancellation.Token;
            _readerTask = Task.Run(() => ReaderLoopAsync(token));

            return true;
        }
        catch
        {
            _connection.Close();
            _readerCancellation?.Dispose();
            _readerCancellation = null;
            _readerTask = null;
            _isConnected = false;
            _isCharging = false;
            _isMicrophoneMuted = false;
            _isMicrophoneMuteStateKnown = false;
            return false;
        }
    }

    public void Disconnect()
    {
        _isConnected = false;

        CancelPendingRequests();

        _readerCancellation?.Cancel();
        _connection.Close();
        _readerCancellation?.Dispose();
        _readerCancellation = null;
        _readerTask = null;

        UpdateChargingState(false);
        _isMicrophoneMuted = false;
        _isMicrophoneMuteStateKnown = false;
        SetBatteryUnavailable();
    }

    public async Task<int?> QueryBatteryAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            return null;

        var response = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_batteryRequestLock)
        {
            _batteryRequest = response;
        }

        try
        {
            _connection.Write(CreateCommand(BatteryResponseSelector));
            return await WaitForResponseAsync(response, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            lock (_batteryRequestLock)
            {
                if (ReferenceEquals(_batteryRequest, response))
                    _batteryRequest = null;
            }
        }
    }

    public async Task<bool?> QueryChargeStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            return null;

        var response = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_chargeStatusRequestLock)
        {
            _chargeStatusRequest = response;
        }

        try
        {
            _connection.Write(CreateCommand(ChargeResponseSelector));
            return await WaitForResponseAsync(response, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            lock (_chargeStatusRequestLock)
            {
                if (ReferenceEquals(_chargeStatusRequest, response))
                    _chargeStatusRequest = null;
            }
        }
    }

    public async Task<bool?> QueryMicrophoneMuteStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            return null;

        var response = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_microphoneMuteRequestLock)
        {
            _microphoneMuteRequest = response;
        }

        try
        {
            _connection.Write(CreateCommand(MicrophoneMuteResponseSelector));
            return await WaitForResponseAsync(response, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            lock (_microphoneMuteRequestLock)
            {
                if (ReferenceEquals(_microphoneMuteRequest, response))
                    _microphoneMuteRequest = null;
            }
        }
    }

    private static byte[] CreateCommand(byte selector)
    {
        return new byte[] { ProtocolReportId, selector };
    }

    private async Task ReaderLoopAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                byte[]? report = await _connection.ReadAsync(
                    cancellationToken);

                if (report is null || report.Length == 0)
                    continue;

                ProcessInputReport(report);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException ex)
            {
                HyperXBatteryTray.Diagnostics.BetaDiagnostics.WriteError("HID.ReaderIoError", ex);
                break;
            }
            catch (Exception ex)
            {
                HyperXBatteryTray.Diagnostics.BetaDiagnostics.WriteError("HID.ReaderError", ex);
                if (cancellationToken.IsCancellationRequested)
                    break;
            }
        }
    }

    private void ProcessInputReport(byte[] report)
    {
        if (report.Length < 2 || report[0] != ProtocolReportId)
            return;

        byte selector = report[1];
        if (selector is 0x0A or 0x0B or 0x0C or 0x0D)
        {
            // Protocol metadata only; never write arbitrary HID payloads or paths.
            HyperXBatteryTray.Diagnostics.BetaDiagnostics.Write("HID.UnsolicitedReport", new
            {
                Model = Name, Selector = selector.ToString("X2"),
                Byte2 = report.Length > 2 ? (int)report[2] : -1,
                Byte3 = report.Length > 3 ? (int)report[3] : -1,
                Byte4 = report.Length > 4 ? (int)report[4] : -1
            });
        }
        if (selector == 0x85 && report.Length >= 3)
        {
            int minutes = report[2];
            if (minutes is 0 or 10 or 20 or 30)
            {
                lock (_autoPowerOffRequestLock)
                    _autoPowerOffRequest?.TrySetResult(minutes);
            }
            return;
        }

        if (selector == 0x0B && report.Length >= 3)
        {
            bool physicallyConnected = report[2] == 1;
            HyperXBatteryTray.Diagnostics.BetaDiagnostics.Write("HID.ConnectionNotification",
                new { Model = Name, Connected = physicallyConnected, Selector = "66:0B" });
            PhysicalConnectionChanged?.Invoke(this, physicallyConnected);
            return;
        }

        if ((selector == ChargeResponseSelector ||
             selector == ChargeNotificationSelector) &&
            report.Length >= 3)
        {
            bool isCharging = report[2] == 1;
            UpdateChargingState(isCharging);

            if (selector == ChargeResponseSelector)
            {
                lock (_chargeStatusRequestLock)
                {
                    _chargeStatusRequest?.TrySetResult(isCharging);
                }
            }

            return;
        }

        if ((selector == MicrophoneMuteResponseSelector ||
             selector == MicrophoneMuteNotificationSelector) &&
            report.Length >= 3)
        {
            bool isMuted = report[2] == 1;
            UpdateMicrophoneMuteState(isMuted);

            if (selector == MicrophoneMuteResponseSelector)
            {
                lock (_microphoneMuteRequestLock)
                {
                    _microphoneMuteRequest?.TrySetResult(isMuted);
                }
            }

            return;
        }

        if ((selector == BatteryResponseSelector ||
             selector == BatteryNotificationSelector) &&
            report.Length >= 5)
        {
            if (report[2] == 0 && report[3] == 0)
                return;

            int battery = report[Definition.BatteryByteIndex];

            if (battery > 100)
                return;

            UpdateBattery(battery);

            if (selector == BatteryResponseSelector)
            {
                lock (_batteryRequestLock)
                {
                    _batteryRequest?.TrySetResult(battery);
                }
            }
        }
    }

    private static async Task<T?> WaitForResponseAsync<T>(
        TaskCompletionSource<T> response,
        CancellationToken cancellationToken)
        where T : struct
    {
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutCancellation.CancelAfter(QueryTimeout);

        try
        {
            return await response.Task.WaitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private void UpdateBattery(int battery)
    {
        if (_battery == battery)
            return;

        _battery = battery;
        BatteryChanged?.Invoke(this, battery);
    }

    private void UpdateChargingState(bool isCharging)
    {
        if (_isCharging == isCharging)
            return;

        _isCharging = isCharging;
        ChargingChanged?.Invoke(this, isCharging);
    }

    private void UpdateMicrophoneMuteState(bool isMuted)
    {
        bool stateChanged =
            !_isMicrophoneMuteStateKnown ||
            _isMicrophoneMuted != isMuted;

        _isMicrophoneMuted = isMuted;
        _isMicrophoneMuteStateKnown = true;

        if (stateChanged)
            MicrophoneMuteChanged?.Invoke(this, isMuted);
    }

    private void SetBatteryUnavailable()
    {
        if (_battery == -1)
            return;

        _battery = -1;
        BatteryChanged?.Invoke(this, -1);
    }

    private void CancelPendingRequests()
    {
        lock (_batteryRequestLock)
        {
            _batteryRequest?.TrySetCanceled();
            _batteryRequest = null;
        }

        lock (_chargeStatusRequestLock)
        {
            _chargeStatusRequest?.TrySetCanceled();
            _chargeStatusRequest = null;
        }

        lock (_microphoneMuteRequestLock)
        {
            _microphoneMuteRequest?.TrySetCanceled();
            _microphoneMuteRequest = null;
        }
        lock (_autoPowerOffRequestLock)
        {
            _autoPowerOffRequest?.TrySetCanceled();
            _autoPowerOffRequest = null;
        }
    }

    public void Dispose()
    {
        Disconnect();
        _connection.Dispose();
    }
}
