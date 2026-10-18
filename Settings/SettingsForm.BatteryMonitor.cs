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
    private const int BatteryModeHeaderLogicalHeight = 50;
    private const int StaticBatteryCardLogicalHeight = 88;
    private const int BatteryModeCardLogicalHeight = 134;
    private const int FinalBatteryModeCardLogicalHeight = 124;
    private const int StaticPreviewTileLogicalWidth = 86;
    private const int StaticPreviewTileLogicalHeight = 64;
    private const int StaticPreviewTileGapLogicalWidth = 6;
    private const int StaticPreviewAreaLeftSpacingLogicalWidth = 8;
    private const int StaticCardVerticalPaddingLogicalHeight = 6;
    private const int StaticTextTitleLogicalHeight = 20;
    private const int StaticTextTitleDescriptionGapLogicalHeight = 6;

    private void RebuildDevicePage()
    {
        BuildDevicePage();
        RefreshDeviceStatus();
    }

    private void ShowBatteryMonitorPage()
    {
        BeginResponsivePage();
        AddPageHeader(_activePageLayout!, "battery_monitor", Glyph.Battery, "Battery", "BatteryMonitorDescription");
        _batteryModeCards.Clear();

        RoundedPanel staticCard = CreateResponsiveCard(StaticBatteryCardLogicalHeight);
        AddResponsiveRow(staticCard);
        BuildStaticBatteryModeCard(
            staticCard,
            L("BatteryMonitorStaticTitle"),
            L("BatteryMonitorStaticDescription"),
            new[]
            {
                new BatteryPreviewItem(BatteryPreviewKind.Normal, Color.WhiteSmoke, L("BatteryPreviewNormal"), showTile: true),
                new BatteryPreviewItem(BatteryPreviewKind.Charging, Color.WhiteSmoke, L("BatteryPreviewCharging"), showTile: true)
            });

        RoundedPanel dynamicCard = CreateResponsiveCard(BatteryModeCardLogicalHeight);
        AddResponsiveRow(dynamicCard);
        BuildBatteryModeCard(dynamicCard, BatteryDisplayMode.BatteryIndicator,
            L("BatteryMonitorDynamicTitle"), L("BatteryMonitorDynamicDescription"),
            new[]
            {
                new BatteryPreviewItem(BatteryPreviewKind.Level, Color.Empty, "≥ 50%", "green"),
                new BatteryPreviewItem(BatteryPreviewKind.Level, Color.Empty, "49 – 30%", "yellow"),
                new BatteryPreviewItem(BatteryPreviewKind.Level, Color.Empty, "29 – 15%", "orange"),
                new BatteryPreviewItem(BatteryPreviewKind.Level, Color.Empty, "< 15%", "red"),
                new BatteryPreviewItem(BatteryPreviewKind.Charging, Color.WhiteSmoke, L("BatteryPreviewCharging"))
            });

        RoundedPanel customCard = CreateResponsiveCard(FinalBatteryModeCardLogicalHeight);
        AddResponsiveRow(customCard, 0);
        List<BatteryColorSettings> customColors = _pendingBatteryColors.OrderByDescending(c => c.MinimumPercent).Take(3).ToList();
        IReadOnlyList<BatteryPreviewItem> customPreviews = customColors.Count >= 3
            ? new BatteryPreviewItem[]
            {
                new BatteryPreviewItem(BatteryPreviewKind.Solid, customColors[0].Color, $">= {customColors[0].MinimumPercent}%"),
                new BatteryPreviewItem(BatteryPreviewKind.Solid, customColors[1].Color, $"{Math.Max(customColors[0].MinimumPercent - 1, customColors[1].MinimumPercent)} – {customColors[1].MinimumPercent}%"),
                new BatteryPreviewItem(BatteryPreviewKind.Solid, customColors[2].Color, $"{Math.Max(customColors[1].MinimumPercent - 1, customColors[2].MinimumPercent)} – {customColors[2].MinimumPercent}%"),
                new BatteryPreviewItem(BatteryPreviewKind.Charging, Color.WhiteSmoke, L("BatteryPreviewCharging"))
            }
            : new BatteryPreviewItem[] { new BatteryPreviewItem(BatteryPreviewKind.Charging, Color.WhiteSmoke, L("BatteryPreviewCharging")) };
        BuildBatteryModeCard(customCard, BatteryDisplayMode.Advanced,
            L("BatteryMonitorCustomTitle"), L("BatteryMonitorCustomDescription"), customPreviews, true);
        UpdateBatteryMonitorModeCards();
    }

    private void BuildStaticBatteryModeCard(
        RoundedPanel card,
        string title,
        string description,
        IReadOnlyList<BatteryPreviewItem> previews)
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = ScaleUiPadding(12, StaticCardVerticalPaddingLogicalHeight, 12, StaticCardVerticalPaddingLogicalHeight),
            BackColor = Color.Transparent
        };
        int previewAreaLogicalWidth =
            previews.Count * StaticPreviewTileLogicalWidth +
            Math.Max(0, previews.Count - 1) * StaticPreviewTileGapLogicalWidth +
            StaticPreviewAreaLeftSpacingLogicalWidth;

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(34)));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(previewAreaLogicalWidth)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(root);

        BatteryModeCard radio = new()
        {
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left,
            Size = ScaleUiSize(28, 28),
            Selected = IsBatteryModeSelected(BatteryDisplayMode.StaticIcon),
            DarkMode = EffectiveTheme == AppTheme.Dark,
            Cursor = Cursors.Hand
        };
        radio.Click += (_, _) => SelectBatteryDisplayMode(BatteryDisplayMode.StaticIcon);
        root.Controls.Add(radio, 0, 0);
        _batteryModeCards.Add((card, radio));

        TableLayoutPanel text = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(StaticTextTitleLogicalHeight)));
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(StaticTextTitleDescriptionGapLogicalHeight)));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label titleLabel = CreateWrappedLabel(title, true, 9.5f);
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        titleLabel.Margin = new Padding(0);
        titleLabel.Click += (_, _) => SelectBatteryDisplayMode(BatteryDisplayMode.StaticIcon);
        text.Controls.Add(titleLabel, 0, 0);

        Label descriptionLabel = CreateWrappedLabel(description, false, 8.5f);
        descriptionLabel.TextAlign = ContentAlignment.TopLeft;
        descriptionLabel.Margin = new Padding(0);
        descriptionLabel.Click += (_, _) => SelectBatteryDisplayMode(BatteryDisplayMode.StaticIcon);
        text.Controls.Add(descriptionLabel, 0, 2);
        root.Controls.Add(text, 1, 0);

        TableLayoutPanel previewRow = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = previews.Count,
            RowCount = 1,
            Margin = ScaleUiPadding(StaticPreviewAreaLeftSpacingLogicalWidth, 0, 0, 0),
            BackColor = Color.Transparent
        };
        previewRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        for (int i = 0; i < previews.Count; i++)
        {
            int columnWidth = StaticPreviewTileLogicalWidth +
                (i < previews.Count - 1 ? StaticPreviewTileGapLogicalWidth : 0);
            previewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(columnWidth)));

            BatteryPreviewItem preview = previews[i];
            preview.Dock = DockStyle.None;
            preview.Anchor = AnchorStyles.None;
            preview.Size = ScaleUiSize(StaticPreviewTileLogicalWidth, StaticPreviewTileLogicalHeight);
            preview.Margin = i < previews.Count - 1
                ? ScaleUiPadding(0, 0, StaticPreviewTileGapLogicalWidth, 0)
                : new Padding(0);
            preview.DarkMode = EffectiveTheme == AppTheme.Dark;
            preview.Click += (_, _) => SelectBatteryDisplayMode(BatteryDisplayMode.StaticIcon);
            previewRow.Controls.Add(preview, i, 0);
        }

        root.Controls.Add(previewRow, 2, 0);
    }

    private void BuildBatteryModeCard(
        RoundedPanel card,
        BatteryDisplayMode mode,
        string title,
        string description,
        IReadOnlyList<BatteryPreviewItem> previews,
        bool showCustomizeButton = false)
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = ScaleUiPadding(12, 8, 12, 8),
            BackColor = Color.Transparent
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(34)));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(BatteryModeHeaderLogicalHeight)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(root);

        BatteryModeCard radio = new()
        {
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left,
            Size = ScaleUiSize(28, 28),
            Selected = IsBatteryModeSelected(mode),
            DarkMode = EffectiveTheme == AppTheme.Dark,
            Cursor = Cursors.Hand
        };
        radio.Click += (_, _) => SelectBatteryDisplayMode(mode);
        root.Controls.Add(radio, 0, 0);
        root.SetRowSpan(radio, 2);
        _batteryModeCards.Add((card, radio));

        TableLayoutPanel text = CreateTextStack(title, description);
        text.Controls[0].Click += (_, _) => SelectBatteryDisplayMode(mode);
        text.Controls[1].Click += (_, _) => SelectBatteryDisplayMode(mode);
        root.Controls.Add(text, 1, 0);

        int previewColumns = showCustomizeButton ? Math.Min(4, previews.Count) : previews.Count;
        TableLayoutPanel previewRow = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = previewColumns + (showCustomizeButton ? 1 : 0),
            RowCount = 1,
            BackColor = Color.Transparent
        };
        previewRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        for (int i = 0; i < previewColumns; i++)
            previewRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Math.Max(1, previewColumns)));

        for (int i = 0; i < previewColumns; i++)
        {
            BatteryPreviewItem preview = previews[i];
            preview.Dock = DockStyle.Fill;
            preview.Margin = ScaleUiPadding(2, 0, 2, 0);
            preview.DarkMode = EffectiveTheme == AppTheme.Dark;
            preview.Click += (_, _) => SelectBatteryDisplayMode(mode);
            previewRow.Controls.Add(preview, i, 0);
        }

        if (showCustomizeButton)
        {
            previewRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AboutActionButton customizeButton = new(_iconCache)
            {
                Text = L("BatteryMonitorCustomize"),
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = ScaleUiPadding(32, 0, 8, 0),
                Margin = ScaleUiPadding(6, 4, 0, 4),
                Font = new Font("Segoe UI", 9f),
                DarkMode = EffectiveTheme == AppTheme.Dark,
                CustomIconPath = GetBatteryMonitorThemeIconPath(EffectiveTheme == AppTheme.Dark),
                OutsideBackColor = EffectiveTheme == AppTheme.Dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251),
                Cursor = Cursors.Hand
            };
            customizeButton.Click += (_, _) =>
            {
                SelectBatteryDisplayMode(mode);
                using CustomizeDynamicIconColorsDialog dialog = new(_iconCache, EffectiveTheme == AppTheme.Dark, _selectedLanguage, _pendingBatteryColors, _pendingUseGradient, _pendingGradientPercent);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _pendingBatteryColors = CloneBatteryColors(dialog.BatteryColors);
                _pendingUseGradient = dialog.UseGradient;
                _pendingGradientPercent = dialog.GradientPercent;
                ShowPage(_currentPage);
            };
            previewRow.Controls.Add(customizeButton, previewColumns, 0);
        }

        root.Controls.Add(previewRow, 1, 1);
    }

    private bool IsBatteryModeSelected(BatteryDisplayMode mode) => _pendingDisplayMode == mode;

    private static string GetBatteryMonitorThemeIconPath(bool dark)
    {
        string themeName = dark ? "dark" : "light";
        return AssetPaths.GetThemeIconPath(
            dark,
            $"theme-{themeName}-25x25.png");
    }

    private void SelectBatteryDisplayMode(BatteryDisplayMode mode)
    {
        _pendingDisplayMode = mode;
        if (mode == BatteryDisplayMode.Advanced)
            _pendingAdvancedDisplayMode = AdvancedDisplayMode.BatteryGradient;

        // Selecting a card changes only the existing persisted display mode.
        // The tray icon rendering/threshold logic remains untouched.

        UpdateBatteryMonitorModeCards();
    }

    private void UpdateBatteryMonitorModeCards()
    {
        if (_batteryModeCards.Count == 0)
            return;

        bool dark = EffectiveTheme == AppTheme.Dark;
        foreach ((RoundedPanel card, BatteryModeCard radio) in _batteryModeCards)
        {
            radio.DarkMode = dark;
            radio.Selected = false;
            card.BorderColor = dark ? DarkBorder : LightBorder;
            card.BackColor = dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251);
            card.OutsideBackColor = dark ? Color.FromArgb(34, 37, 40) : Color.White;
            card.Invalidate();
        }

        BatteryDisplayMode[] modes =
        {
            BatteryDisplayMode.StaticIcon,
            BatteryDisplayMode.BatteryIndicator,
            BatteryDisplayMode.Advanced
        };

        for (int i = 0; i < Math.Min(modes.Length, _batteryModeCards.Count); i++)
        {
            bool selected = _pendingDisplayMode == modes[i];
            _batteryModeCards[i].Radio.Selected = selected;
            _batteryModeCards[i].Card.BorderColor = selected ? Accent : (dark ? DarkBorder : LightBorder);
            _batteryModeCards[i].Card.Invalidate();
        }
    }
}
