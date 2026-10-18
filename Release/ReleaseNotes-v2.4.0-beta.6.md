# Hyper Battery Monitor v2.4.0 Beta 6

## Fixed — Clean Installation from the in-app updater

- Fixed the reproducible failure when **Clean Installation** was selected after an update launched from the application. In Beta 5, the updater started Setup directly from `%LOCALAPPDATA%\Hyper Battery Monitor\Updates`, and the running installer executable could prevent the entire profile folder from being removed. The maintenance log confirmed a sharing violation on `HyperBatteryMonitor-Setup-v2.4.0-beta.5.exe`.
- The Beta 6 updater now launches a verified copy from a unique folder under the **current Windows user's `%TEMP%\HyperBatteryMonitor\Updates`**, outside both HBM user-data directories. The launcher verifies the original cached installer's size and SHA-256, then verifies the staged copy's size and SHA-256 before requesting the normal Windows installer elevation. Staging itself does not need administrator privileges. Setup still defaults to **preserving data**; Clean Installation remains an explicit choice.
- Includes a transition fix for **upgrading from Beta 5 using its existing updater**: when a new Setup is still running from the old data-directory cache, Clean Installation removes all settings, history, and other files, and tolerates **only the locked installer executable whose exact filename matches the version being installed**. The leftover Setup file is retried for removal during subsequent normal application startups. Unrelated locked files, inaccessible paths, unexpected contents and directory links remain errors.
- The Beta 5 maintenance diagnostics in `%TEMP%\HBM-Maintenance.log` are preserved; a successful cleanup with one temporarily locked Setup executable now records that circumstance explicitly.
- Normal upgrades continue preserving settings and battery history. No headset/HID drivers, battery curves, or update-channel selection policy are changed by Beta 6.
- Debug-only updater integration testing: `--test` continues to block installation by default; `--test --allowinstall` explicitly enables real installation of SHA-256-verified assets from the isolated `HyperBatteryMonitor-UpdateTests` feed. `--allowinstall` alone and any test-install flags on Release builds are rejected. A separate warning makes clear that the test release's Setup may replace the production Windows installation and that Clean Installation deletes production user data. The updater retains the isolated test cache and mutex, prevents installing while a production instance is running, and relaunches Debug in test mode when Setup is cancelled. **A fictitious release tag does not isolate the Windows installer: use a disposable test environment or back up production data.**

## Validation status

This release is a **test candidate** until the Windows build and installer tests are completed. Static source validation is not proof of successful execution or publication.

### Required Windows tests

1. **Beta 5 → Beta 6 via in-app updater, Clean Installation selected:** complete the install without the earlier runtime error, confirm that prior `settings.json` and `battery-history.json` were removed, and that `%TEMP%\HBM-Maintenance.log` records any temporarily retained installer. The file in the old cache must be the only allowed leftover and be removed after the installer exits and a later app startup triggers cleanup.
2. **Beta 5 → Beta 6 normal upgrade:** settings and battery history must survive unchanged, without Clean Installation being automatically selected.
3. **Beta 6 staging behavior in a controlled later update:** confirm that the updater copies and validates Setup under user `%TEMP%`, launches that copy, and removes its own staging directory after Setup completes. This new launcher does not run during the Beta 5 → Beta 6 transition.
4. **Cancelled UAC or Setup / corrupted cache:** report failure safely, preserve the original download where appropriate and avoid silently deleting user data.
5. **Regression checks:** Cloud III device recognition/battery/charge state and Cloud III S adaptive detection. Cloud Stinger Core PlayStation `03F0:0A93` still awaits physical validation of the integrated driver; voltage-based estimates may be high during charging.

## Prior changes carried forward from Beta 4 and Beta 5

- Beta 4: adaptive HID control-interface discovery, remembered successful endpoints, revalidation on reconnect, shared selection and Auto Detect behavior, and Windows SetupAPI HID device-path offset correction. Cloud III S `03F0:02CC` detection has since been reported working by a physical tester.
- Beta 5: original-user Clean Installation diagnostics and separation of registry migration from user-data removal; support for the PlayStation HyperX Cloud Stinger Core Wireless receiver `03F0:0A93` (`MI_03&Col03`, Usage `FF00:0001`), with `FF 12 05` validation and bounded retries limited to that receiver. The original `0951:170B` single-read behavior remains unchanged.
- Credit: [@pvpiv](https://github.com/pvpiv), contributor to [PR #6](https://github.com/davidsantanaalves/HyperBatteryMonitor/pull/6), for PlayStation receiver identification, HID descriptors, 52-query diagnostics, retries and connection/charging test data; the changes were integrated locally without merging the PR.
