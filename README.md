# Hyper Battery Monitor (HBM)

A lightweight Windows system tray application for monitoring the battery, charging, connection, and microphone mute status of supported **HyperX wireless headsets**.

The application provides a convenient way to monitor your headset directly from the Windows notification area, without requiring a full companion application to remain open.

## Features

* 🔋 Battery level monitoring directly from the Windows system tray
* ⏳ Estimated remaining battery time with locally saved discharge history per headset model
* 🎧 Expanded HyperX headset and receiver compatibility, including supported PlayStation, Xbox, and TIO variants
* 🔎 Automatic detection of supported HyperX devices with adaptive HID interface validation
* 📊 Multiple battery display modes
* 🎨 Customizable battery indicator colors and thresholds
* 🌈 Optional battery color gradient
* ⚡ Charging status monitoring and indication on supported headsets
* 🎙️ Microphone mute status monitoring on supported headsets
* 🔇 Optional microphone mute indication in the system tray
* 🔄 Alternating battery/microphone-mute tray icon while the microphone is muted
* 🧾 Battery, connection, charging, and microphone status available from the Settings interface, tray menu, and tooltip
* 🔴 Configurable critical battery notifications
* ✅ Fully charged notifications
* 🔌 Disconnected headset indication
* 🖥️ Start with Windows option
* 🌙 Light, Dark, and System themes
* 🌐 Interface available in 11 languages: English, Portuguese (Brazil), Spanish, Ukrainian, German, French, Polish, Russian, Simplified Chinese, Japanese, and Korean
* 🌍 Automatic Windows language detection for new profiles
* ⚙️ Dedicated and responsive Settings interface
* 🖥️ High-DPI-aware interface with improved scaling and text wrapping
* 💾 Persistent application settings and battery history
* 🔄 In-app updates with selectable Stable/Beta channels and built-in release notes
* 🔵 Windows Bluetooth battery fallback on selected headset models when Windows provides a valid battery level
* 🔄 Automatic migration of settings and battery history from previous versions
* 🧹 Optional clean installation, with improved handling of in-app installer staging
* 🖼️ Custom application, device, notification, and system tray icons
* 🪟 Native Windows / Windows Forms interface
* 🚀 Lightweight and designed to run continuously in the background

## What's New in Version 2.4.0

Version 2.4.0 consolidates the changes from the 2.4.0 beta series, expanding HyperX headset/receiver compatibility and improving detection, update delivery, and installation reliability.

