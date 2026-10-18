# Hyper Battery Monitor v2.4.0 — Stable

**First stable release of the v2.4.0 series.** This release consolidates the changes introduced and tested throughout Betas 1–6. It is a Windows installer release through GitHub Releases; it does not imply publication of a Microsoft Store update.

## Expanded headset and receiver support

- Adds **HyperX Cloud Alpha 2 Wireless**, **Cloud Stinger 3 Wireless (PC, PlayStation and Xbox receivers)**, and the **Cloud Flight 2 Xbox** receiver variant.
- Supports additional **Cloud III Wireless** (`03F0:0FCC`) and **Cloud III S Wireless** (`03F0:02CC`) USB receiver variants, and selected **Cloud III S / Cloud Flight 2** headset configurations on the HyperX **Three-In-One (TIO)** receiver. Only one supported TIO child headset is monitored at a time.
- Adds the **HyperX Cloud Stinger Core Wireless for PlayStation** (SKU `4P5J1AA`, original receiver `03F0:0A93`, `MI_03&Col03`, Usage Page `FF00`, Usage `0001`) under the existing Cloud Stinger Core model selection. Its `FF 12 05` response validation and bounded retries apply **only** to this receiver. The original `0951:170B` receiver retains its previous single-read behavior.
- Includes the CloudX Stinger 3 Wireless Xbox receiver/interface parsing adjustment first delivered in Beta 2.

## Adaptive HID discovery and reliability

- Searches compatible HID control-interface candidates and validates actual battery responses instead of trusting the first VID/PID match.
- Uses the same validated receiver resolution for manual model selection and **Auto Detect**. Remembers a previously validated endpoint per receiver/model, then revalidates it on reconnect; avoids persisting unique USB paths or serial numbers.
- Corrects Windows SetupAPI Unicode HID interface-path extraction: the device-path string begins at offset **4**, even when the x64 structure's `cbSize` is **8**.
- Preserves USB/HID precedence, protects TIO headset-slot routing, and avoids showing stale or unverified device readings.
- Improves Auto Detect interaction with a **15-second** discovery window, **1.5-second** minimum display for fast discoveries, countdown and immediate cancellation.

## Windows Bluetooth battery fallback

- Adds a read-only Windows Bluetooth battery-percentage fallback for supported **Cloud III S, Cloud Alpha 2, Cloud Flight 2, Cloud Stinger 3 and Cloud MIX 2** configurations when Windows exposes trustworthy battery data.
- Prefers validated HID readings and automatically returns to a responsive USB receiver. Bluetooth-only mode does **not** infer microphone mute state, charging, or remaining runtime.

## In-app updates, installation and interface

- Adds configurable **Stable/Beta** update channels, numbered prerelease identification, and deliberate return-to-Stable support when running a newer Beta. Returning to an older Stable requires explicit confirmation and is never automatically downloaded.
- Shows contextual **See what's new** release notes in a native themed dialog; improves owner-window z-order and consistent themed scrolling.
- Fixes **Clean Installation** errors caused by the in-app updater executing Setup from the HBM data directory. Future updates stage a SHA-256-verified copy in a unique folder under the current user's **`%TEMP%`**, outside the directories selected for cleanup. No administrator elevation is needed for staging; Setup may still require the normal Windows UAC approval.
- Handles upgrades launched by older builds that still execute a cached Setup from the application data directory: removes settings/history and other user data while allowing **only** the currently executing, version-matched locked installer to remain temporarily. That file is removed on a later startup. Other deletion failures remain errors.
- Separates settings cleanup from startup-registration migration and adds maintenance diagnostics in **`%TEMP%\HBM-Maintenance.log`**. Normal installation/upgrades preserve user settings and history; **Clean Installation intentionally deletes them**.
- Preserves a **Debug-only** `--test --allowinstall` integration-test mode for the separate test-release repository. Release builds reject test-mode flags and never use that feed.

## Hardware and installation validation

- **Cloud III Wireless:** maintainer confirmed correct operation following the v2.4.0 changes.
- **Cloud III S Wireless alternative receiver (`03F0:02CC`):** a tester confirmed successful detection with the Beta 4 adaptive HID implementation.
- **Cloud Stinger Core Wireless PlayStation (`03F0:0A93`):** [@pvpiv](https://github.com/pvpiv) tested the official Beta 5 installer and confirmed detection and a stable 82% battery / 14 h display, plus automatic recovery after receiver unplug/reconnect and headset power-cycle.
- **Beta 6 Clean Installation:** maintainer reproduced and resolved the former locked-installer failure; verified cleanup from a legacy cached installer, later removal of the cached Setup file, normal installation preserving `settings.json`, and an end-to-end installation from the `--test --allowinstall` updater test feed with Clean Installation selected.

**Hardware-coverage limits:** Not every newly supported receiver/firmware variant, TIO arrangement or Bluetooth adapter has received end-to-end testing. Charging-dependent readings on Cloud Stinger 3 may require a supported notification. For the **Cloud Stinger Core PlayStation** variant, charging immediately raises terminal voltage, so the existing voltage-derived battery-percentage curve may **overestimate the charge while charging**; this release does not change the curve. Remaining-runtime values are estimates, not independent hardware measurements.

## Community acknowledgment

Special thanks to **[@pvpiv](https://github.com/pvpiv)**, contributor to [PR #6](https://github.com/davidsantanaalves/HyperBatteryMonitor/pull/6), for identifying the PlayStation receiver, sharing HID descriptors and 52 per-query diagnostic samples, identifying response/retry behavior, and validating reconnection and charging states. The implementation was integrated locally with full credit, without merging the PR.

Found an issue? Please report the headset model/SKU, receiver VID/PID and HID collection when available, connection type, HBM version, and observed behavior at [GitHub Issues](https://github.com/davidsantanaalves/HyperBatteryMonitor/issues). **Do not share unique device serial numbers.**

- **Tag:** `v2.4.0`
- **Windows installer:** `HyperBatteryMonitor-Setup-v2.4.0.exe`
- **Distribution:** GitHub Release — **Stable** (not Pre-release)
