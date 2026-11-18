# Cloud Stinger Core regression tests

Run with the .NET 10 SDK (no headset or additional test packages required):

```sh
dotnet run --project Tests/StingerCore/StingerCore.Tests.csproj -c Release
```

The runner exits nonzero on failure. It compiles the production Stinger driver,
device base, receiver dispatcher and battery monitor directly from their source
files. Only the Windows HID boundary, endpoint preference persistence, diagnostic
sink and unrelated Bluetooth/platform metadata are replaced with test doubles.
No production behavior is copied into a fake device base.

Coverage:

- Kingston `0951:170B`: original feature command bytes, one connection read and
  one battery read, no new header validation and no microphone listener.
- PlayStation `03F0:0A93` and DTS `03F0:0A8E`: stale/short response rejection,
  successful retry, ten-read limit and early return on a missing report.
- Exact captured mute packets, malformed/unrelated reports and duplicate events.
- Unknown initial state, EOF and read failure, resource cleanup, continued battery
  and charging monitoring, throttled reopen and return to event-driven mute state.
- Invalidation forwarding through the production receiver dispatcher and monitor;
  the same mute value must be delivered again after recovery.
- A running battery monitor continues polling after microphone failure without
  emitting a disconnected state; microphone events add no HID query traffic.
- Missing collection, access denied, unexpected read/disposal/subscriber errors.
- Cancellation of pending reads, disconnect during open, and a deliberately late
  read that ignores cancellation/disposal: stale packets and cleanup cannot alter
  the reconnected device, and another reader cannot start before the old task ends.

Tests inspect the tracked task and advance the private retry deadline using
reflection so they can verify recovery without sleeping for ten seconds. They
first verify that ordinary battery queries respect the real cooldown. Asynchronous
checks have bounded timeouts; no device changes or USB writes are performed.

The Windows application is built separately to check the UI subscriptions and
production HID implementation. These tests do not claim physical validation of
Kingston/PlayStation receivers, Windows device discovery, charging calibration,
multiple DTS receivers, or the maintainer's unpublished Beta 3 integration source.
