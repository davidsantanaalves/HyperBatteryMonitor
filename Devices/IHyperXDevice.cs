namespace HyperXBatteryTray.Devices;

public interface IHyperXDevice : IDisposable
{
    string Name { get; }

    ushort VendorId { get; }

    ushort ProductId { get; }

    bool IsConnected { get; }

    int Battery { get; }

    bool IsCharging { get; }

    bool IsMicrophoneMuted { get; }

    bool IsMicrophoneMuteStateKnown { get; }

    bool SupportsMicrophoneMuteMonitoring { get; }

    event EventHandler<int>? BatteryChanged;

    event EventHandler<bool>? ChargingChanged;

    event EventHandler<bool>? MicrophoneMuteChanged;

    bool Connect();

    void Disconnect();

    Task<int?> QueryBatteryAsync(
        CancellationToken cancellationToken = default);

    Task<bool?> QueryChargeStatusAsync(
        CancellationToken cancellationToken = default);

    Task<bool?> QueryMicrophoneMuteStatusAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A Beta-only optimization for drivers that already parse independent battery
/// notifications through their existing HID reader. Does not imply live hardware
/// validation, reliable notification delivery, or physical-connection events.
/// </summary>
public interface IBetaBatteryNotificationDevice
{
    bool SupportsUnsolicitedBatteryNotifications { get; }
}

/// <summary>Optional, per-device capability: RF connection notifications and battery pushes.</summary>
public interface IEventDrivenHidDevice
{
    bool SupportsEventDrivenBattery { get; }
    event EventHandler<bool>? PhysicalConnectionChanged;
}
