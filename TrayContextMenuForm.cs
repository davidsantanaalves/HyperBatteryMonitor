using System.ComponentModel;
using System.Drawing.Drawing2D;
using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Monitoring;
using HyperXBatteryTray.Settings;

namespace HyperXBatteryTray;

internal sealed class TrayContextMenuForm : Form
{
    private const int MenuLogicalWidth = 190;
    private const int MenuLogicalHeight = 245;
    private const int ButtonBarLogicalHeight = 55;
    private const int RowTitleLogicalLeft = 8;
    private const int RowTitleLogicalWidth = 50;
    private const int BatteryTitleLogicalWidth = 46;
    private const int IconColumnLogicalLeft = 50;
    private const int IconColumnLogicalWidth = 30;
    private const int RowValueLogicalLeft = 82;
    private const int StatusRowLogicalTop = 98;
    private const int StatusDotLogicalTop = 98;
    private const int StatusDotLogicalSize = 18;
    private const int StatusTextVerticalOffsetLogical = 1;
    private const int BatteryRowLogicalTop = 127;
    private const int BatteryIconLogicalTop = 121;
    private const int BatteryIconLogicalSize = 30;
    private const int MicrophoneRowLogicalTop = 160;
    private const int MicrophoneIconLogicalTop = 157;
    private const int MicrophoneIconLogicalSize = 25;
    private const int MenuContentRightInsetLogical = 0;
    private const string MicrophoneOpenIconAssetName = "mic";
    private const string MicrophoneMutedIconAssetName = "mute";
    private const int CursorGapLogical = 8;
    private const int WorkAreaMarginLogical = 4;
    private const float CornerRadiusLogical = 10f;
    private const float BorderLogicalWidth = 1f;

    private readonly AppSettings _settings;
    private readonly PngIconCache _iconCache;
    private readonly Action<object?, EventArgs> _settingsAction;
    private readonly Action<object?, EventArgs> _exitAction;
    private readonly PictureBox _deviceImage;
    private readonly Label _deviceName;
    private readonly Label _statusTitle;
    private readonly Label _statusText;
    private readonly Label _batteryTitle;
    private readonly Label _batteryText;
    private readonly TrayStatusDotControl _statusDot;
    private readonly TrayBatteryIconControl _batteryIcon;
    private readonly Label _microphoneTitle;
    private readonly PictureBox _microphoneIcon;
    private readonly Label _microphoneText;
    private readonly Label _autoPowerOffTitle;
    private readonly Label _autoPowerOffText;
    private readonly Label _monitoringTitle;
    private readonly Label _monitoringText;
    private readonly Panel _statusDivider;
    private readonly TraySummaryGlyphControl _batterySummaryGlyph;
    private readonly TraySummaryGlyphControl _microphoneSummaryGlyph;
    private readonly TraySummaryGlyphControl _powerOffSummaryGlyph;
    private readonly TraySummaryGlyphControl _monitoringSummaryGlyph;
    private readonly TrayMenuButton _settingsButton;
    private readonly TrayMenuButton _exitButton;
    private readonly Panel _buttonBar;
    private IHyperXDevice? _device;
    private bool _charging;
    private TimeSpan? _batteryRemainingTime;
    private bool _dark;
    private Bitmap? _deviceBitmap;
    private string? _deviceImageFileName;
    private Bitmap? _microphoneBitmap;
    private bool? _microphoneIconDark;
    private bool? _microphoneIconUsesMuteAsset;
    private ToolTip? _toolTip;
    private bool _closeOnDeactivateEnabled;
    private int _contentLogicalHeight = MenuLogicalHeight;

    public TrayContextMenuForm(
        AppSettings settings,
        IHyperXDevice? device,
        bool charging,
        TimeSpan? batteryRemainingTime,
        AppTheme theme,
        Action<object?, EventArgs> settingsAction,
        Action<object?, EventArgs> exitAction)
    {
        _settings = settings;
        _device = device;
        _charging = charging;
        _batteryRemainingTime = batteryRemainingTime;
        _settingsAction = settingsAction;
        _exitAction = exitAction;
        _iconCache = new PngIconCache();
        _dark = theme == AppTheme.Dark;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(MenuLogicalWidth, MenuLogicalHeight);
        // ShowAt() activates this menu on demand; when focus is lost it hides.
        // It does not need to stay above unrelated applications.
        DoubleBuffered = true;
        Padding = new Padding(0);
        BackColor = Color.FromArgb(45, 48, 51);
        Deactivate += (_, _) =>
        {
            if (_closeOnDeactivateEnabled && !IsDisposed && !Disposing)
                Hide();
        };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
        SetRoundedRegion();

        _deviceImage = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(58, 58),
            Location = new Point(55, 7),
            BackColor = Color.Transparent
        };

