using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Monitoring;
using HyperXBatteryTray.Notifications;
using HyperXBatteryTray.Settings;
using HyperXBatteryTray.Updates;

namespace HyperXBatteryTray;

public sealed class HyperXBatteryMonitorApplicationContext : ApplicationContext
{
    private const string DisconnectedIconFileSuffix = "_disconnected.ico";
    private const string MicrophoneMuteIconFileSuffix = "_mute.ico";
    private const int NotifyIconMaximumTextLength = 127;

    private readonly NotifyIcon _notifyIcon;
    private readonly HyperXDeviceManager _deviceManager;
    private BatteryMonitor? _batteryMonitor;
    private IHyperXDevice? _device;
    private readonly SettingsManager _settingsManager;
    private readonly AppSettings _settings;
    private readonly NotificationService _notificationService;
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly Control _toastActivationDispatcher = new();
    private readonly BatteryRemainingTimeEstimator _batteryRemainingTimeEstimator;

    private Icon? _currentApplicationIcon;
    private SettingsForm? _settingsForm;
    private TrayContextMenuForm? _trayMenu;
    private bool _blinkState;
    private bool _isCharging;
    private bool _isMicrophoneMuted;
    private System.Windows.Forms.Timer? _blinkTimer;
    private System.Windows.Forms.Timer? _leftClickTimer;
    private bool _leftClickPending;
    private Point _leftClickScreenLocation;

    public HyperXBatteryMonitorApplicationContext()
    {
        _settingsManager = new SettingsManager();
        _settings = _settingsManager.Load();
        _notificationService = new NotificationService();
        _updateCoordinator = new UpdateCoordinator(_settings);
        _batteryRemainingTimeEstimator = new BatteryRemainingTimeEstimator(
            _settingsManager.LoadBatteryHistory());

        if (!_settings.ThemeConfigured)
        {
            _settings.Theme = AppTheme.System;
            _settings.ThemeConfigured = true;
            _settingsManager.Save(_settings);
        }

        _deviceManager = new HyperXDeviceManager();

        // Do not enumerate or open HID devices until the user has explicitly
        // selected a supported device in Settings.
        if (!string.IsNullOrWhiteSpace(_settings.SelectedDevice))
            InitializeSelectedDevice();

        _currentApplicationIcon = CreateTrayIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentApplicationIcon,
            Visible = true
        };
        _notifyIcon.MouseUp += NotifyIcon_MouseUp;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        // Toast activation arrives on a background thread, including on cold launch.
        // The hidden WinForms control marshals all Settings navigation to the UI thread.
        _ = _toastActivationDispatcher.Handle;
        UpdateNotificationService.OpenAboutRequested += OnUpdateToastActivated;

        ApplyLocalization();
        ApplyTheme();
        UpdateTrayIcon();
        RestartBlinkTimer();

        UpdateTray();

        if (string.IsNullOrWhiteSpace(_settings.SelectedDevice))
            Application.Idle += Application_Idle;

        if (_batteryMonitor != null)
            _batteryMonitor.Start();

