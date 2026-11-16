namespace HyperXBatteryTray.Devices;

/// <summary>
/// Optional, device-resident auto-shutdown configuration. Values are minutes.
/// Never=0, otherwise 10/20/30. Reading never changes the headset.
/// Set must re-read and verify; a successful HID write alone is insufficient.
/// </summary>
public interface IAutoPowerOffDevice
{
    bool SupportsAutoPowerOff { get; }

    Task<int?> QueryAutoPowerOffAsync(CancellationToken cancellationToken = default);

    Task<bool> SetAutoPowerOffAsync(int minutes, CancellationToken cancellationToken = default);
}
