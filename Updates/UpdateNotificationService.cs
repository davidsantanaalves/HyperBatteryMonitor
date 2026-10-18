using HyperXBatteryTray.Settings;
using Microsoft.Toolkit.Uwp.Notifications;

namespace HyperXBatteryTray.Updates;

/// <summary>Informational update toasts; installation requires explicit About confirmation.</summary>
internal static class UpdateNotificationService
{
    private const string ActivationKey = "hbm";
    private const string ActivationValue = "updates";
    private static int _pendingOpenAbout;

    internal static event Action? OpenAboutRequested;

    // Must be called once at process startup, before constructing the application context.
    internal static void RegisterActivation()
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += args =>
            {
                try
                {
                    if (!ToastArguments.Parse(args.Argument).Contains(ActivationKey, ActivationValue))
                        return;

                    System.Threading.Interlocked.Exchange(ref _pendingOpenAbout, 1);
                    OpenAboutRequested?.Invoke();
                }
                catch
                {
                    // An unrelated/malformed notification must not crash HBM.
                }
            };
        }
        catch
        {
            // Failure to register activation must not prevent normal app startup.
        }
    }

    internal static bool ConsumePendingOpenAbout() =>
        System.Threading.Interlocked.Exchange(ref _pendingOpenAbout, 0) != 0;

    internal static void Available(string version, AppLanguage language) => Show(
        "UpdatesNoticeAvailableTitle", "UpdatesNoticeAvailableBody", version, language);

    internal static void Downloaded(string version, AppLanguage language) => Show(
        "UpdatesNoticeDownloadedTitle", "UpdatesNoticeDownloadedBody", version, language);

    internal static void Reminder(string version, AppLanguage language) => Show(
        "UpdatesNoticeReminderTitle", "UpdatesNoticeReminderBody", version, language);

    internal static void InstallationFailed(string version, AppLanguage language) => Show(
        "UpdatesNoticeInstallFailedTitle", "UpdatesNoticeInstallFailedBody", version, language);

    private static void Show(string titleKey, string bodyKey, string version, AppLanguage language)
    {
        try
        {
            new ToastContentBuilder()
                .AddArgument(ActivationKey, ActivationValue)
                .AddText(ApplicationRuntime.TestIndicator + Localization.Get(titleKey, language))
                .AddText(string.Format(Localization.Get(bodyKey, language), version))
                .Show();
        }
        catch
        {
            // Notifications are best-effort: About always retains the current status.
        }
    }
}
