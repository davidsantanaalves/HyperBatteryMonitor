using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Updates;

namespace HyperXBatteryTray.Settings;

public sealed class SettingsManager
{
    private const string LegacySettingsDirectoryName = "HyperXBatteryTray";
    private const string SettingsFileName = "settings.json";
    private const string BatteryHistoryFileName = "battery-history.json";
    private const string TemporaryFileSuffix = ".tmp";
    private const string MigrationTemporaryFileSuffix = ".migration.tmp";

    private readonly string _settingsDirectory;
    private readonly string _settingsFile;
    private readonly string _batteryHistoryFile;
    private readonly string _legacySettingsDirectory;
    private readonly string _legacySettingsFile;
    private readonly string _legacyBatteryHistoryFile;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsManager()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        _settingsDirectory = ApplicationRuntime.UserDataDirectory;

        _settingsFile = Path.Combine(
            _settingsDirectory,
            SettingsFileName);

        _batteryHistoryFile = Path.Combine(
            _settingsDirectory,
            BatteryHistoryFileName);

        _legacySettingsDirectory = Path.Combine(
            localApplicationData,
            LegacySettingsDirectoryName);

        _legacySettingsFile = Path.Combine(
            _legacySettingsDirectory,
            SettingsFileName);

        _legacyBatteryHistoryFile = Path.Combine(
            _legacySettingsDirectory,
            BatteryHistoryFileName);
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFile))
            {
                AppSettings defaults = AppSettings.CreateDefault();
                defaults.Language = DetectWindowsLanguage();
                defaults.IsNewSettingsProfile = true;

                try
                {
                    Save(defaults);
                }
                catch
                {
                    // Keep the detected language for this session even if persistence fails.
                }

                return defaults;
            }

            string json = File.ReadAllText(_settingsFile);

            AppSettings? settings =
                JsonSerializer.Deserialize<AppSettings>(
                    json,
                    JsonOptions);

            if (settings == null)
                return AppSettings.CreateDefault();

            // Older versions always saved UpdateChannel, even when it was just
            // the default. We cannot identify an earlier manual choice, so
            // preserve an existing legacy value rather than silently overwrite it.
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    !document.RootElement.TryGetProperty(nameof(AppSettings.UpdateChannelUserSelected), out _) &&
                    document.RootElement.TryGetProperty(nameof(AppSettings.UpdateChannel), out _))
                {
                    settings.UpdateChannelUserSelected = true;
                }
            }

            Normalize(settings);

            return settings;
        }
        catch
        {
            return AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);

        Directory.CreateDirectory(_settingsDirectory);

        string json = JsonSerializer.Serialize(
            settings,
            JsonOptions);

        string temporaryFile =
            _settingsFile + TemporaryFileSuffix;

        File.WriteAllText(
            temporaryFile,
            json);

        File.Move(
            temporaryFile,
            _settingsFile,
            overwrite: true);
    }

    public BatteryHistoryData LoadBatteryHistory()
    {
        try
        {
            if (!File.Exists(_batteryHistoryFile))
                return BatteryHistoryData.CreateCurrent();

            string json = File.ReadAllText(_batteryHistoryFile);

            BatteryHistoryData? history =
                JsonSerializer.Deserialize<BatteryHistoryData>(
                    json,
                    JsonOptions);

            if (history?.Devices == null ||
                history.Version != BatteryHistoryData.CurrentVersion)
            {
                return ResetBatteryHistory();
            }

            return history;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Could not load battery history; resetting it: {ex.Message}");
            return ResetBatteryHistory();
        }
    }

    public void SaveBatteryHistory(BatteryHistoryData history)
    {
        history.Version = BatteryHistoryData.CurrentVersion;

        Directory.CreateDirectory(_settingsDirectory);

        string json = JsonSerializer.Serialize(
            history,
            JsonOptions);

        string temporaryFile =
            _batteryHistoryFile + TemporaryFileSuffix;

        File.WriteAllText(
            temporaryFile,
            json);

        File.Move(
            temporaryFile,
            _batteryHistoryFile,
            overwrite: true);
    }

    public void MigrateLegacyUserData()
    {
        if (ApplicationRuntime.IsTestMode)
            return; // Never read, migrate or delete production legacy data in test mode.

        if (!Directory.Exists(_legacySettingsDirectory) ||
            PathsEqual(_legacySettingsDirectory, _settingsDirectory))
        {
            return;
        }

        Directory.CreateDirectory(_settingsDirectory);

        MigrateJsonFile(
            _legacySettingsFile,
            _settingsFile,
            IsValidSettingsFile);

        MigrateJsonFile(
            _legacyBatteryHistoryFile,
            _batteryHistoryFile,
            IsValidBatteryHistoryFile);

        DeleteFileIfExists(
            _legacySettingsFile + TemporaryFileSuffix);
        DeleteFileIfExists(
            _legacyBatteryHistoryFile + TemporaryFileSuffix);

        DeleteFileIfExists(_legacySettingsFile);
        DeleteFileIfExists(_legacyBatteryHistoryFile);

        if (!Directory.EnumerateFileSystemEntries(
                _legacySettingsDirectory).Any())
        {
            Directory.Delete(_legacySettingsDirectory);
        }
    }

    /// <summary>
    /// Removes current and legacy user profiles. Returns true only when an active,
    /// version-matched Inno installer in the old update cache remains temporarily
    /// locked by Windows. All other user data must still have been removed.
    /// </summary>
    public bool DeleteUserData()
    {
        bool lockedInstallerPendingCleanup = false;
        try
        {
            DeleteDirectoryIfExists(_settingsDirectory);
        }
        catch (IOException)
        {
            // Beta 5 launched Setup directly from this cache. Windows holds the
            // running .exe open until Setup exits. Never suppress any unrelated
            // deletion failure, and never preserve settings or battery history.
            bool? recovered = TryRemoveProfileExceptActiveInstaller();
            if (!recovered.HasValue)
                throw;
            lockedInstallerPendingCleanup = recovered.Value;
        }

        if (ApplicationRuntime.IsTestMode)
            return lockedInstallerPendingCleanup;

        if (!PathsEqual(_legacySettingsDirectory, _settingsDirectory))
            DeleteDirectoryIfExists(_legacySettingsDirectory);

        return lockedInstallerPendingCleanup;
    }

    // null: not a recognized cached installer; false: fully removed after retry;
    // true: only the running, locked setup file remains.
    private bool? TryRemoveProfileExceptActiveInstaller()
    {
        // Only the exact setup file for the version being installed may survive.
        // Do not allow an arbitrary executable or user-supplied file to bypass
        // Clean Installation. InformationalVersion is embedded at build time.
        string? versionText = typeof(SettingsManager).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!ReleaseVersion.TryParse(versionText, out ReleaseVersion version))
            return null;

        string cacheDir = Path.Combine(_settingsDirectory, "Updates");
        string installer = Path.Combine(cacheDir, UpdateStateStore.GetInstallerFileName(version));
        if (!Directory.Exists(cacheDir) || !File.Exists(installer))
            return null;

        // This fallback is never allowed through directory links/junctions or
        // reparse-point files, even if they appear to have the correct names.
        FileAttributes rootAttributes = File.GetAttributes(_settingsDirectory);
        FileAttributes cacheAttributes = File.GetAttributes(cacheDir);
        FileAttributes installerAttributes = File.GetAttributes(installer);
        if ((rootAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) !=
                FileAttributes.Directory ||
            (cacheAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) !=
                FileAttributes.Directory ||
            (installerAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            return null;

        // Only ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION justify keeping
        // this one .exe; permissions problems and all other I/O errors still fail.
        try
        {
            File.Delete(installer);
            DeleteDirectoryIfExists(_settingsDirectory);
            return false; // The installer was removable: no deferred residue.
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            // Expected for an Inno Setup executable still running from cache.
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(_settingsDirectory).ToArray())
        {
            if (PathsEqual(entry, cacheDir))
            {
                foreach (string cachedEntry in Directory.EnumerateFileSystemEntries(cacheDir).ToArray())
                {
                    if (PathsEqual(cachedEntry, installer))
                        continue;

                    DeleteProfileEntry(cachedEntry);
                }
            }
            else
            {
                DeleteProfileEntry(entry);
            }
        }

        // Verify that the locked, exact-version installer is literally the only
        // remaining entry. A partial clean must never be reported as success.
        string[] rootEntries = Directory.GetFileSystemEntries(_settingsDirectory);
        string[] cacheEntries = Directory.GetFileSystemEntries(cacheDir);
        if (rootEntries.Length != 1 || !PathsEqual(rootEntries[0], cacheDir) ||
            cacheEntries.Length != 1 || !PathsEqual(cacheEntries[0], installer) ||
            !File.Exists(installer))
        {
            throw new IOException(
                "Clean installation left unexpected files beyond the running installer.");
        }

        return true;
    }

    private static void DeleteProfileEntry(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to follow a reparse point during profile cleanup: '{path}'.");

        if ((attributes & FileAttributes.Directory) != 0)
            DeleteDirectoryIfExists(path);
        else
            File.Delete(path);
    }

    private static void MigrateJsonFile(
        string sourcePath,
        string destinationPath,
        Func<string, bool> validator)
    {
        if (!File.Exists(sourcePath))
            return;

        if (File.Exists(destinationPath) && validator(destinationPath))
            return;

        string migrationTemporaryFile =
            destinationPath + MigrationTemporaryFileSuffix;

        DeleteFileIfExists(migrationTemporaryFile);

        try
        {
            File.Copy(
                sourcePath,
                migrationTemporaryFile,
                overwrite: true);

            if (!validator(migrationTemporaryFile))
            {
                throw new InvalidDataException(
                    $"Legacy user data is invalid and was preserved at '{sourcePath}'.");
            }

            File.Move(
                migrationTemporaryFile,
                destinationPath,
                overwrite: true);
        }
        finally
        {
            DeleteFileIfExists(migrationTemporaryFile);
        }
    }

    private static bool IsValidSettingsFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(
                json,
                JsonOptions) != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidBatteryHistoryFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            BatteryHistoryData? history =
                JsonSerializer.Deserialize<BatteryHistoryData>(
                    json,
                    JsonOptions);

            return history?.Devices != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(
        string firstPath,
        string secondPath)
    {
        return string.Equals(
            Path.GetFullPath(firstPath)
                .TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(secondPath)
                .TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        // Directory.Exists returns false for certain access-denied errors.
        // Reading attributes distinguishes a missing directory from a directory
        // that exists but cannot be inspected by the current user.
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                $"Refusing to delete unexpected or reparse-point user data path: '{path}'.");
        }

        // Background processes can briefly hold the settings/history files.
        // Retry transient sharing violations, but never silently ignore a
        // persistent I/O or permissions failure.
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);

                if (Directory.Exists(path))
                    throw new IOException($"Directory still exists after deletion: '{path}'.");

                return;
            }
            catch (DirectoryNotFoundException)
            {
                // Another process removed the directory after our initial check.
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                System.Threading.Thread.Sleep(200 * attempt);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Could not complete user data removal at '{path}' " +
                    $"(attempt {attempt}/{maxAttempts}).", ex);
            }
        }
    }

    private BatteryHistoryData ResetBatteryHistory()
    {
        BatteryHistoryData history = BatteryHistoryData.CreateCurrent();

        try
        {
            SaveBatteryHistory(history);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Could not persist the reset battery history: {ex.Message}");
        }

        return history;
    }

    private static AppLanguage DetectWindowsLanguage()
    {
        return Localization.DetectLanguage(CultureInfo.CurrentUICulture);
    }

    private static void Normalize(AppSettings settings)
    {
        if (settings.SelectedDevice == null)
            settings.SelectedDevice = string.Empty;

        if (!string.IsNullOrWhiteSpace(settings.SelectedDevice))
        {
            settings.SelectedDevice =
                HyperXDeviceManager.NormalizeSupportedDeviceName(
                    settings.SelectedDevice);
        }

        if (!string.IsNullOrWhiteSpace(settings.SelectedDevice) &&
            !HyperXDeviceManager.IsSupportedDeviceName(settings.SelectedDevice))
        {
            settings.SelectedDevice = string.Empty;
        }

        settings.GradientPercent =
            Math.Clamp(
                settings.GradientPercent,
                0,
                50);

        settings.CriticalBatteryPercent =
            Math.Clamp(
                settings.CriticalBatteryPercent,
                1,
                100);

        if (settings.BatteryColors == null ||
            settings.BatteryColors.Count != 3)
        {
            settings.BatteryColors =
                AppSettings.CreateDefault().BatteryColors;
        }

        foreach (BatteryColorSettings color
                 in settings.BatteryColors)
        {
            color.MinimumPercent =
                Math.Clamp(
                    color.MinimumPercent,
                    0,
                    100);
        }

        settings.BatteryColors =
            settings.BatteryColors
                .OrderByDescending(
                    color => color.MinimumPercent)
                .ToList();

        if (!Enum.IsDefined(settings.Language))
            settings.Language = AppLanguage.English;

        if (!Enum.IsDefined(settings.Theme))
            settings.Theme = AppTheme.System;

        if (!Enum.IsDefined(settings.DisplayMode))
            settings.DisplayMode = BatteryDisplayMode.StaticIcon;

        if (!Enum.IsDefined(settings.AdvancedDisplayMode))
            settings.AdvancedDisplayMode = AdvancedDisplayMode.BatteryGradient;

        if (!Enum.IsDefined(settings.UpdateChannel))
            settings.UpdateChannelUserSelected = false;

        if (!settings.UpdateChannelUserSelected)
            settings.UpdateChannel = AppReleaseInfo.DefaultUpdateChannel;

        if (!Enum.IsDefined(settings.UpdateDownloadMode))
            settings.UpdateDownloadMode = UpdateDownloadMode.NotifyBeforeDownload;
    }
}
