using System.Collections.Concurrent;
using System.Reflection;
using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Hid;
using HyperXBatteryTray.Monitoring;

internal static class Program
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
    private static T? Field<T>(CloudStingerCoreWirelessDevice device, string name) =>
        (T?)typeof(CloudStingerCoreWirelessDevice).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device);
    private static Task Reader(CloudStingerCoreWirelessDevice device) => Field<Task>(device, "_microphoneTask") ?? Task.CompletedTask;
    private static void AllowRetry(CloudStingerCoreWirelessDevice device) =>
        typeof(CloudStingerCoreWirelessDevice).GetField("_microphoneRetryAfter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(device, 0L);
    private static CloudStingerCoreWirelessDevice Dts() => new(CloudStingerCoreWirelessDevice.DtsDefinition);

    public static async Task Main()
    {
        (string Name, Func<Task> Test)[] tests =
        [
            ("packet parsing", Parsing),
            ("Kingston single read and unchanged feature commands", Kingston),
            ("PlayStation response validation/retry bounds", PlayStation),
            ("DTS response validation/retry bounds", DtsBattery),
            ("EOF recovery through receiver dispatcher and monitor", () => Recovery(null)),
            ("I/O failure recovery through receiver dispatcher and monitor", () => Recovery(new IOException("read failed"))),
            ("running battery monitor stays connected after microphone failure", MonitorLoop),
            ("unexpected read/disposal exceptions remain isolated", UnexpectedFailure),
            ("open failure and unavailable collection are rate limited", OpenFailure),
            ("cancellation releases a pending read", Cancellation),
            ("late read cannot mutate reconnected session or duplicate readers", StaleRead),
            ("disconnect during open rejects and disposes late stream", StaleOpen),
            ("unknown-state subscriber exception is observed", SubscriberFailure),
            ("blocked invalidation preserves battery, cleanup and event order", SlowInvalidation),
            ("disconnect during invalidation drops remaining old session delivery", StaleInvalidation),
            ("EOF disposal cannot block battery or charging", () => SlowCleanup(false, false)),
            ("disconnect disposal cannot block reconnect or battery", () => SlowCleanup(true, false)),
            ("blocking cancellation cannot block reconnect or battery", () => SlowCleanup(true, true)),
            ("Kingston receiver family capability", () => ReceiverCapability(CloudStingerCoreWirelessDevice.DeviceDefinition)),
            ("PlayStation receiver family capability", () => ReceiverCapability(CloudStingerCoreWirelessDevice.PlayStationDefinition)),
            ("DTS capability requires receiver verification", () => ReceiverCapability(CloudStingerCoreWirelessDevice.DtsDefinition)),
            ("unrelated receiver capability fallback is unchanged", OtherFamilyCapability)
        ];
        foreach (var test in tests)
        {
            HidConnection.Reset();
            await test.Test().WaitAsync(TimeSpan.FromSeconds(15));
            Console.WriteLine("PASS " + test.Name);
        }
        Console.WriteLine($"All {tests.Length} regression scenarios passed.");
    }

    private static async Task SlowInvalidation()
    {
        var disposalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new PacketStream { BeforeDispose = () => disposalStarted.TrySetResult() };
        var next = new PacketStream();
        HidConnection.OpenMicrophone = _ => HidConnection.MicrophoneOpens == 1 ? first : next;
        HidConnection.BatteryReply = _ => HidConnection.BatteryPacket(charge: 5);
        using var driver = Dts();
        using var release = new ManualResetEventSlim();
        var invalidated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<string>();
        driver.MicrophoneMuteChanged += (_, muted) => events.Enqueue(muted ? "muted" : "open");
        driver.MicrophoneMuteStateInvalidated += (_, _) =>
        {
            events.Enqueue("unknown");
            invalidated.TrySetResult(Monitor.IsEntered(Field<object>(driver, "_featureLock")!));
            release.Wait(Timeout + Timeout);
        };

        Check(driver.Connect() && await driver.QueryBatteryAsync() is >= 0, "initial battery sample");
        await first.Reading.Task.WaitAsync(Timeout);
        first.Packet(0x65, 0x04);
        await Until(() => events.Count == 1);
        Task reader = Reader(driver);
        first.Packet(); // EOF

        try
        {
            Check(!await invalidated.Task.WaitAsync(Timeout), "external callback owns _featureLock");
            Task<int?> battery = Task.Run(() => driver.QueryBatteryAsync());
            Task<bool?> charge = Task.Run(() => driver.QueryChargeStatusAsync());
            Check(await battery.WaitAsync(Timeout) is >= 0, "battery stalled during invalidation");
            Check(await charge.WaitAsync(Timeout) == true, "charging stalled during invalidation");
            await disposalStarted.Task.WaitAsync(Timeout);
            Check(!driver.IsMicrophoneMuteStateKnown && !reader.IsCompleted &&
                HidConnection.MicrophoneOpens == 1 && HidConnection.Commands.Count == 4,
                "reader, mute state or HID traffic changed while subscriber was blocked");
        }
        finally
        {
            release.Set();
            await reader.WaitAsync(Timeout);
        }

        AllowRetry(driver);
        Check(await driver.QueryBatteryAsync() is >= 0, "battery after cleanup");
        await next.Reading.Task.WaitAsync(Timeout);
        next.Packet(0x65, 0x04);
        await Until(() => events.Count == 3);
        Check(events.SequenceEqual(["muted", "unknown", "muted"]) &&
            HidConnection.Commands.Count == 6 && first.DisposeCount == 1,
            "mute publication order, stream ownership or HID traffic changed");
        Task recoveredReader = Reader(driver);
        driver.Disconnect();
        await recoveredReader.WaitAsync(Timeout);
    }

    private static async Task StaleInvalidation()
    {
        var stream = new PacketStream();
        HidConnection.OpenMicrophone = _ => stream;
        using var driver = Dts();
        bool reconnected = false;
        int laterDeliveries = 0;
        driver.MicrophoneMuteStateInvalidated += (_, _) =>
        {
            driver.Disconnect();
            reconnected = driver.Connect();
        };
        driver.MicrophoneMuteStateInvalidated += (_, _) => laterDeliveries++;

        Check(driver.Connect() && await driver.QueryBatteryAsync() is >= 0, "initial battery sample");
        await stream.Reading.Task.WaitAsync(Timeout);
        stream.Packet(0x65, 0x04);
        await Until(() => driver.IsMicrophoneMuteStateKnown);
        Task reader = Reader(driver);
        stream.Packet();
        await reader.WaitAsync(Timeout);
        Check(reconnected && driver.IsConnected && !driver.IsMicrophoneMuteStateKnown &&
            laterDeliveries == 0 && stream.DisposeCount == 1, "old session invalidation reached the new session");
    }

    private static Task Parsing()
    {
        Check(CloudStingerCoreWirelessDevice.ParseDtsMicrophoneReport([0x65, 0]) == false, "boom down");
        Check(CloudStingerCoreWirelessDevice.ParseDtsMicrophoneReport([0x65, 4]) == true, "boom up");
        foreach (byte[] packet in new byte[][] { [], [0x65], [0x65, 4, 0], [0x65, 1], [0x64, 3], [0x64, 1], [1, 1] })
            Check(CloudStingerCoreWirelessDevice.ParseDtsMicrophoneReport(packet) == null, "invalid packet accepted");
        return Task.CompletedTask;
    }

    private static async Task Kingston()
    {
        using var device = new CloudStingerCoreWirelessDevice();
        HidConnection.BatteryReply = _ => { var packet = HidConnection.BatteryPacket(); packet[1] = 0; return packet; };
        Check(device.Connect() && await device.QueryBatteryAsync() == 80, "Kingston must not require FF 12 05");
        Check(HidConnection.ConnectionReads == 1 && HidConnection.BatteryReads == 1, "Kingston must read exactly once per query");
        Check(!device.SupportsMicrophoneMuteMonitoring && HidConnection.MicrophoneOpens == 0, "Kingston microphone changed");
        Check(HidConnection.Commands.Count == 2 && Convert.ToHexString(HidConnection.Commands[0]) ==
            "FF0700FD04000002820305290F00000368100000" && Convert.ToHexString(HidConnection.Commands[1]) ==
            "FF0700FD040CF1020104F00C0000000000000000000000000000000000000000", "feature commands changed");
    }

    private static Task PlayStation() => ValidatedBattery(CloudStingerCoreWirelessDevice.PlayStationDefinition);
    private static Task DtsBattery() => ValidatedBattery(CloudStingerCoreWirelessDevice.DtsDefinition);
    private static async Task ValidatedBattery(HyperXDeviceDefinition definition)
    {
        using var device = new CloudStingerCoreWirelessDevice(definition);
        Check(device.Connect(), "connect");
        HidConnection.BatteryReply = attempt => attempt == 1 ? new byte[64] : HidConnection.BatteryPacket();
        Check(await device.QueryBatteryAsync() == 80 && HidConnection.BatteryReads == 2, "stale response must be retried");
        await Reader(device).WaitAsync(Timeout);
        int reads = HidConnection.BatteryReads;
        HidConnection.BatteryReply = _ => new byte[64];
        Check(await device.QueryBatteryAsync() == null && HidConnection.BatteryReads - reads == 10, "retry must stop after ten reads");
        reads = HidConnection.BatteryReads;
        HidConnection.BatteryReply = _ => null;
        Check(await device.QueryBatteryAsync() == null && HidConnection.BatteryReads - reads == 1, "null report must not retry");
        reads = HidConnection.BatteryReads;
        HidConnection.BatteryReply = attempt => attempt == 1 ? new byte[2] : HidConnection.BatteryPacket();
        Check(await device.QueryBatteryAsync() == 80 && HidConnection.BatteryReads - reads == 2, "short response must be retried");
        if (definition.ProductId == 0x0A93)
            Check(!device.SupportsMicrophoneMuteMonitoring && HidConnection.MicrophoneOpens == 0, "PlayStation microphone changed");
        device.Disconnect();
        await Reader(device).WaitAsync(Timeout);
    }

    private static async Task Recovery(Exception? failure)
    {
        var first = new PacketStream(); var second = new PacketStream();
        HidConnection.OpenMicrophone = _ => HidConnection.MicrophoneOpens == 1 ? first : second;
        HidConnection.BatteryReply = _ => HidConnection.BatteryPacket(charge: 5);
        using var driver = Dts();
        using var receiver = new ReceiverAwareDevice("HyperX Cloud Stinger Core Wireless + 7.1",
            [new() { Definition = CloudStingerCoreWirelessDevice.DtsDefinition, Factory = () => driver }]);
        using var monitor = new BatteryMonitor(receiver);
        var events = new ConcurrentQueue<bool>(); int invalidations = 0;
        monitor.MicrophoneMuteChanged += (_, muted) => events.Enqueue(muted);
        monitor.MicrophoneMuteStateInvalidated += (_, _) => Interlocked.Increment(ref invalidations);
        Check(receiver.Connect() && (await receiver.QueryBatteryAsync()).HasValue, "battery connection");
        await first.Reading.Task.WaitAsync(Timeout);
        int commands = HidConnection.Commands.Count;
        Check(await receiver.QueryMicrophoneMuteStatusAsync() == null, "initial state must be unknown");
        first.Packet(0x65, 4); first.Packet(0x65, 4); first.Packet(0x64, 3); first.Packet(0x65); first.Packet(0x65, 0);
        await Until(() => events.Count == 2);
        Check(events.SequenceEqual(new[] { true, false }), "duplicate/unrelated suppression");
        first.Packet(0x65, 4);
        await Until(() => events.Count == 3);
        int battery = receiver.Battery;
        if (failure == null) first.Packet(); else first.Fail(failure);
        await Reader(driver).WaitAsync(Timeout);
        Check(first.Disposed && Reader(driver).IsCompletedSuccessfully, "reader did not cleanly finish");
        Check(receiver.IsConnected && receiver.Battery == battery && receiver.IsCharging, "microphone failure altered battery/charging connection");
        Check(!receiver.IsMicrophoneMuteStateKnown && !receiver.IsMicrophoneMuted && invalidations == 1, "unknown must propagate through dispatcher/monitor");
        Check(await receiver.QueryMicrophoneMuteStatusAsync() == null, "failed listener must not expose stale mute");
        Check(HidConnection.Commands.Count == commands, "microphone packets/failure generated HID query traffic");
        for (int i = 0; i < 3; i++) Check(await receiver.QueryBatteryAsync() == battery, "battery must continue after failure");
        Check(HidConnection.MicrophoneOpens == 1, "retry must respect cooldown");
        AllowRetry(driver);
        Check(await receiver.QueryBatteryAsync() == battery, "battery during recovery");
        await second.Reading.Task.WaitAsync(Timeout);
        Check(!receiver.IsMicrophoneMuteStateKnown, "recovery cannot infer initial state");
        second.Packet(0x65, 4);
        await Until(() => events.Count == 4);
        Check(receiver.IsMicrophoneMuted, "same mute value after recovery must reach monitor again");
        for (int i = 0; i < 3; i++) await receiver.QueryBatteryAsync();
        Check(HidConnection.MicrophoneOpens == 2, "healthy reader was duplicated");
        receiver.Disconnect();
        await Reader(driver).WaitAsync(Timeout);
        Check(second.Disposed && invalidations == 1, "cancellation emitted stale invalidation");
    }

    private static async Task MonitorLoop()
    {
        var stream = new PacketStream(); HidConnection.OpenMicrophone = _ => stream;
        using var device = Dts();
        using var monitor = new BatteryMonitor(device) { IntervalMilliseconds = 20 };
        var connections = new ConcurrentQueue<bool>();
        monitor.ConnectionChanged += (_, connected) => connections.Enqueue(connected);
        monitor.Start();
        await stream.Reading.Task.WaitAsync(Timeout);
        await Until(() => connections.Contains(true));
        stream.Packet(0x65, 4); await Until(() => device.IsMicrophoneMuteStateKnown);
        stream.Fail(new IOException("listener lost")); await Reader(device).WaitAsync(Timeout);
        int reads = Volatile.Read(ref HidConnection.BatteryReads);
        await Until(() => Volatile.Read(ref HidConnection.BatteryReads) >= reads + 3);
        Check(connections.SequenceEqual(new[] { true }) && device.IsConnected && device.Battery == 80,
            "running monitor disconnected after auxiliary failure");
        Check(!device.IsMicrophoneMuteStateKnown && HidConnection.MicrophoneOpens == 1,
            "running monitor restored stale mute or bypassed cooldown");
        Task loop = (Task)typeof(BatteryMonitor).GetField("_monitorTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor)!;
        monitor.Dispose(); await loop.WaitAsync(Timeout);
        await Reader(device).WaitAsync(Timeout);
    }

    private static async Task UnexpectedFailure()
    {
        var stream = new PacketStream { ThrowOnDispose = true };
        HidConnection.OpenMicrophone = _ => stream;
        using var device = Dts();
        Check(device.Connect() && (await device.QueryBatteryAsync()).HasValue, "connect");
        await stream.Reading.Task.WaitAsync(Timeout);
        stream.Fail(new InvalidOperationException("unexpected read error"));
        await Reader(device).WaitAsync(Timeout);
        Check(Reader(device).IsCompletedSuccessfully && device.IsConnected, "unexpected failure escaped task");
        Check((await device.QueryBatteryAsync()).HasValue, "battery after unexpected error");
    }

    private static async Task OpenFailure()
    {
        using var device = Dts();
        HidConnection.OpenMicrophone = _ => throw new UnauthorizedAccessException("cannot open");
        Check(device.Connect() && (await device.QueryBatteryAsync()).HasValue, "open exception escaped battery query");
        await Reader(device).WaitAsync(Timeout);
        for (int i = 0; i < 3; i++) await device.QueryBatteryAsync();
        Check(HidConnection.MicrophoneOpens == 1 && device.IsConnected, "failed open was not rate limited");
        AllowRetry(device);
        HidConnection.OpenMicrophone = _ => null;
        await device.QueryBatteryAsync(); await Reader(device).WaitAsync(Timeout);
        await device.QueryBatteryAsync();
        Check(HidConnection.MicrophoneOpens == 2 && device.IsConnected, "missing collection was not rate limited");
        AllowRetry(device);
        var stream = new PacketStream(); HidConnection.OpenMicrophone = _ => stream;
        await device.QueryBatteryAsync(); await stream.Reading.Task.WaitAsync(Timeout);
        stream.Packet(0x65, 0); await Until(() => device.IsMicrophoneMuteStateKnown);
        device.Disconnect(); await Reader(device).WaitAsync(Timeout);
        Check(stream.Disposed, "recovered stream leaked");
    }

    private static async Task Cancellation()
    {
        var stream = new PacketStream(); HidConnection.OpenMicrophone = _ => stream;
        using var device = Dts();
        Check(device.Connect(), "connect"); await device.QueryBatteryAsync();
        await stream.Reading.Task.WaitAsync(Timeout);
        Task task = Reader(device);
        device.Disconnect(); await task.WaitAsync(Timeout);
        Check(task.IsCompletedSuccessfully && stream.Disposed && !device.IsMicrophoneMuteStateKnown, "cancel cleanup");
        Check(Field<CancellationTokenSource>(device, "_microphoneCancellation") == null && Field<Stream>(device, "_microphoneStream") == null, "reader resources retained");
    }

    private static async Task StaleRead()
    {
        var old = new PacketStream { IgnoreCancellationAndDisposal = true };
        var next = new PacketStream(); HidConnection.OpenMicrophone = _ => HidConnection.MicrophoneOpens == 1 ? old : next;
        using var device = Dts(); int callbacks = 0;
        device.MicrophoneMuteChanged += (_, _) => Interlocked.Increment(ref callbacks);
        device.MicrophoneMuteStateInvalidated += (_, _) => Interlocked.Increment(ref callbacks);
        Check(device.Connect(), "connect"); await device.QueryBatteryAsync(); await old.Reading.Task.WaitAsync(Timeout);
        Task oldTask = Reader(device);
        device.Disconnect(); Check(device.Connect(), "reconnect"); await device.QueryBatteryAsync();
        Check(ReferenceEquals(oldTask, Reader(device)) && HidConnection.MicrophoneOpens == 1, "reader duplicated before canceled task finished");
        old.Packet(0x65, 4); await oldTask.WaitAsync(Timeout);
        Check(callbacks == 0 && device.IsConnected && !device.IsMicrophoneMuteStateKnown, "stale packet/finally affected new session");
        await device.QueryBatteryAsync(); await next.Reading.Task.WaitAsync(Timeout);
        next.Packet(0x65, 0); await Until(() => callbacks == 1);
        device.Disconnect(); await Reader(device).WaitAsync(Timeout);
    }

    private static async Task StaleOpen()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new PacketStream();
        HidConnection.OpenMicrophone = _ => { entered.TrySetResult(); Check(release.Wait(Timeout), "open not released"); return late; };
        using var device = Dts();
        Check(device.Connect(), "connect"); await device.QueryBatteryAsync(); await entered.Task.WaitAsync(Timeout);
        Task oldTask = Reader(device);
        try
        {
            device.Disconnect(); Check(device.Connect(), "reconnect during open");
            Check((await device.QueryBatteryAsync()).HasValue, "slow open blocked battery query");
            Check(HidConnection.MicrophoneOpens == 1, "duplicate open");
        }
        finally { release.Set(); }
        await oldTask.WaitAsync(Timeout);
        Check(late.Disposed && !late.Reading.Task.IsCompleted && !device.IsMicrophoneMuteStateKnown && device.IsConnected, "late stream was accepted or leaked");
    }

    private static async Task SubscriberFailure()
    {
        var stream = new PacketStream(); HidConnection.OpenMicrophone = _ => stream;
        using var device = Dts();
        device.MicrophoneMuteStateInvalidated += (_, _) => throw new InvalidOperationException("subscriber failure");
        Check(device.Connect(), "connect"); await device.QueryBatteryAsync(); await stream.Reading.Task.WaitAsync(Timeout);
        stream.Packet(0x65, 4); await Until(() => device.IsMicrophoneMuteStateKnown);
        stream.Packet(); await Reader(device).WaitAsync(Timeout);
        Check(Reader(device).IsCompletedSuccessfully && device.IsConnected && !device.IsMicrophoneMuteStateKnown, "callback fault escaped background task");
    }

    private static async Task SlowCleanup(bool disconnect, bool blockCancellation)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Block() { entered.TrySetResult(); Check(release.Wait(Timeout), "cleanup not released"); }
        var stream = new PacketStream
        {
            BeforeDispose = blockCancellation ? null : Block,
            OnCancellation = blockCancellation ? Block : null
        };
        HidConnection.OpenMicrophone = _ => stream;
        HidConnection.BatteryReply = _ => HidConnection.BatteryPacket(charge: 5);
        using var device = Dts();
        Check(device.Connect(), "connect"); await device.QueryBatteryAsync();
        await stream.Reading.Task.WaitAsync(Timeout);
        stream.Packet(0x65, 4); await Until(() => device.IsMicrophoneMuteStateKnown);
        Task reader = Reader(device);
        Task trigger = disconnect ? Task.Run(device.Disconnect) : Task.Run(() => stream.Packet());
        try
        {
            await entered.Task.WaitAsync(Timeout);
            await trigger.WaitAsync(TimeSpan.FromSeconds(1));
            if (disconnect) Check(device.Connect(), "reconnect during cleanup");
            else Check(!device.IsMicrophoneMuteStateKnown, "stale mute while disposal is blocked");
            // Run calls on another thread because a lock regression blocks
            // synchronously before Query* returns its task. Never release
            // disposal until both queries have completed independently.
            int? battery = await Task.Run(() => device.QueryBatteryAsync()).WaitAsync(TimeSpan.FromSeconds(1));
            bool? charging = await Task.Run(() => device.QueryChargeStatusAsync()).WaitAsync(TimeSpan.FromSeconds(1));
            Check(battery.HasValue && charging == true, "battery/charging blocked by cleanup");
            AllowRetry(device); await device.QueryBatteryAsync();
            Check(!reader.IsCompleted && HidConnection.MicrophoneOpens == 1, "duplicate reader during cleanup");
        }
        finally { release.Set(); }
        await trigger.WaitAsync(Timeout); await reader.WaitAsync(Timeout);
        Check(stream.DisposeCount == 1 && reader.IsCompletedSuccessfully, "cleanup must own and dispose stream exactly once");
    }

    private static async Task ReceiverCapability(HyperXDeviceDefinition selected)
    {
        HyperXDeviceDefinition[] definitions = [CloudStingerCoreWirelessDevice.DeviceDefinition,
            CloudStingerCoreWirelessDevice.PlayStationDefinition, CloudStingerCoreWirelessDevice.DtsDefinition];
        HidConnection.Present = definition => definition.ProductId == selected.ProductId;
        var drivers = new List<CloudStingerCoreWirelessDevice>();
        using var receiver = new ReceiverAwareDevice("HyperX Cloud Stinger Core Wireless + 7.1",
            definitions.Select(definition => new HyperXDeviceRegistration
            {
                Definition = definition,
                Factory = () => { var driver = new CloudStingerCoreWirelessDevice(definition); drivers.Add(driver); return driver; }
            }).ToArray());
        Check(!receiver.SupportsMicrophoneMuteMonitoring, "family fallback advertised DTS before discovery");
        Check(receiver.Connect(), "receiver connect");
        Check(!receiver.SupportsMicrophoneMuteMonitoring, "unverified receiver advertised microphone");
        HidConnection.BatteryReply = _ => null;
        Check(await receiver.QueryBatteryAsync() == null && !receiver.SupportsMicrophoneMuteMonitoring,
            "failed verification advertised microphone");
        HidConnection.BatteryReply = _ => HidConnection.BatteryPacket();
        Check((await receiver.QueryBatteryAsync()).HasValue, "receiver verification");
        Check(receiver.SupportsMicrophoneMuteMonitoring == (selected.ProductId == 0x0A8E), "wrong receiver capability");
        receiver.Disconnect();
        Check(!receiver.SupportsMicrophoneMuteMonitoring, "disconnected family advertised microphone");
        foreach (var driver in drivers) await Reader(driver).WaitAsync(Timeout);
    }

    private static Task OtherFamilyCapability()
    {
        var definition = new HyperXDeviceDefinition { Name = "Unrelated receiver", VendorId = 0x0951,
            ProductId = 0x1234, InterfacePattern = "test", SupportsMicrophoneMuteMonitoring = true,
            BatteryCommandBytes = [], BatteryByteIndex = 0 };
        using var receiver = new ReceiverAwareDevice("Other family", [new() { Definition = definition,
            Factory = () => new CloudStingerCoreWirelessDevice(definition) }]);
        Check(receiver.SupportsMicrophoneMuteMonitoring, "unrelated disconnected fallback changed");
        Check(receiver.Connect() && receiver.SupportsMicrophoneMuteMonitoring, "unrelated unverified fallback changed");
        return Task.CompletedTask;
    }
}