        _updateCoordinator.Start();
        if (UpdateNotificationService.ConsumePendingOpenAbout())
            OnUpdateToastActivated();
    }

    private void InitializeSelectedDevice()
    {
        DisposeDeviceMonitor();

        if (string.IsNullOrWhiteSpace(_settings.SelectedDevice))
            return;

        _device = _deviceManager.GetAvailableDevice(_settings.SelectedDevice);

        if (_device == null)
            return;

        _batteryMonitor = new BatteryMonitor(_device)
        {
            IntervalMilliseconds = 5000
        };

        _batteryMonitor.BatteryChanged += BatteryMonitor_BatteryChanged;
        _batteryMonitor.ConnectionChanged += BatteryMonitor_ConnectionChanged;
        _batteryMonitor.ChargingChanged += BatteryMonitor_ChargingChanged;
        _batteryMonitor.MicrophoneMuteChanged += BatteryMonitor_MicrophoneMuteChanged;
    }

    private void DisposeDeviceMonitor()
    {
        if (_batteryMonitor != null)
        {
            _batteryMonitor.BatteryChanged -= BatteryMonitor_BatteryChanged;
            _batteryMonitor.ConnectionChanged -= BatteryMonitor_ConnectionChanged;
            _batteryMonitor.ChargingChanged -= BatteryMonitor_ChargingChanged;
            _batteryMonitor.MicrophoneMuteChanged -= BatteryMonitor_MicrophoneMuteChanged;
            _batteryMonitor.Dispose();
            _batteryMonitor = null;
        }

        _device = null;
        _isCharging = false;
        _isMicrophoneMuted = false;
        _batteryRemainingTimeEstimator.ResetSession();
    }

    private void Application_Idle(object? sender, EventArgs e)
    {
        Application.Idle -= Application_Idle;
        ShowSettings(null, EventArgs.Empty);
    }

    private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme != AppTheme.System || _trayMenu == null || _trayMenu.IsDisposed)
            return;

        if (_trayMenu.IsHandleCreated)
        {
            try
            {
                _trayMenu.BeginInvoke((MethodInvoker)(() =>
                    _trayMenu?.ApplyTheme(GetEffectiveTheme() == AppTheme.Dark)));
            }
            catch (InvalidOperationException)
            {
                // The application is shutting down or the handle is no longer valid.
            }
        }
    }

    private static AppTheme GetWindowsTheme()
    {
        try
        {
            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            object? value = key?.GetValue("AppsUseLightTheme");

            if (value is int intValue)
                return intValue == 0
                    ? AppTheme.Dark
                    : AppTheme.Light;
        }
        catch
        {
        }

        return AppTheme.Light;
    }

    private string L(string key) => Localization.Get(key, _settings.Language);
    private AppTheme GetEffectiveTheme() =>
        _settings.Theme == AppTheme.System
            ? GetWindowsTheme()
            : _settings.Theme;



    private void ApplyLocalization()
    {
        _trayMenu?.ApplyLocalization();
        UpdateTray();
    }

    private void ApplyTheme()
    {
        _trayMenu?.ApplyTheme(GetEffectiveTheme() == AppTheme.Dark);
    }

    private void NotifyIcon_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            CancelPendingLeftClick();
            ShowTrayContextMenu(Cursor.Position);
            return;
        }

        if (e.Button != MouseButtons.Left)
            return;

        if (_leftClickPending)
        {
            CancelPendingLeftClick();
            ShowSettings(sender, e);
            return;
        }

        StartPendingLeftClick();
    }

    private void StartPendingLeftClick()
    {
        if (_leftClickTimer == null)
        {
            _leftClickTimer = new System.Windows.Forms.Timer();
            _leftClickTimer.Tick += LeftClickTimer_Tick;
        }

        _leftClickTimer.Stop();
        _leftClickScreenLocation = Cursor.Position;
        _leftClickTimer.Interval = Math.Max(1, SystemInformation.DoubleClickTime);
        _leftClickPending = true;
        _leftClickTimer.Start();
    }

    private void LeftClickTimer_Tick(object? sender, EventArgs e)
    {
        CancelPendingLeftClick();
        ShowTrayContextMenu(_leftClickScreenLocation);
    }

    private void CancelPendingLeftClick()
    {
        _leftClickPending = false;
        _leftClickTimer?.Stop();
    }

    private void ShowTrayContextMenu(Point screenLocation)
    {
        if (_trayMenu == null || _trayMenu.IsDisposed)
        {
            _trayMenu = new TrayContextMenuForm(
                _settings,
                _device,
                _isCharging,
                GetCurrentBatteryRemainingTime(),
                GetEffectiveTheme(),
                ShowSettings,
                ExitApplication);
            _trayMenu.FormClosed += (_, _) => _trayMenu = null;
        }
        else
        {
            _trayMenu.UpdateDevice(
                _device,
                _isCharging,
                GetCurrentBatteryRemainingTime());
        }

        _trayMenu.ApplyLocalization();
        _trayMenu.ApplyTheme(GetEffectiveTheme() == AppTheme.Dark);
        _trayMenu.ShowAt(screenLocation);
    }

    private void ShowSettings(object? sender, EventArgs e)
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Show();
            _settingsForm.BringToFront();
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(
            _settings,
            _device,
            _isCharging,
            GetCurrentBatteryRemainingTime(),
            _updateCoordinator,
            InstallDownloadedUpdateAsync);
        _settingsForm.SettingsApplied += SettingsForm_SettingsApplied;
        _settingsForm.FormClosed += SettingsForm_FormClosed;
        _settingsForm.Show();
        _settingsForm.BringToFront();
        _settingsForm.Activate();
    }

    private void OnUpdateToastActivated()
    {
        if (_toastActivationDispatcher.IsDisposed || !_toastActivationDispatcher.IsHandleCreated)
            return;

        try
        {
            _toastActivationDispatcher.BeginInvoke((MethodInvoker)(() =>
            {
                if (_toastActivationDispatcher.IsDisposed)
                    return;

                UpdateNotificationService.ConsumePendingOpenAbout();
                ShowSettings(null, EventArgs.Empty);
                _settingsForm?.NavigateToAbout();
            }));
        }
        catch (InvalidOperationException)
        {
            // Ignore activations racing with normal application shutdown.
        }
    }

    private void SettingsForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is SettingsForm form)
            form.SettingsApplied -= SettingsForm_SettingsApplied;
        _settingsForm = null;
    }

    private void SettingsForm_SettingsApplied(object? sender, EventArgs e)
    {
        try
        {
            _settingsManager.Save(_settings);
            _updateCoordinator.PreferencesChanged();
            _notificationService.ResetState();
            InitializeSelectedDevice();
            _settingsForm?.SetDevice(_device);
            _settingsForm?.SetBatteryRemainingTime(
                GetCurrentBatteryRemainingTime());
            ApplyLocalization();
            ApplyTheme();
            RestartBlinkTimer();
            UpdateTrayIcon();
            UpdateTray();

            if (_batteryMonitor != null)
                _batteryMonitor.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _settingsForm,
                string.Format(L("SaveError"), ex.Message),
                Application.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void BatteryMonitor_BatteryChanged(object? sender, int battery)
    {
        UpdateBatteryRemainingTimeState();

        if (_device != null)
        {
            _notificationService.Update(
                _settings.SelectedDevice,
                battery,
                _device.IsConnected,
                _isCharging,
                _settings);
        }

        UpdateTrayIcon();
        UpdateTray();
        _trayMenu?.UpdateDevice(
            _device,
            _isCharging,
            GetCurrentBatteryRemainingTime());
    }

    private void BatteryMonitor_ConnectionChanged(object? sender, bool connected)
    {
        if (!connected)
        {
            _isCharging = false;
            _isMicrophoneMuted = false;
            _notificationService.ResetState();
        }

        UpdateBatteryRemainingTimeState();

        if (connected && _device != null)
        {
            _notificationService.Update(
                _settings.SelectedDevice,
                _device.Battery,
                true,
                _isCharging,
                _settings);
        }

        _settingsForm?.SetCharging(_isCharging);
        UpdateTrayIcon();
        UpdateTray();
        _trayMenu?.UpdateDevice(
            _device,
            _isCharging,
            GetCurrentBatteryRemainingTime());
    }

    private void BatteryMonitor_ChargingChanged(
        object? sender,
        bool charging)
    {
        _isCharging = charging;
        UpdateBatteryRemainingTimeState();

        if (_device != null)
        {
            _notificationService.Update(
                _settings.SelectedDevice,
                _device.Battery,
                _device.IsConnected,
                _isCharging,
                _settings);
        }

        _settingsForm?.SetCharging(_isCharging);
        UpdateTrayIcon();
        UpdateTray();
        _trayMenu?.UpdateDevice(
            _device,
            _isCharging,
            GetCurrentBatteryRemainingTime());
    }

    private void BatteryMonitor_MicrophoneMuteChanged(
        object? sender,
        bool muted)
    {
        _isMicrophoneMuted = muted;

        if (!muted)
            _blinkState = false;

        UpdateTrayIcon();
        UpdateTray();
    }

    private void UpdateTray()
    {
        UpdateNotifyIconTooltip();
        _trayMenu?.UpdateDevice(
            _device,
            _isCharging,
            GetCurrentBatteryRemainingTime());
    }

    private void UpdateNotifyIconTooltip()
    {
        bool selected = !string.IsNullOrWhiteSpace(_settings.SelectedDevice);
        string deviceName = selected
            ? Localization.DeviceDisplayName(
                HyperXDeviceManager.GetSelectionModelName(_settings.SelectedDevice),
                _settings.Language)
            : L("UnknownHeadphones");
        string battery;
        string status;

        if (!selected || _device == null || !_device.IsConnected || _device.Battery < 0)
        {
            battery = L("TrayBatteryNA");
            status = L("TrayDisconnected");
        }
        else
        {
            int batteryPercent = Math.Clamp(_device.Battery, 0, 100);
            string? remaining = BatteryRemainingTimeFormatter.Format(
                GetCurrentBatteryRemainingTime(),
                _settings.Language);

            battery = remaining == null
                ? string.Format(L("TrayBattery"), batteryPercent)
                : string.Format(L("TrayBatteryWithRemaining"), batteryPercent, remaining);

            if (_isCharging)
                battery += $" {L("TrayCharging")}";
            status = L("TrayConnected");
        }

        bool microphoneStatusAvailable =
            selected &&
            _device?.IsConnected == true &&
            _device.SupportsMicrophoneMuteMonitoring &&
            _device.IsMicrophoneMuteStateKnown;
        string microphone = string.Format(
            L("TrayMicrophone"),
            microphoneStatusAvailable
                ? (_device!.IsMicrophoneMuted ? L("MicrophoneMuted") : L("MicrophoneOpen"))
                : L("MicrophoneNA"));

        string tooltip = $"{ApplicationRuntime.TestIndicator}{deviceName}\n{status}\n{battery}\n{microphone}";
        _notifyIcon.Text = tooltip.Length <= NotifyIconMaximumTextLength
            ? tooltip
            : tooltip[..NotifyIconMaximumTextLength];
    }

    private void UpdateBatteryRemainingTimeState()
    {
        int battery = _device?.Battery ?? -1;
        bool connected = _device?.IsConnected == true;

        // Bluetooth runtime percentages must not contaminate the historical HID
        // discharge model: nominal battery life differs between transports.
        bool isBluetooth = _device is BluetoothFallbackDevice fallback && fallback.IsUsingBluetooth;
        bool historyChanged = _batteryRemainingTimeEstimator.Observe(
            _settings.SelectedDevice,
            battery,
            connected && !isBluetooth,
            _isCharging);

        if (historyChanged)
        {
            try
            {
                _settingsManager.SaveBatteryHistory(
                    _batteryRemainingTimeEstimator.CreateHistorySnapshot());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Could not save battery history: {ex.Message}");
            }
        }

        _settingsForm?.SetBatteryRemainingTime(
            GetCurrentBatteryRemainingTime());
    }

    private TimeSpan? GetCurrentBatteryRemainingTime() =>
        _device is BluetoothFallbackDevice fallback && fallback.IsUsingBluetooth
            ? null
            : _batteryRemainingTimeEstimator.EstimateRemaining(
                _settings.SelectedDevice,
                _device?.Battery ?? -1,
                _device?.IsConnected == true,
                _isCharging);

    private static string NormalizeDeviceName(string value) =>
        HyperXDeviceManager.NormalizeSupportedDeviceName(value);

    private void RestartBlinkTimer()
    {
        _blinkTimer?.Stop();
        _blinkTimer?.Dispose();
        _blinkTimer = null;
        _blinkState = false;

        bool microphoneMuteBlinkSupported =
            _settings.ShowMicrophoneMuteInSystray &&
            _device?.SupportsMicrophoneMuteMonitoring == true;

        if (!_settings.BlinkOnCriticalBattery &&
            !microphoneMuteBlinkSupported)
            return;

        _blinkTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _blinkTimer.Tick += (_, _) =>
        {
            bool microphoneMuteBlink = ShouldShowMicrophoneMuteIcon();
            bool criticalBatteryBlink =
                !microphoneMuteBlink &&
                _settings.BlinkOnCriticalBattery &&
                IsCriticalBattery();

            if (!microphoneMuteBlink && !criticalBatteryBlink)
            {
                if (_blinkState)
                {
                    _blinkState = false;
                    UpdateTrayIcon();
                }

                return;
            }

            _blinkState = !_blinkState;
            UpdateTrayIcon();
        };
        _blinkTimer.Start();
    }
    private void UpdateTrayIcon()
    {
        Icon? baseIcon = null;
        Icon? newIcon = null;

        try
        {
            bool microphoneMuteBlink = ShouldShowMicrophoneMuteIcon();
            bool microphoneMutePhase = microphoneMuteBlink && _blinkState;
            bool criticalBlinkPhase =
                !microphoneMuteBlink &&
                _settings.BlinkOnCriticalBattery &&
                _blinkState &&
                IsCriticalBattery();

            if (microphoneMutePhase)
            {
                newIcon = CreateMicrophoneMuteIcon();
            }
            else
            {
                baseIcon = criticalBlinkPhase
                    ? CreateThemeIcon()
                    : CreateTrayIcon();
            }

            if (newIcon == null &&
                _isCharging &&
                _device?.IsConnected == true &&
                _device.Battery >= 0)
            {
                if (_settings.DisplayMode == BatteryDisplayMode.BatteryIndicator)
                {
                    baseIcon!.Dispose();
                    baseIcon = null;
                    newIcon = CreateChargingIcon();
                }
                else
                {
                    newIcon = CreateChargingOverlayIcon(baseIcon!);
                    baseIcon!.Dispose();
                    baseIcon = null;
                }
            }

            if (newIcon == null)
            {
                newIcon = baseIcon;
                baseIcon = null;
            }

            Icon? oldIcon = _currentApplicationIcon;

            _currentApplicationIcon = newIcon;
            _notifyIcon.Icon = newIcon;

            oldIcon?.Dispose();
        }
        catch
        {
            newIcon?.Dispose();
        }
        finally
        {
            baseIcon?.Dispose();
        }
    }

    private bool IsCriticalBattery() =>
        _device != null &&
        _device.IsConnected &&
        _device.Battery >= 0 &&
        _device.Battery <= _settings.CriticalBatteryPercent;

    private Icon CreateTrayIcon()
    {
        bool connected =
            !string.IsNullOrWhiteSpace(_settings.SelectedDevice) &&
            _device?.IsConnected == true &&
            _device.Battery >= 0 &&
            _device.Battery <= 100;

        if (!connected)
            return CreateDisconnectedIcon();

        int battery = _device!.Battery;
        Color batteryColor = GetBatteryColor(battery);

        if (_settings.DisplayMode == BatteryDisplayMode.StaticIcon)
            return CreateThemeIcon();

        if (_settings.DisplayMode == BatteryDisplayMode.BatteryIndicator)
            return CreateBatteryIndicatorIcon(battery);

        return _settings.AdvancedDisplayMode switch
        {
            AdvancedDisplayMode.BatteryGradient =>
                CreateRenderedIcon(
                    batteryColor,
                    null,
                    colorizeHeadset: true),

            AdvancedDisplayMode.BatteryIndicator =>
                CreateRenderedIcon(
                    batteryColor,
                    battery,
                    colorizeHeadset: false),

            AdvancedDisplayMode.PercentageText =>
                CreatePercentageOverlayIcon(
                    battery,
                    connected),

            _ =>
                CreateThemeIcon()
        };
    }

    private Icon CreateBatteryIndicatorIcon(int battery)
    {
        string suffix = battery switch
        {
            >= 50 => "green",
            >= 30 => "yellow",
            >= 15 => "orange",
            _ => "red"
        };

        return LoadIconFile($"{GetThemePrefix()}_{suffix}.ico");
    }

    private Icon CreateChargingIcon() =>
        LoadIconFile($"{GetThemePrefix()}_charging.ico");

    private bool ShouldShowMicrophoneMuteIcon() =>
        _settings.ShowMicrophoneMuteInSystray &&
        _isMicrophoneMuted &&
        _device?.IsConnected == true &&
        _device.SupportsMicrophoneMuteMonitoring;

    private Icon CreateMicrophoneMuteIcon() =>
        LoadIconFile($"{GetThemePrefix()}{MicrophoneMuteIconFileSuffix}");

    private string GetThemePrefix() =>
        GetEffectiveTheme() == AppTheme.Dark ? "dark" : "light";

    private Icon LoadIconFile(string fileName)
    {
        string path = AssetPaths.GetThemeIconPath(
            GetEffectiveTheme() == AppTheme.Dark,
            fileName);

        try
        {
            if (File.Exists(path))
                return new Icon(path);
        }
        catch
        {
        }

        return Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? new Icon(SystemIcons.Application, SystemIcons.Application.Size);
    }

	private Icon CreateChargingOverlayIcon(Icon baseIcon)
	{
		using Bitmap bitmap =
			new Bitmap(
				16,
				16,
				PixelFormat.Format32bppArgb);

		using Graphics graphics =
			Graphics.FromImage(bitmap);

		graphics.SmoothingMode =
			SmoothingMode.AntiAlias;

		graphics.InterpolationMode =
			InterpolationMode.HighQualityBicubic;

		graphics.PixelOffsetMode =
			PixelOffsetMode.HighQuality;

		graphics.Clear(Color.Transparent);

		using Bitmap iconBitmap =
			RenderIcon(
				baseIcon,
				16,
				16,
				replacementColor: null);

		graphics.DrawImage(
			iconBitmap,
			0,
			0,
			16,
			16);

		if (_settings.DisplayMode == BatteryDisplayMode.Advanced &&
			_settings.AdvancedDisplayMode == AdvancedDisplayMode.BatteryIndicator)
		{
			DrawChargingBoltInsideBattery(graphics);
		}
		else if (_settings.DisplayMode == BatteryDisplayMode.Advanced &&
			_settings.AdvancedDisplayMode == AdvancedDisplayMode.PercentageText)
		{
			DrawChargingBoltAfterPercentage(graphics);
		}
		else if (_settings.DisplayMode == BatteryDisplayMode.Advanced &&
			_settings.AdvancedDisplayMode == AdvancedDisplayMode.BatteryGradient)
		{
			DrawChargingBoltCentered(graphics);
		}
		else
		{
			DrawChargingBoltOnRight(graphics);
		}

		return BitmapToIcon(bitmap);
	}

	private void DrawChargingBoltCentered(Graphics graphics)
	{
		using var path = new GraphicsPath();

		path.AddPolygon(new[]
		{
			new PointF(10.0f, 1.5f),
			new PointF(5.4f, 7.3f),
			new PointF(7.8f, 7.3f),
			new PointF(6.1f, 14.5f),
			new PointF(11.1f, 6.2f),
			new PointF(8.7f, 6.2f)
		});

		using var outlineBrush = new SolidBrush(
			Color.FromArgb(235, 0, 0, 0));

		graphics.FillPath(outlineBrush, path);

		using var innerPath = new GraphicsPath();

		innerPath.AddPolygon(new[]
		{
			new PointF(9.4f, 2.8f),
			new PointF(6.8f, 6.6f),
			new PointF(8.5f, 6.6f),
			new PointF(7.3f, 11.8f),
			new PointF(10.0f, 6.8f),
			new PointF(8.4f, 6.8f)
		});

		using var chargingBrush = new SolidBrush(Color.LimeGreen);

		graphics.FillPath(chargingBrush, innerPath);
	}

	private void DrawChargingBoltOnRight(Graphics graphics)
	{
		using var path = new GraphicsPath();

		path.AddPolygon(new[]
		{
			new PointF(15.5f, 0.5f),
			new PointF(8.8f, 8.0f),
			new PointF(11.9f, 8.0f),
			new PointF(9.5f, 15.5f),
			new PointF(17.0f, 6.0f),
			new PointF(13.6f, 6.0f)
		});

		using var outlineBrush = new SolidBrush(
			Color.FromArgb(235, 0, 0, 0));

		graphics.FillPath(outlineBrush, path);

		using var innerPath = new GraphicsPath();

		innerPath.AddPolygon(new[]
		{
			new PointF(14.8f, 2.2f),
			new PointF(10.5f, 7.2f),
			new PointF(13.0f, 7.2f),
			new PointF(11.2f, 12.8f),
			new PointF(15.5f, 6.8f),
			new PointF(13.2f, 6.8f)
		});

		using var chargingBrush = new SolidBrush(Color.LimeGreen);

		graphics.FillPath(chargingBrush, innerPath);
	}

	private void DrawChargingBoltInsideBattery(Graphics graphics)
	{
		using var path = new GraphicsPath();

		path.AddPolygon(new[]
		{
			new PointF(15.5f, 2.0f),
			new PointF(10.5f, 8.0f),
			new PointF(12.9f, 8.0f),
			new PointF(11.0f, 14.5f),
			new PointF(16.8f, 6.0f),
			new PointF(13.9f, 6.0f)
		});

		using var outlineBrush = new SolidBrush(
			Color.FromArgb(235, 0, 0, 0));

		graphics.FillPath(outlineBrush, path);

		using var innerPath = new GraphicsPath();

		innerPath.AddPolygon(new[]
		{
			new PointF(14.8f, 3.0f),
			new PointF(11.5f, 7.5f),
			new PointF(13.5f, 7.5f),
			new PointF(12.2f, 12.0f),
			new PointF(15.3f, 6.8f),
			new PointF(13.5f, 6.8f)
		});

		using var chargingBrush = new SolidBrush(Color.LimeGreen);

		graphics.FillPath(chargingBrush, innerPath);
	}

	private void DrawChargingBoltAfterPercentage(Graphics graphics)
	{
		using var path = new GraphicsPath();

		path.AddPolygon(new[]
		{
			new PointF(15.8f, 2.5f),
			new PointF(12.8f, 7.0f),
			new PointF(14.3f, 7.0f),
			new PointF(13.0f, 12.5f),
			new PointF(16.5f, 7.0f),
			new PointF(14.9f, 7.0f)
		});

		using var outlineBrush = new SolidBrush(
			Color.FromArgb(235, 0, 0, 0));

		graphics.FillPath(outlineBrush, path);

		using var innerPath = new GraphicsPath();

		innerPath.AddPolygon(new[]
		{
			new PointF(15.3f, 3.5f),
			new PointF(13.5f, 6.5f),
			new PointF(14.8f, 6.5f),
			new PointF(13.9f, 10.3f),
			new PointF(15.7f, 6.7f),
			new PointF(14.5f, 6.7f)
		});

		using var chargingBrush = new SolidBrush(Color.LimeGreen);

		graphics.FillPath(chargingBrush, innerPath);
	}

    private Icon CreateDisconnectedIcon() =>
        LoadIconFile($"{GetThemePrefix()}{DisconnectedIconFileSuffix}");

    private Icon CreateThemeIcon() =>
        LoadIconFile($"{GetThemePrefix()}.ico");

    private Icon CreateRenderedIcon(
        Color batteryColor,
        int? battery,
        bool colorizeHeadset)
    {
        // Battery-gradient mode preserves the exact 16x16 scale of the
        // static tray icon and changes only its color.
        if (colorizeHeadset && !battery.HasValue)
        {
            using Bitmap bitmap =
                new Bitmap(
                    16,
                    16,
                    PixelFormat.Format32bppArgb);

            using Graphics graphics =
                Graphics.FromImage(bitmap);

            graphics.SmoothingMode =
                SmoothingMode.AntiAlias;

            graphics.InterpolationMode =
                InterpolationMode.HighQualityBicubic;

            graphics.PixelOffsetMode =
                PixelOffsetMode.HighQuality;

            graphics.Clear(Color.Transparent);

            using Icon source = CreateThemeIcon();

            using Bitmap iconBitmap =
                RenderIcon(
                    source,
                    16,
                    16,
                    batteryColor);

            graphics.DrawImage(
                iconBitmap,
                0,
                0,
                16,
                16);

            return BitmapToIcon(bitmap);
        }

        using Bitmap compositeBitmap =
            new Bitmap(
                16,
                16,
                PixelFormat.Format32bppArgb);

        using Graphics compositeGraphics =
            Graphics.FromImage(compositeBitmap);

        compositeGraphics.SmoothingMode =
            SmoothingMode.AntiAlias;

        compositeGraphics.InterpolationMode =
            InterpolationMode.HighQualityBicubic;

        compositeGraphics.PixelOffsetMode =
            PixelOffsetMode.HighQuality;

        compositeGraphics.Clear(Color.Transparent);

        using Icon compositeSource = CreateThemeIcon();

        // This layout is exclusively for the advanced battery-indicator
        // mode: the headset is on the left and the battery is beside it.
        const int headsetWidth = 11;
        const int headsetHeight = 16;

        using Bitmap headsetBitmap =
            RenderIcon(
                compositeSource,
                headsetWidth,
                headsetHeight,
                null);

        compositeGraphics.DrawImage(
            headsetBitmap,
            0,
            0,
            headsetWidth,
            headsetHeight);

        if (battery.HasValue)
        {
            DrawBattery(
                compositeGraphics,
                9,
                1,
                7,
                14,
                battery.Value,
                batteryColor);
        }

        return BitmapToIcon(compositeBitmap);
    }

    private Icon CreatePercentageOverlayIcon(
        int battery,
        bool connected)
    {
        using Bitmap bitmap =
            new Bitmap(
                16,
                16,
                PixelFormat.Format32bppArgb);

        using Graphics graphics =
            Graphics.FromImage(bitmap);

        graphics.SmoothingMode =
            SmoothingMode.AntiAlias;

        graphics.InterpolationMode =
            InterpolationMode.HighQualityBicubic;

        graphics.PixelOffsetMode =
            PixelOffsetMode.HighQuality;

        graphics.Clear(Color.Transparent);

        using Icon source = CreateThemeIcon();

        using Bitmap iconBitmap =
            RenderIcon(
                source,
                18,
                18,
                replacementColor: null);

        graphics.DrawImage(
            iconBitmap,
            -1,
            -1,
            18,
            18);

        Color textColor =
            connected
                ? GetBatteryColor(battery)
                : Color.Red;

        using var font =
            new Font(
                "Segoe UI",
                5.5f,
                FontStyle.Bold,
                GraphicsUnit.Point);

        string text = $"{battery}%";

        SizeF size =
            graphics.MeasureString(
                text,
                font);

        // Center the percentage over the headset and place a compact,
        // high-contrast background behind it so it remains readable.
        float backgroundWidth =
            Math.Min(15f, size.Width + 4f);

        const float backgroundHeight = 8f;

        float backgroundX =
            (16f - backgroundWidth) / 2f;

        float backgroundY =
            (16f - backgroundHeight) / 2f;

        Color backgroundColor =
            GetEffectiveTheme() == AppTheme.Dark
                ? Color.FromArgb(205, 20, 20, 22)
                : Color.FromArgb(215, 245, 245, 245);

        using var backgroundBrush =
            new SolidBrush(backgroundColor);

        using var backgroundPath =
            new GraphicsPath();

        const float radius = 2f;
        float diameter = radius * 2f;

        backgroundPath.AddArc(
            backgroundX,
            backgroundY,
            diameter,
            diameter,
            180,
            90);

        backgroundPath.AddArc(
            backgroundX + backgroundWidth - diameter,
            backgroundY,
            diameter,
            diameter,
            270,
            90);

        backgroundPath.AddArc(
            backgroundX + backgroundWidth - diameter,
            backgroundY + backgroundHeight - diameter,
            diameter,
            diameter,
            0,
            90);

        backgroundPath.AddArc(
            backgroundX,
            backgroundY + backgroundHeight - diameter,
            diameter,
            diameter,
            90,
            90);

        backgroundPath.CloseFigure();
        graphics.FillPath(backgroundBrush, backgroundPath);

        float x =
            backgroundX +
            (backgroundWidth - size.Width) / 2f;

        float y =
            backgroundY +
            (backgroundHeight - size.Height) / 2f -
            0.8f;

        using var outlineBrush =
            new SolidBrush(
                GetEffectiveTheme() == AppTheme.Dark
                    ? Color.Black
                    : Color.White);

        // Stronger outline around the percentage.
        graphics.DrawString(text, font, outlineBrush, x - 0.7f, y);
        graphics.DrawString(text, font, outlineBrush, x + 0.7f, y);
        graphics.DrawString(text, font, outlineBrush, x, y - 0.7f);
        graphics.DrawString(text, font, outlineBrush, x, y + 0.7f);

        using var brush =
            new SolidBrush(textColor);

        graphics.DrawString(
            text,
            font,
            brush,
            x,
            y);

        return BitmapToIcon(bitmap);
    }

    private static Bitmap RenderIcon(Icon source, int width, int height, Color? replacementColor)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.DrawIcon(source, new Rectangle(0, 0, width, height));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!replacementColor.HasValue)
                    continue;

                Color pixel = bitmap.GetPixel(x, y);

                if (pixel.A > 0)
                {
                    Color replacement = replacementColor.Value;

                    bitmap.SetPixel(
                        x,
                        y,
                        Color.FromArgb(
                            pixel.A,
                            replacement.R,
                            replacement.G,
                            replacement.B));
                }
            }
        }
        return bitmap;
    }

    private void DrawBattery(
        Graphics graphics,
        int x,
        int y,
        int width,
        int height,
        int percentage,
        Color fillColor)
    {
        Color outlineColor =
            GetEffectiveTheme() == AppTheme.Dark
                ? Color.FromArgb(248, 248, 248)
                : Color.FromArgb(70, 70, 70);

        // Strong, compact outline comparable to the reference tray glyph.
        const float outlineWidth = 1.55f;

        using var pen =
            new Pen(
                outlineColor,
                outlineWidth);

        pen.Alignment = PenAlignment.Center;

        DrawRoundedRectangle(
            graphics,
            pen,
            x,
            y + 2,
            width,
            height - 2,
            1.2f);

        using var terminal =
            new SolidBrush(outlineColor);

        FillRoundedRectangle(
            graphics,
            terminal,
            x + 1,
            y,
            width - 2,
            2.5f,
            0.8f);

        // Opaque dark interior for the unfilled portion of the battery.
        // This is deliberately not transparent: the tray background must
        // never show through the empty charge area.
        Color emptyColor =
            GetEffectiveTheme() == AppTheme.Dark
                ? Color.FromArgb(38, 38, 40)
                : Color.FromArgb(55, 55, 58);

        FillRoundedRectangle(
            graphics,
            new SolidBrush(emptyColor),
            x + 1.2f,
            y + 3.2f,
            width - 2.4f,
            height - 4.2f,
            0.7f);

        int innerHeight =
            Math.Max(
                0,
                (height - 6) *
                Math.Clamp(percentage, 0, 100) /
                100);

        if (innerHeight > 0)
        {
            using var fill =
                new SolidBrush(fillColor);

            graphics.FillRectangle(
                fill,
                x + 1.2f,
                y + height - 1.1f - innerHeight,
                width - 2.4f,
                innerHeight);
        }
    }

    private static void DrawRoundedRectangle(
        Graphics graphics,
        Pen pen,
        float x,
        float y,
        float width,
        float height,
        float radius)
    {
        using var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        graphics.DrawPath(pen, path);
    }

    private static void FillRoundedRectangle(
        Graphics graphics,
        Brush brush,
        float x,
        float y,
        float width,
        float height,
        float radius)
    {
        using var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    private static Icon BitmapToIcon(Bitmap bitmap)
    {
        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIconNative(hIcon);
        }
    }

    [DllImport("user32.dll", EntryPoint = "DestroyIcon", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIconNative(IntPtr hIcon);

    private Color GetBatteryColor(int battery)
    {
        battery = Math.Clamp(battery, 0, 100);

        List<BatteryColorSettings> colors = _settings.BatteryColors
            .Where(c => c != null)
            .OrderByDescending(c => c.MinimumPercent)
            .ToList();

        if (colors.Count == 0)
            return Color.LimeGreen;

        // The first configured range whose threshold is at or below the
        // current battery level is the active color.
        int activeIndex = -1;
        for (int i = 0; i < colors.Count; i++)
        {
            if (battery >= colors[i].MinimumPercent)
            {
                activeIndex = i;
                break;
            }
        }

        if (activeIndex < 0)
            activeIndex = colors.Count - 1;

        Color activeColor = colors[activeIndex].Color;

        if (!_settings.UseGradient ||
            _settings.GradientPercent <= 0 ||
            activeIndex >= colors.Count - 1)
        {
            return activeColor;
        }

        BatteryColorSettings lower = colors[activeIndex + 1];
        int transition = Math.Clamp(_settings.GradientPercent, 0, 50);
        int start = colors[activeIndex].MinimumPercent;
        int end = Math.Max(lower.MinimumPercent, start - transition);

        if (battery >= end && battery < start)
        {
            double t = (start - battery) / (double)Math.Max(1, start - end);
            return Blend(activeColor, lower.Color, t);
        }

        return activeColor;
    }

    private static Color Blend(Color first, Color second, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(first.R + (second.R - first.R) * t),
            (int)Math.Round(first.G + (second.G - first.G) * t),
            (int)Math.Round(first.B + (second.B - first.B) * t));
    }
    private async Task<bool> InstallDownloadedUpdateAsync()
    {
        if (!ApplicationRuntime.CanInstallUpdates)
            return false;

        VerifiedInstaller? installer = await _updateCoordinator.PrepareInstallationAsync();
        if (installer is null || !await UpdateInstallerLauncher.TryStartAsync(installer))
            return false;

        // The launcher verifies the installer again after this process has exited.
        // ExitThreadCore disposes the updater, forms, tray icons, timers and HID.
        _updateCoordinator.ClearInstallationFailure();
        ExitThread();
        return true;
    }

    private void ExitApplication(object? sender, EventArgs e) => ExitThread();

    protected override void ExitThreadCore()
    {
        UpdateNotificationService.OpenAboutRequested -= OnUpdateToastActivated;
        _toastActivationDispatcher.Dispose();
        _updateCoordinator.Dispose();
        _settingsForm?.Close();
        _blinkTimer?.Stop();
        _blinkTimer?.Dispose();
        _blinkTimer = null;
        _leftClickTimer?.Stop();
        _leftClickTimer?.Dispose();
        _leftClickTimer = null;

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
        _trayMenu?.Close();
        _trayMenu?.Dispose();
        _trayMenu = null;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentApplicationIcon?.Dispose();
        _currentApplicationIcon = null;

        DisposeDeviceMonitor();
        _notificationService.ResetState();
        _deviceManager.Dispose();
        base.ExitThreadCore();
    }
}

