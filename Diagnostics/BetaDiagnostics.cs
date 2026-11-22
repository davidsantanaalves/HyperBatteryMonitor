using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace HyperXBatteryTray.Diagnostics;

/// <summary>Consent-gated local diagnostics: essential errors, or temporary detailed HID capture.</summary>
internal static class BetaDiagnostics
{
    // Updated terms distinguish essential diagnostics from time-limited raw HID capture.
    internal const int ConsentVersion = 5;
    private const long MaxFileBytes = 10L * 1024 * 1024;
    private const long MaxDirectoryBytes = 100L * 1024 * 1024;
    private const int RetentionDays = 14;
    private const int FlushThresholdBytes = 32 * 1024;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EssentialRepeatWindow = TimeSpan.FromMinutes(10);
    private static readonly object Gate = new();
    private static readonly object EssentialGate = new();
    private static readonly Dictionary<string, (DateTimeOffset LastWritten, long Suppressed)> EssentialRepeats = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim MaintenanceGate = new(1, 1);
    private static Channel<LogEntry>? _channel;
    private static Task? _worker;
    private static long _detailedUntilUtcTicks;
    private static long _droppedEntries;
    private static System.Threading.Timer? _detailedDeadlineTimer;
    private static int _detailedDurationHours;
    private static bool _essentialLoggingEnabled;
    private const int CaptureSessionSchemaVersion = 1;
    private const string CaptureSessionFileName = "detailed-capture-session.json";

    // This file holds only the session deadline and consent revision, not HID data
    // or any persistent change to essential diagnostics preferences.
    private sealed class CaptureSessionState
    {
        public CaptureSessionState() { }

        public int SchemaVersion { get; set; }
        public int ConsentVersion { get; set; }
        public int DurationHours { get; set; }
        public long StartedUtcTicks { get; set; }
        public long DeadlineUtcTicks { get; set; }
    }

    private static string CaptureSessionPath => Path.Combine(
        ApplicationRuntime.UserDataDirectory, CaptureSessionFileName);

    internal enum DetailedCaptureTransition { Started, Resumed, Completed, Stopped }
    internal static event Action<DetailedCaptureTransition, int>? DetailedCaptureTransitioned;

    private readonly record struct LogEntry(string Json, bool Immediate);

    internal static bool IsEnabled => Volatile.Read(ref _channel) != null;
    internal static bool IsEssentialLoggingEnabled
    {
        get { lock (Gate) return _essentialLoggingEnabled && _channel != null; }
    }
    internal static string LogsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HBM Logs");

    // An expired session ends even if Settings is closed; no new events can be detailed.
    internal static DateTimeOffset? DetailedCaptureEndsAtUtc
    {
        get
        {
            long until = Interlocked.Read(ref _detailedUntilUtcTicks);
            if (until == 0) return null;
            if (until <= DateTimeOffset.UtcNow.UtcTicks)
            {
                // The deadline can be observed while Settings is open, or by the
                // one-shot timer when the dialog has already closed. Complete once.
                CompleteDetailedCaptureIfDue(until);
                return null;
            }
            return new DateTimeOffset(until, TimeSpan.Zero);
        }
    }

    internal static bool IsDetailedCaptureActive => IsEnabled && DetailedCaptureEndsAtUtc.HasValue;

    internal static bool StartDetailedCapture(int durationHours, bool allowTemporary = false)
    {
        if (durationHours is < 1 or > 24) return false;
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        long deadlineUtcTicks = startedAt.AddHours(durationHours).UtcTicks;
        bool stopUnusedWriter = false;
        bool started = false;
        lock (Gate)
        {
            // Temporary consent starts the writer without enabling essential logging.
            bool writerWasDormant = _channel == null;
            if (writerWasDormant && allowTemporary)
                StartWriterUnderLock();
            if (_channel == null) return false;

            // Persist before enabling raw HID capture. If this fails, the user
            // receives the existing start-failure dialog, not a false promise
            // that the session will survive a restart.
            CaptureSessionState session = new()
            {
                SchemaVersion = CaptureSessionSchemaVersion,
                ConsentVersion = ConsentVersion,
                DurationHours = durationHours,
                StartedUtcTicks = startedAt.UtcTicks,
                DeadlineUtcTicks = deadlineUtcTicks
            };
            if (!TrySaveCaptureSession(session))
            {
                stopUnusedWriter = writerWasDormant && !_essentialLoggingEnabled &&
                    Interlocked.Read(ref _detailedUntilUtcTicks) == 0;
            }
            else
            {
                ScheduleDetailedDeadlineUnderLock(deadlineUtcTicks);
                Interlocked.Exchange(ref _detailedUntilUtcTicks, deadlineUtcTicks);
                _detailedDurationHours = durationHours;
                started = true;
            }
        }
        if (stopUnusedWriter) Stop();
        if (!started) return false;
        Write("Diagnostics.DetailedCaptureStarted", new { DurationHours = durationHours });
        NotifyDetailedCaptureTransition(DetailedCaptureTransition.Started, durationHours);
        return true;
    }

