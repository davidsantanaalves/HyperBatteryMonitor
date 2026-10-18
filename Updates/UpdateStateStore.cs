using System.Security.Cryptography;
using System.Text.Json;

namespace HyperXBatteryTray.Updates;

/// <summary>
/// Stores one downloaded installer and its reminder state separately from settings.json.
/// Installed releases and user preferences must not be changed by this component.
/// </summary>
internal sealed class UpdateStateStore
{
    private const string UpdatesDirectory = "Updates";
    private const string StateFileName = "downloaded-update.json";
    private const string InstallerPrefix = "HyperBatteryMonitor-Setup-v";
    private const string InstallerSuffix = ".exe";
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _cacheDirectory;
    private readonly string _stateFile;
    private readonly object _gate = new();

    public UpdateStateStore()
        : this(ApplicationRuntime.UserDataDirectory)
    {
    }

    // An explicit parent directory makes it possible to test without touching user data.
    internal UpdateStateStore(string applicationDataDirectory)
    {
        _cacheDirectory = Path.Combine(applicationDataDirectory, UpdatesDirectory);
        _stateFile = Path.Combine(_cacheDirectory, StateFileName);
    }

    internal string CacheDirectory => _cacheDirectory;

    /// <summary>
    /// Removes abandoned, GUID-named partial transfers after an interrupted run.
    /// Never deletes persisted update metadata or a completed installer.
    /// Files locked by an active downloader are left untouched.
    /// </summary>
    internal void CleanupAbandonedDownloads()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
                return;