* **Expanded receiver compatibility:** additional Cloud III / Cloud III S receivers, Cloud Alpha 2 Wireless, Cloud Stinger 3 Wireless (PC, PlayStation, Xbox), Cloud Flight 2 (Xbox), and supported Cloud III S / Cloud Flight 2 setups using a HyperX Three-In-One (TIO) receiver.
* **Cloud Stinger Core PlayStation receiver:** support for `03F0:0A93` (`MI_03&Col03`) with receiver-specific battery response validation and retries, preserving the behavior of the original `0951:170B` receiver. This variant was tested successfully by [@pvpiv](https://github.com/pvpiv) ([PR #6](https://github.com/davidsantanaalves/HyperBatteryMonitor/pull/6)).
* **Adaptive HID discovery:** validates candidate control interfaces using battery responses, remembers working interface preferences, and revalidates them after reconnection; also fixes a Windows SetupAPI HID path extraction issue.
* **Windows Bluetooth fallback:** read-only battery level for selected models when Windows provides a valid battery percentage; working USB/HID reporting takes priority.
* **Improved updater:** Stable/Beta channels, contextual *See what's new* release notes, themed dialogs, more responsive Auto Detect, and better control over returning to an older Stable release.
* **Reliable clean installation:** updater stages a validated installer under the current user's `%TEMP%`, away from the user-data directory, and handles an older cached installer that is still running during cleanup. Normal updates preserve settings and history; a selected clean installation removes them.

Some hardware/firmware combinations and Bluetooth/TIO scenarios still need field validation. Battery percentage for the Cloud Stinger Core PlayStation receiver may be overestimated while charging. For more information, see the [v2.4.0 release notes](Release/ReleaseNotes-v2.4.0.md).

## Headset Status Monitoring

Hyper Battery Monitor displays battery, charging, connection, and supported microphone mute information.

### Microphone mute monitoring

Supported headsets can report their microphone mute state directly to Hyper Battery Monitor.

The microphone state is displayed in:

* The device monitor in the Settings sidebar
* The system tray context menu
* The system tray tooltip

The application displays whether the microphone is:

* **Open**
* **Muted**
* **N/A** (localized) until a supported headset reports its microphone state, or when unavailable

When microphone mute indication is enabled, the system tray icon alternates between the current battery-status icon and the microphone-mute icon while the microphone is muted.

When the microphone is unmuted, the tray returns to displaying only the current battery-status icon.

Microphone mute monitoring is available on Cloud III Wireless, Cloud III S (Dedicated Dongle), Cloud 2 Core, Cloud Alpha, Cloud Flight 2 (Dedicated Dongle), and Cloud Mix 2. See the feature matrix below for all models.

### Improved charging monitoring

Charging-state support has been expanded across the supported headset implementations.

When a headset is charging, Hyper Battery Monitor can indicate the charging state through the system tray and device monitoring interface.

The sidebar and tray menu indicate charging through the battery icon when applicable.

### Improved device monitoring

The device monitor provides a more complete overview of the current headset state:

* Connection status
* Battery percentage
* Charging state
* Microphone state, when supported

The same information is also available from the system tray context menu and tooltip.

## Supported Devices

Version 2.4.0 retains the ten original headset registrations and adds ten further model/receiver registrations. Some variants are grouped under the same headset name below. Feature availability depends on the receiver, firmware and connection method; not all new variants have been validated on physical hardware.

### Feature availability

| Device | Battery | Charging | Microphone Mute |
|---|:---:|:---:|:---:|
| HyperX Cloud III Wireless | ✅ | ✅ | ✅ |
| HyperX Cloud III S **(Dedicated Dongle)** | ✅ | ✅ | ✅ |
| HyperX Cloud 2 Core | ✅ | ✅ | ✅ |
| HyperX Cloud Alpha | ✅ | ✅ | ✅ |
| HyperX Cloud Stinger 2 | ✅ | ✅ | ⛔ |
| HyperX Cloud Flight S | ✅ | ✅ | ⛔ |
| HyperX Cloud Flight Wireless | ✅ | ✅ | ⛔ |
| HyperX Cloud Stinger Core Wireless + 7.1 (original / PlayStation receivers) | ✅ | ✅ | ⛔ |
| HyperX Cloud Flight 2 **(Dedicated Dongle)** | ✅ | ✅ | ✅ |
| HyperX Cloud Mix 2 | ✅ | ⛔ | ✅ |
| HyperX Cloud III Wireless (receiver `03F0:0FCC`) | ✅ | ✅ | ✅ |
| HyperX Cloud III S Wireless (receiver `03F0:02CC`) | ✅ | ✅ | ✅ |
| HyperX Cloud Alpha 2 Wireless | ✅ | ✅ | ✅ |
| HyperX Cloud Stinger 3 Wireless (PC) | ✅ | ⚠️¹ | ✅ |
| HyperX Cloud Stinger 3 Wireless (PlayStation) | ✅ | ⚠️¹ | ✅ |
| HyperX Cloud Stinger 3 Wireless (Xbox) | ✅ | ⚠️¹ | ✅ |
| HyperX Cloud Flight 2 (Xbox receiver) | ✅ | ✅ | ✅ |
| HyperX Cloud III S Wireless (TIO receiver) | ✅ | ✅ | ✅ |
| HyperX Cloud Flight 2 (TIO receiver) | ✅ | ✅ | ✅ |

**¹ Cloud Stinger 3 charging state** may depend on supported device notifications. Availability in the matrix indicates an implemented capability, not universal validation of each headset firmware/receiver revision.

**Dedicated dongle and TIO support:** existing Cloud III S and Cloud Flight 2 dedicated-dongle configurations remain supported. Selected Cloud III S and Cloud Flight 2 variants can also be monitored through a compatible HyperX Three-In-One (TIO) receiver, one headset at a time. TIO slot selection and other new hardware combinations may vary by device and firmware.

**Field validation:** Cloud III Wireless operation was checked by the maintainer; a Cloud III S receiver `03F0:02CC` was confirmed working by a tester in Beta 4. A tester also confirmed Cloud Stinger Core PlayStation `03F0:0A93` using the official Beta 5 installer, including detection and recovery after receiver reconnect and headset power-cycle. Battery readings during charging may be overestimated for this PlayStation receiver because its voltage curve has not been calibrated separately.

**Windows Bluetooth battery fallback:** on selected Cloud III S, Cloud Alpha 2, Cloud Flight 2, Cloud Stinger 3 and Cloud MIX 2 configurations, HBM can read battery percentage exposed by Windows. Valid USB/HID values take precedence. Bluetooth-only fallback does not establish microphone mute, charging status or estimated runtime; some headset/adapter combinations do not expose battery percentage.

Cloud Mix 2 supports battery and microphone mute monitoring; charging-state monitoring is unavailable.

## Requirements

* Windows 10 or later
* .NET 10 (included in the self-contained installer)
* A supported HyperX wireless headset

## Screenshots

These screenshots illustrate earlier versions.

### Device selection

<img width="762" height="552" alt="01 device" src="https://github.com/user-attachments/assets/f0c9ed15-3635-4309-8d34-7ccacedc22a8" />

<img width="440" height="250" alt="01 device-detecting" src="https://github.com/user-attachments/assets/6c605a78-12ce-4b5a-90d4-d8e01cd11cc4" />

### Interface options

<img width="758" height="552" alt="02 Interface" src="https://github.com/user-attachments/assets/fecaf2eb-70eb-436d-a553-1c5c380d8e67" />

### Battery monitor options

<img width="758" height="552" alt="03 Battery Monitor" src="https://github.com/user-attachments/assets/9000017a-50f4-4310-98db-d1d851314709" />

<img width="460" height="522" alt="04 Customize Dynamic Icons Colors" src="https://github.com/user-attachments/assets/4bcc7cc4-5b37-47dc-a950-a00a357dc3c5" />

### Notifications options

<img width="758" height="552" alt="05 Notifications" src="https://github.com/user-attachments/assets/f52cf1bd-6746-4c30-a3fc-7bfda39bf9f3" />

### System Tray Context Menu

<img width="168" height="245" alt="07 Context Menu" src="https://github.com/user-attachments/assets/c5b0377a-6e1f-473a-8346-8ebca2c369dc" />

### System Tray Icon and device monitoring

<img width="132" height="144" alt="08 Tooltip" src="https://github.com/user-attachments/assets/7667b4f4-7b0f-4784-b525-27dd48dfb6d9" />

## Installation

Download the latest version from the **Releases** section of this repository.

Run the installer and follow the installation instructions. The Stable 2.4.0 installer is named `HyperBatteryMonitor-Setup-v2.4.0.exe`.

After installation, Hyper Battery Monitor runs in the Windows system tray.

You can also install Hyper Battery Monitor from the [Microsoft Store](https://apps.microsoft.com/detail/9N4G6WKMM4QJ).

## Usage

After launching the application, the Hyper Battery Monitor icon will appear in the Windows notification area.

Right-click the tray icon to access the device status and available options.

Double-click the tray icon to open the application settings.

Use automatic detection with your headset powered on and connected, or select a model manually. If several compatible headsets are detected, select the desired model manually.

Selecting a headset previews its status in Settings. Click Apply or OK to save the selection and use it for system tray monitoring.

From the Settings window you can configure:

* Device
* Battery monitor mode
* Battery colors
* Battery color gradient
* Charging indication
* Critical battery notification
* Fully charged notification
* Critical battery tray-icon blinking
* Microphone mute tray indication
* Language
* Theme
* Start with Windows

Microphone-related options are automatically unavailable for headset models that do not support microphone mute monitoring.

## Battery Monitor Modes

Hyper Battery Monitor provides multiple ways to display battery information in the Windows system tray.

### Static icon

Displays the standard headset icon, with a separate charging indication when applicable.

### Dynamic glow icon

Displays a glow around the headset icon using predefined battery-level colors.

The default battery ranges are:

* ≥ 50% — Green
* 30–49% — Yellow
* 15–29% — Orange
* < 15% — Red
* Charging — Charging indicator

### Custom dynamic icon

Customize battery-level colors and thresholds, with an optional gradient transition.

## Microphone Monitoring

For supported headsets, Hyper Battery Monitor monitors microphone mute-state changes and updates the interface automatically.

The current microphone state can be viewed in:

* Settings sidebar
* System tray menu
* System tray tooltip

When **Show muted microphone in the system tray** is enabled, the tray icon alternates between:

1. The current battery-status icon
2. The microphone-mute icon

This continues while the microphone remains muted.

When the microphone becomes active again, the tray displays only the normal battery-status icon.

This feature is enabled by default for headset models that support microphone mute monitoring.

## Charging Status

Hyper Battery Monitor monitors charging state on supported devices.

When charging is detected, the application can:

* Display a charging-specific tray icon or charging indicator
* Show the charging state in the device monitoring interface
* Indicate charging through the battery icon in the sidebar and tray menu
* Notify the user when the headset reaches full charge

Charging behavior depends on the selected battery display mode.

## Notifications

The application can notify you when the headset reaches the configured critical battery level.

A fully charged notification can also be displayed when the headset finishes charging.

Available notification-related options include:

* Low battery notification
* Configurable critical battery level
* Critical battery tray-icon blinking
* Fully charged notification
* Microphone mute tray indication on supported headsets

These options can be configured independently through the Settings window.

## Project Status

**Version:** 2.4.0

**Status:** Stable release

Version 2.4.0 adds adaptive HID endpoint discovery, additional headset/receiver variants, Windows Bluetooth battery fallback on supported models, and more reliable in-app updates and Clean Installation.

The project remains under active development, and additional devices and features may be added in future versions.

## Technology

The application is built using:

* C#
* .NET 10
* Windows Forms
* Windows APIs
* Windows Registry
* Microsoft.Toolkit.Uwp.Notifications

The application communicates with supported headsets through Windows HID interfaces.

The project does not distribute third-party proprietary HyperX software or require HyperX NGENUITY to remain open.

## Independent Project

Hyper Battery Monitor is an independent, community-developed utility.

It is **not affiliated with, endorsed by, sponsored by, or officially associated with HyperX or HP Inc.**

HyperX and the respective headset names are trademarks of their respective owners.

## License

This project is licensed under the **MIT License**.

See the [LICENSE](LICENSE) file for the complete license text.

## Author

**David Santana (Dave Santana)**

Independent developer.

## Support the Project

If you find Hyper Battery Monitor useful and would like to support its development:

☕ [Buy Me a Coffee](https://buymeacoffee.com/davesantana)

## Contributing

Suggestions, bug reports, and contributions are welcome.

If you find a problem or have an idea for a new feature, please open an **Issue** in this repository.

## Disclaimer

This software is provided "as is", without warranty of any kind.

The developer is not responsible for any damage, data loss, hardware issues, or other consequences resulting from the use of this software.

Use the application at your own discretion.

## Special Thanks

Special thanks to the following people and projects for their contributions to the HyperX community and to the development of this project:

- auto94 — for making the HyperX-Cloud-2-Battery-Monitor project available to the community. It served as a historical reference during earlier development of HyperX Battery Monitor. The applicable historical provenance and license notice are preserved in THIRD_PARTY_NOTICES.md and the project history.

- minoga11 — for active participation in testing and for suggesting new improvements.

- sladkOy — for the suggestion and help with the Ukrainian translation.

- [@pvpiv](https://github.com/pvpiv) — for [PR #6](https://github.com/davidsantanaalves/HyperBatteryMonitor/pull/6): identification of the Cloud Stinger Core Wireless PlayStation receiver, HID descriptors, 52 battery-query diagnostic samples, response/retry findings, and real-hardware confirmation of detection and reconnection. The contribution was incorporated locally without merging the PR.

- Buda1973 — for helping identify the Cloud III S Wireless receiver and its HID descriptors, as well as validating device detection and reconnection on real hardware.

- Ordinary_Raise_59 — for helping identify the Cloud Stinger 3 Wireless receiver and its HID descriptors, as well as validating device detection and reconnection on real hardware.

And a special thanks to everyone who has been testing the app, showing their support, and sharing feedback, criticism, and suggestions.

# Downloads

## Last Stable release:
[![Stable](https://img.shields.io/github/v/release/davidsantanaalves/HyperBatteryMonitor?label=Stable&color=green)](https://github.com/davidsantanaalves/HyperBatteryMonitor/releases/latest)

## Last BETA release:
[![Beta](https://img.shields.io/github/v/release/davidsantanaalves/HyperBatteryMonitor?include_prereleases&filter=*beta*&sort=date&label=Beta&color=orange)](https://github.com/davidsantanaalves/HyperBatteryMonitor/releases?q=prerelease%3Atrue)
