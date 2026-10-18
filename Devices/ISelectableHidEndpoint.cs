namespace HyperXBatteryTray.Devices;

/// <summary>Passes an enumerated, validated HID candidate to its existing driver.</summary>
internal interface ISelectableHidEndpoint
{
    void SelectHidEndpoint(string path);
}
