namespace HyperXBatteryTray.Devices;

/// <summary>Optional notification that a previously known mute state is now unknown.</summary>
public interface IMicrophoneMuteStateInvalidationSource
{
    event EventHandler? MicrophoneMuteStateInvalidated;
}
