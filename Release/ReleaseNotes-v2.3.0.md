## New features

- Added support for HyperX Cloud Flight S, Cloud Flight Wireless, Cloud Stinger Core Wireless + 7.1, Cloud Flight 2 via Dedicated Dongle, and Cloud Mix 2, bringing the supported lineup to ten models.
- Added automatic headset detection with visual progress and guidance when no compatible headset or multiple headsets are found. HyperX Three-In-One receiver presence is recognized to explain that this connection is not yet supported for monitoring.

## Battery estimate improvements

- Remaining-time estimates now start each session from nominal battery life and gradually adapt using validated discharge windows instead of isolated percentage drops.
- Incompatible old battery history is reset, and implausible discharge samples are filtered to reduce the influence of spurious readings and outliers. Estimates remain approximate.

## UI improvements

- Updated the Device page with supported features, connection information, and battery information.
- Cloud III S and Cloud Flight 2 now display localized Dedicated Dongle labels to clarify their supported connection.
- Improved layout and English, Portuguese (Brazil), and Spanish text, and removed unused localization entries.

Charging monitoring is unavailable for Cloud Mix 2. Microphone mute monitoring is unavailable for Cloud Stinger 2, Cloud Flight S, Cloud Flight Wireless, and Cloud Stinger Core Wireless + 7.1.

**Full Changelog**: https://github.com/davidsantanaalves/HyperBatteryMonitor/compare/v2.2.0...v2.3.0
