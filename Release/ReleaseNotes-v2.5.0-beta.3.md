# Hyper Battery Monitor v2.5.0 BETA 3

**Redesigned About & Updates Experience, Diagnostics for Every Release Channel**

This prerelease focuses on a clearer, more consistent settings experience and gives users direct control over optional diagnostic logging, regardless of whether they use the Stable or Beta release channel. The existing headset monitoring and Auto Power-Off features remain available, subject to their previously documented compatibility limits.

**This is an experimental GitHub prerelease, not a Stable or Microsoft Store release.**

## What's New

### Redesigned Settings → About

The **About** page has been reorganized to keep update controls readable within the existing **760 × 520 logical-pixel** Settings window, without mixing update actions with diagnostic controls.

- A dedicated **Diagnostics** button opens a separate settings dialog.
- The **Updates** section has a more compact, structured layout for release channel and download preferences.
- Contextual status banners and icons distinguish **update available**, **downloading**, **download complete / ready to install**, **up to date**, and **update error** states.
- Update actions such as **Check for updates**, **Download now**, **Install now**, and **Remind me later** are shown when applicable, with rounded controls and improved spacing.
- Release notes remain accessible through **What's new** when an eligible update provides them.

### Optional Diagnostics in Both Beta and Stable Builds

Diagnostic logging is **no longer restricted to Beta builds**. The underlying feature is designed to remain available when HBM is built for either the Stable or Beta release channel.

- **Diagnostics → Status** includes an on/off switch for locally stored logs.
- Consent is **explicit and optional**. HBM remains usable if diagnostics are declined or disabled.
- The dialog provides **Open logs folder** and **Review consent terms** actions.
- Consent terms can be reopened in a **read-only, themed window** without changing the user's choice.
- Consent wording was updated across all **11 supported languages** to remove Beta-only descriptions. Existing users may be asked to review the updated terms again.

**Privacy:** When enabled, diagnostics can include headset status, HID event metadata, raw HID input/output/feature reports, report types, device connection paths, errors, and recovery actions on instrumented HBM communication paths. Some reports and paths can contain device-specific or sensitive information. Logs are stored locally as JSONL files in `Documents\HBM Logs`; HBM does **not** automatically upload them. Retention is limited to **14 days**, with **10 MB per log file** and **100 MB total**. If Documents is synchronized by OneDrive or another backup service, that service may copy the logs independently of HBM. **Review and redact logs before sharing them.**

### Theme, Layout & Accessibility Refinements

- Consistent rounded buttons and status presentation in the About and Diagnostics screens.
- Improved vertical alignment, text wrapping, and overflow handling in Diagnostics.
- Consent review uses HBM's existing themed scrollbar, rather than the standard Windows scrollbar.
- New update-state icons support the active Dark or Light theme, with localized labels and descriptions.

## Existing Experimental Headset Features

This release retains **notification-assisted / event-driven battery monitoring** and the **Auto Power-Off** setting introduced in earlier v2.5.0 Betas. Where supported, Auto Power-Off offers **Never**, **10**, **20**, and **30 minutes**, with a readback check after applying the setting.

**Hardware validation remains limited:** the maintainer has physically confirmed Auto Power-Off reads and writes on the **HyperX Cloud III Wireless (`03F0:05B7`)**, including persistence checks for Never and 10 minutes across a headset power cycle. Other headset models, receiver variants and firmware versions remain experimental candidates; this release does **not** establish universal compatibility.

## Help Us Test

Please let us know if you encounter:

- Clipped text, misaligned controls, or scrolling issues in **About**, **Diagnostics**, or consent dialogs, particularly with non-English languages or high-DPI scaling.
- Unexpected behavior when enabling/disabling diagnostics or reviewing consent terms.
- Incorrect update status, download progress, release notes, or ready-to-install actions.
- Headset disconnections, incorrect battery/charging/microphone status, or Auto Power-Off settings that fail to persist.

When reporting an issue, include the HBM version, Windows version, headset model and receiver VID/PID if available, and steps to reproduce. If sharing diagnostic `.jsonl` files, review and remove sensitive data first.

[Report an issue on GitHub](https://github.com/davidsantanaalves/HyperBatteryMonitor/issues)

## Before Installing

**BETA 3 is intended for community testing.** The newest Stable release remains the recommended choice for users prioritizing maximum reliability. A GitHub prerelease is separate from Microsoft Store-managed updates. Avoid assuming results from one headset, Windows display scale, or language apply to all configurations.

---

**Hyper Battery Monitor** · Open Source · MIT License
