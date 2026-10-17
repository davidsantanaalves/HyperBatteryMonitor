# Hyper Battery Monitor

A lightweight Windows system tray application for monitoring the battery, charging, connection, and microphone mute status of supported **HyperX wireless headsets**.

The application provides a convenient way to monitor your headset directly from the Windows notification area, without requiring a full companion application to remain open.

## Features

* 🔋 Battery level monitoring directly from the Windows system tray
* ⏳ Estimated remaining battery time, with locally saved discharge history per headset model
* 🎧 Support for multiple HyperX wireless headsets
* 📊 Multiple battery indicator display modes
* 🎨 Customizable battery indicator colors
* 🌈 Optional battery color gradient
* ⚡ Charging status monitoring and indication
* 🎙️ Microphone mute status monitoring on supported headsets
* 🔇 Optional microphone mute indication in the system tray
* 🔄 Alternating battery/microphone-mute tray icon while the microphone is muted
* 🧾 Microphone status displayed in the sidebar, tray menu, and tray tooltip
* 🔴 Configurable critical battery notification
* ✅ Fully charged notification
* 🔌 Disconnected headset indication
* 🖥️ Windows startup option
* 🌙 Light, Dark, and System themes
* 🌐 Interface in 11 languages: English, Portuguese (Brazil), Spanish, Ukrainian, German, French, Polish, Russian, Simplified Chinese, Japanese, and Korean
* ⚙️ Dedicated settings window
* 💾 Persistent application settings
* 🖼️ Custom application and tray icons
* 🪟 Native Windows / Windows Forms interface
* 🚀 Lightweight and designed to run in the background

## What's New in Version 2.3.0

* **Five new devices:** Cloud Flight S, Cloud Flight Wireless, Cloud Stinger Core Wireless + 7.1, Cloud Flight 2 (Dedicated Dongle), and Cloud Mix 2.
* **Automatic device detection:** visual progress and guidance when no compatible device or multiple devices are found.
* **Clear connection requirements:** Cloud III S and Cloud Flight 2 are labeled as requiring a Dedicated Dongle. A detected HyperX Three-In-One receiver is identified as an unsupported connection; monitoring through TIO is not supported.
* **Updated Device page:** supported features, connection information, and battery information in a revised layout.
* **More robust remaining-time estimates:** each session starts from nominal battery life and gradually incorporates validated discharge windows. Incompatible old history is reset, and implausible readings are filtered.
* **Localization improvements:** updated English, Portuguese (Brazil), and Spanish text.

Remaining-time estimates are approximate and hidden while charging or disconnected.

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

The device monitor now provides a more complete overview of the current headset state:

* Connection status
* Battery percentage
* Charging state
* Microphone state, when supported

The same information is also available from the system tray context menu and tooltip.

## Supported Devices

Ten established headset models and ten additional receiver/model variants are registered in version 2.4.0. The additional variants below are based on NGENUITY 3.1.0 static protocol research and require real-device validation during Beta testing.

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
| HyperX Cloud Stinger Core Wireless + 7.1 (original and PlayStation receivers) | ✅ | ✅ | ⛔ |
| HyperX Cloud Flight 2 **(Dedicated Dongle)** | ✅ | ✅ | ✅ |
| HyperX Cloud Mix 2 | ✅ | ⛔ | ✅ |
| HyperX Cloud III (Receiver 0FCC) | ✅ | ✅ | ✅ |
| HyperX Cloud III S (Receiver 02CC) | ✅ | ✅ | ✅ |
| HyperX Cloud Alpha 2 Wireless | ✅ | ✅ | ✅ |
| HyperX Cloud Stinger 3 Wireless (PC) | ✅ | ⚠️ Notification-dependent | ✅ |
| HyperX Cloud Stinger 3 Wireless (PlayStation) | ✅ | ⚠️ Notification-dependent | ✅ |
| HyperX Cloud Stinger 3 Wireless (Xbox) | ✅ | ⚠️ Notification-dependent | ✅ |
| HyperX Cloud Flight 2 (Xbox) | ✅ | ✅ | ✅ |
| HyperX Cloud III S (TIO - Three-In-One) | ✅ | ✅ | ✅ |
| HyperX Cloud Flight 2 (TIO - Three-In-One) | ✅ | ✅ | ✅ |

**Existing dedicated-dongle drivers remain unchanged.** Separate entries are provided for new dedicated receivers and for the Cloud III S / Cloud Flight 2 through the HyperX Three-In-One (TIO) receiver. TIO models are resolved through the shared receiver slot table and monitored one at a time. Other TIO child models are not included. The initial hardware validation of these additions is pending; report incompatibilities through the issue tracker.

**Cloud Stinger Core Wireless for PlayStation (SKU 4P5J1AA):** the original USB receiver `03F0:0A93`, revision `4103`, uses HID `MI_03&Col03` (Usage Page `FF00`, Usage `0001`). This receiver is monitored under the existing **Cloud Stinger Core Wireless + 7.1** device selection and is resolved using the adaptive HID discovery introduced in Beta 4. Battery responses `FF 12 05` are validated and stale `FF 01 00` replies are retried **only** for this PlayStation receiver; the original `0951:170B` protocol remains unchanged. Charging, reconnect and Auto Detect were exercised by the contributor; the new HBM integration still requires its own hardware validation. **Battery percentage may be overestimated during charging**, since the existing voltage interpolation curves have not been calibrated for this receiver revision.

