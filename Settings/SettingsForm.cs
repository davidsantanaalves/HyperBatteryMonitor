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
using HyperXBatteryTray.Updates;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly UpdateCoordinator? _updateCoordinator;
    private readonly Func<Task<bool>>? _installDownloadedUpdateAsync;
    private UpdateChannel _pendingUpdateChannel;
    private bool _pendingUpdateChannelUserSelected;
    private UpdateDownloadMode _pendingUpdateDownloadMode;
    private readonly StartupManager _startupManager;
    private readonly PngIconCache _iconCache;
    private DeviceSelector _deviceSelector = null!;
    private ActionButton? _deviceSettingsButton;
    private RoundedLanguageSelector _languageComboBox = null!;
    private ToggleSwitchControl _startupToggle = null!;
    private ThemeOptionControl _lightThemeOption = null!;
    private ThemeOptionControl _darkThemeOption = null!;
    private ThemeOptionControl _systemThemeOption = null!;
    private Label _deviceStatusLabel = null!;
    private Label _deviceStatusDescriptionLabel = null!;
    private Label _batteryValueLabel = null!;
    private Label _chargingLabel = null!;
    private Label _wirelessTechnologyValueLabel = null!;
    private Label _connectionMethodValueLabel = null!;
    private Label _wirelessRangeValueLabel = null!;
    private Label _batteryLifeValueLabel = null!;
    private Label _chargeTimeValueLabel = null!;
    private StatusDotControl _deviceStatusDot = null!;
    private BatteryIconControl _batteryIcon = null!;
    private readonly Panel _pageHost;
    private TableLayoutPanel? _activePageLayout;
    private ThemedVerticalScrollBar? _pageHostScrollBar;
    private ThemedVerticalScrollBar? _activePageScrollBar;
    private readonly Panel _sidebar;
    private Panel _footer = null!;
    private Button _resetButton = null!;
    private Button _okButton = null!;
    private Button _cancelButton = null!;
    private Button _applyButton = null!;
    private Label _versionLabel = null!;
    private Label _applicationNameLabel = null!;
    private RoundedPanel _sidebarDeviceCard = null!;
    private PictureBox _sidebarDeviceImage = null!;
    private Label _sidebarDeviceNameLabel = null!;
    private Label _sidebarStatusTitleLabel = null!;
    private Label _sidebarStatusLabel = null!;
    private StatusDotControl _sidebarStatusDot = null!;
    private Label _sidebarBatteryTitleLabel = null!;
    private Label _sidebarBatteryLabel = null!;
    private BatteryIconControl _sidebarBatteryIcon = null!;
    private Label _sidebarMicrophoneTitleLabel = null!;
    private PictureBox _sidebarMicrophoneIcon = null!;
    private Label _sidebarMicrophoneLabel = null!;
    private Label _sidebarAutoPowerOffTitleLabel = null!;
    private Label _sidebarAutoPowerOffLabel = null!;
    private Label _sidebarMonitoringTitleLabel = null!;
    private Label _sidebarMonitoringLabel = null!;
    private Panel _sidebarSummaryDivider = null!;
    private PngIconControl _sidebarBatterySummaryGlyph = null!;
    private PngIconControl _sidebarMicrophoneSummaryGlyph = null!;
    private PngIconControl _sidebarPowerOffSummaryGlyph = null!;
    private PngIconControl _sidebarMonitoringSummaryGlyph = null!;
    private string? _sidebarImageFileName;
    private bool? _sidebarMicrophoneIconDark;
    private bool? _sidebarMicrophoneIconUsesMuteAsset;
    private readonly Dictionary<string, SidebarItem> _navButtons = new();
    private IHyperXDevice? _device;
    private AppLanguage _selectedLanguage;
    private AppTheme _selectedTheme;
    private string _pendingSelectedDevice;
    private Dictionary<string, DeviceMonitoringMode> _pendingDeviceMonitoringModes;
    private bool _pendingStartupEnabled;
    private bool _pendingNotifyOnLowBattery;
    private bool _pendingNotifyWhenFullyCharged;
    private bool _pendingBlinkOnCriticalBattery;
    private bool _pendingShowMicrophoneMuteInSystray;
    private int _pendingCriticalBatteryPercent;
    private BatteryDisplayMode _pendingDisplayMode;
    private AdvancedDisplayMode _pendingAdvancedDisplayMode;
    private List<BatteryColorSettings> _pendingBatteryColors;
    private bool _pendingUseGradient;
    private int _pendingGradientPercent;
    private readonly List<(RoundedPanel Card, BatteryModeCard Radio)> _batteryModeCards = new();
    private ToggleSwitchControl _notifyOnLowBatteryToggle = null!;
    private ToggleSwitchControl _notifyWhenFullyChargedToggle = null!;
    private ToggleSwitchControl _blinkOnCriticalBatteryToggle = null!;
    private ToggleSwitchControl _showMicrophoneMuteInSystrayToggle = null!;
    private CriticalBatteryNumericControl _criticalBatteryPercentInput = null!;
    private bool _updatingLanguage;
    private bool _isCharging;
    private TimeSpan? _batteryRemainingTime;
    private TimeSpan? _runtimeBatteryRemainingTime;
    private string _currentPage = "Device";

    private static readonly Color Accent = Color.FromArgb(0, 122, 255);
    private static readonly Color LightBackground = Color.FromArgb(250, 251, 253);
    private static readonly Color DarkBackground = Color.FromArgb(31, 34, 37);
    private static readonly Color LightSidebar = Color.FromArgb(247, 249, 252);
    private static readonly Color DarkSidebar = Color.FromArgb(36, 39, 42);
    private static readonly Color LightBorder = Color.FromArgb(222, 227, 234);
    private static readonly Color DarkBorder = Color.FromArgb(66, 71, 76);
    private static readonly Color LightText = Color.FromArgb(24, 32, 45);
    private static readonly Color LightSecondary = Color.FromArgb(82, 95, 115);
    private static readonly Color DarkSecondary = Color.FromArgb(196, 201, 207);

    private const int LogicalDpi = 96;
    private const int SidebarFirstSeparatorLogicalY = 252;
    private const int SidebarSecondSeparatorLogicalY = 458;
    private const int SidebarDeviceCardLogicalHeight = 190;
    private const int SidebarDeviceCardLogicalTop =
        SidebarFirstSeparatorLogicalY +
        (SidebarSecondSeparatorLogicalY - SidebarFirstSeparatorLogicalY - SidebarDeviceCardLogicalHeight) / 2;
    private const int SidebarTitleLogicalLeft = 8;
    private const int SidebarTitleLogicalWidth = 50;
    private const int SidebarBatteryTitleLogicalWidth = 46;
    private const int SidebarIconColumnLogicalLeft = 50;
    private const int SidebarIconColumnLogicalWidth = 32;
    private const int SidebarValueLogicalLeft = 83;
    private const int SidebarStatusRowLogicalTop = 97;
    private const int SidebarStatusDotLogicalTop = 97;
    private const int SidebarStatusDotLogicalSize = 18;
    private const int SidebarStatusTextVerticalOffsetLogical = 1;
    private const int SidebarBatteryRowLogicalTop = 128;
    private const int SidebarBatteryIconLogicalTop = 122;
    private const int SidebarBatteryIconLogicalSize = 32;
    private const int SidebarMicrophoneRowLogicalTop = 160;
    private const int SidebarMicrophoneIconLogicalTop = 157;
    private const int SidebarMicrophoneIconLogicalSize = 25;
    private const int SidebarContentRightInsetLogical = 0;
    private const string MicrophoneOpenIconAssetName = "mic";
    private const string MicrophoneMutedIconAssetName = "mute";
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const string DarkScrollableThemeClass = "DarkMode_Explorer";
    private const string LightScrollableThemeClass = "Explorer";

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme(EffectiveTheme == AppTheme.Dark);
        ApplyCurrentScrollThemes(EffectiveTheme == AppTheme.Dark);
        PositionFooter();
        _iconCache.ClearBitmaps();
        WarmUpIcons();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _iconCache.ClearBitmaps();
        WarmUpIcons();
        _sidebar.Invalidate();

        if (IsHandleCreated && !IsDisposed && !Disposing)
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                if (!IsDisposed && !Disposing)
                {
                    LimitWindowToWorkingArea();
                    ShowPage(_currentPage);
                }
            }));
        }
    }

    private void ApplyTitleBarTheme(bool dark)
    {
        if (!IsHandleCreated)
            return;

        try
        {
            int useDarkMode = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
        }
        catch
        {
            // Keep the application functional on Windows versions without this DWM attribute.
        }
    }

    private void ApplyCurrentScrollThemes(bool dark)
    {
        if (!IsHandleCreated)
            return;

        ApplyNativeScrollTheme(_pageHost, dark);
        if (_activePageLayout != null)
            ApplyNativeScrollTheme(_activePageLayout, dark);
        _pageHostScrollBar?.SetTheme(dark);
        _activePageScrollBar?.SetTheme(dark);
        _pageHostScrollBar?.RefreshState();
        _activePageScrollBar?.RefreshState();
    }

    private static void ApplyNativeScrollTheme(ScrollableControl control, bool dark)
    {
        if (control.IsDisposed || control.Disposing)
            return;

        try
        {
            if (!control.IsHandleCreated)
                control.CreateControl();

            if (!control.IsHandleCreated)
                return;

            _ = SetWindowTheme(
                control.Handle,
                dark ? DarkScrollableThemeClass : LightScrollableThemeClass,
                null);
            control.Invalidate();
        }
        catch (DllNotFoundException)
        {
            // uxtheme.dll is part of supported Windows versions; keep default styling if unavailable.
        }
        catch (EntryPointNotFoundException)
        {
            // Keep the default scrollbar styling if the API is unavailable.
        }
    }

    public event EventHandler? SettingsApplied;
    // Raised only after an explicit, themed Exit confirmation; no pending settings are applied.
    public event EventHandler? ExitRequested;
    // A device-specific monitoring change is persisted by its own modal.
    public event EventHandler<string>? DeviceMonitoringModeSaved;
    public event EventHandler? DeviceAutoPowerOffSnapshotChanged;

    public SettingsForm(
        AppSettings settings,
        IHyperXDevice? device = null,
        bool isCharging = false,
        TimeSpan? batteryRemainingTime = null)
        : this(settings, device, isCharging, batteryRemainingTime, null, null)
    {
    }

    internal SettingsForm(
        AppSettings settings,
        IHyperXDevice? device,
        bool isCharging,
        TimeSpan? batteryRemainingTime,
        UpdateCoordinator? updateCoordinator,
        Func<Task<bool>>? installDownloadedUpdateAsync)
    {
        _settings = settings;
        _updateCoordinator = updateCoordinator;
        _installDownloadedUpdateAsync = installDownloadedUpdateAsync;
        _pendingUpdateChannel = settings.UpdateChannel;
        _pendingUpdateChannelUserSelected = settings.UpdateChannelUserSelected;
        _pendingUpdateDownloadMode = settings.UpdateDownloadMode;
        if (_updateCoordinator is not null)
            _updateCoordinator.Changed += OnUpdateStatusChanged;
        _runtimeDevice = device;
        _device = device;
        _runtimeIsCharging = isCharging;
        _isCharging = isCharging;
        _runtimeBatteryRemainingTime = batteryRemainingTime;
        _batteryRemainingTime = batteryRemainingTime;
        _startupManager = new StartupManager();
        _iconCache = new PngIconCache();
        _selectedLanguage = settings.Language;
        _selectedTheme = settings.Theme;
        _pendingSelectedDevice = HyperXDeviceManager.GetSelectionModelName(settings.SelectedDevice);
        _pendingDeviceMonitoringModes = new Dictionary<string, DeviceMonitoringMode>(
            settings.DeviceMonitoringModes ?? new Dictionary<string, DeviceMonitoringMode>(),
            StringComparer.OrdinalIgnoreCase);
        _pendingStartupEnabled = ApplicationRuntime.IsTestMode ? false :
            settings.IsNewSettingsProfile
                ? AppSettings.DefaultStartWithWindows
                : _startupManager.IsEnabled();
        _pendingNotifyOnLowBattery = settings.NotifyOnLowBattery;
        _pendingNotifyWhenFullyCharged = settings.NotifyWhenFullyCharged;
        _pendingBlinkOnCriticalBattery = settings.BlinkOnCriticalBattery;
        _pendingShowMicrophoneMuteInSystray = settings.ShowMicrophoneMuteInSystray;
        _pendingCriticalBatteryPercent = Math.Clamp(settings.CriticalBatteryPercent, 1, 100);
        _pendingDisplayMode = settings.DisplayMode;
        _pendingAdvancedDisplayMode = settings.AdvancedDisplayMode;
        _pendingBatteryColors = CloneBatteryColors(settings.BatteryColors);
        _pendingUseGradient = settings.UseGradient;
        _pendingGradientPercent = Math.Clamp(settings.GradientPercent, 0, 50);

        Text = GetWindowTitle();
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        // Keep the original Settings window dimensions; the Device page scrolls when needed.
        ClientSize = new Size(760, 520);
        // Standard Windows z-order: owned dialogs remain above this window
        // without preventing other applications from taking the foreground.
        DoubleBuffered = true;
        BackColor = LightBackground;
        Font = new Font("Segoe UI", 9.5f);
        Icon = LoadApplicationIcon();

        _sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 190,
            BackColor = LightSidebar,
            Padding = new Padding(10, 14, 10, 12)
        };
        _sidebar.Paint += Sidebar_Paint;

        _pageHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 22, 20, 66),
            BackColor = LightBackground,
            AutoScroll = true
        };

        Controls.Add(_pageHost);
        Controls.Add(_sidebar);
        _pageHostScrollBar = new ThemedVerticalScrollBar(_pageHost, EffectiveTheme == AppTheme.Dark);

        _sidebarDeviceCard = new RoundedPanel
        {
            Location = new Point(10, SidebarDeviceCardLogicalTop),
            Size = new Size(_sidebar.ClientSize.Width - 20, SidebarDeviceCardLogicalHeight),
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            BorderColor = DarkBorder,
            OutsideBackColor = LightSidebar,
            BackColor = Color.FromArgb(248, 249, 251)
        };

        _sidebarDeviceImage = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(64, 64),
            Location = new Point((_sidebarDeviceCard.Width - 64) / 2, 6),
            BackColor = Color.Transparent
        };

        _sidebarDeviceNameLabel = new Label
        {
            AutoSize = false,
            Size = new Size(_sidebarDeviceCard.Width - 12, 20),
            Location = new Point(6, 69),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 8.5f),
            BackColor = Color.Transparent
        };

        _sidebarStatusTitleLabel = new Label
        {
            AutoSize = false,
            Size = new Size(SidebarTitleLogicalWidth, SidebarStatusDotLogicalSize),
            Location = new Point(SidebarTitleLogicalLeft, SidebarStatusRowLogicalTop),
            Text = L("SidebarStatus"),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f),
            BackColor = Color.Transparent
        };

        _sidebarStatusDot = new StatusDotControl
        {
            Size = new Size(SidebarStatusDotLogicalSize, SidebarStatusDotLogicalSize),
            Location = new Point(
                SidebarIconColumnLogicalLeft + (SidebarIconColumnLogicalWidth - SidebarStatusDotLogicalSize) / 2,
                SidebarStatusDotLogicalTop)
        };

        _sidebarStatusLabel = new Label
        {
            AutoSize = false,
            Size = new Size(
                _sidebarDeviceCard.Width - SidebarValueLogicalLeft - SidebarContentRightInsetLogical,
                SidebarStatusDotLogicalSize),
            Location = new Point(
                SidebarValueLogicalLeft,
                SidebarStatusRowLogicalTop + SidebarStatusTextVerticalOffsetLogical),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f),
            BackColor = Color.Transparent
        };

        _sidebarBatteryTitleLabel = new Label
        {
            AutoSize = false,
            Size = new Size(SidebarBatteryTitleLogicalWidth, 20),
            Location = new Point(SidebarTitleLogicalLeft, SidebarBatteryRowLogicalTop),
            Text = L("SidebarBattery"),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f),
            BackColor = Color.Transparent
        };

        _sidebarBatteryIcon = new BatteryIconControl
        {
            Size = new Size(SidebarBatteryIconLogicalSize, SidebarBatteryIconLogicalSize),
            Location = new Point(SidebarIconColumnLogicalLeft, SidebarBatteryIconLogicalTop),
            DarkMode = false
        };

        _sidebarBatteryLabel = new Label
        {
            AutoSize = false,
            Size = new Size(_sidebarDeviceCard.Width - SidebarValueLogicalLeft - SidebarContentRightInsetLogical, 20),
            Location = new Point(SidebarValueLogicalLeft, SidebarBatteryRowLogicalTop),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f),
            BackColor = Color.Transparent
        };

        _sidebarMicrophoneTitleLabel = new Label
        {
            AutoSize = false,
            Size = new Size(_sidebarStatusTitleLabel.Width, _sidebarStatusTitleLabel.Height),
            Location = new Point(_sidebarStatusTitleLabel.Left, SidebarMicrophoneRowLogicalTop),
            Text = L("SidebarMicrophone"),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f),
            BackColor = Color.Transparent
        };

        _sidebarMicrophoneIcon = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(SidebarMicrophoneIconLogicalSize, SidebarMicrophoneIconLogicalSize),
            Location = new Point(
                SidebarIconColumnLogicalLeft + (SidebarIconColumnLogicalWidth - SidebarMicrophoneIconLogicalSize + 1) / 2,
                SidebarMicrophoneIconLogicalTop),
            BackColor = Color.Transparent
        };

        _sidebarMicrophoneLabel = new Label
        {
            AutoSize = false,
            Size = new Size(
                _sidebarDeviceCard.Width - SidebarValueLogicalLeft - SidebarContentRightInsetLogical,
                _sidebarStatusLabel.Height),
            Location = new Point(
                SidebarValueLogicalLeft,
                SidebarMicrophoneRowLogicalTop),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f),
            BackColor = Color.Transparent
        };

        // The 170 x 190 px device card stays within the existing sidebar.
        // Header: small headset photo, two-line name and connection status.
        // Content: one separator, then only supported status rows.
        _sidebarDeviceImage.Bounds = new Rectangle(8, 7, 48, 48);
        _sidebarDeviceNameLabel.Bounds = new Rectangle(59, 7, 104, 34);
        _sidebarDeviceNameLabel.TextAlign = ContentAlignment.MiddleLeft;
        _sidebarDeviceNameLabel.Font = new Font("Segoe UI Semibold", 8f);
        _sidebarDeviceNameLabel.AutoEllipsis = false;
        _sidebarStatusTitleLabel.Visible = false;
        _sidebarStatusDot.Bounds = new Rectangle(59, 42, 18, 18);
        _sidebarStatusLabel.Bounds = new Rectangle(80, 42, 83, 20);
        _sidebarStatusLabel.Font = new Font("Segoe UI", 8f);
        _sidebarBatteryIcon.Visible = false;
        _sidebarMicrophoneIcon.Visible = false;
        _sidebarBatteryTitleLabel.Font = new Font("Segoe UI", 7.8f);
        _sidebarBatteryTitleLabel.AutoEllipsis = false;
        _sidebarBatteryLabel.Font = new Font("Segoe UI Semibold", 7.5f);
        _sidebarBatteryLabel.TextAlign = ContentAlignment.MiddleRight;
        _sidebarBatteryLabel.AutoEllipsis = false;
        _sidebarMicrophoneTitleLabel.Font = new Font("Segoe UI", 7.8f);
        _sidebarMicrophoneTitleLabel.AutoEllipsis = false;
        _sidebarMicrophoneLabel.Font = new Font("Segoe UI Semibold", 7.5f);
        _sidebarMicrophoneLabel.TextAlign = ContentAlignment.MiddleRight;

        _sidebarSummaryDivider = new Panel
        {
            Location = new Point(9, 69),
            Size = new Size(_sidebarDeviceCard.Width - 18, 1),
            BackColor = DarkBorder
        };
        _sidebarAutoPowerOffTitleLabel = MakeSidebarSummaryLabel(L("SidebarAutoPowerOff"), false);
        _sidebarAutoPowerOffLabel = MakeSidebarSummaryLabel(string.Empty, true);
        _sidebarMonitoringTitleLabel = MakeSidebarSummaryLabel(L("SidebarMonitoringMode"), false);
        _sidebarMonitoringLabel = MakeSidebarSummaryLabel(string.Empty, true);

        // Reuse the application's existing theme-aware icon assets, scaled down
        // consistently for the narrow status summary (no extra image resources).
        _sidebarBatterySummaryGlyph = MakeSidebarSummaryGlyph("battery_monitor");
        _sidebarMicrophoneSummaryGlyph = MakeSidebarSummaryGlyph("mic");
        _sidebarPowerOffSummaryGlyph = MakeSidebarSummaryGlyph("clock");
        _sidebarMonitoringSummaryGlyph = MakeSidebarSummaryGlyph("connection");

        _sidebarDeviceCard.Controls.Add(_sidebarDeviceImage);
        _sidebarDeviceCard.Controls.Add(_sidebarDeviceNameLabel);
        _sidebarDeviceCard.Controls.Add(_sidebarStatusTitleLabel);
        _sidebarDeviceCard.Controls.Add(_sidebarStatusDot);
        _sidebarDeviceCard.Controls.Add(_sidebarStatusLabel);
        _sidebarDeviceCard.Controls.Add(_sidebarBatteryTitleLabel);
        _sidebarDeviceCard.Controls.Add(_sidebarBatteryIcon);
        _sidebarDeviceCard.Controls.Add(_sidebarBatteryLabel);
        _sidebarDeviceCard.Controls.Add(_sidebarMicrophoneTitleLabel);
        _sidebarDeviceCard.Controls.Add(_sidebarMicrophoneIcon);
        _sidebarDeviceCard.Controls.Add(_sidebarMicrophoneLabel);
        _sidebarDeviceCard.Controls.Add(_sidebarSummaryDivider);
        _sidebarDeviceCard.Controls.Add(_sidebarBatterySummaryGlyph);
        _sidebarDeviceCard.Controls.Add(_sidebarMicrophoneSummaryGlyph);
        _sidebarDeviceCard.Controls.Add(_sidebarPowerOffSummaryGlyph);
        _sidebarDeviceCard.Controls.Add(_sidebarMonitoringSummaryGlyph);
        _sidebarDeviceCard.Controls.Add(_sidebarAutoPowerOffTitleLabel);
        _sidebarDeviceCard.Controls.Add(_sidebarAutoPowerOffLabel);
        _sidebarDeviceCard.Controls.Add(_sidebarMonitoringTitleLabel);
        _sidebarDeviceCard.Controls.Add(_sidebarMonitoringLabel);
        _sidebar.Controls.Add(_sidebarDeviceCard);

        _applicationNameLabel = new Label
        {
            AutoSize = false,
            Size = new Size(_sidebar.ClientSize.Width - 20, 20),
            Text = Application.ProductName,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 8.5f),
            Location = new Point(10, ClientSize.Height - 49),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        _versionLabel = new Label
        {
            AutoSize = false,
            Size = new Size(_sidebar.ClientSize.Width - 20, 18),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(10, ClientSize.Height - 27),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        };
        _sidebar.Controls.Add(_applicationNameLabel);
        _sidebar.Controls.Add(_versionLabel);

        BuildSidebar();
        ShowPage("Device");

        _resetButton = CreateFooterButton(L("ResetToDefaults"), false, showResetIcon: true);
        _okButton = CreateFooterButton(L("Ok"), true);
        _cancelButton = CreateFooterButton(L("Cancel"), false);
        _applyButton = CreateFooterButton(L("Apply"), false);
        _resetButton.Click += ResetButton_Click;
        _okButton.Click += OkButton_Click;
        _cancelButton.Click += (_, _) => Close();
        _applyButton.Click += ApplyButton_Click;
        BuildFooter();

        // Configure WinForms DPI autoscaling only after the complete 96-DPI control tree
        // exists. Runtime-created pages use ScaleUi after the handle is available.
        AutoScaleDimensions = new SizeF(LogicalDpi, LogicalDpi);
        AutoScaleMode = AutoScaleMode.Dpi;

        ApplyTheme(_selectedTheme);
        PositionWindowAtTop();
        RefreshDeviceStatus();

        SubscribeToDeviceStatusEvents(_device);
        FormClosed += SettingsForm_FormClosed;
        FormClosing += (_, e) =>
        {
            if (_autoPowerOffApplying) e.Cancel = true;
        };
    }

    private PngIconControl MakeSidebarSummaryGlyph(string iconKey) => new(_iconCache, iconKey)
    {
        Size = new Size(15, 15),
        BackColor = Color.Transparent,
        DarkMode = EffectiveTheme == AppTheme.Dark
    };

    private static Label MakeSidebarSummaryLabel(string text, bool value) => new()
    {
        Text = text,
        AutoSize = false,
        Size = new Size(value ? 85 : 73, 20),
        TextAlign = value ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft,
        Font = new Font(value ? "Segoe UI Semibold" : "Segoe UI", value ? 7.5f : 7.8f),
        // Responsive status rows will wrap instead of clipping with an ellipsis.
        AutoEllipsis = false,
        BackColor = Color.Transparent
    };

    internal void RefreshRuntimeStatusSummary()
    {
        if (IsDisposed || Disposing) return;
        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke((MethodInvoker)RefreshRuntimeStatusSummary);
            return;
        }
        if (_deviceSelector != null)
            RefreshDeviceStatus();
    }

    internal void SetDevice(IHyperXDevice? device)
    {
        StopDevicePreview();

        if (ReferenceEquals(_runtimeDevice, device))
        {
            _device = _runtimeDevice;
            RefreshDeviceStatus();
            return;
        }

        UnsubscribeFromDeviceStatusEvents(_device);

        _runtimeDevice = device;
        _device = device;
        _runtimeIsCharging = false;
        _isCharging = false;
        _runtimeBatteryRemainingTime = null;
        _batteryRemainingTime = null;

        SubscribeToDeviceStatusEvents(_device);
        RefreshDeviceStatus();
    }

    internal void SetBatteryRemainingTime(TimeSpan? batteryRemainingTime)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
                SetBatteryRemainingTime(batteryRemainingTime)));
            return;
        }

        _runtimeBatteryRemainingTime = batteryRemainingTime;

        if (_devicePreviewMonitor != null)
            return;

        _batteryRemainingTime = batteryRemainingTime;
        RefreshDeviceStatus();
    }

    internal void SetCharging(bool charging)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetCharging(charging)));
            return;
        }

        _runtimeIsCharging = charging;

        if (_devicePreviewMonitor != null)
            return;

        _isCharging = charging;
        RefreshDeviceStatus();
    }

}