        _deviceName = CreateLabel(8.5f, FontStyle.Bold, ContentAlignment.MiddleCenter, new Rectangle(8, 69, 152, 21));
        _statusTitle = CreateLabel(8.5f, FontStyle.Regular, ContentAlignment.MiddleLeft, new Rectangle(RowTitleLogicalLeft, StatusRowLogicalTop, RowTitleLogicalWidth, StatusDotLogicalSize));
        _statusDot = new TrayStatusDotControl
        {
            Size = new Size(StatusDotLogicalSize, StatusDotLogicalSize),
            Location = new Point(
                IconColumnLogicalLeft + (IconColumnLogicalWidth - StatusDotLogicalSize) / 2,
                StatusDotLogicalTop)
        };
        _statusText = CreateLabel(
            8.5f,
            FontStyle.Regular,
            ContentAlignment.MiddleLeft,
            new Rectangle(
                RowValueLogicalLeft,
                StatusRowLogicalTop + StatusTextVerticalOffsetLogical,
                MenuLogicalWidth - RowValueLogicalLeft - MenuContentRightInsetLogical,
                StatusDotLogicalSize));
        _batteryTitle = CreateLabel(8.5f, FontStyle.Regular, ContentAlignment.MiddleLeft, new Rectangle(RowTitleLogicalLeft, BatteryRowLogicalTop, BatteryTitleLogicalWidth, 20));
        _batteryIcon = new TrayBatteryIconControl
        {
            Size = new Size(BatteryIconLogicalSize, BatteryIconLogicalSize),
            Location = new Point(IconColumnLogicalLeft, BatteryIconLogicalTop)
        };
        _batteryText = CreateLabel(8.5f, FontStyle.Regular, ContentAlignment.MiddleLeft, new Rectangle(RowValueLogicalLeft, BatteryRowLogicalTop, MenuLogicalWidth - RowValueLogicalLeft - MenuContentRightInsetLogical, 20));
        _microphoneTitle = CreateLabel(
            8.5f,
            FontStyle.Regular,
            ContentAlignment.MiddleLeft,
            new Rectangle(
                RowTitleLogicalLeft,
                MicrophoneRowLogicalTop,
                RowTitleLogicalWidth,
                _batteryTitle.Height));
        _microphoneIcon = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(MicrophoneIconLogicalSize, MicrophoneIconLogicalSize),
            Location = new Point(
                IconColumnLogicalLeft + (IconColumnLogicalWidth - MicrophoneIconLogicalSize + 1) / 2,
                MicrophoneIconLogicalTop),
            BackColor = Color.Transparent
        };
        _microphoneText = CreateLabel(
            8.5f,
            FontStyle.Regular,
            ContentAlignment.MiddleLeft,
            new Rectangle(
                RowValueLogicalLeft,
                MicrophoneRowLogicalTop,
                MenuLogicalWidth - RowValueLogicalLeft - MenuContentRightInsetLogical,
                _batteryText.Height));

        // Match the narrow Settings sidebar card: image/name/status header,
        // ONE divider, and a compact set of supported status rows.
        _deviceImage.Bounds = new Rectangle(8, 7, 48, 48);
        _deviceName.Bounds = new Rectangle(59, 7, MenuLogicalWidth - 66, 34);
        _deviceName.TextAlign = ContentAlignment.MiddleLeft;
        _deviceName.AutoEllipsis = false;
        _statusTitle.Visible = false;
        _statusDot.Bounds = new Rectangle(59, 42, 18, 18);
        _statusText.Bounds = new Rectangle(80, 42, MenuLogicalWidth - 87, 20);
        _batteryIcon.Visible = false;
        _microphoneIcon.Visible = false;
        _batteryText.AutoEllipsis = false;
        _microphoneText.AutoEllipsis = false;
        _batteryText.Font = new Font("Segoe UI Semibold", 7.7f);
        _microphoneText.Font = new Font("Segoe UI Semibold", 7.7f);
        _batteryText.TextAlign = ContentAlignment.MiddleRight;
        _microphoneText.TextAlign = ContentAlignment.MiddleRight;
        _statusDivider = new Panel
        {
            Location = new Point(9, 69),
            Size = new Size(MenuLogicalWidth - 18, 1),
            BackColor = Color.FromArgb(70, 74, 78)
        };
        _autoPowerOffTitle = CreateLabel(8f, FontStyle.Regular,
            ContentAlignment.MiddleLeft, new Rectangle(10, 130, 74, 20));
        _autoPowerOffText = CreateLabel(7.7f, FontStyle.Bold,
            ContentAlignment.MiddleRight, new Rectangle(86, 130, 76, 20));
        _monitoringTitle = CreateLabel(8f, FontStyle.Regular,
            ContentAlignment.MiddleLeft, new Rectangle(10, 154, 74, 20));
        _monitoringText = CreateLabel(7.7f, FontStyle.Bold,
            ContentAlignment.MiddleRight, new Rectangle(86, 154, 76, 20));
        _autoPowerOffText.AutoEllipsis = false;
        _monitoringText.AutoEllipsis = false;
        _batterySummaryGlyph = new TraySummaryGlyphControl(_iconCache, "battery_monitor");
        _microphoneSummaryGlyph = new TraySummaryGlyphControl(_iconCache, "mic");
        _powerOffSummaryGlyph = new TraySummaryGlyphControl(_iconCache, "clock");
        _monitoringSummaryGlyph = new TraySummaryGlyphControl(_iconCache, "connection");
        Controls.AddRange(new Control[]
        {
            _deviceImage, _deviceName, _statusTitle, _statusDot, _statusText,
            _statusDivider, _batteryTitle, _batteryIcon, _batteryText,
            _microphoneTitle, _microphoneIcon, _microphoneText,
            _autoPowerOffTitle, _autoPowerOffText, _monitoringTitle, _monitoringText,
            _batterySummaryGlyph, _microphoneSummaryGlyph, _powerOffSummaryGlyph, _monitoringSummaryGlyph
        });

        _buttonBar = new Panel
        {
            Location = new Point(0, MenuLogicalHeight - ButtonBarLogicalHeight),
            Size = new Size(MenuLogicalWidth, ButtonBarLogicalHeight),
            BackColor = Color.Transparent
        };
        // The two action surfaces sit inside the footer. The separators are
        // drawn by the parent panel, outside the rounded hover backgrounds.
        _buttonBar.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float stroke = Math.Max(1f, PngIconCache.ScaleLogical(1f, DeviceDpi));
            Color lineColor = _dark ? Color.FromArgb(85, 89, 94) : Color.FromArgb(205, 211, 218);
            using Pen pen = new(lineColor, stroke);
            float topY = stroke / 2f;
            float middleX = _buttonBar.ClientSize.Width / 2f;
            e.Graphics.DrawLine(pen, ScaleTray(8), topY,
                _buttonBar.ClientSize.Width - ScaleTray(8), topY);
            e.Graphics.DrawLine(pen, middleX, ScaleTray(12),
                middleX, _buttonBar.ClientSize.Height - ScaleTray(12));
        };
        _settingsButton = new TrayMenuButton(_iconCache, "config");
        // Explicitly use the shared red exit artwork, regardless of dark/light theme.
        _exitButton = new TrayMenuButton(_iconCache, "common:exit");
        _settingsButton.Click += (_, _) => { Hide(); _settingsAction(null, EventArgs.Empty); };
        _exitButton.Click += (_, _) => { Hide(); _exitAction(null, EventArgs.Empty); };
        _buttonBar.Controls.Add(_settingsButton);
        _buttonBar.Controls.Add(_exitButton);
        Controls.Add(_buttonBar);
        LayoutFooterButtons();

        _toolTip = new ToolTip { AutomaticDelay = 350, InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 5000 };
        _settingsButton.MouseEnter += (_, _) => _toolTip?.SetToolTip(_settingsButton, Localization.Get("ContextMenuSettings", _settings.Language));
        _exitButton.MouseEnter += (_, _) => _toolTip?.SetToolTip(_exitButton, Localization.Get("ContextMenuExit", _settings.Language));

        // Build the complete menu in 96-DPI logical coordinates first, then let
        // WinForms scale the full control tree for the monitor that owns the handle.
        AutoScaleDimensions = new SizeF(PngIconCache.LogicalDpi, PngIconCache.LogicalDpi);
        AutoScaleMode = AutoScaleMode.Dpi;

        UpdateDevice(device, charging, batteryRemainingTime);
        ApplyLocalization();
        ApplyTheme(_dark);
    }

    private Label CreateLabel(float size, FontStyle style, ContentAlignment align, Rectangle bounds) => new()
    {
        AutoSize = false,
        Bounds = bounds,
        Font = new Font("Segoe UI", size, style),
        TextAlign = align,
        BackColor = Color.Transparent
    };

    public void UpdateDevice(
        IHyperXDevice? device,
        bool charging,
        TimeSpan? batteryRemainingTime)
    {
        if (IsDisposed || Disposing)
            return;

        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke((MethodInvoker)(() => UpdateDevice(
                device,
                charging,
                batteryRemainingTime)));
            return;
        }

        _device = device;
        _charging = charging;
        _batteryRemainingTime = batteryRemainingTime;
        bool selected = !string.IsNullOrWhiteSpace(_settings.SelectedDevice);
        bool connected = selected && device?.IsConnected == true && device.Battery >= 0 && device.Battery <= 100;
        string normalized = NormalizeDeviceName(_settings.SelectedDevice);
        string imageFile = GetDeviceImage(normalized, selected);
        SetDeviceImage(imageFile);

        _deviceName.Text = selected
            ? Localization.DeviceDisplayName(HyperXDeviceManager.GetSelectionModelName(normalized), _settings.Language)
            : Localization.Get("UnknownHeadphones", _settings.Language);
        _statusText.Text = connected ? Localization.Get("Connected", _settings.Language) : selected ? Localization.Get("Disconnected", _settings.Language) : Localization.Get("SidebarUnknown", _settings.Language);
        _statusDot.Connected = connected;
        _batteryIcon.Connected = connected;
        _batteryIcon.Battery = connected && device != null ? Math.Clamp(device.Battery, 0, 100) : 0;
        _batteryIcon.Charging = connected && charging;
        if (connected)
        {
            int batteryPercent = Math.Clamp(device!.Battery, 0, 100);
            string? remaining = charging
                ? null
                : BatteryRemainingTimeFormatter.Format(
                    batteryRemainingTime,
                    _settings.Language);

            _batteryText.Text = remaining == null
                ? $"{batteryPercent}%"
                : $"{batteryPercent}% ({remaining})";
        }
        else
        {
            _batteryText.Text = Localization.Get("SidebarWaiting", _settings.Language);
        }
        bool microphoneMonitoringSupported =
            selected && HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(normalized, _device);
        bool microphoneStatusAvailable =
            connected &&
            microphoneMonitoringSupported &&
            device!.IsMicrophoneMuteStateKnown;
        bool microphoneMuted = microphoneStatusAvailable && device!.IsMicrophoneMuted;
        _microphoneSummaryGlyph.IconKey = microphoneStatusAvailable && microphoneMuted ? "mute" : "mic";
        _microphoneText.Text = microphoneStatusAvailable
            ? (microphoneMuted
                ? Localization.Get("MicrophoneMuted", _settings.Language)
                : Localization.Get("MicrophoneOpen", _settings.Language))
            : Localization.Get("SidebarWaiting", _settings.Language);
        bool powerOffSupported = selected &&
            HyperXDeviceManager.SupportsAutoPowerOff(_settings.SelectedDevice) &&
            (!connected || device is IAutoPowerOffDevice { SupportsAutoPowerOff: true });
        int? autoPowerOffMinutes = connected ? _settings.RuntimeAutoPowerOffMinutes : null;
        _autoPowerOffText.Text = autoPowerOffMinutes switch
        {
            0 => Localization.Get("AutoPowerOffNever", _settings.Language),
            10 or 20 or 30 => $"{autoPowerOffMinutes} min",
            _ => Localization.Get("SidebarWaiting", _settings.Language)
        };
        _monitoringText.Text = selected
            ? Localization.Get(_settings.GetMonitoringMode(_settings.SelectedDevice) == DeviceMonitoringMode.ListenOnly
                ? "MonitoringModeListenOnly" : "MonitoringModePolling", _settings.Language)
            : Localization.Get("SidebarWaiting", _settings.Language);
        ReflowStatusRows(microphoneMonitoringSupported, powerOffSupported, selected);
        Invalidate(true);
    }

    private void ReflowStatusRows(bool microphoneSupported, bool powerOffSupported, bool selected)
    {
        int row = ScaleTray(80);
        int gap = ScaleTray(4);
        row += PlaceTrayRow(_batteryTitle, _batteryText, _batterySummaryGlyph, row) + gap;
        _microphoneTitle.Visible = microphoneSupported;
        _microphoneText.Visible = microphoneSupported;
        _microphoneSummaryGlyph.Visible = microphoneSupported;
        if (microphoneSupported)
            row += PlaceTrayRow(_microphoneTitle, _microphoneText, _microphoneSummaryGlyph, row) + gap;
        _autoPowerOffTitle.Visible = powerOffSupported;
        _autoPowerOffText.Visible = powerOffSupported;
        _powerOffSummaryGlyph.Visible = powerOffSupported;
        if (powerOffSupported)
            row += PlaceTrayRow(_autoPowerOffTitle, _autoPowerOffText, _powerOffSummaryGlyph, row) + gap;
        _monitoringTitle.Visible = selected;
        _monitoringText.Visible = selected;
        _monitoringSummaryGlyph.Visible = selected;
        if (selected)
            row += PlaceTrayRow(_monitoringTitle, _monitoringText, _monitoringSummaryGlyph, row) + gap;

        // Height follows the number of visible rows, with the same compact
        // bottom margin and button bar independently of headset capabilities.
        _contentLogicalHeight = (int)Math.Ceiling((row + ScaleTray(6)) * 96.0 / Math.Max(1, DeviceDpi))
            + ButtonBarLogicalHeight;
        NormalizeClientSizeForCurrentDpi();
        _buttonBar.Bounds = new Rectangle(0,
            ClientSize.Height - ScaleTray(ButtonBarLogicalHeight),
            ClientSize.Width, ScaleTray(ButtonBarLogicalHeight));
        LayoutFooterButtons();
        _statusDivider.Bounds = new Rectangle(ScaleTray(9), ScaleTray(69),
            ClientSize.Width - ScaleTray(18), ScaleTray(1));
        _deviceImage.Bounds = new Rectangle(ScaleTray(8), ScaleTray(7), ScaleTray(48), ScaleTray(48));
        _deviceName.Bounds = new Rectangle(ScaleTray(59), ScaleTray(7),
            ClientSize.Width - ScaleTray(66), ScaleTray(34));
        _statusDot.Bounds = new Rectangle(ScaleTray(59), ScaleTray(42), ScaleTray(18), ScaleTray(18));
        _statusText.Bounds = new Rectangle(ScaleTray(80), ScaleTray(42),
            ClientSize.Width - ScaleTray(87), ScaleTray(20));
    }

    private void LayoutFooterButtons()
    {
        // Equal-size actions, a central divider and independent rounded hover
        // surfaces. Always use the current DPI (including on monitor changes).
        int midpoint = _buttonBar.ClientSize.Width / 2;
        int outerMargin = ScaleTray(7);
        int dividerGap = ScaleTray(6);
        int verticalMargin = ScaleTray(8);
        int buttonHeight = Math.Max(1, _buttonBar.ClientSize.Height - verticalMargin * 2);
        _settingsButton.Bounds = new Rectangle(outerMargin, verticalMargin,
            Math.Max(1, midpoint - dividerGap - outerMargin), buttonHeight);
        int exitLeft = midpoint + dividerGap;
        _exitButton.Bounds = new Rectangle(exitLeft, verticalMargin,
            Math.Max(1, _buttonBar.ClientSize.Width - exitLeft - outerMargin), buttonHeight);
        _buttonBar.Invalidate();
    }

    private int ScaleTray(int logical) => PngIconCache.ScaleLogicalToInt(logical, DeviceDpi);

    private int PlaceTrayRow(Label label, Label value, TraySummaryGlyphControl icon, int row)
    {
        int left = ScaleTray(29);
        int right = ScaleTray(MenuLogicalWidth - 7);
        int available = Math.Max(ScaleTray(1), right - left);
        int line = ScaleTray(20);
        int gap = ScaleTray(4);
        int titleWidth = TextRenderer.MeasureText(label.Text, label.Font,
            new Size(2000, line), TextFormatFlags.SingleLine).Width + ScaleTray(2);
        int valueWidth = TextRenderer.MeasureText(value.Text, value.Font,
            new Size(2000, line), TextFormatFlags.SingleLine).Width + ScaleTray(2);
        int iconSize = ScaleTray(15);
        icon.Bounds = new Rectangle(ScaleTray(9), row + (line - iconSize) / 2,
            iconSize, iconSize);
        label.AutoEllipsis = false;
        value.AutoEllipsis = false;
        if (titleWidth + gap + valueWidth <= available)
        {
            label.Bounds = new Rectangle(left, row, titleWidth, line);
            value.Bounds = new Rectangle(left + titleWidth + gap, row,
                available - titleWidth - gap, line);
            return line;
        }

        int titleHeight = Math.Max(line, TextRenderer.MeasureText(label.Text, label.Font,
            new Size(available, 500), TextFormatFlags.WordBreak).Height + ScaleTray(2));
        int valueHeight = Math.Max(line, TextRenderer.MeasureText(value.Text, value.Font,
            new Size(available, 500), TextFormatFlags.WordBreak).Height + ScaleTray(2));
        label.Bounds = new Rectangle(left, row, available, titleHeight);
        value.Bounds = new Rectangle(left, row + titleHeight + ScaleTray(1), available, valueHeight);
        return titleHeight + ScaleTray(1) + valueHeight;
    }

    public void ApplyLocalization()
    {
        _statusTitle.Text = Localization.Get("SidebarStatus", _settings.Language);
        _batteryTitle.Text = Localization.Get("SidebarBattery", _settings.Language);
        _microphoneTitle.Text = Localization.Get("SidebarMicrophone", _settings.Language);
        _autoPowerOffTitle.Text = Localization.Get("SidebarAutoPowerOff", _settings.Language);
        _monitoringTitle.Text = Localization.Get("SidebarMonitoringMode", _settings.Language);
        _settingsButton.Text = Localization.Get("ContextMenuSettings", _settings.Language);
        _exitButton.Text = Localization.Get("ContextMenuExit", _settings.Language);
        _settingsButton.AccessibleName = _settingsButton.Text;
        _exitButton.AccessibleName = _exitButton.Text;
        _toolTip?.SetToolTip(_settingsButton, _settingsButton.Text);
        _toolTip?.SetToolTip(_exitButton, _exitButton.Text);
        UpdateDevice(
            _device,
            _charging,
            _batteryRemainingTime);
    }

    public void ApplyTheme(bool dark)
    {
        _dark = dark;
        BackColor = dark ? Color.FromArgb(45, 48, 51) : Color.White;
        Color foreground = dark ? Color.WhiteSmoke : Color.FromArgb(24, 32, 45);
        Color secondary = dark ? Color.FromArgb(196, 201, 207) : Color.FromArgb(82, 95, 115);
        _deviceName.ForeColor = foreground;
        _statusTitle.ForeColor = secondary;
        _statusText.ForeColor = secondary;
        _batteryTitle.ForeColor = secondary;
        _batteryText.ForeColor = secondary;
        _microphoneTitle.ForeColor = secondary;
        _microphoneText.ForeColor = foreground;
        _batteryText.ForeColor = foreground;
        _autoPowerOffTitle.ForeColor = secondary;
        _monitoringTitle.ForeColor = secondary;
        _autoPowerOffText.ForeColor = foreground;
        _monitoringText.ForeColor = foreground;
        _statusDivider.BackColor = dark ? Color.FromArgb(70, 74, 78) : Color.FromArgb(205, 211, 218);
        _batterySummaryGlyph.DarkMode = dark;
        _microphoneSummaryGlyph.DarkMode = dark;
        _powerOffSummaryGlyph.DarkMode = dark;
        _monitoringSummaryGlyph.DarkMode = dark;
        bool selected = !string.IsNullOrWhiteSpace(_settings.SelectedDevice);
        string normalized = NormalizeDeviceName(_settings.SelectedDevice);
        bool microphoneMonitoringSupported =
            selected && HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(normalized, _device);
        bool microphoneMuted =
            _device?.IsConnected == true &&
            _device.Battery >= 0 &&
            _device.Battery <= 100 &&
            microphoneMonitoringSupported &&
            _device.IsMicrophoneMuteStateKnown &&
            _device.IsMicrophoneMuted;
        // Hidden microphone icon is intentionally not loaded in the compact summary.
        _statusDot.Invalidate();
        _batteryIcon.DarkMode = dark;
        _settingsButton.DarkMode = dark;
        _settingsButton.OutsideBackColor = BackColor;
        _exitButton.DarkMode = dark;
        _exitButton.OutsideBackColor = BackColor;
        _buttonBar.Invalidate();
        Invalidate(true);
    }

    public void ShowAt(Point requested)
    {
        Rectangle work = Screen.FromPoint(requested).WorkingArea;

        // Do not force Handle creation directly. A Deactivate/Close transition during
        // CreateHandle can dispose the Form while WinForms is still creating its native
        // window. Showing the Form normally lets WinForms finish handle creation first.
        _closeOnDeactivateEnabled = false;
        if (!Visible)
        {
            Location = requested;
            Show();
        }

        NormalizeClientSizeForCurrentDpi();

        int cursorGap = PngIconCache.ScaleLogicalToInt(CursorGapLogical, DeviceDpi);
        int workAreaMargin = PngIconCache.ScaleLogicalToInt(WorkAreaMarginLogical, DeviceDpi);
        int x = requested.X - Width / 2;
        int y = requested.Y - Height - cursorGap;
        x = Math.Clamp(x, work.Left + workAreaMargin, work.Right - Width - workAreaMargin);
        y = Math.Clamp(y, work.Top + workAreaMargin, work.Bottom - Height - workAreaMargin);
        Location = new Point(x, y);
        BringToFront();
        Activate();
        _closeOnDeactivateEnabled = true;
    }

    private void NormalizeClientSizeForCurrentDpi()
    {
        Size expectedClientSize = new(
            PngIconCache.ScaleLogicalToInt(MenuLogicalWidth, DeviceDpi),
            PngIconCache.ScaleLogicalToInt(_contentLogicalHeight, DeviceDpi));

        if (ClientSize != expectedClientSize)
            ClientSize = expectedClientSize;
        if (_buttonBar != null)
        {
            _buttonBar.Bounds = new Rectangle(0,
                ClientSize.Height - PngIconCache.ScaleLogicalToInt(ButtonBarLogicalHeight, DeviceDpi),
                ClientSize.Width, PngIconCache.ScaleLogicalToInt(ButtonBarLogicalHeight, DeviceDpi));
            if (_settingsButton != null && _exitButton != null)
                LayoutFooterButtons();
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        NormalizeClientSizeForCurrentDpi();
        // Place icons and labels again at the new DPI, without changing the
        // current visibility of device-specific rows.
        ReflowStatusRows(_microphoneTitle.Visible, _autoPowerOffTitle.Visible, _monitoringTitle.Visible);
        _iconCache.ClearBitmaps();
        SetRoundedRegion();
        Invalidate(true);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        SetRoundedRegion();
    }

    private void SetRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
            return;

        Region?.Dispose();
        float radius = PngIconCache.ScaleLogical(CornerRadiusLogical, DeviceDpi);
        using GraphicsPath path = CreateRoundedPath(
            new RectangleF(0, 0, Math.Max(1, Width), Math.Max(1, Height)), radius);
        Region = new Region(path);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(_dark ? Color.FromArgb(45, 48, 51) : Color.White);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float radius = PngIconCache.ScaleLogical(CornerRadiusLogical, DeviceDpi);
        float borderWidth = Math.Max(1f, PngIconCache.ScaleLogical(BorderLogicalWidth, DeviceDpi));
        float inset = borderWidth / 2f;
        using GraphicsPath path = CreateRoundedPath(
            new RectangleF(inset, inset, Math.Max(1f, Width - borderWidth), Math.Max(1f, Height - borderWidth)),
            radius);
        using Pen border = new(_dark ? Color.FromArgb(70, 74, 78) : Color.FromArgb(205, 211, 218), borderWidth);
        e.Graphics.DrawPath(border, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip?.Dispose();
            _iconCache.Dispose();
            _deviceImage.Image = null;
            _deviceBitmap?.Dispose();
            _deviceBitmap = null;
            _microphoneIcon.Image = null;
            _microphoneBitmap?.Dispose();
            _microphoneBitmap = null;
            _microphoneIconDark = null;
            _microphoneIconUsesMuteAsset = null;
        }
        base.Dispose(disposing);
    }

    private void SetMicrophoneIcon(bool dark, bool useMuteIcon)
    {
        if (_microphoneIconDark == dark &&
            _microphoneIconUsesMuteAsset == useMuteIcon)
        {
            return;
        }

        _microphoneIcon.Image = null;
        _microphoneBitmap?.Dispose();
        _microphoneBitmap = null;

        string themePrefix = dark ? "dark" : "light";
        string iconName = useMuteIcon ? MicrophoneMutedIconAssetName : MicrophoneOpenIconAssetName;
        string path = AssetPaths.GetThemeIconPath(
            dark,
            $"{themePrefix}_{iconName}-{MicrophoneIconLogicalSize}x{MicrophoneIconLogicalSize}.png");

        if (File.Exists(path))
        {
            try
            {
                using Bitmap source = new(path);
                _microphoneBitmap = new Bitmap(source);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Could not load microphone icon '{path}': {ex.Message}");
                _microphoneBitmap = null;
            }
        }

        _microphoneIcon.Image = _microphoneBitmap;
        _microphoneIconDark = dark;
        _microphoneIconUsesMuteAsset = useMuteIcon;
    }

    private void SetDeviceImage(string fileName)
    {
        if (string.Equals(_deviceImageFileName, fileName, StringComparison.OrdinalIgnoreCase) &&
            _deviceImage.Image != null)
        {
            return;
        }

        string path = AssetPaths.GetDeviceImagePath(fileName);
        // Detach the current image before disposing its bitmap. PictureBox can
        // access Image during layout/paint, and keeping a disposed bitmap
        // assigned to Image can result in "Parameter is not valid" exceptions.
        _deviceImage.Image = null;
        _deviceBitmap?.Dispose();
        _deviceBitmap = null;
        if (File.Exists(path))
        {
            try
            {
                using Bitmap source = new(path);
                _deviceBitmap = new Bitmap(source);
            }
            catch { }
        }
        _deviceImage.Image = _deviceBitmap;
        _deviceImageFileName = fileName;
    }

    private static string NormalizeDeviceName(string value) =>
        HyperXDeviceManager.NormalizeSupportedDeviceName(value);

    private static string GetDeviceImage(string name, bool selected) => name switch
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

    private static GraphicsPath CreateRoundedPath(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        GraphicsPath p = new();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

internal sealed class TrayMenuButton : Control
{
    private const int IconLogicalSize = 21;
    private const int HoverRadiusLogical = 7;

    private readonly PngIconCache _iconCache;
    private readonly string _iconKey;
    private bool _hover;
    private bool _pressed;
    private bool _dark;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DarkMode { get => _dark; set { _dark = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color OutsideBackColor { get; set; } = Color.Transparent;

    public TrayMenuButton(PngIconCache iconCache, string iconKey)
    {
        _iconCache = iconCache;
        _iconKey = iconKey;
        Font = new Font("Segoe UI", 8.5f);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; _pressed = false; Invalidate(); };
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } };
        MouseUp += (_, _) => { _pressed = false; Invalidate(); };
        GotFocus += (_, _) => Invalidate();
        LostFocus += (_, _) => Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.Clear(OutsideBackColor);

        // Flat gray hover, softly rounded. No border or fill at rest.
        if (_hover || _pressed || Focused)
        {
            Color fill = _pressed
                ? (_dark ? Color.FromArgb(83, 86, 91) : Color.FromArgb(219, 224, 230))
                : (_dark ? Color.FromArgb(67, 70, 75) : Color.FromArgb(233, 236, 241));
            using Brush hoverBrush = new SolidBrush(fill);
            e.Graphics.FillRoundedRectangle(hoverBrush,
                new RectangleF(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)),
                Math.Max(1, PngIconCache.ScaleLogicalToInt(HoverRadiusLogical, DeviceDpi)));
        }

        Color textColor = _dark ? Color.WhiteSmoke : Color.FromArgb(31, 39, 50);
        int inset = PngIconCache.ScaleLogicalToInt(3, DeviceDpi);
        int gap = PngIconCache.ScaleLogicalToInt(5, DeviceDpi);
        int iconSize = Math.Min(Math.Max(1, Width - inset * 2),
            PngIconCache.ScaleLogicalToInt(IconLogicalSize, DeviceDpi));
        TextFormatFlags single = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine;
        Size textSize = TextRenderer.MeasureText(e.Graphics, Text, Font,
            new Size(int.MaxValue, int.MaxValue), single);
        int usableWidth = Math.Max(1, Width - inset * 2);
        if (iconSize + gap + textSize.Width <= usableWidth)
        {
            // Reference layout: icon and caption horizontally centered as a unit.
            int totalWidth = iconSize + gap + textSize.Width;
            int x = (Width - totalWidth) / 2;
            int y = (Height - iconSize) / 2;
            _iconCache.Draw(e.Graphics, _iconKey,
                new RectangleF(x, y, iconSize, iconSize), _dark, DeviceDpi);
            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(x + iconSize + gap, 0, textSize.Width, Height), textColor,
                single | TextFormatFlags.VerticalCenter);
            return;
        }

        // A long translation may not fit beside its icon in this compact menu.
        // Place it below the icon instead of clipping it or widening the menu.
        int stackedIcon = Math.Min(iconSize, PngIconCache.ScaleLogicalToInt(18, DeviceDpi));
        int iconTop = PngIconCache.ScaleLogicalToInt(2, DeviceDpi);
        _iconCache.Draw(e.Graphics, _iconKey,
            new RectangleF((Width - stackedIcon) / 2f, iconTop,
                stackedIcon, stackedIcon), _dark, DeviceDpi);
        int captionTop = iconTop + stackedIcon;
        int captionHeight = Math.Max(1, Height - captionTop);
        using Font compactFont = new(Font.FontFamily, Math.Max(7.2f, Font.Size - 0.8f),
            Font.Style, GraphicsUnit.Point);
        TextRenderer.DrawText(e.Graphics, Text, compactFont,
            new Rectangle(inset, captionTop, usableWidth, captionHeight), textColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.WordBreak);
    }
}