    /// <summary>
    /// Rehydrate a still-authorized session on a new HBM process, preserving the
    /// original UTC deadline. Expired, malformed or outdated consent state is
    /// discarded without resuming raw diagnostics or sending a completion toast.
    /// </summary>
    internal static bool TryResumeDetailedCapture()
    {
        int durationHours;
        long deadlineUtcTicks;
        lock (Gate)
        {
            if (Interlocked.Read(ref _detailedUntilUtcTicks) != 0)
                return false;

            CaptureSessionState? session = TryLoadCaptureSession();
            if (session == null) return false;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!IsValidCaptureSession(session, now))
            {
                DeletePersistedCaptureSession();
                return false;
            }

            // The session-specific, previously granted authorization is bounded
            // by the original deadline; it never changes the saved consent toggle.
            if (_channel == null)
                StartWriterUnderLock();
            if (_channel == null) return false;

            durationHours = session.DurationHours;
            deadlineUtcTicks = session.DeadlineUtcTicks;
            ScheduleDetailedDeadlineUnderLock(deadlineUtcTicks);
            Interlocked.Exchange(ref _detailedUntilUtcTicks, deadlineUtcTicks);
            _detailedDurationHours = durationHours;
        }
        Write("Diagnostics.DetailedCaptureResumed", new
        {
            DurationHours = durationHours,
            RemainingMinutes = Math.Max(1, (int)Math.Ceiling(
                (deadlineUtcTicks - DateTimeOffset.UtcNow.UtcTicks) /
                (double)TimeSpan.TicksPerMinute))
        });
        NotifyDetailedCaptureTransition(DetailedCaptureTransition.Resumed, durationHours);
        return true;
    }

    private static bool IsValidCaptureSession(CaptureSessionState session, DateTimeOffset now)
    {
        return session.SchemaVersion == CaptureSessionSchemaVersion &&
            session.ConsentVersion == ConsentVersion &&
            session.DurationHours is >= 1 and <= 24 &&
            session.StartedUtcTicks > 0 &&
            session.StartedUtcTicks <= now.UtcTicks &&
            session.DeadlineUtcTicks > now.UtcTicks &&
            session.DeadlineUtcTicks - session.StartedUtcTicks ==
                TimeSpan.FromHours(session.DurationHours).Ticks;
    }

    private static CaptureSessionState? TryLoadCaptureSession()
    {
        try
        {
            string path = CaptureSessionPath;
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<CaptureSessionState>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not read detailed capture session: {ex.GetType().Name}");
            return null;
        }
    }

    // Called while holding Gate. Writes once per session, not per monitoring event.
    private static bool TrySaveCaptureSession(CaptureSessionState session)
    {
        string? temporaryFile = null;
        try
        {
            string path = CaptureSessionPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporaryFile = path + ".tmp";
            File.WriteAllText(temporaryFile, JsonSerializer.Serialize(session));
            File.Move(temporaryFile, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not persist detailed capture session: {ex.GetType().Name}");
            return false;
        }
        finally
        {
            if (temporaryFile != null)
            {
                try { File.Delete(temporaryFile); } catch { }
            }
        }
    }

    private static void DeletePersistedCaptureSession()
    {
        try { File.Delete(CaptureSessionPath); }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not clear detailed capture session: {ex.GetType().Name}");
            // A corrupted/inaccessible record must never authorize a new session.
            // If deletion fails, attempt to replace it with an invalid record.
            try { File.WriteAllText(CaptureSessionPath, "{}"); } catch { }
        }
    }

    internal static void StopDetailedCapture()
    {
        bool stopped;
        bool endTemporaryWriter;
        lock (Gate)
        {
            stopped = Interlocked.Exchange(ref _detailedUntilUtcTicks, 0) != 0;
            endTemporaryWriter = !_essentialLoggingEnabled;
            _detailedDurationHours = 0;
            _detailedDeadlineTimer?.Dispose();
            _detailedDeadlineTimer = null;
            if (stopped) DeletePersistedCaptureSession();
        }
        if (stopped)
        {
            Write("Diagnostics.DetailedCaptureStopped");
            NotifyDetailedCaptureTransition(DetailedCaptureTransition.Stopped, 0);
            if (endTemporaryWriter) Stop();
        }
        // A manual stop must not produce the scheduled-completion notification.
    }

    private static void ScheduleDetailedDeadlineUnderLock(long deadlineUtcTicks)
    {
        TimeSpan remaining = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond,
            deadlineUtcTicks - DateTimeOffset.UtcNow.UtcTicks));
        System.Threading.Timer timer = new(OnDetailedDeadlineReached, deadlineUtcTicks,
            remaining, Timeout.InfiniteTimeSpan);
        System.Threading.Timer? old = _detailedDeadlineTimer;
        _detailedDeadlineTimer = timer;
        old?.Dispose();
    }

    private static void OnDetailedDeadlineReached(object? state)
    {
        if (state is long deadlineUtcTicks)
            _ = CompleteDetailedDeadlineAfterMaintenanceAsync(deadlineUtcTicks);
    }

    private static async Task CompleteDetailedDeadlineAfterMaintenanceAsync(long deadlineUtcTicks)
    {
        bool entered = false;
        try
        {
            // Clearing logs temporarily pauses the writer. Defer expiry until
            // the session has been restored, preserving the completion toast.
            await MaintenanceGate.WaitAsync().ConfigureAwait(false);
            entered = true;
            CompleteDetailedCaptureIfDue(deadlineUtcTicks);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Detailed diagnostics deadline notification failed: {ex.GetType().Name}");
        }
        finally
        {
            if (entered) MaintenanceGate.Release();
        }
    }

    private static void CompleteDetailedCaptureIfDue(long expectedDeadlineUtcTicks)
    {
        int completedDurationHours;
        bool endTemporaryWriter;
        lock (Gate)
        {
            if (_channel == null || Interlocked.Read(ref _detailedUntilUtcTicks) != expectedDeadlineUtcTicks)
                return;
            if (DateTimeOffset.UtcNow.UtcTicks < expectedDeadlineUtcTicks)
            {
                // A system-clock adjustment can make a one-shot timer fire early.
                ScheduleDetailedDeadlineUnderLock(expectedDeadlineUtcTicks);
                return;
            }
            Interlocked.Exchange(ref _detailedUntilUtcTicks, 0);
            completedDurationHours = _detailedDurationHours;
            endTemporaryWriter = !_essentialLoggingEnabled;
            _detailedDurationHours = 0;
            _detailedDeadlineTimer?.Dispose();
            _detailedDeadlineTimer = null;
            DeletePersistedCaptureSession();
        }
        Write("Diagnostics.DetailedCaptureCompleted", new { DurationHours = completedDurationHours });
        NotifyDetailedCaptureTransition(DetailedCaptureTransition.Completed, completedDurationHours);
        if (endTemporaryWriter) Stop();
    }

    private static void NotifyDetailedCaptureTransition(DetailedCaptureTransition transition,
        int durationHours)
    {
        try { DetailedCaptureTransitioned?.Invoke(transition, durationHours); }
        catch (Exception ex)
        {
            // Notification failures must never affect diagnostics or HID monitoring.
            Debug.WriteLine($"Diagnostics toast dispatch unavailable: {ex.GetType().Name}");
        }
    }

    internal static void Start()
    {
        lock (Gate)
        {
            StartWriterUnderLock();
            // Starting essential logging is the only operation that enables its
            // persistent in-memory mode. Temporary capture never changes settings.
            _essentialLoggingEnabled = _channel != null;
        }
    }

    internal static void DisableEssentialLogging()
    {
        lock (Gate)
        {
            _essentialLoggingEnabled = false;
            // An already-authorized temporary capture must finish normally.
            // The writer is stopped when that session expires or is cancelled.
            if (Interlocked.Read(ref _detailedUntilUtcTicks) == 0)
                Stop();
        }
    }

    // Gate must be held by the caller. Never changes the essential preference.
    private static void StartWriterUnderLock()
    {
        if (_channel != null) return;
        try
        {
            string folder = LogsFolder;
            Directory.CreateDirectory(folder);
            Prune(folder);
            Channel<LogEntry> channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(4096)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
            _channel = channel;
            _worker = Task.Run(() => WriteLoopAsync(folder, channel.Reader));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Local diagnostics unavailable: {ex.GetType().Name}");
            _channel = null;
            _worker = null;
        }
    }

    private static bool IsEssentialEvent(string eventName)
    {
        return eventName is "UI.UnhandledException" or "Process.UnhandledException"
            or "Task.UnobservedException" or "Monitor.UnhandledCycleError"
            or "Diagnostics.EntriesDropped" or "Diagnostics.DetailedCaptureStopped"
            or "Diagnostics.DetailedCaptureStarted" or "Diagnostics.DetailedCaptureResumed"
            or "Diagnostics.DetailedCaptureCompleted"
            || (eventName.EndsWith("Failed", StringComparison.Ordinal) &&
                eventName is not "Monitor.BatteryQueryFailed");
    }

    private static bool ShouldRecordEssential(string eventName, ref object? details)
    {
        if (!IsEssentialEvent(eventName)) return false;
        // Repeated exceptions and HID-open failures must not produce a new disk entry every cycle.
        if (eventName is "Diagnostics.DetailedCaptureStarted" or "Diagnostics.DetailedCaptureResumed"
            or "Diagnostics.DetailedCaptureStopped" or "Diagnostics.DetailedCaptureCompleted")
            return true;
        lock (EssentialGate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (EssentialRepeats.TryGetValue(eventName, out var previous) &&
                now - previous.LastWritten < EssentialRepeatWindow)
            {
                EssentialRepeats[eventName] = (previous.LastWritten, previous.Suppressed + 1);
                return false;
            }
            if (previous.Suppressed > 0)
                details = new { LastOccurrence = details, SuppressedRepeats = previous.Suppressed };
            EssentialRepeats[eventName] = (now, 0);
            return true;
        }
    }

    internal static void Write(string eventName, object? details = null)
    {
        Channel<LogEntry>? channel = Volatile.Read(ref _channel);
        if (channel == null) return;
        try
        {
            bool detailed = IsDetailedCaptureActive;
            if (!detailed && !ShouldRecordEssential(eventName, ref details)) return;

            LogEntry entry = Serialize(eventName, details,
                immediate: !detailed || IsEssentialEvent(eventName));
            long dropped = Interlocked.Exchange(ref _droppedEntries, 0);
            if (dropped > 0)
            {
                LogEntry notice = Serialize("Diagnostics.EntriesDropped",
                    new { Count = dropped, Reason = "Queue capacity exceeded" }, immediate: true);
                if (!channel.Writer.TryWrite(notice))
                    Interlocked.Add(ref _droppedEntries, dropped);
            }
            if (!channel.Writer.TryWrite(entry))
                Interlocked.Increment(ref _droppedEntries);
        }
        catch { /* Diagnostic recording must never interrupt HID or UI work. */ }
    }

    private static LogEntry Serialize(string name, object? details, bool immediate) =>
        new(JsonSerializer.Serialize(new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = name,
            Details = details
        }), immediate);

    internal static void WriteError(string eventName, Exception? ex) => Write(eventName,
        ex == null ? null : new { Exception = ex.GetType().Name, ex.HResult });

    private static async Task WriteLoopAsync(string folder, ChannelReader<LogEntry> reader)
    {
        string stem = $"HBM-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        int part = 0;
        StreamWriter? writer = null;
        long size = 0;
        int buffered = 0;
        long firstPendingTick = 0;
        try
        {
            while (true)
            {
                bool ready;
                if (buffered > 0)
                {
                    int remainingMs = Math.Max(1, (int)Math.Max(0,
                        FlushInterval.TotalMilliseconds - (Environment.TickCount64 - firstPendingTick)));
                    using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(remainingMs));
                    try { ready = await reader.WaitToReadAsync(timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException)
                    {
                        if (writer != null) await writer.FlushAsync().ConfigureAwait(false);
                        buffered = 0;
                        firstPendingTick = 0;
                        continue;
                    }
                }
                else
                    ready = await reader.WaitToReadAsync().ConfigureAwait(false);

                if (!ready) break;
                while (reader.TryRead(out LogEntry entry))
                {
                    try
                    {
                        int bytes = Encoding.UTF8.GetByteCount(entry.Json) + Environment.NewLine.Length;
                        if (writer == null || size + bytes > MaxFileBytes)
                        {
                            if (writer != null) await writer.DisposeAsync().ConfigureAwait(false);
                            string name = Path.Combine(folder, $"{stem}-{++part:D3}.jsonl");
                            writer = new StreamWriter(new FileStream(name, FileMode.CreateNew,
                                FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous),
                                new UTF8Encoding(false), 32 * 1024);
                            size = 0;
                            buffered = 0;
                            firstPendingTick = 0;
                            Prune(folder);
                        }
                        await writer.WriteLineAsync(entry.Json).ConfigureAwait(false);
                        size += bytes;
                        if (buffered == 0) firstPendingTick = Environment.TickCount64;
                        buffered += bytes;
                        if (entry.Immediate || buffered >= FlushThresholdBytes ||
                            Environment.TickCount64 - firstPendingTick >= FlushInterval.TotalMilliseconds)
                        {
                            await writer.FlushAsync().ConfigureAwait(false);
                            buffered = 0;
                            firstPendingTick = 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Diagnostics write failed: {ex.GetType().Name}");
                    }
                }
            }
        }
        finally
        {
            if (writer != null) await writer.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static (int Files, long Bytes) GetStorageUsage()
    {
        try
        {
            if (!Directory.Exists(LogsFolder)) return (0, 0);
            FileInfo[] files = new DirectoryInfo(LogsFolder).GetFiles("HBM-*.jsonl");
            return (files.Length, files.Sum(file => file.Length));
        }
        catch { return (0, 0); }
    }

    /// <summary>Close the current writer before deleting only HBM JSONL files, then resume active mode.</summary>
    internal static async Task<bool> ClearLogsAsync()
    {
        await MaintenanceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            bool resume = IsEnabled;
            long detailedUntil = Interlocked.Read(ref _detailedUntilUtcTicks);
            if (resume)
            {
                Channel<LogEntry>? channel;
                Task? worker;
                lock (Gate)
                {
                    channel = _channel;
                    worker = _worker;
                    _channel = null;
                    _worker = null;
                    Interlocked.Exchange(ref _detailedUntilUtcTicks, 0);
                }
                channel?.Writer.TryComplete();
                if (worker != null)
                {
                    try { await worker.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
                    catch
                    {
                        // Keep writer closed. Never delete files that might still be in use.
                        return false;
                    }
                }
            }
            bool success = true;
            lock (EssentialGate) EssentialRepeats.Clear();
            Interlocked.Exchange(ref _droppedEntries, 0);
            try
            {
                if (Directory.Exists(LogsFolder))
                {
                    foreach (string path in Directory.EnumerateFiles(LogsFolder, "HBM-*.jsonl", SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(path); }
                        catch { success = false; }
                    }
                }
            }
            catch { success = false; }
            finally
            {
                if (resume)
                {
                    lock (Gate) StartWriterUnderLock();
                    if (IsEnabled && detailedUntil != 0)
                    {
                        Interlocked.Exchange(ref _detailedUntilUtcTicks, detailedUntil);
                        if (detailedUntil <= DateTimeOffset.UtcNow.UtcTicks)
                            CompleteDetailedCaptureIfDue(detailedUntil);
                    }
                }
            }
            return success;
        }
        finally { MaintenanceGate.Release(); }
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
        Channel<LogEntry>? channel;
        Task? worker;
        lock (Gate)
        {
            channel = _channel;
            worker = _worker;
            _channel = null;
            _worker = null;
            _essentialLoggingEnabled = false;
            Interlocked.Exchange(ref _detailedUntilUtcTicks, 0);
            _detailedDurationHours = 0;
            _detailedDeadlineTimer?.Dispose();
            _detailedDeadlineTimer = null;
        }
        channel?.Writer.TryComplete();
        try { worker?.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
}
