using System.Diagnostics;
using System.Text.Json;

namespace HyperXBatteryTray.Devices;

/// <summary>
/// Caches only a validated MI/Col selector per device family and receiver VID/PID.
/// Never stores raw HID paths, instance IDs or serial numbers. Revalidate on every
/// new connection; this is a performance hint, not permission to trust stale data.
/// </summary>
internal static class HidEndpointPreferenceStore
{
    private const string FileName = "hid-endpoints.json";
    private static readonly object Sync = new();
    private static Dictionary<string, string>? _entries;

    internal static string? Get(string family, ushort vid, ushort pid)
    {
        lock (Sync)
        {
            EnsureLoaded();
            return _entries!.TryGetValue(Key(family, vid, pid), out string? value)
                ? value : null;
        }
    }

    internal static void Remember(string family, ushort vid, ushort pid, string collection)
    {
        if (string.IsNullOrWhiteSpace(collection)) return;
        if (!System.Text.RegularExpressions.Regex.IsMatch(collection,
            @"^MI_[0-9A-F]{2}&COL[0-9A-F]{2}$")) return;
        lock (Sync)
        {
            EnsureLoaded();
            string key = Key(family, vid, pid);
            if (_entries!.TryGetValue(key, out string? current) && current == collection)
                return;
            _entries[key] = collection;
            try
            {
                string path = Path.Combine(ApplicationRuntime.UserDataDirectory, FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_entries));
                File.Move(temporary, path, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not persist HID preference: {ex.Message}");
            }
        }
    }

    private static string Key(string family, ushort vid, ushort pid) =>
        $"{family}|{vid:X4}:{pid:X4}";

    private static void EnsureLoaded()
    {
        if (_entries != null) return;
        try
        {
            string path = Path.Combine(ApplicationRuntime.UserDataDirectory, FileName);
            _entries = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not read HID preferences: {ex.Message}");
        }
        _entries ??= new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