// Thin wrapper over HBM's original theme PNG cache; no custom icon rendering.
internal sealed class TraySummaryGlyphControl : Control
{
    private readonly PngIconCache _cache;
    private string _iconKey;
    private bool _dark;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string IconKey
    {
        get => _iconKey;
        set { if (_iconKey == value) return; _iconKey = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DarkMode
    {
        get => _dark;
        set { if (_dark == value) return; _dark = value; Invalidate(); }
    }

    public TraySummaryGlyphControl(PngIconCache cache, string iconKey)
    {
        _cache = cache;
        _iconKey = iconKey;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        // WinForms throws when Transparent is assigned before the style is enabled.
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            _cache.Draw(e.Graphics, _iconKey, new RectangleF(0, 0, Width, Height), _dark, DeviceDpi);
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine($"Tray status glyph unavailable: {ex.Message}");
        }
    }
}

internal sealed class TrayStatusDotControl : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Connected { get; set; }
    public TrayStatusDotControl() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        GraphicsState state = e.Graphics.Save();
        try
        {
            float dpiScale = Math.Max(1, DeviceDpi) / (float)PngIconCache.LogicalDpi;
            e.Graphics.ScaleTransform(dpiScale, dpiScale);

            Color c = Connected ? Color.FromArgb(52, 211, 85) : Color.FromArgb(239, 68, 68);
            using Brush b = new SolidBrush(c);
            using Pen p = new(Color.FromArgb(90, c.R, c.G, c.B), 1.5f);
            e.Graphics.FillEllipse(b, 2.5f, 2.5f, 13f, 13f);
            e.Graphics.DrawEllipse(p, 1f, 1f, 16f, 16f);
        }
        finally
        {
            e.Graphics.Restore(state);
        }
    }
}

