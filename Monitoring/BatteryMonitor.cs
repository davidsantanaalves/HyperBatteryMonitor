using HyperXBatteryTray.Devices;

namespace HyperXBatteryTray.Monitoring;

public sealed class BatteryMonitor : IDisposable
{
    private readonly IHyperXDevice _device;

    private CancellationTokenSource? _cancellation;
    private Task? _monitorTask;

    private int? _lastBattery;
    private bool? _lastChargingState;
    private bool? _lastMicrophoneMuteState;
    private bool _lastConnectionState;
    private bool _microphoneStateInitialized;
    private int _lastBluetoothTransportGeneration = -1;

    public BatteryMonitor(IHyperXDevice device)
    {
        _device = device;
        _device.BatteryChanged += Device_BatteryChanged;
        _device.ChargingChanged += Device_ChargingChanged;
        _device.MicrophoneMuteChanged += Device_MicrophoneMuteChanged;
    }

    public int IntervalMilliseconds { get; set; } = 5000;

    public event EventHandler<int>? BatteryChanged;

    public event EventHandler<bool>? ConnectionChanged;

    public event EventHandler<bool>? ChargingChanged;

    public event EventHandler<bool>? MicrophoneMuteChanged;

    public void Start()
    {
        if (_monitorTask != null)
            return;

        _cancellation = new CancellationTokenSource();
        _monitorTask = MonitorLoopAsync(_cancellation.Token);
    }

    private async Task MonitorLoopAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_device.IsConnected)
                {
                    bool connected = _device.Connect();

                    UpdateConnectionState(connected);

                    if (!connected)
                    {
                        await Task.Delay(
                            IntervalMilliseconds,
                            cancellationToken);

                        continue;
                    }

                    _microphoneStateInitialized = false;
                }

                int? battery = await _device.QueryBatteryAsync(
                    cancellationToken);

                if (!battery.HasValue)
                {
                    _device.Disconnect();
                    ResetDisconnectedState();
                    UpdateConnectionState(false);

                    await Task.Delay(
                        IntervalMilliseconds,
                        cancellationToken);

                    continue;
                }

                // The optional Bluetooth adapter can promote a verified USB
                // receiver without a full disconnect. Reinitialize only the
                // metadata that is transport-specific; original HID drivers
                // never enter this branch.
                if (_device is BluetoothFallbackDevice fallback &&
                    fallback.TransportGeneration != _lastBluetoothTransportGeneration)
                {
                    _lastBluetoothTransportGeneration = fallback.TransportGeneration;
                    // Force a status refresh even if both transports report
                    // the same percentage (tray/runtime estimate may differ).
                    _lastBattery = null;
                    _microphoneStateInitialized = false;
                    _lastChargingState = null;
                    _lastMicrophoneMuteState = null;
                }

                UpdateBatteryState(battery.Value);

                bool? chargeStatus = await _device.QueryChargeStatusAsync(
                    cancellationToken);

                if (chargeStatus.HasValue)
                    UpdateChargingState(chargeStatus.Value);

                if (!_microphoneStateInitialized)
                {
                    if (!_device.SupportsMicrophoneMuteMonitoring)
                    {
                        _microphoneStateInitialized = true;
                    }
                    else
                    {
                        bool? microphoneMuted =
                            await _device.QueryMicrophoneMuteStatusAsync(
                                cancellationToken);

                        if (microphoneMuted.HasValue)
                        {
                            _microphoneStateInitialized = true;
                            UpdateMicrophoneMuteState(microphoneMuted.Value);
                        }
                    }
                }

                await Task.Delay(
                    IntervalMilliseconds,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (_device.IsConnected)
                    _device.Disconnect();

                ResetDisconnectedState();
                UpdateConnectionState(false);

                try
                {
                    await Task.Delay(
                        IntervalMilliseconds,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void Device_BatteryChanged(object? sender, int battery)
    {
        if (battery >= 0)
            UpdateBatteryState(battery);
    }

    private void Device_ChargingChanged(object? sender, bool charging)
    {
        UpdateChargingState(charging);
    }

    private void Device_MicrophoneMuteChanged(object? sender, bool muted)
    {
        _microphoneStateInitialized = true;
        UpdateMicrophoneMuteState(muted);
    }

    private void UpdateBatteryState(int battery)
    {
        if (_lastBattery == battery)
            return;

        _lastBattery = battery;
        BatteryChanged?.Invoke(this, battery);
    }

    private void UpdateChargingState(bool charging)
    {
        if (_lastChargingState == charging)
            return;

        _lastChargingState = charging;
        ChargingChanged?.Invoke(this, charging);
    }

    private void UpdateMicrophoneMuteState(bool muted)
    {
        if (_lastMicrophoneMuteState == muted)
            return;

        _lastMicrophoneMuteState = muted;
        MicrophoneMuteChanged?.Invoke(this, muted);
    }

    private void UpdateConnectionState(bool connected)
    {
        if (_lastConnectionState == connected)
            return;

        _lastConnectionState = connected;
        ConnectionChanged?.Invoke(this, connected);
    }

    private void ResetDisconnectedState()
    {
        _lastBattery = null;
        _lastChargingState = null;
        _lastMicrophoneMuteState = null;
        _microphoneStateInitialized = false;
    }

    public void Dispose()
    {
        _cancellation?.Cancel();

        _device.BatteryChanged -= Device_BatteryChanged;
        _device.ChargingChanged -= Device_ChargingChanged;
        _device.MicrophoneMuteChanged -= Device_MicrophoneMuteChanged;

        _device.Disconnect();
        _cancellation?.Dispose();

        _cancellation = null;
        _monitorTask = null;

        _device.Dispose();
    }
}
