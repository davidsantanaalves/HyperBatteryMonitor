namespace HyperXBatteryTray;

internal static class AssetPaths
{
    private const string AssetsDirectoryName = "Assets";
    private const string BrandingDirectoryName = "Branding";
    private const string DevicesDirectoryName = "Devices";
    private const string IconsDirectoryName = "Icons";
    private const string CommonIconsDirectoryName = "Common";
    private const string DarkIconsDirectoryName = "Dark";
    private const string LightIconsDirectoryName = "Light";

    internal static string BrandingDirectory =>
        Path.Combine(AppContext.BaseDirectory, AssetsDirectoryName, BrandingDirectoryName);

    internal static string DevicesDirectory =>
        Path.Combine(AppContext.BaseDirectory, AssetsDirectoryName, DevicesDirectoryName);

    internal static string CommonIconsDirectory =>
        Path.Combine(AppContext.BaseDirectory, AssetsDirectoryName, IconsDirectoryName, CommonIconsDirectoryName);

    internal static string GetBrandingAssetPath(string fileName) =>
        Path.Combine(BrandingDirectory, fileName);

    internal static string GetDeviceImagePath(string fileName) =>
        Path.Combine(DevicesDirectory, fileName);

    internal static string GetThemeIconsDirectory(bool darkMode) =>
        Path.Combine(
            AppContext.BaseDirectory,
            AssetsDirectoryName,
            IconsDirectoryName,
            darkMode ? DarkIconsDirectoryName : LightIconsDirectoryName);

    internal static string GetThemeIconPath(bool darkMode, string fileName) =>
        Path.Combine(GetThemeIconsDirectory(darkMode), fileName);
}
