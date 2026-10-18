# Hyper Battery Monitor v2.4.0 — Beta 3

Beta 3 improves the update experience, window behavior, theme consistency, and automatic headset detection. This is a **pre-release** intended for testing before the stable v2.4.0 launch.

## What's new

- **Release notes inside HBM:** when an update is found, the available version now includes an inline **See what's new** link. A native, scrollable modal shows the release notes without loading the GitHub page. The modal follows HBM's Light, Dark, or System theme and offers **View on GitHub**.
- **Clear Beta version display:** HBM shows the numbered Beta, such as **v2.4.0 [BETA 3]**, while retaining semantic versions for update comparisons.
- **Improved window focus:** the main window and dialogs no longer remain always on top of unrelated applications. HBM dialogs stay in front of their HBM parent window while the browser and other Windows apps can take focus normally.
- **Consistent scrollbars:** themed vertical scrollbars across scrollable Settings content, device and language selectors, icon-color customization, and the What's new dialog. Scrollbars appear where content requires them.

## Update channels and safety

- **Beta channel:** checks for the newest eligible Beta or Stable release, whichever has the higher version.
- **Stable channel:** selects the latest published Stable release even if the current installation is a newer Beta. For example, a user running v2.4.0-beta.2 can choose to return to v2.3.1 Stable.
- Returning to an older Stable is an **explicit downgrade**, never downloaded automatically; HBM warns before download and again before installation.
- Release metadata and installer SHA-256 validation remain in place. Existing app and user-data migration behavior is intended to remain unchanged.

## Automatic headset detection

- Increased the maximum detection period from **8 seconds to 15 seconds**.
- The detection dialog stays visible for at least **1.5 seconds** when a device is found immediately, without delaying detection itself.
- The **15-second countdown continues updating** for as long as the dialog is open, including the brief period after a successful early detection.
- **Cancel** remains immediate; the minimum display time does not delay cancellation.

## Headset compatibility

- Beta 3 retains the CloudX Stinger 3 Wireless Xbox HID collection selection and response parsing improvements **introduced in Beta 2**, as well as the other headset and receiver options added earlier in the v2.4.0 Beta series.
- Compatibility with every newly supported receiver and headset variant has **not** been physically verified. Reports from testers remain important, especially for the Stinger 3 Xbox receiver.

## Known issues being investigated

A tester reported that the **HyperX Cloud III S Wireless** is not working with HBM. We have requested the HBM version, receiver/connection type, Device screen status, and whether NGENUITY reports the battery correctly. The root cause is **not confirmed**, and Beta 3 does **not** claim to fix this specific report. If necessary, a follow-up Beta will address the findings.

One tester reported an Inno Setup **clean-installation maintenance error** during an update started from within HBM. The update itself reportedly completed and restarted, but the cause of that warning, including whether user-data maintenance was affected, has **not yet been confirmed or fixed**. Normal updates are intended to preserve settings and battery history; a clean installation deletes them only when explicitly selected. Please report this error if encountered, and avoid deleting user data for troubleshooting without a backup.

## For testers

Please check both update channels, the What's new modal in Light/Dark/System themes, scrollbars across Settings, external-browser focus, automatic detection timing, and retention of existing settings and battery history during an update.

If you encounter an issue, please open a report at https://github.com/davidsantanaalves/HyperBatteryMonitor/issues with the installed and target versions, device model where relevant, and the observed behavior (screenshots/logs if available).

**Release:** `v2.4.0-beta.3`
**Expected Windows installer:** `HyperBatteryMonitor-Setup-v2.4.0-beta.3.exe`
**Distribution:** GitHub Pre-release (not Microsoft Store)
