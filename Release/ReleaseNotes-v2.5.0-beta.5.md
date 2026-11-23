# Hyper Battery Monitor v2.5.0 BETA 5

**Device previews, high-DPI fixes, tray controls and installer improvements**

## Device information and monitoring

- Selecting a headset in Settings previews its battery percentage and, when enough battery history is available, its estimated remaining time before you click **Apply** or **OK**.
- For supported headsets, Settings reads and displays the current **Auto Power-Off** value in the preview sidebar without requiring **Apply**, **OK** or opening the Device Settings dialog. The preview leaves the hardware setting and the active device selection unchanged.
- Device previews respect the **Listen-only** and **Polling** behavior introduced in Beta 4.

## High-DPI and UI improvements

- **Device Settings** wraps descriptions and resizes controls to keep text readable at higher Windows scaling. The Auto Power-Off choices can wrap into two rows when space is tight.
- **Diagnostics** adjusts section heights and reflows labels and buttons as available space changes. The dialog recalculates its layout when Windows scaling changes, including when it returns from a higher scale to 100%. Its themed scrollbar appears only when the content needs scrolling.
- **Custom Dynamic Icon** renders at the Windows taskbar's current scale to keep edges sharp and antialiased, including on battery and charging overlays. The icon refreshes when display scaling changes.

## System tray and Settings

- The **Settings** and **Exit** buttons at the bottom of the system tray menu have aligned icons, subtle dividers and flat hover highlights with rounded corners. Both buttons support keyboard activation.
- **Settings** now has an **Exit** button below **About**. A confirmation dialog follows the active theme and warns that unapplied or unsaved changes will be lost. Choose **OK** to exit or **Cancel** to stay in Settings. The dialog is available in all 11 supported languages.

## Installer and updater

- Setup offers installation for the **current user** or **all users** and preserves the scope of an existing installation. It now detects previous installations registered under the current Windows user and avoids cleaning up the previous folder when per-user and all-users installations point to different locations.
- The updater launches Setup with the permissions of the current user. Setup requests Windows administrator approval when an all-users installation requires it.
- Setup offers an optional **Support the developer** link on the final screen. The option is unchecked by default.

## Known issues and testing limitations

- The reported unexpected Auto Power-Off behavior (`BUG-APO-01`) remains under investigation. [GitHub Issue #13](https://github.com/davidsantanaalves/HyperBatteryMonitor/issues/13)
- **Clean Installation** remains opt-in. The full flow when launched from the updater remains unverified.
- Visual validation covered selected scenarios. Other DPI settings, monitor arrangements and hardware combinations remain unverified.
