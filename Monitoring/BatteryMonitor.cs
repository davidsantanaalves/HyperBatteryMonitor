using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Diagnostics;

namespace HyperXBatteryTray.Monitoring;

public sealed class BatteryMonitor : IDisposable
{
    private readonly IHyperXDevice _device;

    private readonly SemaphoreSlim _wake = new(0, 1);
    private volatile bool _physicalDisconnected;
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
        if (_device is IEventDrivenHidDevice events)
            events.PhysicalConnectionChanged += Device_PhysicalConnectionChanged;
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
                if (_physicalDisconnected)
                {
                    _physicalDisconnected = false;
                    BetaDiagnostics.Write("Monitor.PhysicalDisconnect", new { Model = _device.Name });
                    _device.Disconnect();
                    ResetDisconnectedState();
                    UpdateConnectionState(false);
                }
                if (!_device.IsConnected)
                {
                    bool connected = _device.Connect();

                    // Opening a USB/HID or Bluetooth transport does not prove the
                    // headset is responsive. Publish Connected only after a valid
                    // battery response, regardless of the device implementation.
                    BetaDiagnostics.Write("Monitor.ConnectAttempt", new { Model = _device.Name, Connected = connected });

                    if (!connected)
                    {
                        await WaitForNextCycleAsync(IntervalMilliseconds, cancellationToken);
                        continue;
                    }

                    _microphoneStateInitialized = false;
                }

                int? battery = await _device.QueryBatteryAsync(
                    cancellationToken);

                if (!battery.HasValue || battery.Value is < 0 or > 100)
                {
                    BetaDiagnostics.Write("Monitor.BatteryQueryFailed", new { Model = _device.Name });
                    _device.Disconnect();
                    ResetDisconnectedState();
                    UpdateConnectionState(false);

                    await WaitForNextCycleAsync(IntervalMilliseconds, cancellationToken);
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

                // The common monitor's Connected state means a verified, live
                // headset, not merely an opened receiver/transport. This also
                // covers receiver-aware and Bluetooth fallback implementations.
                UpdateConnectionState(true);
                BetaDiagnostics.Write("Monitor.BatteryQuery", new { Model = _device.Name, Battery = battery.Value });
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

                bool canUseEvents = _device is IEventDrivenHidDevice live &&
                    live.SupportsEventDrivenBattery;
                // Only Beta builds opt other receiver families into reduced
                // querying. The opt-in is explicit per verified parser shape:
                // a continuous HID reader without unsolicited battery support
                // must keep its original five-second polling cadence.
                bool betaNotifications = AppReleaseInfo.IsBeta &&
                    _device is IBetaBatteryNotificationDevice notifications &&
                    notifications.SupportsUnsolicitedBatteryNotifications;
                bool reducedPolling = canUseEvents || betaNotifications;
                int interval = reducedPolling ? 60000 : IntervalMilliseconds;
                BetaDiagnostics.Write("Monitor.CycleComplete", new
                {
                    Model = _device.Name,
                    Mode = canUseEvents ? "EventDriven" :
                        betaNotifications ? "BetaNotificationAssisted" : "Polling",
                    RecoveryIntervalMs = interval
                });
                await WaitForNextCycleAsync(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                BetaDiagnostics.WriteError("Monitor.UnhandledCycleError", ex);
                if (_device.IsConnected)
                    _device.Disconnect();

                ResetDisconnectedState();
                UpdateConnectionState(false);

                try
                {
                    await WaitForNextCycleAsync(IntervalMilliseconds, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task WaitForNextCycleAsync(int milliseconds, CancellationToken cancellationToken)
    {
        await _wake.WaitAsync(milliseconds, cancellationToken);
    }

    private void Device_PhysicalConnectionChanged(object? sender, bool connected)
    {
        BetaDiagnostics.Write("Monitor.PhysicalConnection", new { Model = _device.Name, Connected = connected });
        if (!connected)
            _physicalDisconnected = true;
        try { if (_wake.CurrentCount == 0) _wake.Release(); } catch (SemaphoreFullException) { }
    }

    private void Device_BatteryChanged(object? sender, int battery)
    {
        BetaDiagnostics.Write("Monitor.BatteryChanged", new { Model = _device.Name, Battery = battery });
        if (battery >= 0)
            UpdateBatteryState(battery);
    }

    private void Device_ChargingChanged(object? sender, bool charging)
    {
        BetaDiagnostics.Write("Monitor.ChargingEvent", new { Model = _device.Name, Charging = charging });
        UpdateChargingState(charging);
    }

    private void Device_MicrophoneMuteChanged(object? sender, bool muted)
    {
        BetaDiagnostics.Write("Monitor.MuteEvent", new { Model = _device.Name, Muted = muted });
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
        if (_device is IEventDrivenHidDevice events)
            events.PhysicalConnectionChanged -= Device_PhysicalConnectionChanged;

        _device.Disconnect();
        _cancellation?.Dispose();

        _cancellation = null;
        _monitorTask = null;

        _device.Dispose();
    }
}
