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

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm : Form
{
    private const int PageHeaderLogicalHeight = 80;
    private const int PageHeaderBottomMarginLogical = 6;
    private const int DeviceSelectorRowLogicalHeight = 148;
    private const int DeviceConnectionRowLogicalHeight = 105;
    private const int DeviceBatteryRowLogicalHeight = 94;
    private const int DeviceSectionHeaderLogicalHeight = 42;
    private const int StandardSectionHeaderLogicalHeight = 52;
    private const int NotificationToggleColumnLogicalWidth = 54;
    private const int NotificationCriticalLabelInputGapLogicalWidth = 8;
    private const int NotificationCriticalInputPercentGapLogicalWidth = 6;
    private const int NotificationLowBatteryCardLogicalHeight = 112;
    private const int NotificationSimpleCardLogicalHeight = 68;
    private const int NotificationCardBottomSpacingLogicalHeight = 10;
    private const int NotificationCardVerticalPaddingLogicalHeight = 6;
    private const int NotificationPrimaryRowLogicalHeight = 50;
    private const int StandardUiIconLogicalSize = 25;
    private const int LargeUiIconLogicalSize = 36;
    private const int FooterLogicalHeight = 62;
    private const int FooterHorizontalPaddingLogical = 20;
    private const int FooterVerticalPaddingLogical = 13;
    private const int FooterButtonGapLogical = 10;
    private const int FooterButtonLogicalHeight = 36;
    private const int FooterResetButtonMinimumWidthLogical = 124;
    private const int FooterPrimaryButtonMinimumWidthLogical = 84;
    private const int FooterSecondaryButtonMinimumWidthLogical = 92;
    private const int FooterResetButtonLeftPaddingLogical = 32;
    private const int FooterResetButtonRightPaddingLogical = 14;
    private const int FooterStandardButtonHorizontalPaddingLogical = 18;
    private const int WmSetRedraw = 0x000B;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private int ScaleUi(int logicalValue)
    {
        if (logicalValue == 0)
            return 0;

        int dpi = IsHandleCreated ? DeviceDpi : LogicalDpi;
        return PngIconCache.ScaleLogicalToInt(logicalValue, dpi);
    }

    private Size ScaleUiSize(int width, int height) => new(ScaleUi(width), ScaleUi(height));

    private Padding ScaleUiPadding(int left, int top, int right, int bottom) =>
        new(ScaleUi(left), ScaleUi(top), ScaleUi(right), ScaleUi(bottom));

    private PngIconControl CreateTableIcon(string iconKey, int logicalSize, int rightMargin = 0)
    {
        return new PngIconControl(_iconCache, iconKey)
        {
            Dock = DockStyle.None,
            Anchor = AnchorStyles.None,
            Size = ScaleUiSize(logicalSize, logicalSize),
            Margin = ScaleUiPadding(0, 0, rightMargin, 0),
            DarkMode = EffectiveTheme == AppTheme.Dark
        };
    }

    private Control CreateMicrophoneMuteTableIcon()
    {
        bool dark = EffectiveTheme == AppTheme.Dark;
        string themePrefix = dark ? "dark" : "light";
        string path = AssetPaths.GetThemeIconPath(
            dark,
            $"{themePrefix}_mute-{StandardUiIconLogicalSize}x{StandardUiIconLogicalSize}.png");

        return File.Exists(path)
            ? CreateTableIcon("mute", StandardUiIconLogicalSize)
            : CreateTableIcon("notification", StandardUiIconLogicalSize);
    }

    private void Sidebar_Paint(object? sender, PaintEventArgs e)
    {
        using Pen pen = new(EffectiveTheme == AppTheme.Dark ? Color.FromArgb(55, 59, 63) : Color.FromArgb(229, 233, 239));
        e.Graphics.DrawLine(pen, _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height);
        int horizontalInset = ScaleUi(16);
        int firstSeparatorY = ScaleUi(SidebarFirstSeparatorLogicalY);
        int secondSeparatorY = ScaleUi(SidebarSecondSeparatorLogicalY);
        e.Graphics.DrawLine(pen, horizontalInset, firstSeparatorY, _sidebar.Width - horizontalInset, firstSeparatorY);
        e.Graphics.DrawLine(pen, horizontalInset, secondSeparatorY, _sidebar.Width - horizontalInset, secondSeparatorY);
    }

    private void BuildSidebar()
    {
        string[] keys = { "Device", "Interface", "BatteryMonitor", "Notifications", "About" };
        Glyph[] glyphs = { Glyph.Headphones, Glyph.Monitor, Glyph.Battery, Glyph.Bell, Glyph.Info };
        string[] iconKeys =
        {
            "device",
            "interface",
            "battery_monitor",
            "notification",
            "about"
        };
        int y = 4;

        for (int i = 0; i < keys.Length; i++)
        {
            SidebarItem item = new SidebarItem(glyphs[i], iconKeys[i], _iconCache)
            {
                Text = L(GetSidebarLocalizationKey(keys[i])),
                Tag = keys[i],
                Location = new Point(6, y),
                Size = new Size(168, 40),
                Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand
            };
            item.Click += Navigation_Click;
            _sidebar.Controls.Add(item);
            _navButtons[keys[i]] = item;
            y += 46;
        }
    }

    private void BuildDevicePage()
    {
        TableLayoutPanel page = BeginResponsivePage();
        AddPageHeader(page, "device", Glyph.Headphones, "Device", "DeviceDescription");

        RoundedPanel selectorCard = CreateResponsiveCard(DeviceSelectorRowLogicalHeight);
        AddResponsiveRow(selectorCard);
        BuildDeviceSelectorCard(selectorCard);

        RoundedPanel connectionCard = CreateResponsiveCard(DeviceConnectionRowLogicalHeight);
        AddResponsiveRow(connectionCard);
        TableLayoutPanel connectionLayout = CreateSectionLayout(2, DeviceSectionHeaderLogicalHeight);
        connectionCard.Controls.Add(connectionLayout);
        AddDeviceSectionHeader(connectionLayout, "connection", "Connection", "ConnectionDescription");
        AddDeviceInfoColumnsResponsive(connectionLayout, new[]
        {
            ("usb", "WirelessTechnology", "Cloud3Wireless_WirelessTechnology"),
            ("connection", "ConnectionMethod", "Cloud3Wireless_ConnectionMethod"),
            ("location", "WirelessRange", "Cloud3Wireless_Range")
        });

        RoundedPanel batteryCard = CreateResponsiveCard(DeviceBatteryRowLogicalHeight);
        AddResponsiveRow(batteryCard, DeviceBatteryRowLogicalHeight, 0);
        TableLayoutPanel batteryLayout = CreateSectionLayout(2, DeviceSectionHeaderLogicalHeight);
        batteryCard.Controls.Add(batteryLayout);
        AddDeviceSectionHeader(batteryLayout, "battery", "Battery", "BatteryDescription");
        AddDeviceInfoColumnsResponsive(batteryLayout, new[]
        {
            ("clock", "BatteryLife", "Cloud3Wireless_Battery"),
            ("energy", "ChargeTime", "Cloud3Wireless_ChargeTime")
        });

        UpdateDeviceCapabilities();
    }

    private void AddDeviceSectionHeader(
        TableLayoutPanel parent,
        string iconKey,
        string titleKey,
        string descriptionKey)
    {
        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = ScaleUiPadding(14, 4, 14, 2),
            BackColor = Color.Transparent
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(38)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        PngIconControl sectionIcon = CreateTableIcon(iconKey, LargeUiIconLogicalSize);
        sectionIcon.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        header.Controls.Add(sectionIcon, 0, 0);

        TableLayoutPanel text = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(20)));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        text.Controls.Add(CreateWrappedLabel(L(titleKey), true, 11f), 0, 0);
        text.Controls.Add(CreateWrappedLabel(L(descriptionKey), false, 8.2f), 0, 1);
        header.Controls.Add(text, 1, 0);
        parent.Controls.Add(header, 0, 0);
        parent.SetColumnSpan(header, parent.ColumnCount);
    }

    private void AddDeviceInfoColumnsResponsive(
        TableLayoutPanel parent,
        IReadOnlyList<(string IconKey, string LabelKey, string ValueKey)> columns)
    {
        TableLayoutPanel content = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns.Count,
            RowCount = 1,
            Margin = ScaleUiPadding(10, 0, 10, 4),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };

        for (int i = 0; i < columns.Count; i++)
        {
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns.Count));
            TableLayoutPanel column = CreateInfoColumn(columns[i].IconKey, columns[i].LabelKey, columns[i].ValueKey);
            content.Controls.Add(column, i, 0);
        }

        parent.Controls.Add(content, 0, 1);
        parent.SetColumnSpan(content, parent.ColumnCount);
    }

    private TableLayoutPanel CreateInfoColumn(string iconKey, string labelKey, string valueKey)
    {
        TableLayoutPanel column = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = ScaleUiPadding(8, 0, 8, 0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(30)));
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(22)));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        PngIconControl infoIcon = CreateTableIcon(iconKey, StandardUiIconLogicalSize, 4);
        infoIcon.Margin = ScaleUiPadding(0, 1, 4, 1);
        column.Controls.Add(infoIcon, 0, 0);

        Label title = CreateWrappedLabel(L(labelKey), true, 8.3f);
        title.Dock = DockStyle.Fill;
        title.Margin = new Padding(0);
        title.TextAlign = ContentAlignment.MiddleLeft;
        column.Controls.Add(title, 1, 0);

        Label value = CreateWrappedLabel(L(valueKey), false, 8.2f);
        value.Dock = DockStyle.Fill;
        value.Margin = new Padding(0);
        value.TextAlign = ContentAlignment.TopLeft;
        column.Controls.Add(value, 0, 1);
        column.SetColumnSpan(value, 2);
        SetDeviceValueLabel(labelKey, value);
        return column;
    }

    private Label CreateWrappedLabel(string text, bool semibold, float size)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size),
            BackColor = Color.Transparent,
            ForeColor = EffectiveTheme == AppTheme.Dark ? Color.WhiteSmoke : LightText,
            UseCompatibleTextRendering = false
        };
    }

    private TableLayoutPanel BeginResponsivePage()
    {
        _activePageScrollBar?.Dispose();
        _activePageScrollBar = null;
        _pageHost.Controls.Clear();
        _activePageLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            AutoSize = false,
            AutoScroll = true,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        _activePageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _pageHost.Controls.Add(_activePageLayout);
        _activePageScrollBar = new ThemedVerticalScrollBar(_activePageLayout, EffectiveTheme == AppTheme.Dark);
        if (IsHandleCreated)
            ApplyNativeScrollTheme(_activePageLayout, EffectiveTheme == AppTheme.Dark);
        return _activePageLayout;
    }

    private void SetPageHostRedraw(bool enabled)
    {
        if (!_pageHost.IsHandleCreated)
            return;

        _ = SendMessage(
            _pageHost.Handle,
            WmSetRedraw,
            enabled ? new IntPtr(1) : IntPtr.Zero,
            IntPtr.Zero);
    }

    private void AddResponsiveRow(Control control, int height = 0, int bottomMargin = 10)
    {
        if (_activePageLayout == null)
            throw new InvalidOperationException("No active settings page layout.");

        int row = _activePageLayout.RowCount++;
        int rowHeight = height > 0 ? ScaleUi(height) : control.Height;
        _activePageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 0, 0, ScaleUi(bottomMargin));
        _activePageLayout.Controls.Add(control, 0, row);
    }

    private RoundedPanel CreateResponsiveCard(int height)
    {
        RoundedPanel card = CreateCard(Point.Empty, Size.Empty, true);
        card.Dock = DockStyle.Fill;
        card.Height = ScaleUi(height);
        return card;
    }

    private TableLayoutPanel CreateCardLayout()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = ScaleUiPadding(12, 8, 12, 8),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(92)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private TableLayoutPanel CreateSectionLayout(int rows, int headerLogicalHeight = StandardSectionHeaderLogicalHeight)
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(headerLogicalHeight)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return layout;
    }

    private void SetDeviceValueLabel(string labelKey, Label valueLabel)
    {
        switch (labelKey)
        {
            case "WirelessTechnology": _wirelessTechnologyValueLabel = valueLabel; break;
            case "ConnectionMethod": _connectionMethodValueLabel = valueLabel; break;
            case "WirelessRange": _wirelessRangeValueLabel = valueLabel; break;
            case "BatteryLife": _batteryLifeValueLabel = valueLabel; break;
            case "ChargeTime": _chargeTimeValueLabel = valueLabel; break;
        }
    }

    private void ShowInterfacePage()
    {
        TableLayoutPanel page = BeginResponsivePage();
        AddPageHeader(page, "interface", Glyph.Monitor, "Interface", "InterfaceDescription");

        RoundedPanel languageCard = CreateResponsiveCard(68);
        AddResponsiveRow(languageCard);
        TableLayoutPanel language = CreateTwoColumnCardLayout();
        languageCard.Controls.Add(language);
        language.Controls.Add(CreateTableIcon("language", StandardUiIconLogicalSize), 0, 0);
        TableLayoutPanel languageText = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        languageText.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(22)));
        languageText.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        languageText.Controls.Add(CreateWrappedLabel(L("LanguageShort"), true, 9.5f), 0, 0);
        languageText.Controls.Add(CreateWrappedLabel(L("LanguageDescription"), false, 8.5f), 0, 1);
        language.Controls.Add(languageText, 1, 0);
        _languageComboBox = new RoundedLanguageSelector
        {
            Dock = DockStyle.None,
            Anchor = AnchorStyles.None,
            Size = ScaleUiSize(208, 34),
            Font = new Font("Segoe UI", 9f),
            ForeColor = EffectiveTheme == AppTheme.Dark ? Color.WhiteSmoke : LightText,
            DarkMode = EffectiveTheme == AppTheme.Dark,
            Margin = new Padding(0)
        };
        language.Controls.Add(_languageComboBox, 2, 0);
        foreach (AppLanguage languageOption in Localization.SupportedLanguages)
            _languageComboBox.Items.Add(Localization.LanguageDisplay(languageOption));
        _languageComboBox.SelectedIndex = Localization.LanguageIndex(_selectedLanguage);
        _languageComboBox.SelectedIndexChanged += LanguageComboBox_SelectedIndexChanged;

        RoundedPanel themeCard = CreateResponsiveCard(202);
        AddResponsiveRow(themeCard);
        TableLayoutPanel theme = CreateSectionLayout(2);
        theme.Padding = ScaleUiPadding(12, 8, 12, 8);
        themeCard.Controls.Add(theme);
        TableLayoutPanel themeHeader = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        themeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(32)));
        themeHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        themeHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        themeHeader.Controls.Add(CreateTableIcon("theme", StandardUiIconLogicalSize, 6), 0, 0);
        TableLayoutPanel themeText = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        themeText.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(22)));
        themeText.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        themeText.Controls.Add(CreateWrappedLabel(L("ThemeShort"), true, 9.5f), 0, 0);
        themeText.Controls.Add(CreateWrappedLabel(L("ThemeDescription"), false, 8.5f), 0, 1);
        themeHeader.Controls.Add(themeText, 1, 0);
        theme.Controls.Add(themeHeader, 0, 0);
        TableLayoutPanel themeOptions = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = ScaleUiPadding(0, 4, 0, 0), BackColor = Color.Transparent };
        for (int i = 0; i < 3; i++) themeOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        _lightThemeOption = new ThemeOptionControl(AppTheme.Light, "light", ThemeText(AppTheme.Light), _iconCache) { Dock = DockStyle.Fill, Margin = ScaleUiPadding(0, 0, 6, 0), Selected = _selectedTheme == AppTheme.Light, DarkMode = EffectiveTheme == AppTheme.Dark };
        _darkThemeOption = new ThemeOptionControl(AppTheme.Dark, "dark", ThemeText(AppTheme.Dark), _iconCache) { Dock = DockStyle.Fill, Margin = ScaleUiPadding(3, 0, 3, 0), Selected = _selectedTheme == AppTheme.Dark, DarkMode = EffectiveTheme == AppTheme.Dark };
        _systemThemeOption = new ThemeOptionControl(AppTheme.System, "interface", ThemeText(AppTheme.System), _iconCache) { Dock = DockStyle.Fill, Margin = ScaleUiPadding(6, 0, 0, 0), Selected = _selectedTheme == AppTheme.System, DarkMode = EffectiveTheme == AppTheme.Dark };
        _lightThemeOption.Click += ThemeOption_Click; _darkThemeOption.Click += ThemeOption_Click; _systemThemeOption.Click += ThemeOption_Click;
        themeOptions.Controls.Add(_lightThemeOption, 0, 0); themeOptions.Controls.Add(_darkThemeOption, 1, 0); themeOptions.Controls.Add(_systemThemeOption, 2, 0);
        theme.Controls.Add(themeOptions, 0, 1);

        RoundedPanel startupCard = CreateResponsiveCard(68);
        AddResponsiveRow(startupCard, 0);
        TableLayoutPanel startup = CreateThreeColumnCardLayout();
        startupCard.Controls.Add(startup);
        startup.Controls.Add(CreateTableIcon("windows", StandardUiIconLogicalSize), 0, 0);
        TableLayoutPanel startupText = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        startupText.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        startupText.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        Label startupTitle = CreateWrappedLabel(L("StartupShort"), true, 9.5f);
        startupTitle.Margin = new Padding(0);
        startupTitle.TextAlign = ContentAlignment.BottomLeft;
        Label startupDescription = CreateWrappedLabel(L("StartupDescription"), false, 8.5f);
        startupDescription.Margin = new Padding(0);
        startupDescription.TextAlign = ContentAlignment.TopLeft;
        startupText.Controls.Add(startupTitle, 0, 0);
        startupText.Controls.Add(startupDescription, 0, 1);
        startup.Controls.Add(startupText, 1, 0);
        _startupToggle = new ToggleSwitchControl { Dock = DockStyle.None, Anchor = AnchorStyles.Right, Size = ScaleUiSize(42, 24), Checked = _pendingStartupEnabled, DarkMode = EffectiveTheme == AppTheme.Dark, Margin = new Padding(0) };
        _startupToggle.Enabled = !ApplicationRuntime.IsTestMode;
        _startupToggle.CheckedChanged += (_, _) => _pendingStartupEnabled = _startupToggle.Checked;
        startup.Controls.Add(_startupToggle, 2, 0);
    }

    private TableLayoutPanel CreateTwoColumnCardLayout(int contentWidth = 0)
    {
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = ScaleUiPadding(12, 8, 12, 8), Margin = new Padding(0), BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(32)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(208)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return layout;
    }

    private TableLayoutPanel CreateThreeColumnCardLayout()
    {
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = ScaleUiPadding(12, 8, 12, 8), Margin = new Padding(0), BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(32)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(54)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return layout;
    }

    private void ShowNotificationsPage()
    {
        TableLayoutPanel page = BeginResponsivePage();
        AddPageHeader(page, "notification", Glyph.Bell, "Notifications", "NotificationsDescription");
        AddNotificationFlexibleSpacer(page);

        RoundedPanel lowBatteryCard = CreateResponsiveCard(NotificationLowBatteryCardLogicalHeight);
        AddNotificationRow(lowBatteryCard, NotificationLowBatteryCardLogicalHeight, NotificationCardBottomSpacingLogicalHeight);
        TableLayoutPanel low = CreateNotificationCardLayout();
        lowBatteryCard.Controls.Add(low);
        PngIconControl lowBatteryIcon = CreateTableIcon("battery_critical", LargeUiIconLogicalSize);
        low.Controls.Add(lowBatteryIcon, 0, 0);
        low.SetRowSpan(lowBatteryIcon, 2);
        TableLayoutPanel lowText = CreateTextStack(L("NotifyOnLowBattery"), L("NotifyOnLowBatteryDescription").TrimEnd('.'));
        low.Controls.Add(lowText, 1, 0);
        _notifyOnLowBatteryToggle = new ToggleSwitchControl { Dock = DockStyle.None, Anchor = AnchorStyles.Right, Size = ScaleUiSize(42, 24), Checked = _pendingNotifyOnLowBattery, DarkMode = EffectiveTheme == AppTheme.Dark };
        _notifyOnLowBatteryToggle.CheckedChanged += (_, _) => _pendingNotifyOnLowBattery = _notifyOnLowBatteryToggle.Checked;
        low.Controls.Add(_notifyOnLowBatteryToggle, 2, 0);
        low.SetRowSpan(_notifyOnLowBatteryToggle, 2);

        TableLayoutPanel criticalRow = new() { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0) };
        criticalRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        criticalRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(NotificationCriticalLabelInputGapLogicalWidth)));
        criticalRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(70)));
        criticalRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        criticalRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        criticalRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label criticalLabel = CreateWrappedLabel(L("CriticalBatteryLevel"), true, 8.8f);
        criticalLabel.AutoSize = true;
        criticalLabel.Dock = DockStyle.None;
        criticalLabel.Anchor = AnchorStyles.Left;
        criticalLabel.Margin = new Padding(0);
        criticalLabel.TextAlign = ContentAlignment.MiddleLeft;
        criticalRow.Controls.Add(criticalLabel, 0, 0);

        _criticalBatteryPercentInput = new CriticalBatteryNumericControl { Dock = DockStyle.None, Anchor = AnchorStyles.Left, Size = ScaleUiSize(70, 34), Margin = new Padding(0), Minimum = 1, Maximum = 100, Value = Math.Clamp(_pendingCriticalBatteryPercent, 1, 100), Increment = 1, Font = new Font("Segoe UI", 9.5f), DarkMode = EffectiveTheme == AppTheme.Dark };
        _criticalBatteryPercentInput.ValueChanged += (_, _) => _pendingCriticalBatteryPercent = _criticalBatteryPercentInput.Value;
        criticalRow.Controls.Add(_criticalBatteryPercentInput, 2, 0);

        Label percentLabel = CreateWrappedLabel("%", false, 9f);
        percentLabel.AutoSize = true;
        percentLabel.Dock = DockStyle.None;
        percentLabel.Anchor = AnchorStyles.Left;
        percentLabel.Margin = ScaleUiPadding(NotificationCriticalInputPercentGapLogicalWidth, 0, 0, 0);
        percentLabel.TextAlign = ContentAlignment.MiddleLeft;
        criticalRow.Controls.Add(percentLabel, 3, 0);
        low.Controls.Add(criticalRow, 1, 1);

        RoundedPanel blinkCard = CreateResponsiveCard(NotificationSimpleCardLogicalHeight);
        AddNotificationRow(blinkCard, NotificationSimpleCardLogicalHeight, NotificationCardBottomSpacingLogicalHeight);
        AddNotificationSimpleCard(blinkCard, "blink", "FlashSystrayIcon", "FlashSystrayIconDescription", out _blinkOnCriticalBatteryToggle, _pendingBlinkOnCriticalBattery, value => _pendingBlinkOnCriticalBattery = value);

        RoundedPanel fullCard = CreateResponsiveCard(NotificationSimpleCardLogicalHeight);
        AddNotificationRow(fullCard, NotificationSimpleCardLogicalHeight, NotificationCardBottomSpacingLogicalHeight);
        AddNotificationSimpleCard(fullCard, "battery_full", "NotifyWhenFullyCharged", "NotifyWhenFullyChargedDescription", out _notifyWhenFullyChargedToggle, _pendingNotifyWhenFullyCharged, value => _pendingNotifyWhenFullyCharged = value);

        bool microphoneMonitoringSupported =
            HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(
                _pendingSelectedDevice);

        RoundedPanel microphoneMuteCard =
            CreateResponsiveCard(NotificationSimpleCardLogicalHeight);
        AddNotificationRow(
            microphoneMuteCard,
            NotificationSimpleCardLogicalHeight,
            0);
        AddNotificationSimpleCard(
            microphoneMuteCard,
            "notification",
            "ShowMicrophoneMuteInSystray",
            "ShowMicrophoneMuteInSystrayDescription",
            out _showMicrophoneMuteInSystrayToggle,
            microphoneMonitoringSupported && _pendingShowMicrophoneMuteInSystray,
            value => _pendingShowMicrophoneMuteInSystray = value,
            microphoneMonitoringSupported,
            CreateMicrophoneMuteTableIcon());

        AddNotificationFlexibleSpacer(page);
    }

    private static void AddNotificationFlexibleSpacer(TableLayoutPanel page)
    {
        int row = page.RowCount++;
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        page.Controls.Add(new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        }, 0, row);
    }

    private void AddNotificationRow(Control control, int cardHeight, int bottomSpacing)
    {
        AddResponsiveRow(control, cardHeight + bottomSpacing, bottomSpacing);
    }

    private TableLayoutPanel CreateNotificationCardLayout()
    {
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = ScaleUiPadding(12, NotificationCardVerticalPaddingLogicalHeight, 12, NotificationCardVerticalPaddingLogicalHeight), Margin = new Padding(0), BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(38)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(NotificationToggleColumnLogicalWidth)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(NotificationPrimaryRowLogicalHeight)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return layout;
    }

    private TableLayoutPanel CreateTextStack(string title, string description)
    {
        TableLayoutPanel stack = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), BackColor = Color.Transparent };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(22)));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.Controls.Add(CreateWrappedLabel(title, true, 9.5f), 0, 0);
        stack.Controls.Add(CreateWrappedLabel(description, false, 8.5f), 0, 1);
        return stack;
    }

    private void AddNotificationSimpleCard(
        RoundedPanel card,
        string iconKey,
        string titleKey,
        string descriptionKey,
        out ToggleSwitchControl toggle,
        bool checkedValue,
        Action<bool> onChanged,
        bool enabled = true,
        Control? icon = null)
    {
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = ScaleUiPadding(12, NotificationCardVerticalPaddingLogicalHeight, 12, NotificationCardVerticalPaddingLogicalHeight), Margin = new Padding(0), BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(38)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(NotificationToggleColumnLogicalWidth)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(icon ?? CreateTableIcon(iconKey, LargeUiIconLogicalSize), 0, 0);
        layout.Controls.Add(CreateTextStack(L(titleKey), L(descriptionKey).TrimEnd('.')), 1, 0);
        ToggleSwitchControl createdToggle = new()
        {
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Right,
            Size = ScaleUiSize(42, 24),
            Checked = checkedValue,
            DarkMode = EffectiveTheme == AppTheme.Dark,
            Enabled = enabled
        };
        createdToggle.CheckedChanged += (_, _) => onChanged(createdToggle.Checked);
        toggle = createdToggle;
        layout.Controls.Add(createdToggle, 2, 0);
        card.Controls.Add(layout);
    }

    private void AddPageHeader(TableLayoutPanel page, string? iconKey, Glyph fallbackGlyph, string titleKey, string descriptionKey)
    {
        Panel header = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = ScaleUiPadding(0, 0, 0, PageHeaderBottomMarginLogical) };
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0), BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(44)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Control icon;
        if (!string.IsNullOrWhiteSpace(iconKey))
        {
            PngIconControl pageIcon = CreateTableIcon(iconKey, LargeUiIconLogicalSize, 6);
            pageIcon.Anchor = AnchorStyles.Top;
            pageIcon.Margin = ScaleUiPadding(0, 2, 6, 0);
            icon = pageIcon;
        }
        else
        {
            icon = new GlyphControl(fallbackGlyph)
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top,
                Size = ScaleUiSize(38, 38),
                Margin = ScaleUiPadding(0, 2, 6, 0)
            };
        }
        layout.Controls.Add(icon, 0, 0);
        TableLayoutPanel text = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(38)));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        text.Controls.Add(new Label { Text = L(titleKey), AutoSize = false, Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 23f), BackColor = Color.Transparent, ForeColor = EffectiveTheme == AppTheme.Dark ? Color.WhiteSmoke : LightText, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        text.Controls.Add(new Label { Text = L(descriptionKey), AutoSize = false, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f), BackColor = Color.Transparent, ForeColor = EffectiveTheme == AppTheme.Dark ? DarkSecondary : LightSecondary, TextAlign = ContentAlignment.TopLeft }, 0, 1);
        layout.Controls.Add(text, 1, 0);
        header.Controls.Add(layout);
        int row = page.RowCount++;
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(PageHeaderLogicalHeight)));
        page.Controls.Add(header, 0, row);
    }

    private void AddPageHeader(Glyph glyph, string titleKey, string descriptionKey) =>
        AddPageHeader(_activePageLayout ?? throw new InvalidOperationException(), null, glyph, titleKey, descriptionKey);

    private RoundedPanel CreateCard(Point location, Size size, bool inner = false)
    {
        return new RoundedPanel
        {
            Location = location,
            Size = size,
            Tag = inner ? "inner" : "outer",
            BorderColor = EffectiveTheme == AppTheme.Dark ? DarkBorder : LightBorder,
            OutsideBackColor = inner
                ? (EffectiveTheme == AppTheme.Dark ? Color.FromArgb(34, 37, 40) : Color.White)
                : (EffectiveTheme == AppTheme.Dark ? Color.FromArgb(32, 35, 38) : LightBackground),
            BackColor = inner
                ? (EffectiveTheme == AppTheme.Dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251))
                : (EffectiveTheme == AppTheme.Dark ? Color.FromArgb(34, 37, 40) : Color.White)
        };
    }

    private Label CreateLabel(string text, bool semibold, Point location, float size)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Location = location,
            Font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size),
            BackColor = Color.Transparent
        };
    }

    private void BuildFooter()
    {
        _footer = new Panel
        {
            Location = new Point(_sidebar.Width, ClientSize.Height - FooterLogicalHeight),
            Size = new Size(ClientSize.Width - _sidebar.Width, FooterLogicalHeight),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = LightBackground
        };
        _footer.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using Pen pen = new(EffectiveTheme == AppTheme.Dark ? DarkBorder : LightBorder, 1f);
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        };

        TableLayoutPanel footerLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(
                FooterHorizontalPaddingLogical,
                FooterVerticalPaddingLogical,
                FooterHorizontalPaddingLogical,
                FooterVerticalPaddingLogical),
            BackColor = Color.Transparent
        };
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _resetButton.Anchor = AnchorStyles.Left;
        _resetButton.Margin = new Padding(0);
        footerLayout.Controls.Add(_resetButton, 0, 0);

        FlowLayoutPanel actions = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        _okButton.Margin = new Padding(0, 0, FooterButtonGapLogical, 0);
        _cancelButton.Margin = new Padding(0, 0, FooterButtonGapLogical, 0);
        _applyButton.Margin = new Padding(0);
        actions.Controls.Add(_okButton);
        actions.Controls.Add(_cancelButton);
        actions.Controls.Add(_applyButton);
        footerLayout.Controls.Add(actions, 1, 0);

        _footer.Controls.Add(footerLayout);
        Controls.Add(_footer);
        _footer.BringToFront();
        Resize += (_, _) => PositionFooter();
        PositionFooter();
    }

    private void PositionFooter()
    {
        if (_footer == null)
            return;

        _footer.Location = new Point(_sidebar.Width, ClientSize.Height - _footer.Height);
        _footer.Width = Math.Max(0, ClientSize.Width - _sidebar.Width);
        _footer.PerformLayout();
    }

    private Button CreateFooterButton(string text, bool primary, bool showResetIcon = false)
    {
        return new ActionButton(_iconCache)
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(
                showResetIcon
                    ? FooterResetButtonMinimumWidthLogical
                    : (primary ? FooterPrimaryButtonMinimumWidthLogical : FooterSecondaryButtonMinimumWidthLogical),
                FooterButtonLogicalHeight),
            Padding = showResetIcon
                ? new Padding(
                    FooterResetButtonLeftPaddingLogical,
                    0,
                    FooterResetButtonRightPaddingLogical,
                    0)
                : new Padding(
                    FooterStandardButtonHorizontalPaddingLogical,
                    0,
                    FooterStandardButtonHorizontalPaddingLogical,
                    0),
            Font = new Font("Segoe UI", 9f),
            Primary = primary,
            ShowResetIcon = showResetIcon
        };
    }

    private static string GetSidebarLocalizationKey(string navigationKey)
    {
        return navigationKey switch
        {
            "BatteryMonitor" => "Battery",
            _ => navigationKey
        };
    }

    private void Navigation_Click(object? sender, EventArgs e)
    {
        if (sender is not SidebarItem item || item.Tag is not string key)
            return;
        ShowPage(key);
    }

    private void ShowPage(string key)
    {
        _currentPage = key;
        _languageComboBox?.ClosePopup();
        foreach ((string name, SidebarItem item) in _navButtons)
            item.Selected = name == key;

        bool redrawWasSuspended = _pageHost.IsHandleCreated;
        if (redrawWasSuspended)
            SetPageHostRedraw(false);

        _pageHost.SuspendLayout();
        try
        {
            switch (key)
            {
                case "Device":
                    RebuildDevicePage();
                    break;

                case "Interface":
                    ShowInterfacePage();
                    break;

                case "BatteryMonitor":
                    ShowBatteryMonitorPage();
                    break;

                case "Notifications":
                    ShowNotificationsPage();
                    break;

                case "About":
                    ShowAboutPage();
                    break;

                default:
                    TableLayoutPanel page = BeginResponsivePage();
                    AddPageHeader(page, "about", Glyph.Info, key, string.Empty);
                    break;
            }
        }
        finally
        {
            _pageHost.ResumeLayout(true);
            _activePageLayout?.PerformLayout();

            if (redrawWasSuspended)
                SetPageHostRedraw(true);

            _pageHost.Invalidate(true);
            _pageHost.Update();
        }
    }

}