internal sealed class TrayBatteryIconControl : Control
{
    private int _battery;
    private bool _connected;
    private bool _charging;
    private bool _dark;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Battery { get => _battery; set { _battery = Math.Clamp(value, 0, 100); Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Connected { get => _connected; set { _connected = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Charging { get => _charging; set { _charging = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DarkMode { get => _dark; set { _dark = value; Invalidate(); } }
    public TrayBatteryIconControl() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Math.Min(Width / 46f, Height / 46f);
        float x = (Width - 46f * scale) / 2f;
        float y = (Height - 46f * scale) / 2f;
        RectangleF body = new(x + 9f * scale, y + 17f * scale, 26f * scale, 14f * scale);
        Color frame = _dark ? Color.WhiteSmoke : Color.FromArgb(55, 65, 75);
        using Pen pen = new(frame, Math.Max(1f, 2f * scale));
        e.Graphics.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
        // Explicitly redraw the left edge so it remains visually continuous at
        // the small tray-menu scale, including on different DPI/scaling modes.
        e.Graphics.DrawLine(pen, body.X, body.Y, body.X, body.Bottom);
        using Brush terminal = new SolidBrush(frame);
        e.Graphics.FillRectangle(terminal, x + 36f * scale, y + 21f * scale, 3f * scale, 6f * scale);
        if (_connected)
        {
            Color level = _battery >= 50 ? Color.LimeGreen : _battery >= 30 ? Color.Gold : _battery >= 15 ? Color.Orange : Color.Red;
            float fillWidth = Math.Max(0, 22f * _battery / 100f) * scale;
            using Brush b = new SolidBrush(level);
            e.Graphics.FillRectangle(b, x + 11f * scale, y + 19f * scale, fillWidth, 10f * scale);
        }
        if (_charging && _connected)
        {
            using Pen bolt = new(Color.LimeGreen, Math.Max(1f, 1.2f * scale)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            PointF[] pts = { new(x + 25f * scale, y + 18f * scale), new(x + 20f * scale, y + 24f * scale), new(x + 23f * scale, y + 24f * scale), new(x + 20f * scale, y + 30f * scale), new(x + 27f * scale, y + 22f * scale), new(x + 24f * scale, y + 22f * scale) };
            e.Graphics.DrawLines(bolt, pts);
        }
    }
}
