# Hyper Battery Monitor v2.4.0 — Beta 5

Beta 5 adds a new HyperX Cloud Stinger Core Wireless receiver variant, fixes clean-installation reliability, and includes the adaptive HID discovery improvements introduced in Beta 4. This is a **GitHub pre-release**, not the stable v2.4.0 release or a Microsoft Store update.

## New in Beta 5

### Clean installation: reliability and diagnostics

- Fixed a misleading installer failure: the user-data cleanup and Windows startup-registration migration are now independent. A startup Registry migration error no longer makes an otherwise successful clean installation appear to have failed; it is logged and can be retried on normal startup.
- **Clean Installation** removes the current `%LOCALAPPDATA%\Hyper Battery Monitor` and legacy `%LOCALAPPDATA%\HyperXBatteryTray` data directories, including previous settings and battery history. A **normal upgrade preserves** settings and battery history.
- Added explicit checks for unsafe/reparse-point data paths, verification of removal, and limited retries for transient file-sharing errors. Persistent file-system failures are not silently ignored.
- Improved installer maintenance error reporting and added `%TEMP%\HBM-Maintenance.log` for actionable .NET exception details. The log is written outside the deleted settings directories; inspect local file paths before sharing it.
- **Maintainer validation:** Beta 5 installer normal-upgrade and clean-installation tests completed without the previously reported error.

### HyperX Cloud Stinger Core Wireless — PlayStation receiver

- Added the original receiver for **HyperX Cloud Stinger Core Wireless for PlayStation, SKU 4P5J1AA**: USB `03F0:0A93` (revision `4103`), HID `MI_03&Col03`, Usage Page `FF00`, Usage `0001`.
- The PlayStation variant uses the existing **Cloud Stinger Core Wireless + 7.1** model selection in both manual selection and Auto Detect; it participates in receiver-aware detection.
- On **this PlayStation receiver only**, validates battery responses beginning `FF 12 05` and retries stale `FF 01 00` responses (up to 10 reads, 20 ms spacing).
- **Original receiver `0951:170B` behavior remains unchanged:** its existing single-read battery protocol is preserved.
- Does not modify either battery-voltage curve or the unrelated headset drivers.
- Credit: thanks to the contributor of [PR #6](https://github.com/davidsantanaalves/HyperBatteryMonitor/pull/6) for receiver identification, HID descriptors, 52 per-query diagnostic samples, retry findings, and charging/reconnection checks. This support was integrated locally without merging the PR.

## Included from Beta 4 — Adaptive HID detection and compatibility

Beta 5 contains **all** of the following Beta 4 changes:

- **Adaptive HID endpoint selection:** scans known eligible receiver/collection candidates, verifies that a candidate actually returns a valid battery response, and does not simply trust the first matching VID/PID.
- Uses the same validated receiver selection for **Auto Detect and manual headset selection**.
- **Remembers a previously validated collection** (`MI/Col`) for the receiver/model and prioritizes it on future connections; revalidates before use. The preference stores the interface selector, not the raw hardware path or serial number.
- Preserves safeguards against incorrect selection among multiple dedicated/TIO receivers, including one-device-at-a-time TIO routing and USB/HID priority over Windows Bluetooth battery fallback.
- **Windows HID compatibility fix:** corrects SetupAPI Unicode `DevicePath` extraction (path offset **4 bytes**, even when x64 interface-detail `cbSize` is **8**). This affects the reliability of opening HID device paths.
- Improved candidate interface handling for the **HyperX Cloud III S Wireless alternative USB receiver `03F0:02CC`**, including the documented control Usage Page `01C0` / Usage `0001` candidate rather than relying only on VID/PID.
- **Physical feedback since Beta 4:** a Cloud III S Wireless tester reports that the device is now correctly detected and works with Beta 4. This confirms that tester's receiver/setup, not every Cloud III S hardware revision.

## Other v2.4.0 Beta-series improvements retained

- Additional supported headset families/variants: **Cloud Alpha 2**, **Cloud Stinger 3 (PC/PlayStation/Xbox)**, **Cloud Flight 2 Xbox**, alternative **Cloud III/Cloud III S** receivers, and supported **Cloud III S/Cloud Flight 2** configurations on HyperX Three-In-One (TIO) receivers.
- A targeted **CloudX Stinger 3 Wireless Xbox** receiver/interface and response-parsing correction first included in Beta 2; validation on every Xbox receiver revision remains pending.
- Read-only **Windows Bluetooth battery-percentage fallback** for supported Cloud III S, Cloud Alpha 2, Cloud Flight 2, Cloud Stinger 3, and Cloud MIX 2 configurations, while prioritizing verified USB/HID readings. Bluetooth availability depends on Windows and the device; microphone/charging/remaining-time telemetry is not inferred from Bluetooth battery data.
- **Stable/Beta update channels**, numbered Beta version display, and explicit safety confirmations when choosing to return from a Beta to an older Stable release.
- Inline **See what's new** GitHub release notes in a native Light/Dark/System-themed dialog, alongside a link to the original release.
- Window-focus/z-order fixes (HBM dialogs remain above their owners without forcing the whole application always on top) and consistent themed vertical scrollbars.
- Improved automatic detection UX: a **15-second** search limit, **1.5-second** minimum display time after very fast detection, a live countdown, and immediate Cancel.
- Additional device artwork and expanded localized device/receiver information.

## Tested and still under validation

- **Confirmed by maintainer:** Beta 5 builds successfully in Debug; the clean-installation installer path passed testing; regression checks on the maintainer's HyperX Cloud III passed.
- **Confirmed by tester:** Cloud III S Wireless detection works with the Beta 4 adaptive HID implementation on that tester's hardware.
- **PlayStation Cloud Stinger Core:** HID identity, response/retry behavior and charging codes `03` (not charging), `05` (charging), `06` (fully charged) are backed by contributor diagnostics. **End-to-end hardware validation of this locally integrated Beta 5 implementation is still pending**. A successful .NET Debug build and unrelated-headset regression testing do not replace that test.
- **Known limitation:** charging raises terminal voltage on this PlayStation receiver revision; the existing voltage-to-percentage curve may **overestimate charge percentage while charging**. No curve recalibration was included.
- Other experimental receivers, TIO configurations, and Bluetooth integrations may require further physical-device testing. TIO monitoring remains limited to one selected supported child headset at a time.

If you discover a device detection, battery accuracy, upgrade, or installation issue, please report the model, receiver VID/PID and interface (when available), connection type, installed HBM version, and observed behavior at [GitHub Issues](https://github.com/davidsantanaalves/HyperBatteryMonitor/issues). Do not publish personal device serial numbers.

- **Tag:** `v2.4.0-beta.5`
- **Windows installer:** `HyperBatteryMonitor-Setup-v2.4.0-beta.5.exe`
- **Distribution:** GitHub Pre-release (not Microsoft Store)