            foreach (string path in Directory.EnumerateFiles(_cacheDirectory, "*.download",
                         SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(path);
                // UpdateDownloader uses exactly .{32-character-hex-guid}.download.
                if (name.Length != 42 || name[0] != '.' ||
                    !name.EndsWith(".download", StringComparison.Ordinal) ||
                    !name.Skip(1).Take(32).All(Uri.IsHexDigit))
                    continue;

                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        continue;

                    // FileShare.None in the downloader prevents deleting an active transfer.
                    using (FileStream probe = new(path, FileMode.Open, FileAccess.Read,
                               FileShare.None)) { }
                    TryDeleteFile(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Removes a prior version's cached Setup executable left behind by a clean
    /// installation started by the Beta 5 updater. Only runs without persisted
    /// update metadata; a currently running installer may stay locked until the
    /// next launch. Unrelated files and valid downloads are never touched.
    /// </summary>
    internal void CleanupOrphanedInstallersAfterCleanInstall()
    {
        try
        {
            if (File.Exists(_stateFile) || !Directory.Exists(_cacheDirectory) ||
                (File.GetAttributes(_cacheDirectory) & FileAttributes.ReparsePoint) != 0)
                return;

            foreach (string file in Directory.EnumerateFiles(_cacheDirectory,
                         InstallerPrefix + "*" + InstallerSuffix, SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (!name.StartsWith(InstallerPrefix, StringComparison.Ordinal) ||
                    !name.EndsWith(InstallerSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string versionText = name.Substring(InstallerPrefix.Length,
                    name.Length - InstallerPrefix.Length - InstallerSuffix.Length);
                if (!ReleaseVersion.TryParse(versionText, out ReleaseVersion version) ||
                    !string.Equals(name, GetInstallerFileName(version), StringComparison.Ordinal) ||
                    (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    continue;

                TryDeleteFile(file); // In-use Setup files remain until a later run.
            }

            if (!Directory.EnumerateFileSystemEntries(_cacheDirectory).Any())
                Directory.Delete(_cacheDirectory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static string GetInstallerFileName(ReleaseVersion version) =>
        InstallerPrefix + version + InstallerSuffix;

    internal string GetInstallerPath(DownloadedUpdate update)
    {
        if (!TryValidateMetadata(update, out ReleaseVersion version))
            throw new InvalidDataException("Invalid persisted update metadata.");

        return Path.Combine(_cacheDirectory, GetInstallerFileName(version));
    }

    public DownloadedUpdate? Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_stateFile))
                    return null;

                DownloadedUpdate? update = JsonSerializer.Deserialize<DownloadedUpdate>(
                    File.ReadAllText(_stateFile), JsonOptions);
                return update is not null && TryValidateMetadata(update, out _) ? update : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                    or JsonException or NotSupportedException)
            {
                return null;
            }
        }
    }

    public void SaveDownloaded(DownloadedUpdate update)
    {
        if (!TryValidateMetadata(update, out ReleaseVersion version))
            throw new ArgumentException("Invalid downloaded update metadata.", nameof(update));

        lock (_gate)
        {
            DownloadedUpdate? old = Load();
            Directory.CreateDirectory(_cacheDirectory);

            string temporary = _stateFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(update, JsonOptions));
                File.Move(temporary, _stateFile, overwrite: true);
            }
            finally
            {
                TryDeleteFile(temporary);
            }

            if (old is null || old.Tag != update.Tag || old.Sha256 != update.Sha256 ||
                old.SizeBytes != update.SizeBytes)
                ClearInstallationResult();

            if (old is not null && TryValidateMetadata(old, out ReleaseVersion oldVersion)
                && oldVersion.CompareTo(version) != 0)
            {
                // The replacement has already been committed; a failed cleanup is harmless.
                TryDeleteFile(GetInstallerPath(old));
            }
        }
    }

    /// <summary>
    /// Returns a cached installer only after checking its complete size and SHA-256.
    /// No download or network request is performed here.
    /// </summary>
    public async Task<DownloadedUpdate?> GetVerifiedAsync(CancellationToken cancellationToken = default)
    {
        DownloadedUpdate? update = Load();
        if (update is null)
            return null;

        string path = GetInstallerPath(update);
        try
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length != update.SizeBytes ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                DiscardIfCurrent(update);
                return null;
            }

            byte[] hash;
            await using (FileStream stream = new(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            if (!string.Equals(Convert.ToHexStringLower(hash), update.Sha256,
                    StringComparison.Ordinal))
            {
                DiscardIfCurrent(update);
                return null;
            }

            // A different update might have been saved while the file was being hashed.
            return Load() is { } current && current.Tag == update.Tag &&
                   current.Sha256 == update.Sha256 &&
                   current.SizeBytes == update.SizeBytes ? current : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<DownloadedUpdate?> GetEligibleAsync(
        string installedVersion, UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        if (!ReleaseVersion.TryParse(installedVersion, out ReleaseVersion installed))
            throw new ArgumentException("Invalid installed version.", nameof(installedVersion));
        if (channel is not (UpdateChannel.Stable or UpdateChannel.Beta))
            throw new ArgumentOutOfRangeException(nameof(channel));

        DownloadedUpdate? update = Load();
        if (update is null)
            return null;

        _ = ReleaseVersion.TryParse(update.Tag, out ReleaseVersion cachedVersion);
        if (cachedVersion.CompareTo(installed) <= 0 ||
            (channel == UpdateChannel.Stable && cachedVersion.IsPrerelease) ||
            (cachedVersion.IsPrerelease && !IsBeta(cachedVersion)))
        {
            DiscardIfCurrent(update);
            return null;
        }

        return await GetVerifiedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The external launcher records failures when the main application is not
    /// running. This marker is advisory UI state, never proof of install success.
    /// </summary>
    internal bool HasInstallationFailure()
    {
        try
        {
            string file = Path.Combine(_cacheDirectory, "install-result.txt");
            return File.Exists(file) && string.Equals(File.ReadAllText(file).Trim(),
                "failed", StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal void ClearInstallationResult()
    {
        try { File.Delete(Path.Combine(_cacheDirectory, "install-result.txt")); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void DeferInstallation(DateTimeOffset now)
    {
        UpdateReminder(now, defer: true);
    }

    public void MarkReminderShown(DateTimeOffset now)
    {
        UpdateReminder(now, defer: false);
    }

    public bool ShouldRemindOnStartup() => Load() is { InstallationDeferred: true };

    public bool ShouldRemindOnSchedule(DateTimeOffset now) =>
        Load() is { InstallationDeferred: true, NextReminderAtUtc: { } due } &&
        now.ToUniversalTime() >= due;

    /// <summary>Forget a downloaded installer, for example after successful upgrade.</summary>
    public void Discard()
    {
        lock (_gate)
        {
            DownloadedUpdate? update = Load();
            if (File.Exists(_stateFile))
                File.Delete(_stateFile);
            if (update is not null)
                TryDeleteFile(GetInstallerPath(update));
            ClearInstallationResult();
        }
    }

    private void UpdateReminder(DateTimeOffset now, bool defer)
    {
        lock (_gate)
        {
            DownloadedUpdate? current = Load();
            if (current is null || (!defer && !current.InstallationDeferred))
                return;

            SaveDownloaded(current with
            {
                InstallationDeferred = true,
                NextReminderAtUtc = now.ToUniversalTime().Add(ReminderInterval)
            });
        }
    }

    private void DiscardIfCurrent(DownloadedUpdate update)
    {
        lock (_gate)
        {
            DownloadedUpdate? current = Load();
            if (current is not null && current.Tag == update.Tag &&
                current.Sha256 == update.Sha256)
            {
                Discard();
            }
        }
    }

    private static bool TryValidateMetadata(DownloadedUpdate update, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(update.Tag) ||
            !ReleaseVersion.TryParse(update.Tag, out version) ||
            string.IsNullOrWhiteSpace(update.Sha256) || update.Sha256.Length != 64 ||
            update.SizeBytes <= 0 || update.SizeBytes > UpdateDownloader.MaximumInstallerBytes ||
            update.DownloadedAtUtc == default ||
            (update.InstallationDeferred != update.NextReminderAtUtc.HasValue))
            return false;

        foreach (char c in update.Sha256)
        {
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }
        return true;
    }

    private static bool IsBeta(ReleaseVersion version) =>
        version.Prerelease is "beta" ||
        (version.Prerelease?.StartsWith("beta.", StringComparison.Ordinal) ?? false);

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
