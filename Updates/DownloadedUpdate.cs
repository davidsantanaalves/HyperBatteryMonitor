namespace HyperXBatteryTray.Updates;

/// <summary>
/// Verified installer metadata. Never treat metadata loaded from disk as proof that
/// the installer is still intact: rehash it before offering installation.
/// </summary>
internal sealed record DownloadedUpdate(
    string Tag,
    string Sha256,
    long SizeBytes,
    DateTimeOffset DownloadedAtUtc,
    bool InstallationDeferred,
    DateTimeOffset? NextReminderAtUtc);
