using HyperXBatteryTray.Settings;

namespace HyperXBatteryTray.Updates;

internal enum UpdateStatus
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Downloaded,
    InstallationFailed,
    Error,
    StoreManaged
}

internal sealed record UpdateSnapshot(
    UpdateStatus Status, string? Version = null, int Percent = 0,
    bool IsStableRollback = false);

internal sealed record VerifiedInstaller(string Path, DownloadedUpdate Metadata);

/// <summary>
/// Coordinates discovery, downloads, persistence and noninteractive notifications.
/// The separate launcher is started by the application context only after user approval.
/// </summary>
internal sealed class UpdateCoordinator : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan MaximumTimerDelay = TimeSpan.FromDays(24);

    private readonly AppSettings _settings;
    private readonly GitHubReleaseChecker _checker = new();
    private readonly UpdateStateStore _store = new();
    private readonly UpdateDownloader _downloader;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly System.Threading.Timer? _checkTimer;
    private readonly System.Threading.Timer? _reminderTimer;
    private readonly string _installedVersion;
    private readonly bool _storeManaged;
    private AvailableRelease? _available;
    private UpdateChannel _activeChannel;
    private UpdateDownloadMode _activeDownloadMode;
    private string? _lastAvailableNotificationTag;
    private bool _started;
    private bool _disposed;
    private UpdateSnapshot _snapshot;

    internal event Action<UpdateSnapshot>? Changed;

    internal UpdateSnapshot Snapshot => _snapshot;
    // The release was already validated by GitHubReleaseChecker. Expose its
    // release notes without a second unauthenticated browser/web request.
    internal AvailableRelease? ReleaseForSnapshot =>
        _available is { } release &&
        string.Equals(release.Tag, _snapshot.Version, StringComparison.Ordinal)
            ? release
            : null;

    internal bool IsStoreManaged => _storeManaged;
    internal bool IsReminderScheduled => !_storeManaged && _store.Load()?.InstallationDeferred == true;

    internal UpdateCoordinator(AppSettings settings)
    {
        _settings = settings;
        _activeChannel = settings.UpdateChannel;
        _activeDownloadMode = settings.UpdateDownloadMode;
        _lifetimeToken = _shutdown.Token;
        _downloader = new UpdateDownloader(_store);
        _storeManaged = WindowsPackageDetector.IsPackaged();
        _installedVersion = AppReleaseInfo.SemanticVersion;
        _snapshot = new UpdateSnapshot(_storeManaged ? UpdateStatus.StoreManaged : UpdateStatus.Idle);

        if (!_storeManaged)
        {
            _checkTimer = new System.Threading.Timer(_state => { _ = CheckAsync(false); }, null,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _reminderTimer = new System.Threading.Timer(_state => { _ = RemindAsync(); }, null,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    internal void Start()
    {
        if (_started || _disposed || _storeManaged)
            return;
        _started = true;
        // Remove incomplete transfer files left by a previous hard termination.
        _store.CleanupAbandonedDownloads();
        // Beta 6: remove abandoned installer files left by the previous updater after clean installation.
        _store.CleanupOrphanedInstallersAfterCleanInstall();
        _checkTimer?.Change(CheckInterval, CheckInterval);
        _ = CheckAsync(false, startup: true);
    }

    internal void PreferencesChanged()
    {
        if (_disposed || _storeManaged || !_started ||
            (_activeChannel == _settings.UpdateChannel &&
             _activeDownloadMode == _settings.UpdateDownloadMode))
            return;

        bool channelChanged = _activeChannel != _settings.UpdateChannel;
        _activeChannel = _settings.UpdateChannel;
        _activeDownloadMode = _settings.UpdateDownloadMode;
        // Recheck on channel changes. Changing only the download preference to
        // automatic can use an already-validated release discovered this session.
        if (!channelChanged && _settings.UpdateDownloadMode == UpdateDownloadMode.DownloadAutomatically &&
            _snapshot.Status == UpdateStatus.Available &&
            !_snapshot.IsStableRollback && _available is not null)
            _ = DownloadAvailableAsync();
        else
            _ = CheckAsync(false);
    }

    internal async Task CheckAsync(bool manual, bool startup = false)
    {
        if (_disposed || _storeManaged)
            return;
        try { await _operationGate.WaitAsync(_lifetimeToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        try
        {
            if (_disposed) return;
            Publish(new UpdateSnapshot(UpdateStatus.Checking));
            UpdateChannel channel = _settings.UpdateChannel;
            DownloadedUpdate? cached = await _store.GetEligibleAsync(_installedVersion, channel,
                _lifetimeToken).ConfigureAwait(false);
            if (cached is not null)
            {
                if (startup && _store.HasInstallationFailure())
                {
                    UpdateNotificationService.InstallationFailed(cached.Tag, _settings.Language);
                    _store.DeferInstallation(DateTimeOffset.UtcNow);
                }
                Publish(CachedSnapshot(cached));
                if (startup && cached.InstallationDeferred && !_store.HasInstallationFailure())
                {
                    UpdateNotificationService.Reminder(cached.Tag, _settings.Language);
                    _store.MarkReminderShown(DateTimeOffset.UtcNow);
                }
                ArmReminder();
            }

            AvailableRelease? release;
            try
            {
                release = await _checker.FindUpdateAsync(_installedVersion, channel,
                    _lifetimeToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or
                System.Text.Json.JsonException or InvalidDataException or TaskCanceledException)
            {
                Publish(cached is not null
                    ? CachedSnapshot(cached)
                    : new UpdateSnapshot(UpdateStatus.Error));
                return;
            }

            if (_disposed) return;
            _available = release;
            if (release is null)
            {
                Publish(cached is not null
                    ? CachedSnapshot(cached)
                    : new UpdateSnapshot(UpdateStatus.UpToDate));
                return;
            }

            if (cached is not null && ReleaseVersion.TryParse(cached.Tag, out ReleaseVersion cachedVersion) &&
                release.Version.CompareTo(cachedVersion) <= 0)
            {
                Publish(CachedSnapshot(cached));
                return;
            }

            bool isStableRollback = IsStableRollback(release.Version, channel);
            // Never download an older Stable automatically. A channel switch that
            // lowers the installed version always requires deliberate consent.
            if (!isStableRollback &&
                _settings.UpdateDownloadMode == UpdateDownloadMode.DownloadAutomatically)
            {
                await DownloadCoreAsync(release).ConfigureAwait(false);
                return;
            }

            Publish(new UpdateSnapshot(UpdateStatus.Available, release.Tag, 0, isStableRollback));
            if (!isStableRollback && !manual && _lastAvailableNotificationTag != release.Tag)
            {
                _lastAvailableNotificationTag = release.Tag;
                UpdateNotificationService.Available(release.Tag, _settings.Language);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Shutdown cancels network I/O and hashing without a user-visible error.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            HttpRequestException or InvalidDataException or OperationCanceledException)
        {
            Publish(new UpdateSnapshot(UpdateStatus.Error));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal async Task DownloadAvailableAsync()
    {
        if (_disposed || _storeManaged)
            return;
        try { await _operationGate.WaitAsync(_lifetimeToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        try
        {
            AvailableRelease? release = _available;
            if (release is null)
                return;
            if (!ReleaseVersion.TryParse(_installedVersion, out ReleaseVersion installed) ||
                !UpdateChannelPolicy.IsEligibleTarget(installed, release.Version,
                    _settings.UpdateChannel))
                return;
            await DownloadCoreAsync(release).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            HttpRequestException or InvalidDataException or OperationCanceledException)
        {
            Publish(new UpdateSnapshot(UpdateStatus.Error));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal void RemindLater()
    {
        if (_disposed || _storeManaged ||
            _snapshot.Status is not (UpdateStatus.Downloaded or UpdateStatus.InstallationFailed))
            return;
        try
        {
            _store.DeferInstallation(DateTimeOffset.UtcNow);
            ArmReminder();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Serialize the final cache validation with update checks/downloads. The caller
    /// launches the helper and then performs the normal WinForms ExitThread().
    /// </summary>
    internal async Task<VerifiedInstaller?> PrepareInstallationAsync()
    {
        if (_disposed || _storeManaged || !ApplicationRuntime.CanInstallUpdates)
            return null;
        try { await _operationGate.WaitAsync(_lifetimeToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
        try
        {
            DownloadedUpdate? update = await _store.GetEligibleAsync(_installedVersion,
                _settings.UpdateChannel, _lifetimeToken).ConfigureAwait(false);
            return update is null ? null : new VerifiedInstaller(_store.GetInstallerPath(update), update);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or OperationCanceledException)
        {
            Publish(new UpdateSnapshot(UpdateStatus.Error));
            return null;
        }
        finally { _operationGate.Release(); }
    }

    internal void ClearInstallationFailure() => _store.ClearInstallationResult();

    private bool IsStableRollback(ReleaseVersion target, UpdateChannel channel) =>
        ReleaseVersion.TryParse(_installedVersion, out ReleaseVersion installed) &&
        UpdateChannelPolicy.IsStableRollback(installed, target, channel);

    private UpdateSnapshot CachedSnapshot(DownloadedUpdate update) =>
        new(_store.HasInstallationFailure() ? UpdateStatus.InstallationFailed :
            UpdateStatus.Downloaded, update.Tag, 0,
            ReleaseVersion.TryParse(update.Tag, out ReleaseVersion target) &&
            IsStableRollback(target, _settings.UpdateChannel));

    private async Task DownloadCoreAsync(AvailableRelease release)
    {
        bool isStableRollback = IsStableRollback(release.Version, _settings.UpdateChannel);
        Publish(new UpdateSnapshot(UpdateStatus.Downloading, release.Tag, 0, isStableRollback));
        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            int percent = p.TotalBytes <= 0 ? 0 :
                (int)Math.Clamp(p.DownloadedBytes * 100L / p.TotalBytes, 0, 100);
            if (_snapshot.Status == UpdateStatus.Downloading && _snapshot.Version == release.Tag)
                Publish(new UpdateSnapshot(UpdateStatus.Downloading, release.Tag, percent,
                    isStableRollback));
        });
        DownloadedUpdate downloaded = await _downloader.DownloadAsync(release, progress,
            _lifetimeToken).ConfigureAwait(false);
        if (_disposed) return;
        Publish(new UpdateSnapshot(UpdateStatus.Downloaded, downloaded.Tag, 0,
            isStableRollback));
        UpdateNotificationService.Downloaded(downloaded.Tag, _settings.Language);
        ArmReminder();
    }

    private async Task RemindAsync()
    {
        if (_disposed || _storeManaged) return;
        try
        {
            DownloadedUpdate? update = await _store.GetEligibleAsync(_installedVersion,
                _settings.UpdateChannel, _lifetimeToken).ConfigureAwait(false);
            if (update is not null && _store.ShouldRemindOnSchedule(DateTimeOffset.UtcNow))
            {
                UpdateNotificationService.Reminder(update.Tag, _settings.Language);
                _store.MarkReminderShown(DateTimeOffset.UtcNow);
            }
            ArmReminder();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ArmReminder()
    {
        if (_disposed || _storeManaged) return;
        DownloadedUpdate? update = _store.Load();
        TimeSpan due = update?.NextReminderAtUtc is DateTimeOffset next
            ? next - DateTimeOffset.UtcNow
            : Timeout.InfiniteTimeSpan;
        if (due != Timeout.InfiniteTimeSpan)
        {
            if (due < TimeSpan.Zero) due = TimeSpan.Zero;
            if (due > MaximumTimerDelay) due = MaximumTimerDelay;
        }
        _reminderTimer?.Change(due, Timeout.InfiniteTimeSpan);
    }

    private void Publish(UpdateSnapshot snapshot)
    {
        if (_disposed) return;
        _snapshot = snapshot;
        Changed?.Invoke(snapshot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        _checkTimer?.Dispose();
        _reminderTimer?.Dispose();
        _shutdown.Dispose();
        // In-flight tasks hold the gate; do not dispose it during cancellation.
    }
}
