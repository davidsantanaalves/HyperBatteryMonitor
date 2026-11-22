using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Settings;
using HyperXBatteryTray.Diagnostics;

namespace HyperXBatteryTray.Monitoring;

public sealed class BatteryMonitor : IDisposable
{
    private readonly IHyperXDevice _device;
    private readonly DeviceMonitoringMode _requestedMode;

    private readonly SemaphoreSlim _wake = new(0, 1);
    private volatile bool _physicalDisconnected;
    private volatile bool _passivePowerOnSignal;
    private volatile bool _passiveReaderStopped;
    private CancellationTokenSource? _cancellation;
    private Task? _monitorTask;

    private int? _lastBattery;
    private bool? _lastChargingState;
    private bool? _lastMicrophoneMuteState;
    private bool _lastConnectionState;
    private bool _microphoneStateInitialized;
    private int _lastBluetoothTransportGeneration = -1;

    public BatteryMonitor(IHyperXDevice device, DeviceMonitoringMode mode = DeviceMonitoringMode.Polling)
    {
        _device = device;
        _requestedMode = mode;
        _device.BatteryChanged += Device_BatteryChanged;
        _device.ChargingChanged += Device_ChargingChanged;
        _device.MicrophoneMuteChanged += Device_MicrophoneMuteChanged;
        if (_device is IMicrophoneMuteStateInvalidationSource mute)
            mute.MicrophoneMuteStateInvalidated += Device_MicrophoneMuteStateInvalidated;
        if (_device is IEventDrivenHidDevice events)
            events.PhysicalConnectionChanged += Device_PhysicalConnectionChanged;
    }

    public int IntervalMilliseconds { get; set; } = 5000;

    public event EventHandler<int>? BatteryChanged;

    public event EventHandler<bool>? ConnectionChanged;

    public event EventHandler<bool>? ChargingChanged;

    public event EventHandler<bool>? MicrophoneMuteChanged;
    public event EventHandler? MicrophoneMuteStateInvalidated;

    public void Start()
    {
        if (_monitorTask != null)
            return;

        _cancellation = new CancellationTokenSource();
        if (AppReleaseInfo.IsBeta && _requestedMode == DeviceMonitoringMode.ListenOnly &&
            _device is Cloud3WirelessDevice cloud3)
        {
            // Beta 4 experiment, restricted to the original Cloud III 03F0:05B7.
            cloud3.EnableListenOnlyMonitoring();
            cloud3.HidReaderStopped += Cloud3_HidReaderStopped;
            _monitorTask = MonitorCloud3ListenOnlyAsync(cloud3, _cancellation.Token);
        }
        else if (AppReleaseInfo.IsBeta && _requestedMode == DeviceMonitoringMode.ListenOnly &&
            _device is IListenOnlyHidDevice passive)
        {
            passive.EnableListenOnlyMonitoring();
            passive.HidReaderStopped += Passive_HidReaderStopped;
            _monitorTask = MonitorAdditionalListenOnlyAsync(passive, _cancellation.Token);
        }
        else
        {
            _monitorTask = MonitorLoopAsync(_cancellation.Token);
        }
    }

    // No periodic 66 89 / 66 8A traffic while the original Cloud III is monitored.
    // A single battery query after USB listener open (or a power-on event) establishes
    // responsiveness; charging/mute are initialized once per live headset session.
    private async Task MonitorCloud3ListenOnlyAsync(
        Cloud3WirelessDevice cloud3, CancellationToken cancellationToken)
    {
        bool initialBatteryQueryPending = false;
        bool sessionInitialized = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_passiveReaderStopped)
                {
                    _passiveReaderStopped = false;
                    cloud3.Disconnect();
                    ResetDisconnectedState();
                    UpdateConnectionState(false);
                    sessionInitialized = false;
                    // A failed reader is a transport fault, not ordinary headset-off.
                    await WaitForNextCycleAsync(IntervalMilliseconds, cancellationToken);
                    continue;
                }

                if (!cloud3.IsListening)
                {
                    cloud3.Disconnect();
                    ResetDisconnectedState();
                    UpdateConnectionState(false);
                    _physicalDisconnected = false;
                    _passivePowerOnSignal = false;
                    sessionInitialized = false;

                    bool opened = cloud3.Connect();
                    BetaDiagnostics.Write("Monitor.ConnectAttempt", new { Model = _device.Name, Connected = opened });
                    if (!opened)
                    {
                        // Retry USB presence only. No HID battery command is sent.
                        await WaitForNextCycleAsync(IntervalMilliseconds, cancellationToken);
                        continue;
                    }
                    initialBatteryQueryPending = true;
                }

