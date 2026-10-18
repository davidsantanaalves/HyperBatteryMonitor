using System.Buffers;
using System.Net;
using System.Security.Cryptography;

namespace HyperXBatteryTray.Updates;

internal readonly record struct UpdateDownloadProgress(long DownloadedBytes, long TotalBytes);

/// <summary>
/// Downloads only the exact GitHub installer selected by GitHubReleaseChecker.
/// A completed download is published to the state store only after size/hash checks.
/// No installer is executed by this class.
/// </summary>
internal sealed class UpdateDownloader
{
    internal const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private readonly HttpClient _httpClient;
    private readonly UpdateStateStore _stateStore;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public UpdateDownloader(UpdateStateStore stateStore, HttpClient? httpClient = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<DownloadedUpdate> DownloadAsync(
        AvailableRelease release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ValidateRelease(release);
        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DownloadedUpdate? metadata = _stateStore.Load();
            if (metadata is not null && metadata.Tag == release.Tag &&
                metadata.Sha256 == release.Sha256 && metadata.SizeBytes == release.SizeBytes)
            {
                DownloadedUpdate? cached = await _stateStore.GetVerifiedAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (cached is not null)
                    return cached;
            }

            Directory.CreateDirectory(_stateStore.CacheDirectory);
            string destination = Path.Combine(_stateStore.CacheDirectory,
                UpdateStateStore.GetInstallerFileName(release.Version));
            string temporary = Path.Combine(_stateStore.CacheDirectory,
                "." + Guid.NewGuid().ToString("N") + ".download");

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(DownloadTimeout);
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, release.DownloadUrl);
                request.Headers.UserAgent.ParseAdd("Hyper-Battery-Monitor-Updater/2.4");
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new HttpRequestException("The installer endpoint did not return a full download.");
                if (response.Content.Headers.ContentLength is long length &&
                    length != release.SizeBytes)
                    throw new InvalidDataException("Installer Content-Length does not match the release asset.");

                await using Stream source = await response.Content.ReadAsStreamAsync(timeout.Token)
                    .ConfigureAwait(false);
                using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
                long total = 0;
                try
                {
                    await using FileStream target = new(temporary, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 131072,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    int read;
                    while ((read = await source.ReadAsync(buffer.AsMemory(), timeout.Token)
                                   .ConfigureAwait(false)) != 0)
                    {
                        total += read;
                        if (total > release.SizeBytes)
                            throw new InvalidDataException("Installer data exceeds the release asset size.");
                        hasher.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token)
                            .ConfigureAwait(false);
                        progress?.Report(new UpdateDownloadProgress(total, release.SizeBytes));
                    }
                    await target.FlushAsync(timeout.Token).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                if (total != release.SizeBytes ||
                    !string.Equals(Convert.ToHexStringLower(hasher.GetHashAndReset()),
                        release.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Installer SHA-256/size validation failed.");

                File.Move(temporary, destination, overwrite: true);
                DownloadedUpdate downloaded = new(release.Tag, release.Sha256,
                    release.SizeBytes, DateTimeOffset.UtcNow, false, null);
                _stateStore.SaveDownloaded(downloaded);
                return downloaded;
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private static void ValidateRelease(AvailableRelease release)
    {
        if (release.SizeBytes is <= 0 or > MaximumInstallerBytes ||
            !ReleaseVersion.TryParse(release.Tag, out ReleaseVersion tagVersion) ||
            tagVersion.CompareTo(release.Version) != 0 ||
            release.Sha256.Length != 64 ||
            release.Sha256.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("The release metadata is invalid.", nameof(release));

        string expectedName = UpdateStateStore.GetInstallerFileName(release.Version);
        string expectedPath = UpdateRepository.ReleaseDownloadPath +
            Uri.EscapeDataString(release.Tag) + "/" + Uri.EscapeDataString(expectedName);
        Uri url = release.DownloadUrl;
        if (url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com" ||
            !url.IsDefaultPort || url.UserInfo.Length != 0 ||
            url.Query.Length != 0 || url.Fragment.Length != 0 ||
            !string.Equals(url.AbsolutePath, expectedPath, StringComparison.Ordinal))
            throw new ArgumentException("Untrusted installer URL.", nameof(release));
    }
}
