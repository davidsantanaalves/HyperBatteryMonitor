using HyperXBatteryTray.Hid;

namespace HyperXBatteryTray.Devices;

public abstract class HyperXBatteryDeviceBase : IHyperXDevice, ISelectableHidEndpoint
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);

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

    private readonly object _reportRequestLock = new();
    private TaskCompletionSource<byte[]>? _reportRequest;
    private Func<byte[], bool>? _reportMatcher;
    private volatile bool _usesCorrelatedReports;

    private int _battery = -1;
    private bool _isCharging;
    private bool _isMicrophoneMuted;
    private bool _isMicrophoneMuteStateKnown;
    private bool _isConnected;

    protected HyperXBatteryDeviceBase(HyperXDeviceDefinition definition)
    {
        Definition = definition;
    }

    protected HyperXDeviceDefinition Definition { get; }

    protected HidConnection Connection => _connection;

    protected virtual bool UsesContinuousReader => false;

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

            _isConnected = true;
            _isMicrophoneMuted = false;
            _isMicrophoneMuteStateKnown = false;

            if (UsesContinuousReader)
            {
                _readerCancellation = new CancellationTokenSource();
                CancellationToken token = _readerCancellation.Token;
                _readerTask = Task.Run(() => ReaderLoopAsync(token));
            }

            return true;
        }
        catch
        {
            _connection.Close();
            _readerCancellation?.Dispose();
            _readerCancellation = null;
            _readerTask = null;
            _isConnected = false;
            return false;
        }
    }

    public virtual void Disconnect()
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

    public virtual async Task<int?> QueryBatteryAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            return null;

        if (!UsesContinuousReader)
            return await QueryBatteryDirectAsync(cancellationToken);

        var response = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_batteryRequestLock)
        {
            _batteryRequest = response;
        }

        try
        {
            _connection.Write(CreateBatteryCommand());
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

    public virtual async Task<bool?> QueryChargeStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            return null;

        byte[]? command = CreateChargeStatusCommand();

        if (command is null)
            return null;

        if (!UsesContinuousReader)
            return await QueryChargeStatusDirectAsync(command, cancellationToken);

        var response = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_chargeStatusRequestLock)
        {
            _chargeStatusRequest = response;
        }

        try
        {
            _connection.Write(command);
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

    public virtual async Task<bool?> QueryMicrophoneMuteStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            return null;

        byte[]? command = CreateMicrophoneMuteCommand();

        if (command is null)
            return null;

        if (!UsesContinuousReader)
            return await QueryMicrophoneMuteDirectAsync(command, cancellationToken);

        var response = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_microphoneMuteRequestLock)
        {
            _microphoneMuteRequest = response;
        }

        try
        {
            _connection.Write(command);
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

    // Opt-in request correlation for protocols whose responses contain multiple states.
    // The existing continuous reader remains the only input-report reader.
    protected async Task<byte[]?> QueryReportAsync(
        byte[] command,
        Func<byte[], bool> matchesResponse,
        CancellationToken cancellationToken)
    {
        if (!IsConnected || !UsesContinuousReader)
            return null;

        var response = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_reportRequestLock)
        {
            if (_reportRequest != null)
                throw new InvalidOperationException("A HID status request is already pending.");

            _usesCorrelatedReports = true;
            _reportRequest = response;
            _reportMatcher = matchesResponse;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeout);
        try
        {
            if (!await SendReportAsync(command, timeout.Token).ConfigureAwait(false))
                return null;

            return await response.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        finally
        {
            lock (_reportRequestLock)
            {
                if (ReferenceEquals(_reportRequest, response))
                {
                    _reportRequest = null;
                    _reportMatcher = null;
                }
            }
        }
    }

    protected virtual Task<bool> SendReportAsync(byte[] command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Connection.Write(command);
        return Task.FromResult(true);
    }

    protected virtual byte[] CreateBatteryCommand()
    {
        return CreateCommand(Definition.BatteryCommandBytes);
    }

    protected virtual byte[]? CreateChargeStatusCommand()
    {
        return null;
    }

    protected virtual byte[]? CreateMicrophoneMuteCommand()
    {
        return null;
    }

    protected static byte[] CreateCommand(byte[] commandBytes)
    {
        return (byte[])commandBytes.Clone();
    }

    protected virtual bool TryParseBatteryReport(
        byte[] report,
        out int battery,
        out bool isResponse)
    {
        battery = -1;
        isResponse = true;

        if (Definition.BatteryByteIndex < 0 ||
            Definition.BatteryByteIndex >= report.Length)
        {
            return false;
        }

        battery = report[Definition.BatteryByteIndex];
        return battery is >= 0 and <= 100;
    }

    protected virtual bool TryParseChargeStatusReport(
        byte[] report,
        out bool isCharging,
        out bool isResponse)
    {
        isCharging = false;
        isResponse = false;
        return false;
    }

    protected virtual bool TryParseMicrophoneMuteReport(
        byte[] report,
        out bool isMuted,
        out bool isResponse)
    {
        isMuted = false;
        isResponse = false;
        return false;
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

                if (_usesCorrelatedReports && cancellationToken.IsCancellationRequested)
                    break;

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

    protected void CompleteReportRequest(byte[] report)
    {
        lock (_reportRequestLock)
        {
            if (_reportMatcher?.Invoke(report) == true)
                _reportRequest?.TrySetResult(report);
        }
    }

    protected virtual void ProcessInputReport(byte[] report)
    {
        if (TryParseBatteryReport(
                report,
                out int battery,
                out bool batteryResponse))
        {
            UpdateBattery(battery);

            if (batteryResponse)
            {
                lock (_batteryRequestLock)
                {
                    _batteryRequest?.TrySetResult(battery);
                }
            }
        }

        if (TryParseChargeStatusReport(
                report,
                out bool isCharging,
                out bool chargeResponse))
        {
            UpdateChargingState(isCharging);

            if (chargeResponse)
            {
                lock (_chargeStatusRequestLock)
                {
                    _chargeStatusRequest?.TrySetResult(isCharging);
                }
            }
        }

        if (TryParseMicrophoneMuteReport(
                report,
                out bool isMuted,
                out bool microphoneMuteResponse))
        {
            UpdateMicrophoneMuteState(isMuted);

            if (microphoneMuteResponse)
            {
                lock (_microphoneMuteRequestLock)
                {
                    _microphoneMuteRequest?.TrySetResult(isMuted);
                }
            }
        }
    }

    private async Task<int?> QueryBatteryDirectAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            _connection.Write(CreateBatteryCommand());

            byte[]? response = await ReadResponseAsync(cancellationToken);

            if (response is null ||
                !TryParseBatteryReport(
                    response,
                    out int battery,
                    out _))
            {
                return null;
            }

            UpdateBattery(battery);
            return battery;
        }
        catch (IOException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool?> QueryChargeStatusDirectAsync(
        byte[] command,
        CancellationToken cancellationToken)
    {
        try
        {
            _connection.Write(command);
            byte[]? response = await ReadResponseAsync(cancellationToken);

            if (response is null ||
                !TryParseChargeStatusReport(
                    response,
                    out bool isCharging,
                    out _))
            {
                return null;
            }

            UpdateChargingState(isCharging);
            return isCharging;
        }
        catch (IOException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool?> QueryMicrophoneMuteDirectAsync(
        byte[] command,
        CancellationToken cancellationToken)
    {
        try
        {
            _connection.Write(command);
            byte[]? response = await ReadResponseAsync(cancellationToken);

            if (response is null ||
                !TryParseMicrophoneMuteReport(
                    response,
                    out bool isMuted,
                    out _))
            {
                return null;
            }

            UpdateMicrophoneMuteState(isMuted);
            return isMuted;
        }
        catch (IOException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<byte[]?> ReadResponseAsync(
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutCancellation.CancelAfter(QueryTimeout);

        return await _connection.ReadAsync(
            timeoutCancellation.Token);
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

    protected void UpdateBattery(int battery)
    {
        if (_battery == battery)
            return;

        _battery = battery;
        BatteryChanged?.Invoke(this, battery);
    }

    protected void UpdateChargingState(bool isCharging)
    {
        if (_isCharging == isCharging)
            return;

        _isCharging = isCharging;
        ChargingChanged?.Invoke(this, isCharging);
    }

    protected void UpdateMicrophoneMuteState(bool isMuted)
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
        lock (_reportRequestLock)
        {
            _reportRequest?.TrySetCanceled();
            _reportRequest = null;
            _reportMatcher = null;
        }

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
