# Hyper Battery Monitor v2.5.0 BETA 4

**Selectable monitoring modes, Device Settings, clearer headset status and controlled diagnostics**

This experimental GitHub prerelease combines the Beta 4 changes with the published Beta 3 fixes, including Cloud Stinger Core Wireless DTS support. It is not a Stable or Microsoft Store release.

## Monitoring modes

- **Listen-only is the default for the original Cloud III Wireless receiver (`03F0:05B7`)**, with Polling available as an alternative in Device Settings.
- Other eligible models keep **Polling by default**. Experimental Listen-only can be selected for supported dedicated HID paths in the Cloud III alternate receiver, Cloud III S, Cloud 2 Core, Cloud Alpha, Cloud MIX 2 and Cloud Stinger 3 families. Actual availability depends on the receiver and an unambiguous supported endpoint.
- Bluetooth, TIO and unsupported or ambiguous paths retain their existing verification/fallback behavior. Listen-only eligibility is not proof that every firmware emits every event.
- Listen-only removes scheduled battery polling while the applicable HID listener remains healthy. Initial/session recovery queries and a one-shot supported Auto Power-Off read can still occur: this mode does not mean zero HID traffic.

## Device Settings and Auto Power-Off

- Open **Device → Settings** for two blue monitoring-mode cards and four compact Auto Power-Off choices: **Never, 10, 20 and 30 minutes**, where supported.
- **Save in this modal is the hardware commit action.** Opening or canceling it, or using the main Settings OK/Apply buttons, does not write Auto Power-Off. A changed hardware value requires device confirmation; a failed hardware change keeps the modal open and does not save the new monitoring mode.
- Monitoring preferences are saved per model. The last verified Auto Power-Off value is also remembered per supported model for historical display.
- With a supported headset powered off, the four choices are disabled and the last verified value is selected when available. Without a prior verified value, none is selected. This history never triggers an automatic hardware write. Unsupported models show an unavailable message.

## Sidebar, tray and tooltip

- The compact headset summary includes image, connection, battery/remaining time, microphone, Auto Power-Off and monitoring mode as applicable to the selected model and verified receiver.
- Unavailable measurements use `...`; unsupported feature rows are hidden. Labels and values wrap when needed, with content-dependent height, centered status icons and one divider below the header.
- The custom tray popup preserves left/right click access and fixes transparent-control initialization that could prevent opening it.
- The native tooltip follows the available state and the Windows 127-character limit, reducing optional content when necessary.
- All 11 language catalogs include the new controls, states and diagnostic explanations.

## Optional local diagnostics

- Essential logging records significant errors and persistent faults, filters routine traffic and consolidates repeated errors.
- Detailed HID capture is explicitly consented and time limited, with selectable **1, 3, 6, 12 or 24-hour** sessions. It can run without enabling the saved essential-logging preference.
- An authorized session resumes after restarting HBM only until its **original UTC deadline**. It records nothing while HBM is closed; restart does not grant a new duration. Expired, malformed or outdated authorization is rejected.
- The diagnostics modal includes session status, storage usage, log opening/clearing, responsive duration selection and start/resume/stop/completion notifications.
- Logs stay local in `Documents\HBM Logs`, with existing rotation/retention limits. HBM does not upload them. Detailed reports and device paths can contain sensitive identifiers; review them before manually sharing. A separate Documents synchronization service may copy files independently.

## Preserved Beta 3 compatibility and credits

- Cloud Stinger Core Wireless DTS (`03F0:0A8E`) keeps battery monitoring, passive boom-microphone events, receiver-dependent capability reporting and recovery that isolates microphone failures from battery/charging monitoring.
- DTS microphone state remains unknown until a boom event after startup/reconnection/recovery. No initial-state query is established; configurable Auto Power-Off remains unsupported for DTS. Existing charging/voltage-calibration and hardware-coverage limits still apply.
- The original Kingston/PlayStation paths, receiver selection, user preferences, updater Beta/Stable policy, migration and installer identities are preserved.
- Thanks to **[@shapkees](https://github.com/shapkees)** for the DTS implementation, captures, reported hardware validation and regression tests in [PR #16](https://github.com/davidsantanaalves/HyperBatteryMonitor/pull/16). The original commits and authorship remain in the branch history.
- Existing acknowledgements, including **@pvpiv** and the historical **auto94** reference/license notice, remain in README and `THIRD_PARTY_NOTICES.md`.

## Known issues and validation scope

**The externally reported Cloud III Auto Power-Off reset to Never remains open; Beta 4 does not claim a definitive fix.** It was not reproduced on the maintainer's headset. Listen-only is an experimental mitigation, not proof of root cause or resolution.

The maintainer owns only Cloud III Wireless. Other models have source-level compatibility review and automated regression coverage where available; final Beta 4 physical validation of every model has not been performed locally. Listen-only can show stale state if firmware stops sending events silently. Charging/100%, RF off/on, USB hotplug and Windows sleep/resume remain important field checks. UI/DPI/language samples do not establish exhaustive visual coverage.

When reporting an issue, include HBM/Windows version, model, receiver VID/PID, firmware if known, monitoring mode and reproduction steps. Share detailed logs only voluntarily after review.

- **Target tag** `v2.5.0-beta.4`
- **Expected installer** `HyperBatteryMonitor-Setup-v2.5.0-beta.4.exe`
- **Distribution** GitHub prerelease. Stable Latest and Microsoft Store remain unchanged.
