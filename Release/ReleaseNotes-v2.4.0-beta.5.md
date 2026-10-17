# Hyper Battery Monitor v2.4.0 Beta 5 — Test candidate

## Clean installation reliability and diagnostics

- Corrects a misleading clean-install error: failure to migrate the Windows startup entry can no longer be reported as failure to delete user data. Startup migration is retried at normal application startup.
- Clean installation continues to remove **both** `%LOCALAPPDATA%\Hyper Battery Monitor` and the legacy `%LOCALAPPDATA%\HyperXBatteryTray` profile directories. Standard updates retain settings and battery history.
- Verifies that the expected profile path is a genuine directory rather than a reparse point; avoids reporting success for certain inaccessible paths.
- Retries transient file-system sharing violations, but still reports persistent deletion and access failures rather than ignoring them.
- Writes a diagnostic exception report to the **original user** `%TEMP%\HBM-Maintenance.log` when the user-data operation or startup registration migration fails. The log is outside the cleaned directories; review paths/usernames before sharing.
- Installer logging now distinguishes failure to launch the maintenance executable from a nonzero maintenance exit code.

## Compatibility — Cloud Stinger Core Wireless for PlayStation

- Adds a separately registered, documented HID receiver variant for **HyperX Cloud Stinger Core Wireless for PlayStation (SKU 4P5J1AA)**: `03F0:0A93`, `MI_03&Col03`, Usage `FF00:0001`. This variant joins the existing model's receiver-aware detection path for both manual selection and Auto Detect.
- The PlayStation receiver validates `FF 12 05` battery replies and retries stale `FF 01 00` responses (up to 10 reads at 20 ms intervals). **The original `0951:170B` receiver retains its original single-read behavior and response parsing.**
- Preserves the Beta 4 adaptive HID discovery, Windows HID-path offset correction, existing charging codes `03`/`05`/`06`, voltage curves, Bluetooth fallback and other drivers.
- The contributor to [PR #6](https://github.com/davidsantanaalves/HyperBatteryMonitor/pull/6) supplied the device descriptors and 52-query validation evidence. No PR merge was performed.
- **Known limitation:** charging voltage rises immediately on the PlayStation revision; the existing Kingston-derived voltage curve may overestimate displayed percent during charging. A new curve will require independent calibration evidence.
- **Validation status:** the Beta 5 clean-install candidate was built and installed successfully by the maintainer. This additional HID compatibility patch has not yet been compiled or physically tested in HBM on the PlayStation receiver, and Beta 5 is not published.

## Test cases before publishing

1. Upgrade without Clean Installation: `settings.json` and `battery-history.json` survive, device still connects.
2. Select Clean Installation: current and legacy data directories are removed, fresh profile is created on launch.
3. Run Clean Installation when a legacy startup registry value exists: deletion status is independent from a possible registry error.
4. Simulate an inaccessible file or folder in a controlled test profile: installer must fail and the `%TEMP%` diagnostic should identify the failing path; no false success.
5. Confirm HBM still detects and monitors a previously working headset after the installation.
6. Check manual selection and Auto Detect on `03F0:0A93` (including `MI_03&Col03`), valid battery parsing after stale `FF 01 00`, charging transitions `03`/`05`/`06`, receiver reconnect and headset power-cycle.
7. Recheck the original `0951:170B` single-read battery path and unrelated models for regressions; verify charge percentage is treated as an estimate while charging the PlayStation revision.

**Root cause status:** the false error caused by coupling startup registration to reset is corrected in code, but the specific exception experienced on the reporter's Windows machine is still unknown until a new installation test or the maintenance log confirms it.
