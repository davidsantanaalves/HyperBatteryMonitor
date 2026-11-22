using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HyperXBatteryTray;
using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Monitoring;
using HyperXBatteryTray.Settings;

internal static class Beta4Contracts
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static object? Get(object target, string name) => target.GetType().GetField(name, Flags)!.GetValue(target);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    private static bool Selected(Control card) => (bool)card.GetType().GetProperty("Selected")!.GetValue(card)!;
    private static Form Dialog(SettingsForm owner, Func<DeviceMonitoringMode, Task<bool>> save)
    {
        Type type = typeof(SettingsForm).GetNestedType("DeviceSettingsDialog", BindingFlags.NonPublic)!;
        return (Form)Activator.CreateInstance(type, Flags, null,
            [owner, true, DeviceMonitoringMode.ListenOnly, save], null)!;
    }

    internal static void Run()
    {
        var groups = new (string Name, Action Test)[]
        {
            ("model defaults and independent mode overrides", Modes),
            ("verified cache, old JSON and settings normalization", Persistence),
            ("four offline read-only choices and unsupported state", OfflineChoices),
            ("modal cancel, failed Save retry and successful close", SaveLifecycle),
            ("tooltip length and offline state in all 11 locales", Tooltips),
            ("verified DTS microphone reaches the new tooltip", DtsTooltip)
        };
        foreach (var group in groups) { group.Test(); Console.WriteLine("PASS Beta 4 " + group.Name); }
        Console.WriteLine($"All {groups.Length} additional Beta 4 contract groups passed (no HID opened, no settings saved).");
    }

    private static void Modes()
    {
        var settings = new AppSettings();
        Check((int)DeviceMonitoringMode.Polling == 0 && (int)DeviceMonitoringMode.ListenOnly == 1, "driver boundary enum drift");
        Check(settings.GetMonitoringMode("HyperX Cloud III") == DeviceMonitoringMode.ListenOnly, "Cloud III default");
        foreach (string name in new[] { "HyperX Cloud III S", "HyperX Cloud Alpha", "HyperX Cloud Mix 2", "unknown", "" })
            Check(settings.GetMonitoringMode(name) == DeviceMonitoringMode.Polling, "non-original default: " + name);
        settings.DeviceMonitoringModes["HyperX Cloud III"] = DeviceMonitoringMode.Polling;
        settings.DeviceMonitoringModes["HyperX Cloud Alpha"] = DeviceMonitoringMode.ListenOnly;
        Check(settings.GetMonitoringMode("HyperX Cloud III") == DeviceMonitoringMode.Polling &&
            settings.GetMonitoringMode("HyperX Cloud Alpha") == DeviceMonitoringMode.ListenOnly, "independent overrides");
        settings.DeviceMonitoringModes["unknown"] = DeviceMonitoringMode.ListenOnly;
        Check(settings.GetMonitoringMode("unknown") == DeviceMonitoringMode.Polling, "unsupported override");
    }

    private static void Persistence()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"SelectedDevice\":\"HyperX Cloud III\"}")!;
        Check(old.GetMonitoringMode(old.SelectedDevice) == DeviceMonitoringMode.ListenOnly &&
            old.GetLastKnownAutoPowerOff(old.SelectedDevice) == null, "old profile upgrade");
        Check(!old.RememberVerifiedAutoPowerOff("unknown", 20) && !old.RememberVerifiedAutoPowerOff("HyperX Cloud III", 5), "unverified cache value");
        Check(old.RememberVerifiedAutoPowerOff("HyperX Cloud III", 20), "remember verified value");
        old.RuntimeAutoPowerOffMinutes = 30;
        string json = JsonSerializer.Serialize(old);
        Check(!json.Contains("RuntimeAutoPowerOffMinutes"), "runtime value serialized");
        var roundtrip = JsonSerializer.Deserialize<AppSettings>(json)!;
        Check(roundtrip.GetLastKnownAutoPowerOff("HyperX Cloud III") == 20 && roundtrip.RuntimeAutoPowerOffMinutes == null, "cache roundtrip");
        roundtrip.LastKnownAutoPowerOffMinutes["unknown"] = 20;
        roundtrip.LastKnownAutoPowerOffMinutes["HyperX Cloud Alpha"] = 5;
        roundtrip.DeviceMonitoringModes["unknown"] = DeviceMonitoringMode.ListenOnly;
        roundtrip.DeviceMonitoringModes["HyperX Cloud Alpha"] = (DeviceMonitoringMode)99;
        typeof(SettingsManager).GetMethod("Normalize", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [roundtrip]);
        Check(roundtrip.LastKnownAutoPowerOffMinutes.Count == 1 && roundtrip.DeviceMonitoringModes.Count == 0, "invalid persisted entries survived");
    }

    private static void OfflineChoices()
    {
        using var owner = new SettingsForm(new AppSettings());
        using var modal = Dialog(owner, _ => Task.FromResult(true));
        var cards = (Control[])Get(modal, "_powerChoices")!;
        Check(cards.Length == 4, "four Auto Power-Off choices");
        Call(modal, "UpdateAutoPowerOff", true, false, 20, "offline");
        Check(cards.Select(Selected).SequenceEqual(new[] { false, false, true, false }) && cards.All(x => !x.Enabled), "offline verified selection");
        Call(modal, "UpdateAutoPowerOff", true, false, null, "offline");
        Check(cards.All(x => !Selected(x) && !x.Enabled), "unknown value guessed");
        Call(modal, "UpdateAutoPowerOff", false, false, 20, "unsupported");
        Check(cards.All(x => !Selected(x) && !x.Enabled), "unsupported options selected");
    }

    private static void SaveLifecycle()
    {
        using var owner = new SettingsForm(new AppSettings());
        int saves = 0;
        using (var cancelled = Dialog(owner, _ => { saves++; return Task.FromResult(true); }))
        {
            cancelled.DialogResult = DialogResult.Cancel;
            cancelled.Close();
            Check(saves == 0, "Cancel called Save");
        }
        using var modal = Dialog(owner, mode => { saves++; Check(mode == DeviceMonitoringMode.ListenOnly, "chosen mode"); return Task.FromResult(saves > 1); });
        Call(modal, "SaveClick", null, EventArgs.Empty);
        Check(saves == 1 && modal.DialogResult != DialogResult.OK && ((Control)Get(modal, "_saveButton")!).Enabled, "failed Save closed or locked modal");
        Call(modal, "SaveClick", null, EventArgs.Empty);
        Check(saves == 2 && modal.DialogResult == DialogResult.OK, "successful retry did not close");
    }

    private static string Tooltip(AppSettings settings, IHyperXDevice? device)
    {
        using var notify = new NotifyIcon();
        var context = (HyperXBatteryMonitorApplicationContext)RuntimeHelpers.GetUninitializedObject(typeof(HyperXBatteryMonitorApplicationContext));
        Set(context, "_settings", settings);
        Set(context, "_device", device);
        Set(context, "_notifyIcon", notify);
        Set(context, "_batteryRemainingTimeEstimator", new BatteryRemainingTimeEstimator(BatteryHistoryData.CreateCurrent()));
        Call(context, "UpdateNotifyIconTooltip");
        return notify.Text;
    }

    private static void Tooltips()
    {
        using var driver = new Cloud3WirelessDevice();
        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
        {
            var settings = new AppSettings { SelectedDevice = "HyperX Cloud III", Language = language, RuntimeAutoPowerOffMinutes = 20 };
            Set(driver, "_isConnected", true);
            Set(driver, "_battery", 80);
            Set(driver, "_isMicrophoneMuteStateKnown", true);
            string live = Tooltip(settings, driver);
            Check(live.Length <= 127 && live.Contains("80%"), "live tooltip length/state " + language);
            Set(driver, "_isConnected", false);
            string offline = Tooltip(settings, driver);
            Check(offline.Length <= 127 && offline.Contains("...") && !offline.Contains("80%"), "stale offline tooltip " + language);
            settings.SelectedDevice = "HyperX Cloud Flight Wireless";
            string unsupported = Tooltip(settings, null);
            Check(!unsupported.Contains(Localization.Get("SidebarAutoPowerOff", language) + ":"), "unsupported Auto Power-Off tooltip " + language);
        }
    }

    private static void DtsTooltip()
    {
        Type driverType = typeof(CloudStingerCoreWirelessDevice);
        var definition = (HyperXDeviceDefinition)driverType.GetField("DtsDefinition", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        using var driver = (CloudStingerCoreWirelessDevice)Activator.CreateInstance(driverType, Flags, null, [definition], null)!;
        Type baseType = typeof(HyperXBatteryDeviceBase);
        foreach (var pair in new[] { ("_isConnected", (object)true), ("_battery", (object)80), ("_isMicrophoneMuteStateKnown", (object)true), ("_isMicrophoneMuted", (object)true) })
            baseType.GetField(pair.Item1, Flags)!.SetValue(driver, pair.Item2);
        var settings = new AppSettings { SelectedDevice = "HyperX Cloud Stinger Core Wireless + 7.1", Language = AppLanguage.English };
        using var receiver = HyperXDeviceManager.CreateDevice(settings.SelectedDevice)!;
        Set(receiver, "_active", driver);
        Set(receiver, "_verified", true);
        string text = Tooltip(settings, receiver);
        Check(text.Contains(Localization.Get("SidebarMicrophone", settings.Language)) && text.Contains(Localization.Get("MicrophoneMuted", settings.Language)), "DTS capability omitted from tooltip");
    }
}
