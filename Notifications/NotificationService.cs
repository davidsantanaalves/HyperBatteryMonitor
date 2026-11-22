using HyperXBatteryTray;
using Microsoft.Toolkit.Uwp.Notifications;
using HyperXBatteryTray.Settings;

namespace HyperXBatteryTray.Notifications;

public sealed class NotificationService
{
    private bool _lowBatteryNotificationShown;
    private bool _fullyChargedNotificationShown;
    private bool _wasConnected;

    public void Update(
        string deviceName,
        int battery,
        bool connected,
        bool charging,
        AppSettings settings)
    {
        if (!connected || battery < 0 || battery > 100)
        {
            ResetState();
            return;
        }

        if (!_wasConnected)
        {
            _lowBatteryNotificationShown = false;
            _fullyChargedNotificationShown = false;
        }

        if (!settings.NotifyOnLowBattery)
            _lowBatteryNotificationShown = false;

        if (!settings.NotifyWhenFullyCharged)
            _fullyChargedNotificationShown = false;

        if (charging)
            _lowBatteryNotificationShown = false;
        else
            _fullyChargedNotificationShown = false;

        int criticalBattery = Math.Clamp(
            settings.CriticalBatteryPercent,
            1,
            100);

        if (settings.NotifyOnLowBattery &&
            !charging &&
            battery <= criticalBattery &&
            !_lowBatteryNotificationShown)
        {
            if (ShowLowBatteryNotification(
                    deviceName,
                    battery,
                    settings.Language))
            {
                _lowBatteryNotificationShown = true;
            }
        }

        if (settings.NotifyWhenFullyCharged &&
            charging &&
            battery >= 100 &&
            !_fullyChargedNotificationShown)
        {
            if (ShowFullyChargedNotification(
                    deviceName,
                    settings.Language))
            {
                _fullyChargedNotificationShown = true;
            }
        }

        _wasConnected = true;
    }

    public void ResetState()
    {
        _lowBatteryNotificationShown = false;
        _fullyChargedNotificationShown = false;
        _wasConnected = false;
    }

    public bool ShowDetailedCaptureStarted(int durationHours, AppLanguage language)
    {
        string title = Localization.Get("NotificationDetailedCaptureStartedTitle", language);
        string message = string.Format(
            Localization.Get("NotificationDetailedCaptureStartedBody", language), durationHours);
        return ShowNotification(title, message, openAboutOnClick: true);
    }

    public bool ShowDetailedCaptureResumed(TimeSpan remaining, AppLanguage language)
    {
        // Round up to avoid telling the user that a live session has 0 minutes left.
        int remainingMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        int hours = remainingMinutes / 60;
        int minutes = remainingMinutes % 60;
        return ShowNotification(
            Localization.Get("NotificationDetailedCaptureResumedTitle", language),
            string.Format(Localization.Get("NotificationDetailedCaptureResumedBody", language),
                hours, minutes),
            openAboutOnClick: true);
    }

    public bool ShowDetailedCaptureCompleted(AppLanguage language, bool essentialLoggingActive = true)
    {
        return ShowNotification(
            Localization.Get("NotificationDetailedCaptureCompletedTitle", language),
            Localization.Get(essentialLoggingActive
                ? "NotificationDetailedCaptureCompletedBody"
                : "NotificationDetailedCaptureCompletedTemporaryBody", language),
            openAboutOnClick: true);
    }

    public bool ShowDetailedCaptureStopped(AppLanguage language, bool essentialLoggingActive = true)
    {
        return ShowNotification(
            Localization.Get("NotificationDetailedCaptureStoppedTitle", language),
            Localization.Get(essentialLoggingActive
                ? "NotificationDetailedCaptureStoppedBody"
                : "NotificationDetailedCaptureStoppedTemporaryBody", language),
            openAboutOnClick: true);
    }

