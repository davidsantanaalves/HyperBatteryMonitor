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
        InvalidateAutoPowerOffForDeviceChange();
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
        IHyperXDevice? previewDevice = HyperXDeviceManager.CreateDevice(_pendingSelectedDevice,
            GetPendingDeviceMonitoringMode());
        _device = previewDevice;
        _isCharging = false;
        _batteryRemainingTime = null;

        if (previewDevice == null)
            return;

        BatteryMonitor previewMonitor = new(previewDevice, GetPendingDeviceMonitoringMode());

        previewMonitor.BatteryChanged += DevicePreviewMonitor_BatteryChanged;
        previewMonitor.ConnectionChanged += DevicePreviewMonitor_ConnectionChanged;
        previewMonitor.ChargingChanged += DevicePreviewMonitor_ChargingChanged;
        previewMonitor.MicrophoneMuteChanged += DevicePreviewMonitor_MicrophoneMuteChanged;
        previewMonitor.MicrophoneMuteStateInvalidated += DevicePreviewMonitor_MicrophoneMuteStateInvalidated;

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
            _devicePreviewMonitor.MicrophoneMuteStateInvalidated -= DevicePreviewMonitor_MicrophoneMuteStateInvalidated;
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
        if (device is IMicrophoneMuteStateInvalidationSource mute)
            mute.MicrophoneMuteStateInvalidated += Device_MicrophoneMuteStateInvalidated;
    }

    private void UnsubscribeFromDeviceStatusEvents(IHyperXDevice? device)
    {
        if (device == null)
            return;

        device.BatteryChanged -= Device_BatteryChanged;
        device.MicrophoneMuteChanged -= Device_MicrophoneMuteChanged;
        if (device is IMicrophoneMuteStateInvalidationSource mute)
            mute.MicrophoneMuteStateInvalidated -= Device_MicrophoneMuteStateInvalidated;
    }

    private void DevicePreviewMonitor_BatteryChanged(object? sender, int battery) =>
        RefreshDevicePreviewStatus(sender);

    private void DevicePreviewMonitor_ConnectionChanged(object? sender, bool connected) =>
        RefreshDevicePreviewStatus(sender);

    private void DevicePreviewMonitor_MicrophoneMuteChanged(object? sender, bool muted) =>
        RefreshDevicePreviewStatus(sender);

    private void DevicePreviewMonitor_MicrophoneMuteStateInvalidated(object? sender, EventArgs e) =>
        RefreshDevicePreviewStatus(sender);

    private void Device_MicrophoneMuteStateInvalidated(object? sender, EventArgs e) =>
        Device_MicrophoneMuteChanged(sender, false);

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
        RefreshAutoPowerOffState();
        RefreshNotificationMicrophoneToggle();
    }

    private void RefreshNotificationMicrophoneToggle()
    {
        if (_currentPage != "Notifications" || _showMicrophoneMuteInSystrayToggle is not { IsDisposed: false } toggle)
            return;

        bool supported = HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(
            _pendingSelectedDevice, _device);
        toggle.Enabled = supported;
        toggle.SetChecked(supported && _pendingShowMicrophoneMuteInSystray, notify: false);
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
            _sidebarBatteryLabel.Text = L("SidebarWaiting");
        }
        bool microphoneMonitoringSupported =
            selected && HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(normalized, _device);
        bool microphoneStatusAvailable =
            connected &&
            microphoneMonitoringSupported &&
            _device!.IsMicrophoneMuteStateKnown;
        bool microphoneMuted = microphoneStatusAvailable && _device!.IsMicrophoneMuted;
        _sidebarMicrophoneLabel.Text = microphoneStatusAvailable
            ? (microphoneMuted ? L("MicrophoneMuted") : L("MicrophoneOpen"))
            : L("SidebarWaiting");
        _sidebarMicrophoneSummaryGlyph.IconKey = microphoneStatusAvailable && microphoneMuted
            ? MicrophoneMutedIconAssetName : MicrophoneOpenIconAssetName;

        Color foreground = EffectiveTheme == AppTheme.Dark ? Color.WhiteSmoke : LightText;
        Color secondary = EffectiveTheme == AppTheme.Dark ? DarkSecondary : LightSecondary;
        _sidebarDeviceNameLabel.ForeColor = foreground;
        _sidebarStatusTitleLabel.ForeColor = secondary;
        _sidebarStatusLabel.ForeColor = secondary;
        _sidebarBatteryTitleLabel.ForeColor = secondary;
        _sidebarBatteryLabel.ForeColor = secondary;
        _sidebarMicrophoneTitleLabel.ForeColor = secondary;
        _sidebarMicrophoneLabel.ForeColor = secondary;

        bool powerOffSupported = selected &&
            HyperXDeviceManager.SupportsAutoPowerOff(deviceName) &&
            (!connected || _device is IAutoPowerOffDevice { SupportsAutoPowerOff: true });
        _sidebarAutoPowerOffLabel.Text = !connected
            ? L("SidebarWaiting")
            : FormatSidebarAutoPowerOff(ReferenceEquals(_device, _runtimeDevice)
                ? _settings.RuntimeAutoPowerOffMinutes : _autoPowerOffCurrent);
        _sidebarMonitoringLabel.Text = selected
            ? L(GetPendingDeviceMonitoringMode() == DeviceMonitoringMode.ListenOnly
                ? "MonitoringModeListenOnly" : "MonitoringModePolling")
            : L("SidebarWaiting");
        ReflowSidebarSummary(microphoneMonitoringSupported, powerOffSupported, selected);
        _sidebarAutoPowerOffTitleLabel.ForeColor = secondary;
        _sidebarAutoPowerOffLabel.ForeColor = foreground;
        _sidebarMonitoringTitleLabel.ForeColor = secondary;
        _sidebarMonitoringLabel.ForeColor = foreground;
        _sidebarBatteryLabel.ForeColor = foreground;
        _sidebarMicrophoneLabel.ForeColor = foreground;
        _sidebarSummaryDivider.BackColor = EffectiveTheme == AppTheme.Dark ? DarkBorder : LightBorder;

        if (string.Equals(_sidebarImageFileName, imageFile, StringComparison.OrdinalIgnoreCase))
            return;
        _sidebarImageFileName = imageFile;
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

    private string FormatSidebarAutoPowerOff(int? minutes) => minutes switch
    {
        0 => L("AutoPowerOffNever"),
        10 or 20 or 30 => $"{minutes} min",
        _ => L("SidebarWaiting")
    };

    private void ReflowSidebarSummary(bool microphoneSupported, bool powerOffSupported, bool selected)
    {
        // Keep the original sidebar width. Each supported row receives enough
        // space for its complete localized caption and value; unusually long
        // pairs stack rather than drawing ellipses or overlapping one another.
        int width = _sidebar.ClientSize.Width - ScaleUi(20);
        _sidebarDeviceCard.Width = width;
        int row = ScaleUi(80);
        int gap = ScaleUi(4);
        row += PositionSidebarRow(_sidebarBatteryTitleLabel, _sidebarBatteryLabel,
            _sidebarBatterySummaryGlyph, row) + gap;

        _sidebarMicrophoneTitleLabel.Visible = microphoneSupported;
        _sidebarMicrophoneLabel.Visible = microphoneSupported;
        _sidebarMicrophoneSummaryGlyph.Visible = microphoneSupported;
        if (microphoneSupported)
        {
            row += PositionSidebarRow(_sidebarMicrophoneTitleLabel, _sidebarMicrophoneLabel,
                _sidebarMicrophoneSummaryGlyph, row) + gap;
        }
        _sidebarAutoPowerOffTitleLabel.Visible = powerOffSupported;
        _sidebarAutoPowerOffLabel.Visible = powerOffSupported;
        _sidebarPowerOffSummaryGlyph.Visible = powerOffSupported;
        if (powerOffSupported)
        {
            row += PositionSidebarRow(_sidebarAutoPowerOffTitleLabel, _sidebarAutoPowerOffLabel,
                _sidebarPowerOffSummaryGlyph, row) + gap;
        }
        _sidebarMonitoringTitleLabel.Visible = selected;
        _sidebarMonitoringLabel.Visible = selected;
        _sidebarMonitoringSummaryGlyph.Visible = selected;
        if (selected)
        {
            row += PositionSidebarRow(_sidebarMonitoringTitleLabel, _sidebarMonitoringLabel,
                _sidebarMonitoringSummaryGlyph, row) + gap;
        }

        int height = row + ScaleUi(4); // 4px row gap plus 4px bottom padding
        _sidebarDeviceCard.Size = new Size(width, height);
        int upper = ScaleUi(SidebarFirstSeparatorLogicalY);
        int bandHeight = ScaleUi(SidebarSecondSeparatorLogicalY - SidebarFirstSeparatorLogicalY);
        _sidebarDeviceCard.Location = new Point(ScaleUi(10), upper + Math.Max(0, (bandHeight - height) / 2));
        _sidebarDeviceImage.Bounds = new Rectangle(ScaleUi(8), ScaleUi(7), ScaleUi(48), ScaleUi(48));
        _sidebarDeviceNameLabel.Bounds = new Rectangle(ScaleUi(59), ScaleUi(7), width - ScaleUi(66), ScaleUi(34));
        _sidebarStatusDot.Bounds = new Rectangle(ScaleUi(59), ScaleUi(42), ScaleUi(18), ScaleUi(18));
        _sidebarStatusLabel.Bounds = new Rectangle(ScaleUi(80), ScaleUi(42), width - ScaleUi(87), ScaleUi(20));
        _sidebarSummaryDivider.Bounds = new Rectangle(ScaleUi(9), ScaleUi(69), width - ScaleUi(18), ScaleUi(1));
        _sidebarDeviceCard.Invalidate();
    }

    private int PositionSidebarRow(Label title, Label value, PngIconControl icon, int top)
    {
        int left = ScaleUi(29);
        int right = _sidebarDeviceCard.ClientSize.Width - ScaleUi(7);
        int available = Math.Max(ScaleUi(1), right - left);
        int line = ScaleUi(20);
        int gap = ScaleUi(4);
        int titleWidth = TextRenderer.MeasureText(title.Text, title.Font,
            new Size(2000, line), TextFormatFlags.SingleLine).Width + ScaleUi(2);
        int valueWidth = TextRenderer.MeasureText(value.Text, value.Font,
            new Size(2000, line), TextFormatFlags.SingleLine).Width + ScaleUi(2);

        // Center icon geometrically on the first text line; no manual Y offset.
        int iconSize = ScaleUi(15);
        icon.Bounds = new Rectangle(ScaleUi(9), top + (line - iconSize) / 2,
            iconSize, iconSize);
        title.AutoEllipsis = false;
        value.AutoEllipsis = false;
        if (titleWidth + gap + valueWidth <= available)
        {
            title.Bounds = new Rectangle(left, top, titleWidth, line);
            value.Bounds = new Rectangle(left + titleWidth + gap, top,
                available - titleWidth - gap, line);
            return line;
        }

        // Long languages and remaining-time values use a two-line pair.
        int titleHeight = Math.Max(line, TextRenderer.MeasureText(title.Text, title.Font,
            new Size(available, 500), TextFormatFlags.WordBreak).Height + ScaleUi(2));
        int valueHeight = Math.Max(line, TextRenderer.MeasureText(value.Text, value.Font,
            new Size(available, 500), TextFormatFlags.WordBreak).Height + ScaleUi(2));
        title.Bounds = new Rectangle(left, top, available, titleHeight);
        value.Bounds = new Rectangle(left, top + titleHeight + ScaleUi(1), available, valueHeight);
        return titleHeight + ScaleUi(1) + valueHeight;
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
    private DeviceMonitoringMode GetPendingDeviceMonitoringMode()
    {
        string model = HyperXDeviceManager.GetSelectionModelName(_pendingSelectedDevice);
        if (!HyperXDeviceManager.SupportsListenOnlyMonitoring(model))
            return DeviceMonitoringMode.Polling;
        if (_pendingDeviceMonitoringModes.TryGetValue(model, out DeviceMonitoringMode chosen))
            return chosen;
        return string.Equals(model, "HyperX Cloud III", StringComparison.OrdinalIgnoreCase)
            ? DeviceMonitoringMode.ListenOnly : DeviceMonitoringMode.Polling;
    }

}
