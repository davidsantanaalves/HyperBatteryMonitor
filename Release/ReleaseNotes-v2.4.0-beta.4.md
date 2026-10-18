# Hyper Battery Monitor v2.4.0 Beta 4

This experimental pre-release improves device detection and HID receiver compatibility. It is intended for users willing to help validate newer receiver variants.

## Adaptive HID receiver discovery

- Tries known compatible HID control collections and receiver variants in sequence, confirming a valid battery response before choosing a device.
- Shares this validated receiver-selection approach between manual headset selection and automatic device detection.
- Remembers the previously validated `MI/Col` collection for a headset family and receiver VID/PID, prioritizing it after reconnection but always validating it again before use.
- Stores only the validated interface selector, not the raw device path or hardware serial number.
- Avoids selecting arbitrary receivers when multiple dedicated devices respond; maintains single-device routing for supported TIO configurations.
- Preserves the USB/HID-first policy and the existing Bluetooth battery fallback where Windows exposes battery level.

## HID compatibility fix

- Corrects Windows SetupAPI Unicode HID device path extraction: `DevicePath` begins at byte offset 4 even when the x64 structure's `cbSize` is 8.
- Adds candidate validation for the alternate HyperX Cloud III S Wireless receiver `03F0:02CC`, guided by NGENUITY research for Usage Page `0x01C0` and Usage `0x0001`.
- Retains established interface behavior for legacy drivers and the targeted Cloud Stinger 3 Xbox receiver fix shipped in Beta 2.

## Compatibility notes and known limitations

- **Cloud III S Wireless (`03F0:02CC`):** this is a candidate compatibility fix, **not yet confirmed on the tester's hardware**. Please report whether HBM detects the headset, displays the battery percentage, and maintains the connection after reconnecting the dongle.
- Other alternate receivers and experimental TIO/Bluetooth scenarios still require broader physical-hardware validation.
- The prior clean-install maintenance error was not reproduced on retest and has been closed as *Cannot Reproduce*; no root-cause fix is claimed.
- Inno Setup may show a nonblocking admin/HKCU advisory during build. The installer continues to use original-user maintenance operations for profile migration.

## Feedback

If the headset is not detected, include the exact model/connection type, Windows VID/PID, observed HBM status and whether HyperX NGENUITY displays a battery percentage. Report issues at: https://github.com/davidsantanaalves/HyperBatteryMonitor/issues
