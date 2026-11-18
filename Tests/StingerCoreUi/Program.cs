using System.Reflection;
using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Settings;

internal static class Program
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static FieldInfo Field(object target, string name)
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Instance) is { } field) return field;
        throw new InvalidOperationException("Missing field " + name);
    }
    private static object? Get(object target, string name) => Field(target, name).GetValue(target);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [STAThread]
    private static void Main()
    {
        // Use real receiver capability and UI code. Only receiver state is
        // scripted: no HID opens, no windows shown, no preferences saved.
        var assembly = typeof(CloudStingerCoreWirelessDevice).Assembly;
        Type trayType = assembly.GetType("HyperXBatteryTray.TrayContextMenuForm")!;
        int cases = 0;
        foreach (string definitionName in new[] { "DeviceDefinition", "PlayStationDefinition", "DtsDefinition" })
        foreach (string state in new[] { "disconnected", "unverified", "unknown", "open", "muted" })
        {
            var definition = (HyperXDeviceDefinition)typeof(CloudStingerCoreWirelessDevice)
                .GetField(definitionName, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            using var driver = (CloudStingerCoreWirelessDevice)Activator.CreateInstance(typeof(CloudStingerCoreWirelessDevice),
                Instance, null, [definition], null)!;
            using var receiver = HyperXDeviceManager.CreateDevice("HyperX Cloud Stinger Core Wireless + 7.1")!;
            bool verified = state is "unknown" or "open" or "muted";
            if (state != "disconnected")
            {
                Set(driver, "_isConnected", true);
                Set(driver, "_battery", 80);
                Set(driver, "_isMicrophoneMuteStateKnown", state is "open" or "muted");
                Set(driver, "_isMicrophoneMuted", state == "muted");
                Set(receiver, "_active", driver);
            }
            Set(receiver, "_verified", verified);
            bool supported = definitionName == "DtsDefinition" && verified;
            Check(receiver.SupportsMicrophoneMuteMonitoring == supported, "receiver capability");
            var settings = new AppSettings { SelectedDevice = receiver.Name, Language = AppLanguage.English, Theme = AppTheme.Dark };
            Check(!HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(settings.SelectedDevice), "static family capability");
            Check(HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(settings.SelectedDevice, receiver) == supported, "UI capability");
            using var form = new SettingsForm(settings, receiver);
            using var tray = (Form)Activator.CreateInstance(trayType, Instance, null,
                [settings, receiver, false, null, AppTheme.Dark,
                    new Action<object?, EventArgs>((_, _) => { }), new Action<object?, EventArgs>((_, _) => { })], null)!;
            bool muteAsset = !supported || state == "muted";
            Check(Equals(Get(form, "_sidebarMicrophoneIconUsesMuteAsset"), muteAsset), "Settings icon");
            Check(Equals(Get(tray, "_microphoneIconUsesMuteAsset"), muteAsset), "tray icon");
            object capabilityIcon = Get(form, "_microphoneCapabilityIcon")!;
            Check(Equals(capabilityIcon.GetType().GetProperty("IconKey")!.GetValue(capabilityIcon),
                supported ? "supported" : "unsupported"), "Settings capability badge");
            string expected = supported && state == "muted" ? "Muted" : supported && state == "open" ? "Open" : "N/A";
            Check(((Label)Get(form, "_sidebarMicrophoneLabel")!).Text == expected, "Settings microphone text");
            Check(((Label)Get(tray, "_microphoneText")!).Text == expected, "tray microphone text");
            trayType.GetMethod("ApplyTheme")!.Invoke(tray, [false]);
            Check(Equals(Get(tray, "_microphoneIconUsesMuteAsset"), muteAsset), "theme change reverted icon");
            Console.WriteLine($"PASS {definition.Name}: {state}, supported={supported}, icon={(muteAsset ? "mute" : "mic")}, text={expected}");
            cases++;
        }
        Console.WriteLine($"All {cases} UI cases passed.");
    }
}
