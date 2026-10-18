using System.Diagnostics;

namespace HyperXBatteryTray.Devices;

/// <summary>
/// Read-only Bluetooth transport that implements the existing monitor contract.
/// Connect() never blocks the UI waiting for Windows device enumeration:
/// a positive connection state is reported only after a valid battery reading.
/// </summary>
internal sealed class BluetoothBatteryDevice : IHyperXDevice
{
    private readonly string _model;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _queryLock = new(1, 1);
    private Task<int?>? _initialRead;
    private DateTime _lastReadUtc;
    private const int CachedBatterySeconds = 12;
    private volatile int _battery = -1;
    private volatile bool _connected;
    private volatile bool _disposed;

    internal BluetoothBatteryDevice(string model) => _model = model;

    internal bool HasPendingDiscovery
    {
        get
        {
            lock (_sync) return _initialRead is { IsCompleted: false };
        }
    }

    public string Name => _model;
    public ushort VendorId => 0;
    public ushort ProductId => 0;
    public bool IsConnected => _connected;
    public int Battery => _battery;
    public bool IsCharging => false; // Not inferred without an explicit Windows charging property.
    public bool IsMicrophoneMuted => false;
    public bool IsMicrophoneMuteStateKnown => false;
    public bool SupportsMicrophoneMuteMonitoring => false;

    public event EventHandler<int>? BatteryChanged;
    public event EventHandler<bool>? ChargingChanged { add { } remove { } }
    public event EventHandler<bool>? MicrophoneMuteChanged { add { } remove { } }

    public bool Connect()
    {
        lock (_sync)
        {
            if (_disposed) return false;
            if (_connected) return true;
            if (_initialRead == null)
            {
                _initialRead = Task.Run(() => BluetoothBatteryProbe.ReadBatteryAsync(_model, _lifetime.Token));
                return false;
            }
            if (!_initialRead.IsCompleted) return false;
            try
            {
                int? percent = _initialRead.GetAwaiter().GetResult();
                if (percent is >= 0 and <= 100)
                {
                    _battery = percent.Value;
                    _connected = true;
                    _lastReadUtc = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Bluetooth battery discovery for '{_model}' failed: {ex.Message}");
            }
            _initialRead = null;
            return _connected;
        }
    }

    public void Disconnect()
    {
        lock (_sync)
        {
            _connected = false;
            _battery = -1;
            _lastReadUtc = DateTime.MinValue;
        }
    }

    public async Task<int?> QueryBatteryAsync(CancellationToken cancellationToken = default)
    {
        if (!_connected || _disposed) return null;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _queryLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_disposed || !_connected) return null;
            if (DateTime.UtcNow - _lastReadUtc < TimeSpan.FromSeconds(CachedBatterySeconds))
                return _battery;

            int? percent = await BluetoothBatteryProbe.ReadBatteryAsync(_model, linked.Token).ConfigureAwait(false);
            if (_disposed || !_connected || percent is not (>= 0 and <= 100))
                return null;
            bool changed = _battery != percent.Value;
            _battery = percent.Value;
            _lastReadUtc = DateTime.UtcNow;
            if (changed)
                BatteryChanged?.Invoke(this, percent.Value);
            return percent;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _queryLock.Release();
        }
    }

    public Task<bool?> QueryChargeStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<bool?>(null);

    public Task<bool?> QueryMicrophoneMuteStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<bool?>(null);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Disconnect();
        // An asynchronous Windows API operation might still be completing;
        // avoid disposing the cancellation source/semaphore while it is in use.
    }
}
