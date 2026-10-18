namespace HyperXBatteryTray;

/// <summary>
/// Single source of truth for process-wide execution mode. Release builds have
/// no way to activate test-only endpoints or update installation paths.
/// </summary>
internal enum ApplicationMode
{
    Normal,
    Test
}

internal static class ApplicationRuntime
{
    private const string TestArgument = "--test";
    private const string AllowInstallArgument = "--allowinstall";
    private const string NormalUserDataDirectoryName = "Hyper Battery Monitor";
#if DEBUG
    private const string TestUserDataDirectoryName = "Hyper Battery Monitor Test";
    private static ApplicationMode _mode = ApplicationMode.Normal;
    private static bool _testInstallationEnabled;
    internal static bool IsTestMode => _mode == ApplicationMode.Test;
    internal static bool IsTestInstallationEnabled => IsTestMode && _testInstallationEnabled;
#else
    internal static bool IsTestMode => false;
    internal static bool IsTestInstallationEnabled => false;
#endif

    // In production, installation remains enabled. In test mode it requires
    // BOTH command-line flags and a Debug build.
    internal static bool CanInstallUpdates => !IsTestMode || IsTestInstallationEnabled;

    internal static bool Configure(string[] arguments)
    {
        bool requested = arguments.Any(arg =>
            string.Equals(arg, TestArgument, StringComparison.OrdinalIgnoreCase));
        bool allowInstall = arguments.Any(arg =>
            string.Equals(arg, AllowInstallArgument, StringComparison.OrdinalIgnoreCase));
#if DEBUG
        // Never allow --allowinstall on its own: it must not change production mode.
        if (allowInstall && !requested)
            return false;
        _mode = requested ? ApplicationMode.Test : ApplicationMode.Normal;
        _testInstallationEnabled = requested && allowInstall;
        return true;
#else
        // Release builds cannot enable the test feed or the test installer.
        return !requested && !allowInstall;
#endif
    }

    internal static string UserDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
#if DEBUG
        IsTestMode ? TestUserDataDirectoryName : NormalUserDataDirectoryName);
#else
        NormalUserDataDirectoryName);
#endif

    internal static string MutexName => IsTestMode
        ? "HyperBatteryMonitor_Test_8D7E0A6C9D5B4F0B9C3E5F5E9C2A1B71"
        : "HyperBatteryMonitor_8D7E0A6C9D5B4F0B9C3E5F5E9C2A1B71";

    internal static string TestIndicator => IsTestMode ? "[TEST MODE] " : string.Empty;
}