Cloud Mix 2 supports battery and microphone mute monitoring over its USB receiver; charging-state monitoring is unavailable.

### Bluetooth battery fallback (Windows)

When a paired Bluetooth headset is actively connected and Windows exposes a valid battery level, HBM can monitor its battery without a USB receiver. This is a read-only **fallback** for Cloud III S, Cloud Alpha 2, Cloud Flight 2, Cloud Stinger 3 and Cloud MIX 2. The original USB HID protocols retain priority and have not been changed. No setting or separate Bluetooth headset entry is required.

Bluetooth reads use Windows device battery properties, falling back to the standard Bluetooth LE Battery Service when present. The feature does **not** claim that all headset firmware/Windows Bluetooth adapters expose the battery level. Unknown or unavailable values are shown as disconnected/N/A; no percentages are guessed. Bluetooth microphone mute, charging state and remaining-runtime estimates are unavailable unless independently implemented and validated. When switching from Bluetooth back to a USB receiver, HBM periodically checks whether HID can return a valid headset battery value (15-second recovery interval while Bluetooth is active), then automatically promotes the USB receiver. A present dongle with no responding headset is never enough to switch. HID-to-Bluetooth fallback requires two failed HID battery readings and an independently valid Windows Bluetooth battery result. When a TIO child changes slots, monitoring invalidates the old routing and re-reads the mode table before using the new slot. Ambiguous same-model child matches are rejected. Bluetooth support and the TIO slot transitions require real-device testing before a release can claim hardware compatibility.

## Requirements

* Windows 10 or later
* .NET 10 (included in the self-contained installer)
* A supported HyperX wireless headset

## Screenshots

These screenshots illustrate earlier versions. Version 2.3.0 updates the Device page and adds automatic detection.

### Device selection

<img width="762" height="552" alt="01 device" src="https://github.com/user-attachments/assets/02641fa7-336f-4d0f-a763-5e5e98ca53b9" />

### Interface options

<img width="762" height="552" alt="02 Interface" src="https://github.com/user-attachments/assets/43d3a9e6-795f-46ec-97a2-d0afe80cad00" />

### Battery monitor options

<img width="762" height="552" alt="03 Battery Monitor" src="https://github.com/user-attachments/assets/b2938732-a482-447c-b9d4-f111e01f2417" />

<img width="460" height="522" alt="04 Customize Dynamic Icons Colors" src="https://github.com/user-attachments/assets/33547ce8-778d-4e1a-b8bb-4751ccff5a4b" />

### Notifications options

<img width="762" height="552" alt="05 Notifications" src="https://github.com/user-attachments/assets/8cc5d4bb-3c03-4b89-8dcf-8684d08499b6" />

### System Tray Menu

<img width="178" height="298" alt="07 Context Menu" src="https://github.com/user-attachments/assets/28d1a174-86f5-4e63-8627-0750e21699da" />

### System Tray Icon and device monitoring

<img width="132" height="144" alt="08 Tooltip" src="https://github.com/user-attachments/assets/05bc1803-c4ef-4bbd-9b0d-6a006c7e324d" />

## Installation

Download the latest available version from the [Releases](https://github.com/davidsantanaalves/HyperBatteryMonitor/releases) section of this repository.

The expected installer filename for version 2.3.1 is:

`HyperBatteryMonitor-Setup-v2.3.1.exe`

Run the installer and follow the installation instructions.

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

**Version:** 2.3.0

**Status:** Stable release

Version 2.3.0 supports ten headset models and adds automatic device detection, a revised Device page, and more robust adaptive remaining-time estimates.

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

## Device Protocol Implementation

The current device protocol implementation is based primarily on independent analysis of HyperX NGENUITY and the protocol research maintained by this project.

Historical development references and the original license notice are preserved in [Third-Party Notices](THIRD_PARTY_NOTICES.md). See the [v2.3.1 provenance audit](ProvenanceAudit-auto94-v2.3.1.md) for the technical comparison and its limitations.

## License

This project is licensed under the **MIT License**.

See the [LICENSE](LICENSE) file for the complete license text.

## Author

**David Santana (Dave Santana)**

Independent developer.

## Support the Project

If you find Hyper Battery Monitor useful and would like to support its development:

☕ [Buy Me a Coffee](https://buymeacoffee.com/davesantana)

You can also support the project via PIX:

`davesantana@outlook.com.br`

## Contributing

Suggestions, bug reports, and contributions are welcome.

### Community acknowledgements

- **Contributor to [PR #6](https://github.com/davidsantanaalves/HyperBatteryMonitor/pull/6)** — thank you for identifying the **HyperX Cloud Stinger Core Wireless for PlayStation (4P5J1AA / `03F0:0A93`)** control endpoint, sharing 52 battery-query diagnostic results, establishing the receiver-specific `FF 12 05` response validation/retry behavior, and checking charging and reconnection scenarios. This implementation was independently integrated into the Beta 5 codebase without merging the PR; your testing and research made the additional receiver support possible.

If you find a problem or have an idea for a new feature, please open an **Issue** in this repository.

## Disclaimer

This software is provided "as is", without warranty of any kind.

The developer is not responsible for any damage, data loss, hardware issues, or other consequences resulting from the use of this software.

Use the application at your own discretion.
