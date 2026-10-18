using System.ComponentModel;
using System.Drawing.Drawing2D;
using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Monitoring;
using HyperXBatteryTray.Settings;

namespace HyperXBatteryTray;

internal sealed class TrayContextMenuForm : Form
{
    private const int MenuLogicalWidth = 168;
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

        Controls.AddRange(new Control[]
        {
            _deviceImage, _deviceName, _statusTitle, _statusDot, _statusText,
            _batteryTitle, _batteryIcon, _batteryText,
            _microphoneTitle, _microphoneIcon, _microphoneText
        });

        _buttonBar = new Panel
        {
            Location = new Point(0, MenuLogicalHeight - ButtonBarLogicalHeight),
            Size = new Size(MenuLogicalWidth, ButtonBarLogicalHeight),
            BackColor = Color.Transparent
        };
        _settingsButton = new TrayMenuButton(_iconCache, "config")
        {
            Location = new Point(0, 0),
            Size = new Size(84, 55),
            OutsideBackColor = Color.Transparent,
            DrawTopSeparator = true,
            DrawRightSeparator = true
        };
        _exitButton = new TrayMenuButton(_iconCache, "exit")
        {
            Location = new Point(84, 0),
            Size = new Size(84, 55),
            OutsideBackColor = Color.Transparent,
            DrawTopSeparator = true
        };
        _settingsButton.Click += (_, _) => { Hide(); _settingsAction(null, EventArgs.Empty); };
        _exitButton.Click += (_, _) => { Hide(); _exitAction(null, EventArgs.Empty); };
        _buttonBar.Controls.Add(_settingsButton);
        _buttonBar.Controls.Add(_exitButton);
        Controls.Add(_buttonBar);

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
            _batteryText.Text = Localization.Get("BatteryNA", _settings.Language);
        }
        bool microphoneMonitoringSupported =
            selected && HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(normalized);
        bool microphoneStatusAvailable =
            connected &&
            microphoneMonitoringSupported &&
            device!.IsMicrophoneMuteStateKnown;
        bool microphoneMuted = microphoneStatusAvailable && device!.IsMicrophoneMuted;
        _microphoneText.Text = microphoneStatusAvailable
            ? (microphoneMuted
                ? Localization.Get("MicrophoneMuted", _settings.Language)
                : Localization.Get("MicrophoneOpen", _settings.Language))
            : Localization.Get("MicrophoneNA", _settings.Language);
        bool useMuteIcon = !selected || !microphoneMonitoringSupported || microphoneMuted;
        SetMicrophoneIcon(_dark, useMuteIcon);
        Invalidate(true);
    }

    public void ApplyLocalization()
    {
        _statusTitle.Text = Localization.Get("SidebarStatus", _settings.Language);
        _batteryTitle.Text = Localization.Get("SidebarBattery", _settings.Language);
        _microphoneTitle.Text = Localization.Get("SidebarMicrophone", _settings.Language);
        _toolTip?.SetToolTip(_settingsButton, Localization.Get("ContextMenuSettings", _settings.Language));
        _toolTip?.SetToolTip(_exitButton, Localization.Get("ContextMenuExit", _settings.Language));
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
        _microphoneText.ForeColor = secondary;
        bool selected = !string.IsNullOrWhiteSpace(_settings.SelectedDevice);
        string normalized = NormalizeDeviceName(_settings.SelectedDevice);
        bool microphoneMonitoringSupported =
            selected && HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(normalized);
        bool microphoneMuted =
            _device?.IsConnected == true &&
            _device.Battery >= 0 &&
            _device.Battery <= 100 &&
            microphoneMonitoringSupported &&
            _device.IsMicrophoneMuteStateKnown &&
            _device.IsMicrophoneMuted;
        bool useMuteIcon = !selected || !microphoneMonitoringSupported || microphoneMuted;
        SetMicrophoneIcon(dark, useMuteIcon);
        _statusDot.Invalidate();
        _batteryIcon.DarkMode = dark;
        _settingsButton.DarkMode = dark;
        _settingsButton.OutsideBackColor = dark
            ? Color.FromArgb(45, 48, 51)
            : Color.FromArgb(247, 248, 250);
        _exitButton.DarkMode = dark;
        _exitButton.OutsideBackColor = dark
            ? Color.FromArgb(45, 48, 51)
            : Color.FromArgb(247, 248, 250);
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
            PngIconCache.ScaleLogicalToInt(MenuLogicalHeight, DeviceDpi));

        if (ClientSize != expectedClientSize)
            ClientSize = expectedClientSize;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        NormalizeClientSizeForCurrentDpi();
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
    private const float IconLogicalSize = 25f;
    private const float SeparatorLogicalWidth = 1f;

    private readonly PngIconCache _iconCache;
    private readonly string _iconKey;
    private bool _hover;
    private bool _pressed;
    private bool _dark;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DarkMode { get => _dark; set { _dark = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color OutsideBackColor { get; set; } = Color.Transparent;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DrawTopSeparator { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DrawRightSeparator { get; set; }

    public TrayMenuButton(PngIconCache iconCache, string iconKey)
    {
        _iconCache = iconCache;
        _iconKey = iconKey;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; _pressed = false; Invalidate(); };
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } };
        MouseUp += (_, _) => { _pressed = false; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.Clear(OutsideBackColor);
        Color fill = _pressed
            ? (_dark ? Color.FromArgb(35, 39, 42) : Color.FromArgb(225, 229, 235))
            : _hover
                ? (_dark ? Color.FromArgb(17, 23, 34) : Color.FromArgb(208, 208, 208))
                : (_dark ? Color.FromArgb(35, 47, 70) : Color.FromArgb(238, 238, 238));
        using (Brush brush = new SolidBrush(fill))
            e.Graphics.FillRectangle(brush, ClientRectangle);
        float separatorWidth = Math.Max(1f, PngIconCache.ScaleLogical(SeparatorLogicalWidth, DeviceDpi));
        using Pen separatorPen = new(_dark ? Color.FromArgb(70, 74, 78) : Color.FromArgb(215, 220, 226), separatorWidth);
        if (DrawTopSeparator)
            e.Graphics.DrawLine(separatorPen, 0, separatorWidth / 2f, Width - 1, separatorWidth / 2f);
        if (DrawRightSeparator)
            e.Graphics.DrawLine(separatorPen, Width - separatorWidth / 2f, 0, Width - separatorWidth / 2f, Height);

        float iconSize = PngIconCache.ScaleLogical(IconLogicalSize, DeviceDpi);
        _iconCache.Draw(
            e.Graphics,
            _iconKey,
            new RectangleF((Width - iconSize) / 2f, (Height - iconSize) / 2f, iconSize, iconSize),
            _dark,
            DeviceDpi);
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
