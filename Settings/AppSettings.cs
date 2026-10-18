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
