# Hyper Battery Monitor v2.5.0 BETA 1

**Event-Driven Monitoring, Reduced HID Polling & Optional Local Beta Diagnostics**

This experimental pre-release improves how Hyper Battery Monitor (HBM) receives status updates from compatible HyperX wireless headsets. Selected devices can use incoming HID notifications instead of relying only on frequent battery queries, while periodic recovery checks remain in place.

**Community testing is important:** behavior may differ across headset models, USB receivers, HID collections and firmware versions. This is **not** a Stable release or a Microsoft Store update.

## 🚀 What's New

### ⚡ Event-Driven Monitoring — Cloud III Wireless

For the **HyperX Cloud III Wireless (`03F0:05B7`)**, HBM experimentally processes unsolicited HID reports for:

- Battery level updates (`66 0D`)
- Charging transitions (`66 0C`)
- Microphone mute/unmute (`66 0A`)
- Physical connection and power-off notifications (`66 0B`)

While a headset is responsive and validated, its scheduled recovery battery query changes from **every 5 seconds to every 60 seconds**. A failed query or physical disconnect retains automatic reconnection attempts at the normal **5-second** interval. Initial receiver and battery verification are still required.

This represents **up to 91.7% fewer scheduled battery recovery queries** in qualifying connected sessions. It does **not** mean measured 91.7% lower CPU use, total HID traffic or power consumption.

### 🎧 Expanded Experimental Notification-Assisted Monitoring

The following families can also use **Beta-only notification-assisted battery monitoring**, where their existing HID handlers and verified receiver paths advertise support:

- Cloud III Wireless — alternate receiver (`03F0:0FCC`)
- Cloud III S Wireless — standard and alternate receivers (`03F0:06BE` / `03F0:02CC`)
- Cloud Alpha Wireless
- Cloud II Core Wireless
- Cloud MIX 2
- Cloud Stinger 3 Wireless — PC, PlayStation and Xbox variants

Eligible sessions use 60-second battery recovery checks; all other devices retain their established polling cadence. **These additional models have not all received physical event-delivery validation.** Receiver detection alone does not guarantee that a headset is responding.

### 🔍 Optional Local Beta Diagnostics

HBM now offers explicit, **optional** diagnostic logging to help investigate compatibility and connectivity issues.

- **Accept telemetry:** the Beta runs with local diagnostic logs enabled.
- **Decline or close the consent window:** the Beta **still runs normally**, with diagnostic logging disabled. Nothing is uploaded by HBM.
- **Enable it later:** open **Settings → About → Enable telemetry** (*Ativar telemetria* in Portuguese) to review the same consent terms again. After accepting, the button changes to **Open diagnostic logs folder**.
- Consent is remembered for the current terms, rather than prompting repeatedly on every launch. Existing users may be asked again after the terms were updated.
- When enabled, logs use structured **JSONL** under **`Documents\HBM Logs`**, with automatic rotation and retention (10 MB per file, maximum 100 MB in the folder, up to 14 days).
- Consent and related actions are localized in all **11 supported languages**.

**Privacy:** HBM does **not** automatically send logs to a server. If your Windows Documents folder is synchronized through OneDrive or another backup service, that separate service may sync the local files according to your Windows settings. Please review any logs before sharing them in an issue report.

### 🎨 Consent Dialog and Interface Improvements

- Consent window follows the selected **Dark, Light or System** theme.
- Opens **centered on screen** during first startup, or centered over the Settings window when reopened from About.
- Uses rounded action buttons consistent with HBM, with hover/pressed states and **✓ / ✕** icons.
- Automatically sizes translated button labels to avoid clipped text.
- Shows vertical scrolling only when the consent text actually needs it.

### 🛠️ Connection Detection and Reliability

- **Fixed a shared monitoring-state issue:** a detected USB receiver or successfully opened HID interface is no longer enough for HBM to announce **Connected**. The common monitor first requires a valid battery response (**0–100%**).
- This safeguard applies across **all device families using the common monitor**, not just Cloud III Wireless, including receiver-aware drivers and the Bluetooth fallback path.
- Prevents transient **Connected → Disconnected** status changes caused solely by an unresponsive headset while its USB receiver remains plugged in.
- Preserves existing reconnect attempts, supported notification handling, charger/microphone states, and per-device polling/fallback behavior.

## ✅ Early Test Results and Validation Scope

The maintainer has **successfully built and tested the latest local Beta without reported errors**. Eight opt-in JSONL sessions from a Cloud III Wireless (`03F0:05B7`) test system provided **1,086 diagnostic events**, including **12 unsolicited HID reports**. Those logs confirmed live battery, microphone, charging and power-off events, as well as recovery after an extended powered-off period. They also helped identify the shared connection-state issue addressed above.

These observations **do not** certify every headset, receiver variant, firmware version, Windows sleep/resume condition or long-term performance. The final GitHub installer must be separately built and checked before distribution.

## 🧪 Help Us Test

We're particularly interested in:

1. **Battery:** does the percentage update correctly during use, charging and discharging?
2. **Connection:** with the USB receiver still plugged in, does HBM consistently show **Disconnected** when the headset is powered off, and recover when powered on?
3. **Charging and microphone:** do changes appear quickly and accurately?
4. **Auto Power-Off:** does the headset retain its configured automatic shutdown timeout after long sessions with HBM running?
5. **Stability:** any freezes, audio interruptions, unexpected disconnects or unusual resource usage?
6. **Other hardware:** test the receiver/family combinations above and whether their Beta notification-assisted mode behaves correctly.

### Known Issue Under Investigation — Auto Power-Off

A user reported that **Cloud III Wireless Auto Power-Off** could change from a configured duration to **Never** while using HBM v2.4.0. The lower frequency of scheduled battery queries in this Beta may reduce potential interactions, but **a causal link and a fix have not been established**. A prior controlled test saw a 20-minute headset shutoff occur as configured, but this does not rule out other firmware or long-session conditions.

If you've experienced this issue, please share the device/firmware details, configured timeout, how long the headset was idle, and whether the behavior repeats with this Beta.

## 📝 Reporting an Issue

Open a [GitHub Issue](https://github.com/davidsantanaalves/HyperBatteryMonitor/issues) with:

- Headset model and, if known, receiver **VID/PID** and HID **MI/Col** (do not share serial numbers).
- Windows version, HBM version **`v2.5.0-beta.1`**, and whether **NGENUITY** was running.
- Reproduction steps, expected behavior and observed behavior.
- Relevant **reviewed** `.jsonl` logs from `Documents\HBM Logs`, **only if you opted in**. Telemetry is optional and you can still report bugs without logs.

Please remove personal or sensitive information before uploading files.

## ⚠️ Before Installing

**This is an experimental GitHub prerelease for community testing.** We recommend the latest Stable release (**v2.4.1** at the time these notes were prepared) if you depend on HBM for everyday use. Beta installation does not automatically restore Stable if you later prefer to switch back; use the explicit Stable channel/downgrade confirmation or reinstall Stable as appropriate.

Thanks to everyone contributing hardware captures, bug reports and testing.

---

**Hyper Battery Monitor** — Open Source · MIT License

[Report an Issue](https://github.com/davidsantanaalves/HyperBatteryMonitor/issues)
