# Hyper Battery Monitor v2.5.0 BETA 2

**Headset Auto Power-Off Settings, Improved HID Diagnostics & Device UI Refinements**

This experimental GitHub prerelease adds the ability to read and change supported headsets' automatic power-off settings directly in Hyper Battery Monitor (HBM), without opening NGENUITY. It also improves HID communication diagnostics and refines the Device settings layout.

**Beta notice:** Features and protocol support can vary by headset model, receiver variant and firmware. This is not a Stable or Microsoft Store release.

## What's New

### Auto Power-Off — Read and Configure from HBM

A new **Auto Power-Off** control is available in **Settings → Device → Battery** on compatible headsets. The available settings are:

- **Never**
- **10 minutes**
- **20 minutes**
- **30 minutes**

HBM reads the current value from the headset rather than assuming a default. Changing the selection does not immediately write to the device: **Apply** or **OK** sends the configuration, followed by readback verification. Opening Settings or pressing **Cancel** does not write a new value.

Experimental support is implemented for the documented legacy-protocol paths of **Cloud III Wireless, Cloud III S Wireless, Cloud II Core Wireless, Cloud Alpha Wireless and Cloud Stinger 2 Wireless**, subject to the actual responsive USB/HID route. **Physical read/write validation has been completed only for the Cloud III Wireless (`03F0:05B7`)**. Other models and receiver variants remain candidates for community testing; support is not guaranteed. Unsupported, disconnected or Bluetooth-fallback-only devices cannot change this setting.

### Improved HID Configuration Reliability

- Headset configuration changes use the Windows HID output-report API (`HidD_SetOutputReport`), with a transport fallback where appropriate.
- A successful USB transmission alone is **not** reported as a successful setting change. HBM verifies the applied value through additional HID reads.
- A short firmware processing delay is tolerated during readback; a successful configuration command is not blindly retransmitted.
- Existing event-driven monitoring and recovery polling remain in place.

### Expanded Optional Local Beta Diagnostics

With explicit diagnostic consent, Beta 2 captures HID traffic at the shared transport layer, including available **RX/TX raw report bytes**, report type, method, status, device path, and error details. This improves the evidence available for both existing functions and future headset capabilities.

- Diagnostic logs remain **optional** and **local only**; HBM does not automatically upload them.
- Updated consent wording explains the inclusion of raw HID data and device identifiers. Existing users may be asked to consent again.
- Logs are written as JSONL under **`Documents\HBM Logs`**, with file rotation and retention limits.
- Consent and UI strings are available in HBM's **11 supported languages**.

**Privacy:** Raw HID reports and device paths may contain device identifiers or sensitive information. Review and redact diagnostic files before sharing them publicly. If your Documents folder is synced by OneDrive or another service, that service may also sync those files independently of HBM.

### Device Settings UI and DPI Refinements

- Added a compact rounded **Auto Power-Off** selector to the Battery card.
- Refined label/selector alignment, margins and vertical spacing.
- Preserved the original **760 × 520 logical-pixel** Settings window baseline, with responsive handling for display scaling and longer translations.
- Improved unavailable/unsupported state presentation without inventing a headset setting.

## Validation and Current Limitations

Maintainer testing of **Cloud III Wireless (`03F0:05B7`)** confirmed HID reads and writes for **Never, 10, 20 and 30 minutes**, including readback after writing. The **Never** and **10-minute** settings were also read back after the headset was physically powered off and on. Diagnostic sessions exercised successful raw HID RX/TX logging and reconnection handling.

These results do **not** establish identical behavior for other headset models, all firmware revisions, every Windows DPI configuration, or long-term resource usage. The initial report of Auto Power-Off unexpectedly reverting to Never has not been independently proven to be caused or resolved by a particular polling interval.

## Help Us Test

Please report the headset model, receiver variant (VID/PID if known), Windows version, selected Auto Power-Off value, and whether it persists after powering the headset off and on. We are also interested in UI scaling issues, recovery after disconnection, and configuration failures. Do not include serial numbers or unreviewed raw diagnostic logs.

[Report an issue on GitHub](https://github.com/davidsantanaalves/HyperBatteryMonitor/issues)

## Before Installing

This is a **prerelease for testing**. If you rely on HBM for everyday use, prefer the latest Stable release. Switching from an installed Beta to an older Stable may require an explicitly confirmed downgrade or reinstall. The Microsoft Store release channel is managed separately.

---

**Hyper Battery Monitor** · Open Source · MIT License
