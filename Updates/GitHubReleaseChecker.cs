using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HyperXBatteryTray.Updates;

internal sealed record AvailableRelease(
    string Tag,
    ReleaseVersion Version,
    Uri DownloadUrl,
    Uri ReleasePageUrl,
    string Sha256,
    long SizeBytes,
    string? ReleaseNotes);

/// <summary>
/// Discovers installable updates only; does not download or execute installers.
/// The GitHub repository and installer filename follow the current v2.3.1 source.
/// </summary>
internal sealed class GitHubReleaseChecker
{

    private const string InstallerPrefix = "HyperBatteryMonitor-Setup-v";
    private const string InstallerSuffix = ".exe";



    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly HttpClient _httpClient;

    public GitHubReleaseChecker(HttpClient? httpClient = null) =>
        _httpClient = httpClient ?? SharedHttpClient;

    public async Task<AvailableRelease?> FindUpdateAsync(
        string installedVersion,
        UpdateChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (!ReleaseVersion.TryParse(installedVersion, out ReleaseVersion current))
            throw new ArgumentException("Invalid installed Semantic Versioning version.", nameof(installedVersion));
        if (channel is not (UpdateChannel.Stable or UpdateChannel.Beta))
            throw new ArgumentOutOfRangeException(nameof(channel));

        using HttpRequestMessage request = new(HttpMethod.Get, UpdateRepository.ApiUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Hyper-Battery-Monitor-Updater/2.4");

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The GitHub Releases API returned an unexpected response.");

        AvailableRelease? best = null;
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (!TryParseRelease(item, channel, out AvailableRelease? candidate) ||
                candidate is null)
                continue;

            if (best is null || candidate.Version.CompareTo(best.Version) > 0)
                best = candidate;
        }
        // Compare only after selecting the newest valid release within the chosen
        // channel. A Beta user switching to Stable may need an older stable build.
        return best is not null &&
            UpdateChannelPolicy.IsEligibleTarget(current, best.Version, channel)
            ? best : null;
    }

    private static bool TryParseRelease(
        JsonElement item,
        UpdateChannel channel,
        out AvailableRelease? release)
    {
        release = null;
        if (item.ValueKind != JsonValueKind.Object ||
            !TryGetBoolean(item, "draft", out bool draft) || draft ||
            !TryGetBoolean(item, "prerelease", out bool prerelease) ||
            !TryGetString(item, "tag_name", out string? tag) ||
            !ReleaseVersion.TryParse(tag, out ReleaseVersion version))
            return false;

        // Avoid a release accidentally marked Stable even though its tag is a prerelease,
        // or marked as a prerelease without a SemVer prerelease identifier.
        if (prerelease != version.IsPrerelease ||
            (channel == UpdateChannel.Stable && prerelease) ||
            (prerelease && channel == UpdateChannel.Beta &&
             !IsBetaVersion(version)))
            return false;

        if (!TryGetString(item, "html_url", out string? releaseUrl) ||
            !TryGetTrustedUrl(releaseUrl, UpdateRepository.ReleasePagePath, out Uri? pageUrl) ||
            !item.TryGetProperty("assets", out JsonElement assets) ||
            assets.ValueKind != JsonValueKind.Array)
            return false;

        string expectedInstaller = InstallerPrefix + version + InstallerSuffix;
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object ||
                !TryGetString(asset, "name", out string? name) ||
                !string.Equals(name, expectedInstaller, StringComparison.Ordinal) ||
                !TryGetString(asset, "state", out string? state) || state != "uploaded" ||
                !asset.TryGetProperty("size", out JsonElement sizeElement) ||
                !sizeElement.TryGetInt64(out long size) || size <= 0 ||
                !TryGetString(asset, "digest", out string? digest) ||
                !TryGetSha256(digest, out string? sha256) ||
                !TryGetString(asset, "browser_download_url", out string? downloadUrl) ||
                !TryGetTrustedUrl(downloadUrl, UpdateRepository.ReleaseDownloadPath, out Uri? installerUrl))
                continue;

            // Keep only assets belonging to the same release tag and exact expected installer.
            if (!installerUrl.AbsolutePath.EndsWith(
                    "/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name),
                    StringComparison.Ordinal))
                continue;

            // GitHub release descriptions are Markdown, not HTML. Keep a bounded
            // copy of the release body so the About page can display it safely.
            string? notes = null;
            if (item.TryGetProperty("body", out JsonElement body) &&
                body.ValueKind == JsonValueKind.String)
            {
                string? text = body.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    notes = text.Length > 65536 ? text[..65536] : text;
            }
            release = new AvailableRelease(tag, version, installerUrl, pageUrl, sha256, size, notes);
            return true;
        }
        return false;
    }

    // The Beta channel receives stable versions and explicitly named beta prereleases.
    // Alpha/RC labels require a separate product decision before being offered.
    private static bool IsBetaVersion(ReleaseVersion version) =>
        version.Prerelease is "beta" ||
        (version.Prerelease?.StartsWith("beta.", StringComparison.Ordinal) ?? false);

    private static bool TryGetBoolean(JsonElement element, string key, out bool result)
    {
        result = false;
        if (!element.TryGetProperty(key, out JsonElement value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        result = value.GetBoolean();
        return true;
    }

    private static bool TryGetString(JsonElement element, string key, [NotNullWhen(true)] out string? result)
    {
        result = null;
        return element.TryGetProperty(key, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(result = value.GetString());
    }

    private static bool TryGetSha256(string? digest, [NotNullWhen(true)] out string? sha256)
    {
        sha256 = null;
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            digest.Length != 71)
            return false;
        string hex = digest[7..];
        foreach (char character in hex)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }
        sha256 = hex.ToLowerInvariant();
        return true;
    }

    private static bool TryGetTrustedUrl(string? input, string expectedPathPrefix, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || parsed.Host != "github.com" ||
            !parsed.IsDefaultPort || parsed.UserInfo.Length != 0 ||
            parsed.Query.Length != 0 || parsed.Fragment.Length != 0 ||
            !parsed.AbsolutePath.StartsWith(expectedPathPrefix, StringComparison.Ordinal))
            return false;
        uri = parsed;
        return true;
    }

    private static HttpClient CreateHttpClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };
}
