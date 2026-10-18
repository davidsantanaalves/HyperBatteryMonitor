using System.Globalization;
using System.Text.RegularExpressions;

namespace HyperXBatteryTray.Updates;

/// <summary>
/// Compares three-part Semantic Versioning versions, including prerelease identifiers.
/// Build metadata is accepted but deliberately ignored for precedence (SemVer 2.0.0).
/// </summary>
internal readonly record struct ReleaseVersion : IComparable<ReleaseVersion>
{
    private static readonly Regex VersionPattern = new(
        @"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private ReleaseVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }
    public bool IsPrerelease => Prerelease is not null;

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(text))
            return false;

        Match match = VersionPattern.Match(text);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
        {
            return false;
        }

        string? prerelease = match.Groups[4].Success ? match.Groups[4].Value : null;
        if (prerelease is not null)
        {
            foreach (string identifier in prerelease.Split('.'))
            {
                // SemVer disallows numeric prerelease identifiers with leading zeros.
                if (IsNumeric(identifier) && identifier.Length > 1 && identifier[0] == '0')
                    return false;
            }
        }

        version = new ReleaseVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;

        string[] left = Prerelease.Split('.');
        string[] right = other.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            result = CompareIdentifier(left[i], right[i]);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (Prerelease is null ? string.Empty : $"-{Prerelease}");

    private static int CompareIdentifier(string left, string right)
    {
        bool leftNumeric = IsNumeric(left);
        bool rightNumeric = IsNumeric(right);
        if (leftNumeric && rightNumeric)
        {
            // Compare arbitrarily long numeric identifiers without parsing them as integers.
            int lengthComparison = left.Length.CompareTo(right.Length);
            return lengthComparison != 0 ? lengthComparison : StringComparer.Ordinal.Compare(left, right);
        }
        if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
        return StringComparer.Ordinal.Compare(left, right);
    }

    private static bool IsNumeric(string value)
    {
        foreach (char character in value)
        {
            if (character < '0' || character > '9')
                return false;
        }
        return value.Length != 0;
    }
}
