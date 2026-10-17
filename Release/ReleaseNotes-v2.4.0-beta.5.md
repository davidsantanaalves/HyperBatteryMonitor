# Hyper Battery Monitor v2.4.0 Beta 5 — Test candidate

## Clean installation reliability and diagnostics

- Corrects a misleading clean-install error: failure to migrate the Windows startup entry can no longer be reported as failure to delete user data. Startup migration is retried at normal application startup.
- Clean installation continues to remove **both** `%LOCALAPPDATA%\Hyper Battery Monitor` and the legacy `%LOCALAPPDATA%\HyperXBatteryTray` profile directories. Standard updates retain settings and battery history.
- Verifies that the expected profile path is a genuine directory rather than a reparse point; avoids reporting success for certain inaccessible paths.
- Retries transient file-system sharing violations, but still reports persistent deletion and access failures rather than ignoring them.
- Writes a diagnostic exception report to the **original user** `%TEMP%\HBM-Maintenance.log` when the user-data operation or startup registration migration fails. The log is outside the cleaned directories; review paths/usernames before sharing.
- Installer logging now distinguishes failure to launch the maintenance executable from a nonzero maintenance exit code.

## Compatibility

- Retains the **Beta 4 adaptive HID discovery**, including the Windows HID-path offset correction; no device commands, drivers, Bluetooth fallback, or Auto Detect behavior changed in this patch.
- Beta 5 is a **development/test candidate**, not yet compiled, installed, or published.

## Test cases before publishing

1. Upgrade without Clean Installation: `settings.json` and `battery-history.json` survive, device still connects.
2. Select Clean Installation: current and legacy data directories are removed, fresh profile is created on launch.
3. Run Clean Installation when a legacy startup registry value exists: deletion status is independent from a possible registry error.
4. Simulate an inaccessible file or folder in a controlled test profile: installer must fail and the `%TEMP%` diagnostic should identify the failing path; no false success.
5. Confirm HBM still detects and monitors a previously working headset after the installation.

**Root cause status:** the false error caused by coupling startup registration to reset is corrected in code, but the specific exception experienced on the reporter's Windows machine is still unknown until a new installation test or the maintenance log confirms it.
