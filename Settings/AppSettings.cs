using System.Drawing;
using System.Text.Json.Serialization;
using UpdateChannelEnum = HyperXBatteryTray.Updates.UpdateChannel;
using UpdateDownloadModeEnum = HyperXBatteryTray.Updates.UpdateDownloadMode;

namespace HyperXBatteryTray.Settings;

public enum AppTheme
{
    Light,
    Dark,
    System
}

public enum DeviceMonitoringMode
{
    Polling,
    ListenOnly
}

public enum BatteryDisplayMode
{
    StaticIcon,
    BatteryIndicator,
    Advanced
}

public enum AdvancedDisplayMode
{
    BatteryGradient,
    BatteryIndicator,
    PercentageText
}

public sealed class BatteryColorSettings
{
    public string Name { get; set; } = string.Empty;
    public int MinimumPercent { get; set; }
    public int Argb { get; set; }

    [JsonIgnore]
    public Color Color
    {
        get => Color.FromArgb(Argb);
        set => Argb = value.ToArgb();
    }
}

public sealed class AppSettings
{
    public const bool DefaultStartWithWindows = true;

    internal bool IsNewSettingsProfile { get; set; }

    public string SelectedDevice { get; set; } = string.Empty;
    // Explicit choices only; missing models resolve to their model-specific defaults.
    public Dictionary<string, DeviceMonitoringMode> DeviceMonitoringModes { get; set; } = new();

    // One verified on-device read per active RF connection; never serialized,
    // inherited by another headset, or used to initiate periodic HID queries.
    [JsonIgnore]
    public int? RuntimeAutoPowerOffMinutes { get; set; }

    // The last VERIFIED setting for this exact model, for read-only display
    // while disconnected. It is never used to send an HID write automatically.
    public Dictionary<string, int> LastKnownAutoPowerOffMinutes { get; set; } = new();

    public int? GetLastKnownAutoPowerOff(string? model)
    {
        string key = HyperXBatteryTray.Devices.HyperXDeviceManager.GetSelectionModelName(model);
        if (!HyperXBatteryTray.Devices.HyperXDeviceManager.SupportsAutoPowerOff(key))
            return null;
        return LastKnownAutoPowerOffMinutes != null &&
            LastKnownAutoPowerOffMinutes.TryGetValue(key, out int minutes) &&
            minutes is 0 or 10 or 20 or 30 ? minutes : null;
    }

    // Called only after a device query or confirmed setter; returns true on change.
    public bool RememberVerifiedAutoPowerOff(string? model, int minutes)
    {
        string key = HyperXBatteryTray.Devices.HyperXDeviceManager.GetSelectionModelName(model);
        if (!HyperXBatteryTray.Devices.HyperXDeviceManager.SupportsAutoPowerOff(key) ||
            minutes is not (0 or 10 or 20 or 30)) return false;
        LastKnownAutoPowerOffMinutes ??= new Dictionary<string, int>();
        if (LastKnownAutoPowerOffMinutes.TryGetValue(key, out int existing) &&
            existing == minutes) return false;
        LastKnownAutoPowerOffMinutes[key] = minutes;
        return true;
    }


    public DeviceMonitoringMode GetMonitoringMode(string? model)
    {
        string key = HyperXBatteryTray.Devices.HyperXDeviceManager.GetSelectionModelName(model);
        if (!HyperXBatteryTray.Devices.HyperXDeviceManager.SupportsListenOnlyMonitoring(key))
            return DeviceMonitoringMode.Polling;
        if (DeviceMonitoringModes != null && DeviceMonitoringModes.TryGetValue(key, out DeviceMonitoringMode selected) &&
            Enum.IsDefined(selected))
            return selected;
        return string.Equals(key, "HyperX Cloud III", StringComparison.OrdinalIgnoreCase)
            ? DeviceMonitoringMode.ListenOnly : DeviceMonitoringMode.Polling;
    }

    public BatteryDisplayMode DisplayMode { get; set; } = BatteryDisplayMode.StaticIcon;
    public AdvancedDisplayMode AdvancedDisplayMode { get; set; } = AdvancedDisplayMode.BatteryGradient;
    public bool UseGradient { get; set; } = true;
    public int GradientPercent { get; set; } = 10;
    public bool NotifyOnLowBattery { get; set; } = true;
    public bool NotifyWhenFullyCharged { get; set; } = true;
    public bool BlinkOnCriticalBattery { get; set; } = true;
    public bool ShowMicrophoneMuteInSystray { get; set; } = true;
    public int CriticalBatteryPercent { get; set; } = 10;
    public List<BatteryColorSettings> BatteryColors { get; set; } = CreateDefaultColors();
    public AppLanguage Language { get; set; } = AppLanguage.English;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool ThemeConfigured { get; set; }
    public UpdateChannelEnum UpdateChannel { get; set; } = AppReleaseInfo.DefaultUpdateChannel;
    public int BetaDiagnosticsConsentVersion { get; set; }
    public bool UpdateChannelUserSelected { get; set; }
    public UpdateDownloadModeEnum UpdateDownloadMode { get; set; } = UpdateDownloadModeEnum.NotifyBeforeDownload;

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            SelectedDevice = string.Empty,
            DisplayMode = BatteryDisplayMode.BatteryIndicator,
            AdvancedDisplayMode = AdvancedDisplayMode.BatteryGradient,
            UseGradient = true,
            GradientPercent = 10,
            NotifyOnLowBattery = true,
            NotifyWhenFullyCharged = true,
            BlinkOnCriticalBattery = true,
            ShowMicrophoneMuteInSystray = true,
            CriticalBatteryPercent = 10,
            BatteryColors = CreateDefaultColors(),
            Language = AppLanguage.English,
            Theme = AppTheme.System,
            ThemeConfigured = false,
            UpdateChannel = AppReleaseInfo.DefaultUpdateChannel,
            UpdateChannelUserSelected = false,
            UpdateDownloadMode = UpdateDownloadModeEnum.NotifyBeforeDownload
        };
    }

    private static List<BatteryColorSettings> CreateDefaultColors()
    {
        return new List<BatteryColorSettings>
        {
            new() { Name = "Green", MinimumPercent = 60, Color = Color.LimeGreen },
            new() { Name = "Yellow", MinimumPercent = 30, Color = Color.Gold },
            new() { Name = "Red", MinimumPercent = 0, Color = Color.Red }
        };
    }
}
