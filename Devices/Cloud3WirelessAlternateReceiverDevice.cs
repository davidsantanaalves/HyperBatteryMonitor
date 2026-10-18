using HyperXBatteryTray.Hid;

namespace HyperXBatteryTray.Devices;

public sealed class Cloud3WirelessAlternateReceiverDevice : IHyperXDevice, ISelectableHidEndpoint
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
        Name = "HyperX Cloud III (Receiver 0FCC)",
        VendorId = 0x03F0,
        ProductId = 0x0FCC,
        InterfacePattern = "VID_03F0&PID_0FCC",
        BatteryCommandBytes = new byte[] { ProtocolReportId, BatteryResponseSelector },
        BatteryByteIndex = 4,
        SupportsMicrophoneMuteMonitoring = true,
        SupportsChargingMonitoring = true,
        NominalBatteryLifeHours = 120
    };

    private readonly HidConnection _connection = new();
    private string? _selectedHidEndpoint;

    void ISelectableHidEndpoint.SelectHidEndpoint(string path) =>
        _selectedHidEndpoint = path;
    private readonly object _batteryRequestLock = new();
    private readonly object _chargeStatusRequestLock = new();
    private readonly object _microphoneMuteRequestLock = new();

    private CancellationTokenSource? _readerCancellation;
    private Task? _readerTask;
    private TaskCompletionSource<int>? _batteryRequest;
    private TaskCompletionSource<bool>? _chargeStatusRequest;
    private TaskCompletionSource<bool>? _microphoneMuteRequest;

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

    public bool SupportsMicrophoneMuteMonitoring =>
        Definition.SupportsMicrophoneMuteMonitoring;

    public event EventHandler<int>? BatteryChanged;

    public event EventHandler<bool>? ChargingChanged;

    public event EventHandler<bool>? MicrophoneMuteChanged;

    public bool Connect()
    {
        if (_isConnected)
            return true;

        string? devicePath = _selectedHidEndpoint ?? HidConnection.FindDevice(Definition);

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
            catch (IOException)
            {
                break;
            }
            catch
            {
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
    }

    public void Dispose()
    {
        Disconnect();
        _connection.Dispose();
    }
}
