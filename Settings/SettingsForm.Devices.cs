using HyperXBatteryTray;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Monitoring;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm : Form
{
    private IHyperXDevice? _runtimeDevice;
    private bool _runtimeIsCharging;
    private BatteryMonitor? _devicePreviewMonitor;

    private void DeviceSelector_SelectionChanged(object? sender, EventArgs e)
    {
        _pendingSelectedDevice = _deviceSelector.SelectedDeviceName;
        StartDevicePreviewForSelection();
        RefreshDeviceStatus();
    }

    private void StartDevicePreviewForSelection()
    {
        StopDevicePreview();

        if (string.IsNullOrWhiteSpace(_pendingSelectedDevice) ||
            (_runtimeDevice != null && HyperXDeviceManager.IsSameDevice(
                _pendingSelectedDevice,
                _settings.SelectedDevice)))
        {
            return;
        }

        UnsubscribeFromDeviceStatusEvents(_device);
        IHyperXDevice? previewDevice = HyperXDeviceManager.CreateDevice(_pendingSelectedDevice);
        _device = previewDevice;
        _isCharging = false;
        _batteryRemainingTime = null;

        if (previewDevice == null)
            return;

        BatteryMonitor previewMonitor = new(previewDevice);

        previewMonitor.BatteryChanged += DevicePreviewMonitor_BatteryChanged;
        previewMonitor.ConnectionChanged += DevicePreviewMonitor_ConnectionChanged;
        previewMonitor.ChargingChanged += DevicePreviewMonitor_ChargingChanged;
        previewMonitor.MicrophoneMuteChanged += DevicePreviewMonitor_MicrophoneMuteChanged;

        _devicePreviewMonitor = previewMonitor;
        previewMonitor.Start();
    }

    private void StopDevicePreview()
    {
        if (_devicePreviewMonitor != null)
        {
            _devicePreviewMonitor.BatteryChanged -= DevicePreviewMonitor_BatteryChanged;
            _devicePreviewMonitor.ConnectionChanged -= DevicePreviewMonitor_ConnectionChanged;
            _devicePreviewMonitor.ChargingChanged -= DevicePreviewMonitor_ChargingChanged;
            _devicePreviewMonitor.MicrophoneMuteChanged -= DevicePreviewMonitor_MicrophoneMuteChanged;
            _devicePreviewMonitor.Dispose();
            _devicePreviewMonitor = null;
        }

        if (!ReferenceEquals(_device, _runtimeDevice))
        {
            UnsubscribeFromDeviceStatusEvents(_device);
            _device = _runtimeDevice;
            SubscribeToDeviceStatusEvents(_device);
        }

        _isCharging = _runtimeIsCharging;
        _batteryRemainingTime = _runtimeBatteryRemainingTime;
    }

    private void SubscribeToDeviceStatusEvents(IHyperXDevice? device)
    {
        if (device == null)
            return;

        device.BatteryChanged += Device_BatteryChanged;
        device.MicrophoneMuteChanged += Device_MicrophoneMuteChanged;
    }

    private void UnsubscribeFromDeviceStatusEvents(IHyperXDevice? device)
    {
        if (device == null)
            return;

        device.BatteryChanged -= Device_BatteryChanged;
        device.MicrophoneMuteChanged -= Device_MicrophoneMuteChanged;
    }

    private void DevicePreviewMonitor_BatteryChanged(object? sender, int battery) =>
        RefreshDevicePreviewStatus(sender);

    private void DevicePreviewMonitor_ConnectionChanged(object? sender, bool connected) =>
        RefreshDevicePreviewStatus(sender);

    private void DevicePreviewMonitor_MicrophoneMuteChanged(object? sender, bool muted) =>
        RefreshDevicePreviewStatus(sender);

    private void DevicePreviewMonitor_ChargingChanged(object? sender, bool charging)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => DevicePreviewMonitor_ChargingChanged(sender, charging)));
            return;
        }

        if (!ReferenceEquals(sender, _devicePreviewMonitor))
            return;

        _isCharging = charging;
        RefreshDeviceStatus();
    }

    private void RefreshDevicePreviewStatus(object? sender)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => RefreshDevicePreviewStatus(sender)));
            return;
        }

        if (!ReferenceEquals(sender, _devicePreviewMonitor))
            return;

        RefreshDeviceStatus();
    }

    private void RefreshDeviceStatus()
    {
        bool selected = _deviceSelector.SelectedIndex > 0;
        bool connected = selected && _device?.IsConnected == true && _device.Battery >= 0 && _device.Battery <= 100;

        // The main Device page no longer displays the legacy status card.
        // Keep these updates conditional so device selection/status refreshes
        // continue to update the sidebar and information cards without
        // dereferencing controls that are not created on the new layout.
        if (_deviceStatusLabel != null)
        {
            _deviceStatusLabel.Text = connected ? L("Connected") : selected ? L("Disconnected") : L("UnknownHeadphones");
            _deviceStatusLabel.Visible = true;
        }
        if (_deviceStatusDescriptionLabel != null)
        {
            _deviceStatusDescriptionLabel.Text = connected ? L("ConnectedDescription") : selected ? L("DisconnectedDescription") : L("UnknownDeviceDescription");
            _deviceStatusDescriptionLabel.Visible = true;
        }
        if (_batteryValueLabel != null)
            _batteryValueLabel.Text = connected ? $"{Math.Clamp(_device!.Battery, 0, 100)}%" : L("BatteryNA");
        if (_chargingLabel != null)
        {
            _chargingLabel.Text = _isCharging ? L("ChargingStatus") : string.Empty;
            _chargingLabel.Visible = connected && _isCharging;
        }

        Color primaryText = EffectiveTheme == AppTheme.Dark ? Color.WhiteSmoke : LightText;
        Color secondaryText = EffectiveTheme == AppTheme.Dark ? DarkSecondary : LightSecondary;
        if (_deviceStatusLabel != null) _deviceStatusLabel.ForeColor = primaryText;
        if (_deviceStatusDescriptionLabel != null) _deviceStatusDescriptionLabel.ForeColor = secondaryText;
        if (_batteryValueLabel != null) _batteryValueLabel.ForeColor = primaryText;
        if (_chargingLabel != null) _chargingLabel.ForeColor = secondaryText;
        if (_batteryIcon != null)
        {
            _batteryIcon.Connected = connected;
            _batteryIcon.Battery = connected && _device != null ? Math.Clamp(_device.Battery, 0, 100) : 0;
            _batteryIcon.DarkMode = EffectiveTheme == AppTheme.Dark;
            _batteryIcon.Charging = connected && _isCharging;
            _batteryIcon.Invalidate();
        }
        if (_deviceStatusDot != null)
        {
            _deviceStatusDot.Connected = connected;
            _deviceStatusDot.Invalidate();
        }

        UpdateSidebarDeviceStatus(selected, connected);
        UpdateDeviceInformation();
    }

    private void UpdateSidebarDeviceStatus(bool selected, bool connected)
    {
        if (_sidebarDeviceCard == null)
            return;

        string deviceName = _deviceSelector?.SelectedDeviceName ?? string.Empty;
        string normalized = string.IsNullOrWhiteSpace(deviceName)
            ? string.Empty
            : HyperXDeviceManager.NormalizeSupportedDeviceName(deviceName);

        string imageFile = normalized switch
        {
            "HyperX Cloud III" => "cloud3.png",
            "HyperX Cloud III S" => "cloud3.png",
            "HyperX Cloud 2 Core" => "cloud2core.png",
            "HyperX Cloud Alpha" => "cloudalpha.png",
            "HyperX Cloud Stinger 2" => "cloudstinger2.png",
            "HyperX Cloud Flight S" => "cloudflights.png",
            "HyperX Cloud Flight Wireless" => "cloudflightwireless.png",
            "HyperX Cloud Stinger Core Wireless + 7.1" => "cloudstingercorewireless7.1.png",
            "HyperX Cloud Flight 2" => "cloudflight2.png",
            "HyperX Cloud Mix 2" => "cloudmix2.png",
        "HyperX Cloud Alpha 2 Wireless" => "cloudalpha2.png",
        "HyperX Cloud Stinger 3 Wireless" => "cloudstinger3.png",
        "HyperX Cloud III (Receiver 0FCC)" => "cloud3.png",
        "HyperX Cloud III S (Receiver 02CC)" => "cloud3.png",
        "HyperX Cloud III S (TIO - Three-In-One)" => "cloud3.png",
        "HyperX Cloud Flight 2 (Xbox)" => "cloudflight2.png",
        "HyperX Cloud Flight 2 (TIO - Three-In-One)" => "cloudflight2.png",
        "HyperX Cloud Stinger 3 Wireless (PC)" => "cloudstinger3.png",
        "HyperX Cloud Stinger 3 Wireless (PlayStation)" => "cloudstinger3.png",
        "HyperX Cloud Stinger 3 Wireless (Xbox)" => "cloudstinger3.png",
            _ => "unknown-device.png"
        };

        _sidebarDeviceNameLabel.Text = selected
            ? Localization.DeviceDisplayName(HyperXDeviceManager.GetSelectionModelName(normalized), _selectedLanguage)
            : L("UnknownHeadphones");
        _sidebarStatusLabel.Text = connected
            ? L("Connected")
            : selected ? L("Disconnected") : L("SidebarUnknown");
        _sidebarStatusDot.Connected = connected;
        _sidebarStatusDot.Invalidate();

        _sidebarBatteryIcon.Connected = connected;
        _sidebarBatteryIcon.Battery = connected && _device != null ? Math.Clamp(_device.Battery, 0, 100) : 0;
        _sidebarBatteryIcon.Charging = connected && _isCharging;
        _sidebarBatteryIcon.DarkMode = EffectiveTheme == AppTheme.Dark;
        _sidebarBatteryIcon.Invalidate();

        if (connected)
        {
            int batteryPercent = Math.Clamp(_device!.Battery, 0, 100);
            string? remaining = _isCharging
                ? null
                : BatteryRemainingTimeFormatter.Format(
                    _batteryRemainingTime,
                    _selectedLanguage);

            _sidebarBatteryLabel.Text = remaining == null
                ? $"{batteryPercent}%"
                : $"{batteryPercent}% ({remaining})";
        }
        else
        {
            _sidebarBatteryLabel.Text = L("BatteryNA");
        }
        bool microphoneMonitoringSupported =
            selected && HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(normalized);
        bool microphoneStatusAvailable =
            connected &&
            microphoneMonitoringSupported &&
            _device!.IsMicrophoneMuteStateKnown;
        bool microphoneMuted = microphoneStatusAvailable && _device!.IsMicrophoneMuted;
        _sidebarMicrophoneLabel.Text = microphoneStatusAvailable
            ? (microphoneMuted ? L("MicrophoneMuted") : L("MicrophoneOpen"))
            : L("MicrophoneNA");
        bool useMuteIcon = !selected || !microphoneMonitoringSupported || microphoneMuted;
        UpdateSidebarMicrophoneIcon(EffectiveTheme == AppTheme.Dark, useMuteIcon);

        Color foreground = EffectiveTheme == AppTheme.Dark ? Color.WhiteSmoke : LightText;
        Color secondary = EffectiveTheme == AppTheme.Dark ? DarkSecondary : LightSecondary;
        _sidebarDeviceNameLabel.ForeColor = foreground;
        _sidebarStatusTitleLabel.ForeColor = secondary;
        _sidebarStatusLabel.ForeColor = secondary;
        _sidebarBatteryTitleLabel.ForeColor = secondary;
        _sidebarBatteryLabel.ForeColor = secondary;
        _sidebarMicrophoneTitleLabel.ForeColor = secondary;
        _sidebarMicrophoneLabel.ForeColor = secondary;

        _sidebarDeviceImage.Image?.Dispose();
        _sidebarDeviceImage.Image = null;

        if (!string.IsNullOrWhiteSpace(imageFile))
        {
            string imagePath = AssetPaths.GetDeviceImagePath(imageFile);
            if (File.Exists(imagePath))
            {
                try
                {
                    using Image source = Image.FromFile(imagePath);
                    _sidebarDeviceImage.Image = new Bitmap(source);
                }
                catch
                {
                    _sidebarDeviceImage.Image = null;
                }
            }
        }

        _sidebarDeviceImage.Visible = _sidebarDeviceImage.Image != null;
    }

    private void UpdateSidebarMicrophoneIcon(bool dark, bool useMuteIcon)
    {
        if (_sidebarMicrophoneIcon == null ||
            (_sidebarMicrophoneIconDark == dark &&
             _sidebarMicrophoneIconUsesMuteAsset == useMuteIcon))
        {
            return;
        }

        Image? previous = _sidebarMicrophoneIcon.Image;
        _sidebarMicrophoneIcon.Image = null;
        previous?.Dispose();

        string themePrefix = dark ? "dark" : "light";
        string iconName = useMuteIcon ? MicrophoneMutedIconAssetName : MicrophoneOpenIconAssetName;
        string path = AssetPaths.GetThemeIconPath(
            dark,
            $"{themePrefix}_{iconName}-{SidebarMicrophoneIconLogicalSize}x{SidebarMicrophoneIconLogicalSize}.png");

        if (File.Exists(path))
        {
            try
            {
                using Image source = Image.FromFile(path);
                _sidebarMicrophoneIcon.Image = new Bitmap(source);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Could not load microphone icon '{path}': {ex.Message}");
                _sidebarMicrophoneIcon.Image = null;
            }
        }

        _sidebarMicrophoneIconDark = dark;
        _sidebarMicrophoneIconUsesMuteAsset = useMuteIcon;
    }

    private void UpdateDeviceInformation()
    {
        if (_wirelessTechnologyValueLabel == null) return;

        string device = _deviceSelector?.SelectedDeviceName ?? string.Empty;
        string normalized = string.IsNullOrWhiteSpace(device)
            ? string.Empty
            : HyperXDeviceManager.NormalizeSupportedDeviceName(device);

        string[] values = normalized switch
        {
            "HyperX Cloud III (Receiver 0FCC)" => new[]
            {
                L("Cloud3Wireless_WirelessTechnology"),
                L("Cloud3Wireless_ConnectionMethod"),
                L("Cloud3Wireless_Range"),
                L("Cloud3Wireless_Battery"),
                L("Cloud3Wireless_ChargeTime")
            },
            "HyperX Cloud III" => new[]
            {
                L("Cloud3Wireless_WirelessTechnology"),
                L("Cloud3Wireless_ConnectionMethod"),
                L("Cloud3Wireless_Range"),
                L("Cloud3Wireless_Battery"),
                L("Cloud3Wireless_ChargeTime")
            },
            "HyperX Cloud III S (Receiver 02CC)" => new[]
            {
                L("Cloud3S_WirelessTechnology"),
                L("Cloud3S_ConnectionMethod"),
                L("Cloud3S_Range"),
                L("Cloud3S_Battery"),
                L("Cloud3S_ChargeTime")
            },
            "HyperX Cloud III S" => new[]
            {
                L("Cloud3S_WirelessTechnology"),
                L("Cloud3S_ConnectionMethod"),
                L("Cloud3S_Range"),
                L("Cloud3S_Battery"),
                L("Cloud3S_ChargeTime")
            },
            "HyperX Cloud 2 Core" => new[]
            {
                L("Cloud2Core_WirelessTechnology"),
                L("Cloud2Core_ConnectionMethod"),
                L("Cloud2Core_Range"),
                L("Cloud2Core_Battery"),
                L("Cloud2Core_ChargeTime")
            },
            "HyperX Cloud Alpha" => new[]
            {
                L("CloudAlpha_WirelessTechnology"),
                L("CloudAlpha_ConnectionMethod"),
                L("CloudAlpha_Range"),
                L("CloudAlpha_Battery"),
                L("CloudAlpha_ChargeTime")
            },
            "HyperX Cloud Stinger 2" => new[]
            {
                L("CloudStinger2_WirelessTechnology"),
                L("CloudStinger2_ConnectionMethod"),
                L("CloudStinger2_Range"),
                L("CloudStinger2_Battery"),
                L("CloudStinger2_ChargeTime")
            },
            "HyperX Cloud Flight S" => new[]
            {
                L("CloudFlightS_WirelessTechnology"),
                L("CloudFlightS_ConnectionMethod"),
                L("CloudFlightS_Range"),
                L("CloudFlightS_Battery"),
                L("CloudFlightS_ChargeTime")
            },
            "HyperX Cloud Flight Wireless" => new[]
            {
                L("CloudFlightWireless_WirelessTechnology"),
                L("CloudFlightWireless_ConnectionMethod"),
                L("CloudFlightWireless_Range"),
                L("CloudFlightWireless_Battery"),
                L("CloudFlightWireless_ChargeTime")
            },
            "HyperX Cloud Stinger Core Wireless + 7.1" => new[]
            {
                L("CloudStingerCoreWireless_WirelessTechnology"),
                L("CloudStingerCoreWireless_ConnectionMethod"),
                L("CloudStingerCoreWireless_Range"),
                L("CloudStingerCoreWireless_Battery"),
                L("CloudStingerCoreWireless_ChargeTime")
            },
            "HyperX Cloud Flight 2" => new[]
            {
                L("CloudFlight2_WirelessTechnology"),
                L("CloudFlight2_ConnectionMethod"),
                L("CloudFlight2_Range"),
                L("CloudFlight2_Battery"),
                L("CloudFlight2_ChargeTime")
            },
            "HyperX Cloud Stinger 3 Wireless" => new[]
            {
                L("DeviceInformationNA"),
                L("DeviceInformationNA"),
                L("DeviceInformationNA"),
                L("DeviceInformationNA"),
                L("DeviceInformationNA")
            },
            "HyperX Cloud Mix 2" => new[]
            {
                L("CloudMix2_WirelessTechnology"),
                L("CloudMix2_ConnectionMethod"),
                L("CloudMix2_Range"),
                L("CloudMix2_Battery"),
                L("CloudMix2_ChargeTime")
            },
            _ => new[]
            {
                L("DeviceInformationNA"),
                L("DeviceInformationNA"),
                L("DeviceInformationNA"),
                L("DeviceInformationNA"),
                L("DeviceInformationNA")
            }
        };

        // Show the actual transport only when a verified Bluetooth battery
        // session is active. The USB-specific range/lifetime is not applicable.
        if (_device is BluetoothFallbackDevice bluetooth && bluetooth.IsUsingBluetooth)
        {
            values[0] = "Bluetooth";
            values[1] = "Bluetooth";
            values[2] = L("DeviceInformationNA");
            values[3] = L("DeviceInformationNA");
        }

        _wirelessTechnologyValueLabel.Text = values[0];
        _connectionMethodValueLabel.Text = values[1];
        _wirelessRangeValueLabel.Text = values[2];
        _batteryLifeValueLabel.Text = values[3];
        _chargeTimeValueLabel.Text = values[4];

        Color valueColor = EffectiveTheme == AppTheme.Dark ? Color.WhiteSmoke : LightText;
        _wirelessTechnologyValueLabel.ForeColor = valueColor;
        _connectionMethodValueLabel.ForeColor = valueColor;
        _wirelessRangeValueLabel.ForeColor = valueColor;
        _batteryLifeValueLabel.ForeColor = valueColor;
        _chargeTimeValueLabel.ForeColor = valueColor;
        UpdateDeviceCapabilities();
    }

    private void Device_BatteryChanged(object? sender, int battery)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(RefreshDeviceStatus));
            return;
        }
        RefreshDeviceStatus();
    }

    private void Device_MicrophoneMuteChanged(object? sender, bool muted)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(RefreshDeviceStatus));
            return;
        }
        RefreshDeviceStatus();
    }
}
