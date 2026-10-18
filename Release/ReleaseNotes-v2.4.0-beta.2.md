# Hyper Battery Monitor v2.4.0 Beta 2

## Bug fixes

- **CloudX Stinger 3 Wireless (Xbox / BS1U0AA):** use its physically identified HID control collection (`03F0:0DC5`, `MI_03&Col05`, Usage Page `01C0`, Usage `0001`) instead of opening the first USB HID collection that matches the receiver's VID/PID. This targets both failed automatic detection and the *Disconnected* status after manual selection.
- **Cloud Stinger 3 protocol:** align incoming response and notification header checks with the NGENUITY 3.1.0 Windows input dispatcher. Preserve report type, command selector and status/battery-value validation; do not reject reports solely because of previously assumed intermediate header bytes.

## Unchanged

- Cloud Stinger 3 PC/PlayStation interface selection remains unchanged because their specific `MI/Col` paths have not been physically verified.
- Existing HyperX headset drivers, TIO support, Bluetooth fallback and the updater retain their prior behavior.
- Beta and Stable channels continue to work as in Beta 1.

## Testing requested

Please test **CloudX Stinger 3 Wireless Xbox (BS1U0AA)** with the headset powered on and the USB receiver connected. Check both automatic detection and manual device selection. Confirm whether the battery percentage and connection status update normally. Also test with NGENUITY fully exited if the headset remains disconnected.

This release is based on physical Windows USB interface enumeration and static protocol analysis. **Successful HID communication on the tester's hardware is not yet confirmed.** Please report your results at https://github.com/davidsantanaalves/HyperBatteryMonitor/issues.

## Download / version

- Tag: `v2.4.0-beta.2`
- Expected Windows installer: `HyperBatteryMonitor-Setup-v2.4.0-beta.2.exe`
- Displayed version: `v 2.4.0 [BETA]` (the Beta ordinal is used internally for update comparisons).
