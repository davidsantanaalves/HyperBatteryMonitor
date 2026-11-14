# Hyper Battery Monitor v2.4.1 — Stable

**Security maintenance release.** This update refreshes the .NET runtime bundled with the Windows application. It introduces no new headset features or user-interface changes.

## .NET runtime security update

- Updates the self-contained **.NET 10 runtime from 10.0.12 to 10.0.15**, incorporating Microsoft's runtime security fixes.
- Updates the bundled Windows Desktop runtime to **10.0.15** while keeping the existing **Windows x64** target and **Stable** release channel.
- Does not introduce new application dependencies or change device detection, battery monitoring, notifications, or update-channel behavior.

Because Hyper Battery Monitor is distributed as a **self-contained** Windows application, updating the .NET runtime installed separately on Windows does **not** replace the runtime bundled with an existing HBM installation. An application update is required to receive the corrected bundled runtime.

## Build and functional validation

- The maintainer successfully generated a **self-contained Release/win-x64** validation build using **.NET SDK 10.0.404**.
- Verified that the build reports **HBM 2.4.1** and embeds **.NET runtime 10.0.15** (`coreclr.dll` file version `10.0.1526.53008`).
- Confirmed that the application starts, Settings opens, and the **HyperX Cloud III Wireless** is detected with battery and microphone information visible.

**Validation scope:** These checks cover the runtime packaging and an initial functional smoke test. They do not constitute an exhaustive regression test of every supported headset or installer path.

Found an issue? Please report the headset model/SKU, receiver VID/PID and HID collection when available, connection type, HBM version, and observed behavior at [GitHub Issues](https://github.com/davidsantanaalves/HyperBatteryMonitor/issues). **Do not share unique device serial numbers.**

- **Tag:** `v2.4.1`
- **Windows installer:** `HyperBatteryMonitor-Setup-v2.4.1.exe`
- **Distribution:** GitHub Release — **Stable** (not Pre-release)