                if (_physicalDisconnected)
                {
                    _physicalDisconnected = false;
                    BetaDiagnostics.Write("Monitor.PhysicalDisconnect", new { Model = _device.Name });
                    // The latest validated power-on battery event may have arrived
                    // after the queued power-off event; do not demote it.
                    if (!cloud3.IsConnected)
                    {
                        ResetDisconnectedState();
                        UpdateConnectionState(false);
                        sessionInitialized = false;
                    }
                }

                if (_passivePowerOnSignal)
                {
                    _passivePowerOnSignal = false;
                    // No extra query when the power-on burst already contained 66 0D.
                    if (!cloud3.IsConnected)
                        initialBatteryQueryPending = true;
                }

                if (initialBatteryQueryPending)
                {
                    initialBatteryQueryPending = false;
                    int? battery = await cloud3.QueryBatteryAsync(cancellationToken);
                    if (battery is >= 0 and <= 100 && cloud3.IsConnected)
                    {
                        BetaDiagnostics.Write("Monitor.BatteryQuery", new { Model = _device.Name, Battery = battery.Value });
                        UpdateConnectionState(true);
                        UpdateBatteryState(battery.Value);
                    }
                    else if (!cloud3.IsConnected)
                    {
                        // RF headset may be off; preserve the HID reader for unsolicited events.
                        BetaDiagnostics.Write("Monitor.BatteryQueryFailed", new { Model = _device.Name });
                        UpdateConnectionState(false);
                    }
                }

                if (cloud3.IsConnected)
                {
                    UpdateConnectionState(true);
                    if (!sessionInitialized)
                    {
                        sessionInitialized = true;
                        bool? charging = await cloud3.QueryChargeStatusAsync(cancellationToken);
                        if (charging.HasValue)
                            UpdateChargingState(charging.Value);
                        if (cloud3.SupportsMicrophoneMuteMonitoring)
                        {
                            bool? muted = await cloud3.QueryMicrophoneMuteStatusAsync(cancellationToken);
                            if (muted.HasValue)
                                UpdateMicrophoneMuteState(muted.Value);
                        }
                    }
                }
                else
                {
                    UpdateConnectionState(false);
                    sessionInitialized = false;
                }

