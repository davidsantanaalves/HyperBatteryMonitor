using System.Diagnostics;
using HyperXBatteryTray.Settings;
using HyperXBatteryTray.Updates;

namespace HyperXBatteryTray;

internal static class Program
{
    private const string ResetUserDataArgument = "--reset-user-data";
    private const string MigrateUserDataArgument = "--migrate-user-data";

    [STAThread]
    static int Main(string[] args)
    {
        if (!ApplicationRuntime.Configure(args))
        {
            Debug.WriteLine("--test is supported only in DEBUG builds.");
            return 2;
        }

        if (ApplicationRuntime.IsTestMode &&
            (HasArgument(args, ResetUserDataArgument) ||
             HasArgument(args, MigrateUserDataArgument)))
        {
            // Installer maintenance commands must never operate in test mode.
            return 2;
        }

        SettingsManager settingsManager = new();

        if (HasArgument(args, ResetUserDataArgument))
            return RunUserDataMaintenance(settingsManager, reset: true);

        if (HasArgument(args, MigrateUserDataArgument))
            return RunUserDataMaintenance(settingsManager, reset: false);

        try
        {
            settingsManager.MigrateLegacyUserData();
            new StartupManager().MigrateLegacyRegistration();
        }
        catch (Exception ex)
        {
            // The installer invokes explicit user-data maintenance and stops previous-
            // folder cleanup on data migration failure. A direct/MSIX launch
            // preserves legacy data and continues if automatic migration fails.
            Debug.WriteLine(
                $"Could not migrate legacy Hyper Battery Monitor state: {ex.Message}");
        }

        using Mutex applicationMutex = new(
            initiallyOwned: false,
            ApplicationRuntime.MutexName);

        ApplicationConfiguration.Initialize();
        UpdateNotificationService.RegisterActivation();
        Application.Run(
            new HyperXBatteryMonitorApplicationContext());

        return 0;
    }

    private static int RunUserDataMaintenance(SettingsManager settingsManager, bool reset)
    {
        string operation = reset ? "Clean installation" : "User data migration";

        try
        {
            if (reset)
                settingsManager.DeleteUserData();
            else
                settingsManager.MigrateLegacyUserData();
        }
        catch (Exception ex)
        {
            // WinExe has no visible console. Preserve the actual failure outside
            // the profile directories that a clean installation removes.
            WriteMaintenanceLog($"{operation} failed", ex);
            return 1;
        }

        // Startup registration is independent of the data reset/migration.
        // A registry failure must not turn a completed clean installation into
        // a false data-deletion error. Normal startup retries this migration.
        try
        {
            new StartupManager().MigrateLegacyRegistration();
        }
        catch (Exception ex)
        {
            WriteMaintenanceLog("Windows startup registration migration failed; " +
                "user data maintenance succeeded", ex);
        }

        return 0;
    }

    private static void WriteMaintenanceLog(string message, Exception exception)
    {
        Debug.WriteLine($"{message}: {exception}");

        try
        {
            // Do not log inside the folders removed by --reset-user-data.
            string logPath = Path.Combine(
                Path.GetTempPath(), "HBM-Maintenance.log");

            // A maintenance operation runs only at install/update time. Keep
            // repeated failures from making an indefinitely growing log.
            if (File.Exists(logPath) &&
                new FileInfo(logPath).Length > 1024 * 1024)
            {
                File.Delete(logPath);
            }

            File.AppendAllText(logPath,
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logFailure)
        {
            // Diagnostics must never change the maintenance exit code.
            Debug.WriteLine($"Could not write maintenance log: {logFailure}");
        }
    }

    private static bool HasArgument(
        IEnumerable<string> args,
        string expectedArgument)
    {
        return args.Any(arg =>
            string.Equals(
                arg,
                expectedArgument,
                StringComparison.OrdinalIgnoreCase));
    }
}
