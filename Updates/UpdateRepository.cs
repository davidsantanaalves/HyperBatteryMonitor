namespace HyperXBatteryTray.Updates;

/// <summary>
/// Allowed GitHub Releases origins. A test origin is available only in DEBUG
/// when Program.Main is explicitly launched with --test.
/// </summary>
internal static class UpdateRepository
{
    private const string Owner = "davidsantanaalves";
    private const string ProductionName = "HyperBatteryMonitor";
#if DEBUG
    private const string TestName = "HyperBatteryMonitor-UpdateTests";
    private static string Name => ApplicationRuntime.IsTestMode ? TestName : ProductionName;
#else
    private static string Name => ProductionName;
#endif

    internal static string ApiUrl =>
        $"https://api.github.com/repos/{Owner}/{Name}/releases?per_page=100";
    internal static string ReleaseDownloadPath =>
        $"/{Owner}/{Name}/releases/download/";
    internal static string ReleasePagePath =>
        $"/{Owner}/{Name}/releases/tag/";
}
