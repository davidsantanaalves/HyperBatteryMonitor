using System.Diagnostics;

namespace HyperXBatteryTray.Devices;

/// <summary>
/// Uses an already-supported HID driver when it can report a real headset battery.
/// Bluetooth is battery-only fallback. While Bluetooth is active, HID is revalidated
/// at a bounded interval so a transport change does not require an application restart.
/// </summary>
internal sealed class BluetoothFallbackDevice : IHyperXDevice
{
    private const int HidFailuresBeforeBluetooth = 2;
    private static readonly TimeSpan HidRecoveryProbeInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BluetoothDiscoveryGracePeriod = TimeSpan.FromSeconds(12);

    private readonly IHyperXDevice _hid;
    private readonly BluetoothBatteryDevice _bluetooth;
    private IHyperXDevice? _active;
    private bool _disposed;
    private int _transportGeneration;
    private bool _awaitingBluetoothFallback;
    private DateTime _bluetoothFallbackStartedUtc;
    private DateTime _nextHidRecoveryProbeUtc;
    private int _hidConsecutiveBatteryFailures;

    public BluetoothFallbackDevice(IHyperXDevice hid, string modelName)
    {
        _hid = hid;
        _bluetooth = new BluetoothBatteryDevice(modelName);
    }

    public bool IsUsingBluetooth => ReferenceEquals(_active, _bluetooth);
    internal int TransportGeneration => Volatile.Read(ref _transportGeneration);

    public string Name => _hid.Name;
    public ushort VendorId => _active?.VendorId ?? _hid.VendorId;
    public ushort ProductId => _active?.ProductId ?? _hid.ProductId;
    public bool IsConnected => _active?.IsConnected == true;
    public int Battery => _active?.Battery ?? -1;
    public bool IsCharging => _active?.IsCharging == true;
    public bool IsMicrophoneMuted => _active?.IsMicrophoneMuted == true;
    public bool IsMicrophoneMuteStateKnown => _active?.IsMicrophoneMuteStateKnown == true;
    public bool SupportsMicrophoneMuteMonitoring =>
        _active?.SupportsMicrophoneMuteMonitoring ?? _hid.SupportsMicrophoneMuteMonitoring;

    public event EventHandler<int>? BatteryChanged;
    public event EventHandler<bool>? ChargingChanged;
    public event EventHandler<bool>? MicrophoneMuteChanged;

    public bool Connect()
    {
        if (_disposed) return false;
        if (_active?.IsConnected == true) return true;
        ClearActive();

        // A USB receiver can remain physically present when the headset changes
        // to Bluetooth. After failed HID reads, allow Windows time to confirm
        // a live Bluetooth battery before reopening the unresponsive HID handle.
        if (_awaitingBluetoothFallback)
        {
            if (_bluetooth.Connect())
            {
                SetActive(_bluetooth);
                _awaitingBluetoothFallback = false;
                ScheduleHidRecoveryProbe();
                return true;
            }

            if (_bluetooth.HasPendingDiscovery &&
                DateTime.UtcNow - _bluetoothFallbackStartedUtc < BluetoothDiscoveryGracePeriod)
                return false;

            // A slow/failed Windows Bluetooth call must never prevent USB recovery.
            _awaitingBluetoothFallback = false;
        }

        if (_hid.Connect())
        {
            SetActive(_hid);
            return true;
        }

        if (_bluetooth.Connect())
        {
            SetActive(_bluetooth);
            ScheduleHidRecoveryProbe();
            return true;
        }
        return false;
    }

    public async Task<int?> QueryBatteryAsync(CancellationToken cancellationToken = default)
    {
        IHyperXDevice? active = _active;
        if (active == null) return null;

        if (ReferenceEquals(active, _bluetooth) && DateTime.UtcNow >= _nextHidRecoveryProbeUtc)
        {
            // Serialized with the BatteryMonitor loop. Never promote a dongle
            // merely because Windows enumerates it: the headset must respond.
            ScheduleHidRecoveryProbe();
            try
            {
                if (_hid.Connect())
                {
                    int? verified = await _hid.QueryBatteryAsync(cancellationToken).ConfigureAwait(false);
                    if (verified is >= 0 and <= 100 && !cancellationToken.IsCancellationRequested)
                    {
                        ClearActive(); // Unsubscribe and disconnect Bluetooth only.
                        _hidConsecutiveBatteryFailures = 0;
                        _awaitingBluetoothFallback = false;
                        SetActive(_hid);
                        Debug.WriteLine($"'{Name}' automatically switched from Bluetooth to verified USB HID.");
                        return verified;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"USB recovery probe for '{Name}' failed: {ex.Message}");
            }
            finally
            {
                if (!ReferenceEquals(_active, _hid))
                    _hid.Disconnect();
            }
        }

        int? percent = await active.QueryBatteryAsync(cancellationToken).ConfigureAwait(false);
        if (ReferenceEquals(active, _hid))
        {
            if (percent is >= 0 and <= 100)
            {
                _hidConsecutiveBatteryFailures = 0;
                _awaitingBluetoothFallback = false;
            }
            else if (!cancellationToken.IsCancellationRequested &&
                ++_hidConsecutiveBatteryFailures >= HidFailuresBeforeBluetooth)
            {
                _awaitingBluetoothFallback = true;
                _bluetoothFallbackStartedUtc = DateTime.UtcNow;
                // Start Bluetooth discovery without blocking the HID monitor.
                _bluetooth.Connect();
            }
        }
        return percent;
    }

    public Task<bool?> QueryChargeStatusAsync(CancellationToken cancellationToken = default) =>
        _active?.QueryChargeStatusAsync(cancellationToken) ?? Task.FromResult<bool?>(null);

    public Task<bool?> QueryMicrophoneMuteStatusAsync(CancellationToken cancellationToken = default) =>
        _active?.QueryMicrophoneMuteStatusAsync(cancellationToken) ?? Task.FromResult<bool?>(null);

    private void ScheduleHidRecoveryProbe() =>
        _nextHidRecoveryProbeUtc = DateTime.UtcNow + HidRecoveryProbeInterval;

    private void SetActive(IHyperXDevice device)
    {
        _active = device;
        Interlocked.Increment(ref _transportGeneration);
        device.BatteryChanged += RelayBattery;
        device.ChargingChanged += RelayCharging;
        device.MicrophoneMuteChanged += RelayMicrophone;
    }

    private void RelayBattery(object? sender, int value)
    {
        if (ReferenceEquals(sender, _active)) BatteryChanged?.Invoke(this, value);
    }
    private void RelayCharging(object? sender, bool value)
    {
        if (ReferenceEquals(sender, _active)) ChargingChanged?.Invoke(this, value);
    }
    private void RelayMicrophone(object? sender, bool value)
    {
        if (ReferenceEquals(sender, _active)) MicrophoneMuteChanged?.Invoke(this, value);
    }

    private void ClearActive()
    {
        IHyperXDevice? previous = _active;
        if (previous == null) return;
        _active = null;
        previous.BatteryChanged -= RelayBattery;
        previous.ChargingChanged -= RelayCharging;
        previous.MicrophoneMuteChanged -= RelayMicrophone;
        previous.Disconnect();
    }

    public void Disconnect()
    {
        IHyperXDevice? previous = _active;
        ClearActive();
        if (!ReferenceEquals(previous, _hid)) _hid.Disconnect();
        if (!ReferenceEquals(previous, _bluetooth)) _bluetooth.Disconnect();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
        _hid.Dispose();
        _bluetooth.Dispose();
    }
}
