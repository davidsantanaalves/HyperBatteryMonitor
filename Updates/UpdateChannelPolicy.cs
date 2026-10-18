namespace HyperXBatteryTray.Updates;

/// <summary>
/// A channel selects which release family is desired. Upgrades obey SemVer;
/// the only supported downgrade is an explicit switch from an installed
/// prerelease to the latest eligible Stable release.
/// </summary>
internal static class UpdateChannelPolicy
{
    internal static bool IsStableRollback(
        ReleaseVersion installed, ReleaseVersion target, UpdateChannel channel) =>
        channel == UpdateChannel.Stable && installed.IsPrerelease &&
        !target.IsPrerelease && target.CompareTo(installed) < 0;

    internal static bool IsEligibleTarget(
        ReleaseVersion installed, ReleaseVersion target, UpdateChannel channel)
    {
        if (channel is not (UpdateChannel.Stable or UpdateChannel.Beta))
            return false;

        if (target.IsPrerelease &&
            (channel == UpdateChannel.Stable || !IsBeta(target)))
            return false;

        return target.CompareTo(installed) > 0 ||
               IsStableRollback(installed, target, channel);
    }

    private static bool IsBeta(ReleaseVersion version) =>
        version.Prerelease is "beta" ||
        (version.Prerelease?.StartsWith("beta.", StringComparison.Ordinal) ?? false);
}
