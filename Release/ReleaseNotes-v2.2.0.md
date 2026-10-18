## New features

- Estimated remaining battery time in the Settings sidebar, system tray menu, and tray tooltip. Estimates start from the model's nominal battery life and adapt to locally saved discharge history. They are approximate and are hidden while charging or disconnected.

## Fixes

- Implemented charging-status monitoring for HyperX Cloud Stinger 2, with separate validation of battery and charging responses.
- Corrected HID control-interface selection for Cloud III S, Cloud 2 Core, Cloud Alpha, and Cloud Stinger 2. HID communication now uses report lengths supplied by Windows for the connected device.
- Microphone status now remains unavailable until a valid state is received, including after reconnection, instead of initially reporting an open microphone.
- Limited tray tooltip text to the Windows maximum length.

## UI improvements

- Simplified charging presentation in the sidebar and tray menu, using the battery icon without a separate lightning image beside the percentage.
- Improved consistency of unavailable battery/device information labels in Portuguese and Spanish.

The supported headset lineup is unchanged. Microphone mute monitoring remains unavailable for Cloud Stinger 2.

**Full Changelog**: https://github.com/davidsantanaalves/HyperBatteryMonitor/compare/v2.1.1...v2.2.0
