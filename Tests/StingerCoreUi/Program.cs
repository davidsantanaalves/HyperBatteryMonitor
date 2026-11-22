using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HyperXBatteryTray;
using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Monitoring;
using HyperXBatteryTray.Notifications;
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
            bool muteAsset = supported && state == "muted";
            object settingsGlyph = Get(form, "_sidebarMicrophoneSummaryGlyph")!;
            object trayGlyph = Get(tray, "_microphoneSummaryGlyph")!;
            string expectedIcon = muteAsset ? "mute" : "mic";
            Check(Equals(settingsGlyph.GetType().GetProperty("IconKey")!.GetValue(settingsGlyph), expectedIcon), "Settings icon");
            Check(Equals(trayGlyph.GetType().GetProperty("IconKey")!.GetValue(trayGlyph), expectedIcon), "tray icon");
            object capabilityIcon = Get(form, "_microphoneCapabilityIcon")!;
            Check(Equals(capabilityIcon.GetType().GetProperty("IconKey")!.GetValue(capabilityIcon),
                supported ? "supported" : "unsupported"), "Settings capability badge");
            string expected = Localization.Get(supported && state == "muted" ? "MicrophoneMuted"
                : supported && state == "open" ? "MicrophoneOpen" : "SidebarWaiting", settings.Language);
            Check(((Label)Get(form, "_sidebarMicrophoneLabel")!).Text == expected, "Settings microphone text");
            Check(((Label)Get(tray, "_microphoneText")!).Text == expected, "tray microphone text");
            trayType.GetMethod("ApplyTheme")!.Invoke(tray, [false]);
            Check(Equals(trayGlyph.GetType().GetProperty("IconKey")!.GetValue(trayGlyph), expectedIcon), "theme change reverted icon");
            Console.WriteLine($"PASS {definition.Name}: {state}, supported={supported}, icon={(muteAsset ? "mute" : "mic")}, text={expected}");
            cases++;
        }
        Console.WriteLine($"All {cases} original UI cases passed.");
        ColdTrayStartsMuteBlink();
        Console.WriteLine("PASS verified DTS starts mute-only tray blink and respects preferences");
        NotificationsRefreshesMuteCapability();
        Console.WriteLine("PASS Notifications refreshes in place and preserves mute preferences");
        Console.WriteLine($"All {cases + 2} UI cases passed.");
        Beta4Contracts.Run();
    }

    private const string Model = "HyperX Cloud Stinger Core Wireless + 7.1";

    private static void ColdTrayStartsMuteBlink()
    {
        foreach (bool mutePreference in new[] { true, false })
        {
            using var receiver = CreateUnverifiedDts(out var driver);
            var settings = new AppSettings
            {
                SelectedDevice = Model,
                Theme = AppTheme.Light,
                BlinkOnCriticalBattery = false,
                ShowMicrophoneMuteInSystray = mutePreference,
                NotifyOnLowBattery = false,
                NotifyWhenFullyCharged = false
            };
            using var notify = new NotifyIcon();
            var context = CreateContext(settings, receiver, notify);
            Call(context, "RestartBlinkTimer");
            Check(Get(context, "_blinkTimer") == null, "unverified receiver started the timer");
            try
            {
                Set(receiver, "_verified", true);
                Call(context, "BatteryMonitor_ConnectionChanged", null, true);
                Set(driver, "_isMicrophoneMuted", true);
                Set(driver, "_isMicrophoneMuteStateKnown", true);
                Call(context, "BatteryMonitor_MicrophoneMuteChanged", null, true);

                var timer = Get(context, "_blinkTimer") as System.Windows.Forms.Timer;
                Check((timer?.Enabled == true) == mutePreference, "mute-only timer disagrees with user preference");
                if (mutePreference)
                {
                    Call(context, "BatteryMonitor_ConnectionChanged", null, true);
                    Check(ReferenceEquals(timer, Get(context, "_blinkTimer")), "duplicate confirmation restarted the timer");
                    PumpUntil(() => Equals(Get(context, "_blinkState"), true));
                    PumpUntil(() => Equals(Get(context, "_blinkState"), false));
                }

                Set(receiver, "_verified", false);
                Call(context, "BatteryMonitor_ConnectionChanged", null, false);
                Check(Get(context, "_blinkTimer") == null, "timer survived loss of mute capability");
            }
            finally
            {
                (Get(context, "_blinkTimer") as IDisposable)?.Dispose();
                (Get(context, "_currentApplicationIcon") as IDisposable)?.Dispose();
            }
        }
    }

    private static void NotificationsRefreshesMuteCapability()
    {
        foreach (bool saved in new[] { true, false })
        {
            using var receiver = CreateUnverifiedDts(out _);
            var settings = new AppSettings { SelectedDevice = Model, ShowMicrophoneMuteInSystray = saved };
            using var form = new SettingsForm(settings, receiver);
            _ = form.Handle;
            Call(form, "ShowPage", "Notifications");

            var toggle = (Control)Get(form, "_showMicrophoneMuteInSystrayToggle")!;
            var checkedProperty = toggle.GetType().GetProperty("Checked")!;
            bool IsChecked() => (bool)checkedProperty.GetValue(toggle)!;
            bool Pending() => (bool)Get(form, "_pendingShowMicrophoneMuteInSystray")!;
            Check(!toggle.Enabled && !IsChecked() && Pending() == saved, "unverified Notifications state");

            using var notify = new NotifyIcon();
            var context = CreateContext(settings, receiver, notify);
            Set(context, "_settingsForm", form);
            try
            {
                Set(receiver, "_verified", true);
                Call(context, "BatteryMonitor_ConnectionChanged", null, true);
                Check(toggle.Enabled && IsChecked() == saved, "verification did not refresh the open page");

                checkedProperty.SetValue(toggle, !saved);
                Check(Pending() == !saved && settings.ShowMicrophoneMuteInSystray == saved, "edit was not pending");
                Set(receiver, "_verified", false);
                Call(context, "BatteryMonitor_ConnectionChanged", null, false);
                Check(!toggle.Enabled && !IsChecked() && Pending() == !saved,
                    "temporary loss of capability overwrote the pending choice");

                Set(receiver, "_verified", true);
                Call(context, "BatteryMonitor_ConnectionChanged", null, true);
                Check(ReferenceEquals(toggle, Get(form, "_showMicrophoneMuteInSystrayToggle")) &&
                    toggle.Enabled && IsChecked() == !saved && settings.ShowMicrophoneMuteInSystray == saved,
                    "reverification lost the pending edit or rebuilt Notifications");
            }
            finally
            {
                (Get(context, "_blinkTimer") as IDisposable)?.Dispose();
                (Get(context, "_currentApplicationIcon") as IDisposable)?.Dispose();
            }
        }
    }

    private static IHyperXDevice CreateUnverifiedDts(out CloudStingerCoreWirelessDevice driver)
    {
        object definition = typeof(CloudStingerCoreWirelessDevice)
            .GetField("DtsDefinition", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        driver = (CloudStingerCoreWirelessDevice)Activator.CreateInstance(
            typeof(CloudStingerCoreWirelessDevice), Instance, null, [definition], null)!;
        Set(driver, "_isConnected", true);
        Set(driver, "_battery", 80);
        IHyperXDevice receiver = HyperXDeviceManager.CreateDevice(Model)!;
        Set(receiver, "_active", driver);
        return receiver;
    }

    private static HyperXBatteryMonitorApplicationContext CreateContext(
        AppSettings settings, IHyperXDevice receiver, NotifyIcon notify)
    {
        // Use production handlers without starting HID monitoring, updater, or a visible tray icon.
        var context = (HyperXBatteryMonitorApplicationContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HyperXBatteryMonitorApplicationContext));
        Set(context, "_settings", settings);
        Set(context, "_device", receiver);
        Set(context, "_notifyIcon", notify);
        Set(context, "_notificationService", new NotificationService());
        Set(context, "_batteryRemainingTimeEstimator", new BatteryRemainingTimeEstimator(BatteryHistoryData.CreateCurrent()));
        return context;
    }

    private static void Call(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, Instance)!.Invoke(target, args);

    private static void PumpUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(condition(), "tray timer did not change phase");
    }
}