                BetaDiagnostics.Write("Monitor.CycleComplete", new
                {
                    Model = _device.Name,
                    Mode = "ListenOnly",
                    RecoveryIntervalMs = 0
                });
                // Only HID events, actual transport failure or cancellation wake this loop.
                await WaitForNextCycleAsync(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                BetaDiagnostics.WriteError("Monitor.ListenOnlyCycleError", ex);
                cloud3.Disconnect();
                ResetDisconnectedState();
                UpdateConnectionState(false);
                sessionInitialized = false;
                initialBatteryQueryPending = false;
                try
                {
                    await WaitForNextCycleAsync(IntervalMilliseconds, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    // Passive mode for receiver-aware, already-notification-capable dedicated protocols.
    // Keep the receiver reader open after RF-off; never fabricate a battery value.
    // If receiver verification selects a non-passive TIO child, revert to legacy
    // polling rather than presenting an unverified headset as event-capable.
    private async Task MonitorAdditionalListenOnlyAsync(
        IListenOnlyHidDevice passive, CancellationToken cancellationToken)
    {
        bool sessionInitialized = false;
        bool initialQueryPending = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_passiveReaderStopped || !passive.IsListening)
                {
                    _passiveReaderStopped = false;
                    _device.Disconnect();
                    ResetDisconnectedState();
                    UpdateConnectionState(false);
                    sessionInitialized = false;
                    _physicalDisconnected = false;
                    _passivePowerOnSignal = false;
                    if (!_device.Connect())
                    {
                        await WaitForNextCycleAsync(IntervalMilliseconds, cancellationToken);
                        continue;
                    }
                    initialQueryPending = true;
                }

                if (_physicalDisconnected)
                {
                    _physicalDisconnected = false;
                    if (_device.Battery is < 0 or > 100)
                    {
                        ResetDisconnectedState();
                        UpdateConnectionState(false);
                        sessionInitialized = false;
                    }
                }

                if (_passivePowerOnSignal)
                {
                    _passivePowerOnSignal = false;
                    // Power-on without battery push may require one validation query.
                    if (_device.Battery is < 0 or > 100)
                        initialQueryPending = true;
                }

                if (initialQueryPending)
                {
                    initialQueryPending = false;
                    int? battery = await _device.QueryBatteryAsync(cancellationToken);
                    if (battery is >= 0 and <= 100)
                    {
                        UpdateConnectionState(true);
                        UpdateBatteryState(battery.Value);
                    }
                    else if (!passive.IsListening)
                    {
                        // Receiver selection still needs bounded explicit validation
                        // while an RF device has not yet been identified.
                        _device.Disconnect();
                        ResetDisconnectedState();
                        UpdateConnectionState(false);
                        await WaitForNextCycleAsync(IntervalMilliseconds, cancellationToken);
                        continue;
                    }
                }

                if (!passive.IsListening)
                {
                    // Example: an active TIO child has no verified passive reader.
                    await MonitorLoopAsync(cancellationToken);
                    return;
                }

                if (_device.Battery is >= 0 and <= 100)
                {
                    UpdateConnectionState(true);
                    UpdateBatteryState(_device.Battery);
                    if (!sessionInitialized)
                    {
                        sessionInitialized = true;
                        bool? charging = await _device.QueryChargeStatusAsync(cancellationToken);
                        if (charging.HasValue) UpdateChargingState(charging.Value);
                        if (_device.SupportsMicrophoneMuteMonitoring)
                        {
                            bool? mute = await _device.QueryMicrophoneMuteStatusAsync(cancellationToken);
                            if (mute.HasValue) UpdateMicrophoneMuteState(mute.Value);
                        }
                    }
                }
                else
                {
                    UpdateConnectionState(false);
                    sessionInitialized = false;
                }
                BetaDiagnostics.Write("Monitor.CycleComplete", new
                {
                    Model = _device.Name,
                    Mode = "ListenOnly",
                    RecoveryIntervalMs = 0
                });
                await WaitForNextCycleAsync(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                BetaDiagnostics.WriteError("Monitor.ListenOnlyCycleError", ex);
                _device.Disconnect();
                ResetDisconnectedState();
                UpdateConnectionState(false);
                sessionInitialized = false;
                try { await WaitForNextCycleAsync(IntervalMilliseconds, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            }
        }
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
        else if (_requestedMode == DeviceMonitoringMode.ListenOnly &&
            _device is IListenOnlyHidDevice)
            _passivePowerOnSignal = true;
        WakeMonitor();
    }

    private void Passive_HidReaderStopped(object? sender, EventArgs e)
    {
        _passiveReaderStopped = true;
        WakeMonitor();
    }

    private void Cloud3_HidReaderStopped(object? sender, EventArgs e)
    {
        _passiveReaderStopped = true;
        WakeMonitor();
    }

    private void WakeMonitor()
    {
        try { if (_wake.CurrentCount == 0) _wake.Release(); } catch (SemaphoreFullException) { }
    }

    private void Device_BatteryChanged(object? sender, int battery)
    {
        BetaDiagnostics.Write("Monitor.BatteryChanged", new { Model = _device.Name, Battery = battery });
        if (battery >= 0)
        {
            if (_requestedMode == DeviceMonitoringMode.ListenOnly &&
                _device is IListenOnlyHidDevice)
            {
                // Wake the monitor to publish the validated live connection on its
                // normal synchronization context, rather than the HID reader thread.
                WakeMonitor();
            }
            UpdateBatteryState(battery);
        }
        else if (_requestedMode == DeviceMonitoringMode.ListenOnly)
            WakeMonitor();
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

    private void Device_MicrophoneMuteStateInvalidated(object? sender, EventArgs e)
    {
        _microphoneStateInitialized = false;
        _lastMicrophoneMuteState = null;
        MicrophoneMuteStateInvalidated?.Invoke(this, EventArgs.Empty);
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
        if (_device is IMicrophoneMuteStateInvalidationSource mute)
            mute.MicrophoneMuteStateInvalidated -= Device_MicrophoneMuteStateInvalidated;
        if (_device is IEventDrivenHidDevice events)
            events.PhysicalConnectionChanged -= Device_PhysicalConnectionChanged;
        if (_device is Cloud3WirelessDevice cloud3)
            cloud3.HidReaderStopped -= Cloud3_HidReaderStopped;
        if (_device is IListenOnlyHidDevice passive)
            passive.HidReaderStopped -= Passive_HidReaderStopped;

        _device.Disconnect();
        _cancellation?.Dispose();

        _cancellation = null;
        _monitorTask = null;

        _device.Dispose();
    }
}