    private static bool ShowLowBatteryNotification(
        string deviceName,
        int battery,
        AppLanguage language)
    {
        string displayName = Localization.DeviceDisplayName(deviceName, language);
        string title = string.Format(
            Localization.Get("NotificationLowBatteryTitle", language),
            displayName);

        string batteryText = string.Format(
            Localization.Get("NotificationBattery", language),
            battery);

        string instruction = Localization.Get(
            "NotificationLowBatteryInstruction",
            language);

        return ShowNotification(
            title,
            batteryText,
            instruction,
            deviceName);
    }

    private static bool ShowFullyChargedNotification(
        string deviceName,
        AppLanguage language)
    {
        string displayName = Localization.DeviceDisplayName(deviceName, language);
        string title = string.Format(
            Localization.Get("NotificationFullyChargedTitle", language),
            displayName);

        string batteryText = Localization.Get(
            "NotificationFullyChargedBattery",
            language);

        string instruction = Localization.Get(
            "NotificationFullyChargedInstruction",
            language);

        return ShowNotification(
            title,
            batteryText,
            instruction,
            deviceName);
    }

    private static bool ShowNotification(
        string title,
        string message,
        string? instruction = null,
        string? deviceName = null,
        bool openAboutOnClick = false)
    {
        try
        {
            ToastContentBuilder builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message);

            if (openAboutOnClick)
                builder.AddArgument("hbm", "diagnostics");

            if (!string.IsNullOrWhiteSpace(instruction))
                builder.AddText(instruction);

            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                string? imagePath = ResolveDeviceImagePath(deviceName);
                if (!string.IsNullOrWhiteSpace(imagePath))
                    builder.AddAppLogoOverride(new Uri(imagePath, UriKind.Absolute));
            }

            builder.Show();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveDeviceImagePath(
        string deviceName)
    {
        string normalized = deviceName.Trim();

        string? fileName = normalized switch
        {
            _ when normalized.Contains(
                "Cloud III S",
                StringComparison.OrdinalIgnoreCase)
                => "cloud3.png",

            _ when normalized.Contains(
                "Cloud III",
                StringComparison.OrdinalIgnoreCase)
                => "cloud3.png",

            _ when normalized.Contains(
                "Cloud 2 Core",
                StringComparison.OrdinalIgnoreCase)
                => "cloud2core.png",

            _ when normalized.Contains(
                "Cloud Alpha 2",
                StringComparison.OrdinalIgnoreCase)
                => "cloudalpha2.png",

            _ when normalized.Contains(
                "Cloud Stinger 3",
                StringComparison.OrdinalIgnoreCase)
                => "cloudstinger3.png",

            _ when normalized.Contains(
                "Cloud Alpha",
                StringComparison.OrdinalIgnoreCase)
                => "cloudalpha.png",

            _ when normalized.Contains(
                "Cloud Stinger 2",
                StringComparison.OrdinalIgnoreCase)
                => "cloudstinger2.png",

            _ when normalized.Equals("HyperX Cloud Flight S", StringComparison.OrdinalIgnoreCase)
                => "cloudflights.png",

            _ when normalized.Equals("HyperX Cloud Flight Wireless", StringComparison.OrdinalIgnoreCase)
                => "cloudflightwireless.png",

            _ when normalized.Equals("HyperX Cloud Stinger Core Wireless + 7.1", StringComparison.OrdinalIgnoreCase)
                => "cloudstingercorewireless7.1.png",

            _ when normalized.Contains(
                "Cloud Flight 2",
                StringComparison.OrdinalIgnoreCase)
                => "cloudflight2.png",

            _ when normalized.Equals("HyperX Cloud Mix 2", StringComparison.OrdinalIgnoreCase)
                => "cloudmix2.png",

            _ => null
        };

        if (fileName == null)
            return null;

        string path = AssetPaths.GetDeviceImagePath(fileName);

        return File.Exists(path)
            ? path
            : null;
    }
}
