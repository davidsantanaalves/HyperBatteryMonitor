namespace HyperXBatteryTray.Settings;

// A separate partial avoids modifying the existing Settings navigation/layout code.
public sealed partial class SettingsForm
{
    internal void NavigateToAbout() => ShowPage("About");
}
