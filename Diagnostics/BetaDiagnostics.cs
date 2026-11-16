using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace HyperXBatteryTray.Diagnostics;

/// <summary>Opt-in, local-only, bounded diagnostics for compiled Beta releases.</summary>
internal static class BetaDiagnostics
{
    internal const int ConsentVersion = 3;
    private const long MaxFileBytes = 10L * 1024 * 1024;
    private const long MaxDirectoryBytes = 100L * 1024 * 1024;
    private const int RetentionDays = 14;
    private static readonly object Gate = new();
    private static Channel<string>? _channel;
    private static Task? _worker;
    private static string? _folder;
    private static long _droppedEntries;

    internal static bool IsEnabled => Volatile.Read(ref _channel) != null;

    internal static string LogsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HBM Logs");

    internal static void Start()
    {
        if (!AppReleaseInfo.IsBeta) return;
        lock (Gate)
        {
            if (_channel != null) return;
            try
            {
                _folder = LogsFolder;
                Directory.CreateDirectory(_folder);
                Prune(_folder);
                _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(4096)
                {
                    // Never silently evict an earlier HID report. Count saturation instead.
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false
                });
                string folder = _folder;
                ChannelReader<string> reader = _channel.Reader;
                _worker = Task.Run(() => WriteLoopAsync(folder, reader));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Local beta diagnostics unavailable: {ex.GetType().Name}");
                _channel = null;
            }
        }
    }

    internal static void Write(string eventName, object? details = null)
    {
        if (!AppReleaseInfo.IsBeta) return;
        Channel<string>? channel = Volatile.Read(ref _channel);
        if (channel == null) return;
        try
        {
            string entry = JsonSerializer.Serialize(new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = eventName,
                Details = details
            });
            long dropped = Interlocked.Exchange(ref _droppedEntries, 0);
            if (dropped > 0)
            {
                string notice = JsonSerializer.Serialize(new
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Event = "Diagnostics.EntriesDropped",
                    Details = new { Count = dropped, Reason = "Queue capacity exceeded" }
                });
                if (!channel.Writer.TryWrite(notice))
                    Interlocked.Add(ref _droppedEntries, dropped);
            }
            if (!channel.Writer.TryWrite(entry))
                Interlocked.Increment(ref _droppedEntries);
        }
        catch { /* Logging must never interrupt HID or UI work. */ }
    }

    internal static void WriteError(string eventName, Exception? ex) => Write(eventName,
        ex == null ? null : new { Exception = ex.GetType().Name, ex.HResult });

    private static async Task WriteLoopAsync(string folder, ChannelReader<string> reader)
    {
        string stem = $"HBM-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}";
        int part = 0;
        StreamWriter? writer = null;
        long size = 0;
        try
        {
            await foreach (string entry in reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    long bytes = System.Text.Encoding.UTF8.GetByteCount(entry) + 2;
                    if (writer == null || size + bytes > MaxFileBytes)
                    {
                        if (writer != null) await writer.DisposeAsync().ConfigureAwait(false);
                        string name = Path.Combine(folder, $"{stem}-{++part:D3}.jsonl");
                        writer = new StreamWriter(new FileStream(name, FileMode.CreateNew,
                            FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous));
                        size = 0;
                        Prune(folder);
                    }
                    await writer.WriteLineAsync(entry).ConfigureAwait(false);
                    // Flush on every event to retain context if the app crashes.
                    await writer.FlushAsync().ConfigureAwait(false);
                    size += bytes;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Beta diagnostics write failed: {ex.GetType().Name}");
                }
            }
        }
        finally
        {
            if (writer != null) await writer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void Prune(string folder)
    {
        try
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            FileInfo[] files = new DirectoryInfo(folder).GetFiles("HBM-*.jsonl")
                .OrderBy(f => f.LastWriteTimeUtc).ToArray();
            foreach (FileInfo file in files.Where(f => f.LastWriteTimeUtc < cutoff))
            {
                try { file.Delete(); } catch { }
            }
            files = new DirectoryInfo(folder).GetFiles("HBM-*.jsonl")
                .OrderBy(f => f.LastWriteTimeUtc).ToArray();
            long total = files.Sum(f => f.Length);
            foreach (FileInfo file in files)
            {
                if (total <= MaxDirectoryBytes) break;
                try { long length = file.Length; file.Delete(); total -= length; } catch { }
            }
        }
        catch { /* Retention must never break monitoring. */ }
    }

    internal static void Stop()
    {
        Channel<string>? channel;
        Task? worker;
        lock (Gate)
        {
            channel = _channel;
            worker = _worker;
            _channel = null;
            _worker = null;
        }
        channel?.Writer.TryComplete();
        try { worker?.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
}
