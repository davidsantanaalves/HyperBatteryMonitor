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
using HyperXBatteryTray.Diagnostics;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm : Form
{
    private const int AboutActionGapLogicalWidth = 10;
    private const int AboutLegalCardLogicalHeight = 88;
    private const int AboutSectionBottomMarginLogicalHeight = 6;

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint GetDiagnosticsWindowDpi(IntPtr hwnd);

    private void ShowAboutPage()
    {
        TableLayoutPanel page = BeginResponsivePage();
        bool dark = EffectiveTheme == AppTheme.Dark;
        Color foreground = dark ? Color.WhiteSmoke : LightText;
        Color secondary = dark ? DarkSecondary : LightSecondary;

        Panel identity = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        TableLayoutPanel identityLayout = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0), BackColor = Color.Transparent };
        identityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(78)));
        identityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        identityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(124)));
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        PictureBox logo = new()
        {
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left,
            SizeMode = PictureBoxSizeMode.StretchImage,
            Margin = ScaleUiPadding(0, 0, 14, 0),
            BackColor = Color.Transparent
        };
        try
        {
            string logoPath = AssetPaths.GetBrandingAssetPath("hxbm-logo-64x64.png");
            if (File.Exists(logoPath))
            {
                using Image source = Image.FromFile(logoPath);
                logo.Image = new Bitmap(source);
                logo.Size = ScaleUiSize(source.Width, source.Height);
            }
        }
        catch { }
        identityLayout.Controls.Add(logo, 0, 0);
        TableLayoutPanel identityText = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
        identityText.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(29))); identityText.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(19))); identityText.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        identityText.Controls.Add(new Label { Text = Application.ProductName, Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 15f), ForeColor = foreground, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        identityText.Controls.Add(new Label { Text = string.Format(L("AboutVersion"), AppReleaseInfo.DisplayVersion), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f), ForeColor = secondary, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        identityText.Controls.Add(new Label { Text = L("AboutTagline"), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9f), ForeColor = foreground, BackColor = Color.Transparent, TextAlign = ContentAlignment.TopLeft }, 0, 2);
        identityLayout.Controls.Add(identityText, 1, 0);
        ActionButton diagnostics = CreateUpdateButton("BetaDiagnosticsDialogTitle", dark);
        diagnostics.TealAccent = true;
        diagnostics.IconKey = "diagnostics";
        diagnostics.IconLogicalSize = StandardUiIconLogicalSize;
        diagnostics.Font = new Font("Segoe UI", 8f);
        diagnostics.OutsideBackColor = dark ? DarkBackground : LightBackground;
        diagnostics.Dock = DockStyle.None;
        diagnostics.Anchor = AnchorStyles.Right;
        diagnostics.Size = ScaleUiSize(120, 32);
        diagnostics.Margin = new Padding(0);
        diagnostics.Click += (_, _) => ShowDiagnosticsDialog();
        identityLayout.Controls.Add(diagnostics, 2, 0);
        identity.Controls.Add(identityLayout);
        AddResponsiveRow(identity, 80, 0);

        TableLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int i = 0; i < actions.ColumnCount; i++)
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / actions.ColumnCount));

        AboutActionButton githubButton = CreateAboutActionButton(L("AboutGitHub"), AboutActionIcon.GitHub, dark, 0, 0, 118);
        githubButton.Click += (_, _) => OpenExternalUrl("https://github.com/davidsantanaalves/HyperBatteryMonitor");
        AboutActionButton supportButton = CreateAboutActionButton(L("AboutSupportButton"), AboutActionIcon.Support, dark, 0, 0, 118);
        supportButton.Click += (_, _) => OpenExternalUrl("https://buymeacoffee.com/davesantana");
        AboutActionButton documentationButton = CreateAboutActionButton(L("AboutDocumentation"), AboutActionIcon.Documentation, dark, 0, 0, 138);
        documentationButton.Click += (_, _) => OpenExternalUrl("https://github.com/davidsantanaalves/HyperBatteryMonitor#readme");
        AboutActionButton hyperXButton = CreateAboutActionButton(L("AboutHyperX"), AboutActionIcon.External, dark, 0, 0, 118);
        hyperXButton.Click += (_, _) => OpenExternalUrl("https://hyperx.com/");

        AboutActionButton[] actionButtons = { githubButton, supportButton, documentationButton, hyperXButton };
        for (int i = 0; i < actionButtons.Length; i++)
        {
            AboutActionButton button = actionButtons[i];
            button.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            button.Margin = ScaleUiPadding(0, 0, i < actionButtons.Length - 1 ? AboutActionGapLogicalWidth : 0, 0);
            actions.Controls.Add(button, i, 0);
        }
        AddResponsiveRow(actions, 42, 6);

        // A narrow separator row restores breathing room between the link buttons and Legal.
        Panel separatorRow = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        separatorRow.Controls.Add(new Panel
        {
            Dock = DockStyle.Top,
            Height = ScaleUi(1),
            Margin = ScaleUiPadding(0, 3, 0, 0),
            BackColor = dark ? DarkBorder : LightBorder
        });
        AddResponsiveRow(separatorRow, 6, 0);

        RoundedPanel legalCard = CreateResponsiveCard(AboutLegalCardLogicalHeight);
        AddResponsiveRow(
            legalCard,
            AboutLegalCardLogicalHeight + AboutSectionBottomMarginLogicalHeight,
            AboutSectionBottomMarginLogicalHeight);
        TableLayoutPanel legal = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = ScaleUiPadding(12, 6, 12, 6), BackColor = Color.Transparent };
        legal.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(30))); legal.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        legal.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(StandardUiIconLogicalSize))); legal.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); legal.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(18)));
        legalCard.Controls.Add(legal);
        legal.Controls.Add(CreateTableIcon("legal", StandardUiIconLogicalSize), 0, 0);
        Label legalTitle = new() { Text = L("AboutLegalTitle"), Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 9f), ForeColor = foreground, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
        legal.Controls.Add(legalTitle, 1, 0);
        Label legalText = new() { Text = L("AboutLegalText"), Dock = DockStyle.Fill, AutoSize = false, Font = new Font("Segoe UI", 8f), ForeColor = secondary, BackColor = Color.Transparent, TextAlign = ContentAlignment.TopLeft };
        legal.Controls.Add(legalText, 1, 1);
        LinkLabel licensesLink = new() { Text = L("AboutThirdPartyLicenses"), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5f), BackColor = Color.Transparent, LinkColor = dark ? Accent : Color.FromArgb(0, 102, 204), ActiveLinkColor = dark ? Accent : Color.FromArgb(0, 102, 204), VisitedLinkColor = dark ? Accent : Color.FromArgb(0, 102, 204) };
        licensesLink.LinkClicked += (_, _) => OpenExternalUrl("https://github.com/davidsantanaalves/HyperBatteryMonitor/blob/main/LICENSE");
        legal.Controls.Add(licensesLink, 1, 2);

        // Updates follow Legal, with the flexible spacer after both cards.
        BuildAboutUpdatesCard();

        // Keep any unused vertical space outside the About cards. Without a trailing
        // flexible row, WinForms can visually stretch the final content row to fill
        // the remaining page height after the acknowledgements section was removed.
        int spacerRow = page.RowCount++;
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        page.Controls.Add(new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        }, 0, spacerRow);
    }

    private AboutActionButton CreateAboutActionButton(string text, AboutActionIcon icon, bool dark, int x, int y, int width)
    {
        return new AboutActionButton(_iconCache)
        {
            Text = text,
            Icon = icon,
            DarkMode = dark,
            OutsideBackColor = dark ? DarkBackground : LightBackground,
            Location = new Point(ScaleUi(x), ScaleUi(y)),
            Size = ScaleUiSize(width, 38),
            Font = new Font("Segoe UI", 8f)
        };
    }

    private void ShowDiagnosticsDialog()
    {
        bool dark = EffectiveTheme == AppTheme.Dark;
        // The modal can remain open while Windows changes display scaling.
        // Keep the effective DPI mutable; every layout measurement uses S().
        int dpi = Math.Max(LogicalDpi, DeviceDpi);
        int S(int x) => PngIconCache.ScaleLogicalToInt(x, dpi);
        Color background = dark ? DarkBackground : Color.White;
        Color cardFill = dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251);
        Color fg = dark ? Color.WhiteSmoke : LightText;
        Color subtle = dark ? DarkSecondary : LightSecondary;
        Color border = dark ? DarkBorder : LightBorder;
        Color accentLink = dark ? Color.FromArgb(95, 174, 255) : Color.FromArgb(0, 102, 204);

        using DeviceDetectionOverlayForm overlay = new(this);
        using Form modal = new()
        {
            Text = L("BetaDiagnosticsDialogTitle"),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.None,
            ClientSize = new Size(S(680), S(715)),
            // Layout is manually scaled for the current DPI and explicitly
            // recalculated on WM_DPICHANGED. Avoid WinForms scaling twice.
            AutoScaleMode = AutoScaleMode.None,
            ShowInTaskbar = false,
            ShowIcon = false,
            MinimizeBox = false,
            MaximizeBox = false,
            BackColor = background,
            ForeColor = fg,
            Font = new Font("Segoe UI", 9f)
        };
        void RoundWindow()
        {
            if (modal.Width <= 0 || modal.Height <= 0) return;
            modal.Region?.Dispose();
            using GraphicsPath curve = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(0.5f, 0.5f, Math.Max(1, modal.Width - 1f),
                    Math.Max(1, modal.Height - 1f)), S(10));
            modal.Region = new Region(curve);
        }
        modal.SizeChanged += (_, _) => RoundWindow();
        RoundWindow();

        // The diagnostics modal is independent of the 760 x 520 Settings window.
        // Keep a comfortable, DPI-scaled canvas; if the available desktop area is
        // smaller, users can scroll rather than lose text or consent controls.
        Panel viewport = new()
        {
            Dock = DockStyle.Fill,
            // Enable scrolling only when the measured content exceeds this viewport.
            // Leaving AutoScroll on after a DPI downscale can retain obsolete ranges.
            AutoScroll = false,
            BackColor = background,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        modal.Controls.Add(viewport);
        Panel canvas = new()
        {
            Location = Point.Empty,
            Size = new Size(S(680), S(710)),
            BackColor = background,
            Margin = new Padding(0)
        };
        viewport.Controls.Add(canvas);
        // Match the themed scrollbar already used by Settings pages. It overlays
        // the native scroll track but leaves wheel, touchpad and keyboard scrolling
        // with the WinForms viewport. Disposal is tied to this modal's lifetime.
        using ThemedVerticalScrollBar themedScrollBar = new(viewport, dark);

        RoundedPanel Card()
        {
            RoundedPanel card = new()
            {
                BackColor = cardFill,
                OutsideBackColor = background,
                BorderColor = border,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            canvas.Controls.Add(card);
            return card;
        }
        Label Text(string value, float fontSize = 8.6f, bool strong = false,
            Color? color = null)
        {
            return new Label
            {
                Text = value,
                AutoSize = false,
                ForeColor = color ?? fg,
                BackColor = Color.Transparent,
                Font = new Font(strong ? "Segoe UI Semibold" : "Segoe UI", fontSize),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = false,
                UseCompatibleTextRendering = false,
                Margin = new Padding(0)
            };
        }
        PngIconControl Icon(string name, int logicalSize) => new(_iconCache, name)
        {
            DarkMode = dark,
            Size = new Size(S(logicalSize), S(logicalSize)),
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        // Privacy and retention have no dedicated PNGs; draw these small glyphs
        // at the exact target bounds, never in autosized table cells.
        Panel OutlineGlyph(bool shield)
        {
            Panel glyph = new() { BackColor = Color.Transparent, Margin = new Padding(0) };
            glyph.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float side = Math.Min(S(26), Math.Min(glyph.Width, glyph.Height) - S(4));
                if (side < 1) return;
                float x = (glyph.Width - side) / 2f;
                float y = (glyph.Height - side) / 2f;
                using Pen pen = new(fg, Math.Max(1.2f, S(2)));
                if (shield)
                {
                    using GraphicsPath shape = new();
                    shape.AddLines(new[]
                    {
                        new PointF(x + side / 2, y),
                        new PointF(x + side, y + side * .2f),
                        new PointF(x + side * .88f, y + side * .68f),
                        new PointF(x + side / 2, y + side),
                        new PointF(x + side * .12f, y + side * .68f),
                        new PointF(x, y + side * .2f)
                    });
                    shape.CloseFigure();
                    e.Graphics.DrawPath(pen, shape);
                }
                else
                {
                    e.Graphics.DrawEllipse(pen, x, y, side, side * .35f);
                    e.Graphics.DrawArc(pen, x, y + side * .33f, side, side * .35f, 0, 180);
                    e.Graphics.DrawArc(pen, x, y + side * .62f, side, side * .35f, 0, 180);
                    e.Graphics.DrawLine(pen, x, y + side * .17f, x, y + side * .79f);
                    e.Graphics.DrawLine(pen, x + side, y + side * .17f, x + side, y + side * .79f);
                }
            };
            return glyph;
        }
        ActionButton Button(string value, bool primary)
        {
            return new ActionButton(_iconCache)
            {
                Text = value,
                DarkMode = dark,
                Primary = primary,
                OutsideBackColor = cardFill,
                Font = new Font("Segoe UI", 8.8f),
                Margin = new Padding(0)
            };
        }

        PngIconControl headerIcon = Icon("diagnostics", 36);
        Label headerTitle = Text(L("BetaDiagnosticsDialogTitle"), 16f, true);
        canvas.Controls.Add(headerIcon);
        canvas.Controls.Add(headerTitle);

        RoundedPanel intro = Card();
        PngIconControl introIcon = Icon("legal", 25);
        Label introTitle = Text(L("BetaDiagnosticsIntroTitle"), 10.8f, true);
        Label introDescription = Text(L("BetaDiagnosticsIntroDescription"), 8.5f, false, subtle);
        intro.Controls.Add(introIcon);
        intro.Controls.Add(introTitle);
        intro.Controls.Add(introDescription);

        Label statusText = Text(string.Empty, 10f, true);
        ToggleSwitchControl toggle = new()
        {
            Size = new Size(S(44), S(25)),
            DarkMode = dark,
            Checked = BetaDiagnostics.IsEssentialLoggingEnabled,
            AccessibleName = L("BetaDiagnosticsDialogTitle")
        };
        intro.Controls.Add(statusText);
        intro.Controls.Add(toggle);

        RoundedPanel detailed = Card();
        PngIconControl detailedIcon = Icon("diagnostics", 27);
        Label detailedTitle = Text(L("BetaDiagnosticsDetailedTitle"), 10f, true);
        Label detailedDescription = Text(L("BetaDiagnosticsDetailedDescription"), 8.3f, false, subtle);
        Label detailedState = Text(string.Empty, 8.8f);
        ActionButton startDetailed = Button(L("BetaDiagnosticsDetailedStart"), true);
        ActionButton stopDetailed = Button(L("BetaDiagnosticsDetailedStop"), false);
        stopDetailed.DangerAccent = true;
        detailed.Controls.Add(detailedIcon);
        detailed.Controls.Add(detailedTitle);
        detailed.Controls.Add(detailedDescription);
        detailed.Controls.Add(detailedState);
        detailed.Controls.Add(startDetailed);
        detailed.Controls.Add(stopDetailed);

        RoundedPanel storage = Card();
        Panel storageIcon = OutlineGlyph(false);
        Label storageTitle = Text(L("BetaDiagnosticsStorageTitle"), 10f, true);
        Label storageUsage = Text(string.Empty, 8.9f, false, subtle);
        ActionButton clearLogs = Button(L("BetaDiagnosticsClearLogs"), false);
        storage.Controls.Add(storageIcon);
        storage.Controls.Add(storageTitle);
        storage.Controls.Add(storageUsage);
        storage.Controls.Add(clearLogs);
        ActionButton openLogs = Button(L("BetaDiagnosticsOpenLogsShort"), false);
        storage.Controls.Add(openLogs);

        RoundedPanel privacy = Card();
        Panel privacyGlyph = OutlineGlyph(true);
        Label privacyText = Text(L("BetaDiagnosticsPrivacyText"), 8.6f);
        privacy.Controls.Add(privacyGlyph);
        privacy.Controls.Add(privacyText);

        RoundedPanel retention = Card();
        Panel retentionGlyph = OutlineGlyph(false);
        Label retentionText = Text(L("BetaDiagnosticsRetentionText"), 8.6f);
        retention.Controls.Add(retentionGlyph);
        retention.Controls.Add(retentionText);

        // Footer matches the main Settings window: separator and a standalone Close.
        Panel footerDivider = new() { BackColor = border, Height = S(1) };
        canvas.Controls.Add(footerDivider);
        ActionButton close = Button(L("BetaDiagnosticsClose"), true);
        close.OutsideBackColor = background;
        canvas.Controls.Add(close);

        LinkLabel terms = new()
        {
            Text = L("BetaDiagnosticsTermsReview"),
            Font = new Font("Segoe UI", 8.5f),
            BackColor = Color.Transparent,
            ForeColor = subtle,
            LinkColor = accentLink,
            ActiveLinkColor = accentLink,
            VisitedLinkColor = accentLink,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false
        };
        canvas.Controls.Add(terms);

        // Diagnostics must follow the same responsive-DPI contract as the Device Settings
        // dialog: measure actual localized text and action captions, then reflow the
        // cards instead of placing labels inside fixed-height rectangles.
        // An undersized screen can scroll; no content or action may be clipped.
        bool diagnosticsLayoutInProgress = false;
        void LayoutDiagnostics()
        {
            if (diagnosticsLayoutInProgress || viewport.IsDisposed || canvas.IsDisposed)
                return;

            diagnosticsLayoutInProgress = true;
            try
            {
                // The viewport.Width is the *full* usable width even if a native
                // scrollbar from the previous DPI is still present. Leave room for
                // a possible vertical scrollbar and one rounding pixel, but never
                // make the canvas wider than its viewport (which would create a
                // horizontal scrollbar and can keep the vertical one stuck on).
                int canvasWidth = Math.Max(1,
                    viewport.Width - SystemInformation.VerticalScrollBarWidth - 1);
                if (canvas.Width != canvasWidth)
                    canvas.Width = canvasWidth;

                int left = S(18);
                int cardWidth = Math.Max(1, canvasWidth - S(36));
                int textLeft = S(66);
                int textWidth = Math.Max(1, cardWidth - textLeft - S(16));
                int cardGap = S(8);
                int insideLeft = S(17);
                int insideRight = S(18);
                TextFormatFlags wrap = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
                TextFormatFlags single = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

                int Measure(Label label, int width, int minimumHeight)
                {
                    if (string.IsNullOrEmpty(label.Text))
                        return minimumHeight;
                    int measured = TextRenderer.MeasureText(label.Text, label.Font,
                        new Size(Math.Max(1, width), int.MaxValue), wrap).Height;
                    return Math.Max(minimumHeight, measured + S(4));
                }

                int ActionWidth(ActionButton button, int minimumWidth)
                {
                    int measured = TextRenderer.MeasureText(button.Text, button.Font,
                        new Size(int.MaxValue, int.MaxValue), single).Width;
                    // ActionButton paints inside a rounded rectangle; keep room
                    // for both ends, theme borders and Windows text overhang.
                    return Math.Max(minimumWidth, measured + S(32));
                }

                int ActionHeight(ActionButton button) => Math.Max(S(34),
                    TextRenderer.MeasureText(button.Text, button.Font,
                        new Size(int.MaxValue, int.MaxValue), single).Height + S(13));

                // Header can also wrap for longer localized titles.
                int headerTextWidth = Math.Max(1, cardWidth - S(52));
                int headerHeight = Measure(headerTitle, headerTextWidth, S(47));
                headerIcon.Bounds = new Rectangle(left + S(3), S(18), S(36), S(36));
                headerTitle.Bounds = new Rectangle(left + S(52), S(12),
                    headerTextWidth, headerHeight);
                int top = Math.Max(S(68), S(12) + headerHeight + S(8));

                // Essential logging card.
                int introTitleTop = S(9);
                int introTitleHeight = Measure(introTitle, textWidth, S(28));
                int introDescriptionTop = introTitleTop + introTitleHeight + S(1);
                int introDescriptionHeight = Measure(introDescription, textWidth, S(38));
                int statusTop = introDescriptionTop + introDescriptionHeight + S(7);
                int statusWidth = Math.Max(1, cardWidth - textLeft - S(76));
                int statusHeight = Measure(statusText, statusWidth, S(29));
                int introHeight = Math.Max(S(124), statusTop +
                    Math.Max(statusHeight, S(25)) + S(12));

                intro.Bounds = new Rectangle(left, top, cardWidth, introHeight);
                introIcon.Bounds = new Rectangle(S(19), S(21), S(25), S(25));
                introTitle.Bounds = new Rectangle(textLeft, introTitleTop,
                    textWidth, introTitleHeight);
                introDescription.Bounds = new Rectangle(textLeft, introDescriptionTop,
                    textWidth, introDescriptionHeight);
                statusText.Bounds = new Rectangle(textLeft, statusTop,
                    statusWidth, statusHeight);
                toggle.Bounds = new Rectangle(Math.Max(S(4), cardWidth - S(62)),
                    statusTop + Math.Max(0, (statusHeight - S(25)) / 2), S(44), S(25));
                top += introHeight + cardGap;

                // Detailed capture card. Give the Start and Stop actions the
                // *same measured bounds*, so toggling them never shifts the UI.
                int detailedTitleWidth = Math.Max(1, cardWidth - S(74));
                int detailedTitleHeight = Measure(detailedTitle, detailedTitleWidth, S(30));
                int detailedDescriptionTop = S(8) + detailedTitleHeight + S(3);
                int detailedDescriptionWidth = Math.Max(1, cardWidth - S(34));
                int detailedDescriptionHeight = Measure(detailedDescription,
                    detailedDescriptionWidth, S(36));
                int detailedBodyTop = detailedDescriptionTop + detailedDescriptionHeight + S(7);
                int detailedActionWidth = Math.Max(
                    ActionWidth(startDetailed, S(150)), ActionWidth(stopDetailed, S(150)));
                int detailedActionHeight = Math.Max(
                    ActionHeight(startDetailed), ActionHeight(stopDetailed));
                int bodyAvailable = Math.Max(1, cardWidth - insideLeft - insideRight);
                bool detailedInline = bodyAvailable >= detailedActionWidth + S(12) + S(145);
                int detailedStateWidth = detailedInline
                    ? bodyAvailable - detailedActionWidth - S(12)
                    : bodyAvailable;
                int detailedStateHeight = Measure(detailedState, detailedStateWidth, S(34));
                int detailedButtonTop = detailedInline
                    ? detailedBodyTop + Math.Max(0, (detailedStateHeight - detailedActionHeight) / 2)
                    : detailedBodyTop + detailedStateHeight + S(7);
                int detailedButtonX = detailedInline
                    ? cardWidth - insideRight - detailedActionWidth
                    : insideLeft;
                int displayedActionWidth = detailedInline
                    ? detailedActionWidth
                    : Math.Max(1, bodyAvailable);
                int detailedHeight = Math.Max(S(139),
                    (detailedInline
                        ? detailedBodyTop + Math.Max(detailedStateHeight, detailedActionHeight)
                        : detailedButtonTop + detailedActionHeight) + S(12));

                detailed.Bounds = new Rectangle(left, top, cardWidth, detailedHeight);
                detailedIcon.Bounds = new Rectangle(S(15), S(14), S(27), S(27));
                detailedTitle.Bounds = new Rectangle(S(54), S(8),
                    detailedTitleWidth, detailedTitleHeight);
                detailedDescription.Bounds = new Rectangle(insideLeft,
                    detailedDescriptionTop, detailedDescriptionWidth, detailedDescriptionHeight);
                detailedState.Bounds = new Rectangle(insideLeft, detailedBodyTop,
                    detailedStateWidth, detailedStateHeight);
                Rectangle detailedActionBounds = new(detailedButtonX, detailedButtonTop,
                    displayedActionWidth, detailedActionHeight);
                startDetailed.Bounds = detailedActionBounds;
                stopDetailed.Bounds = detailedActionBounds;
                top += detailedHeight + cardGap;

                // Log storage card. Show actions beside the summary only when both
                // captions and the summary have sufficient measured space.
                int storageOpenWidth = ActionWidth(openLogs, S(125));
                int storageClearWidth = ActionWidth(clearLogs, S(110));
                int storageActionHeight = Math.Max(ActionHeight(openLogs), ActionHeight(clearLogs));
                int storageButtonGap = S(8);
                int storageActionsWidth = storageOpenWidth + storageButtonGap + storageClearWidth;
                int storageSummaryWidth = Math.Max(1,
                    cardWidth - S(63) - insideRight - storageActionsWidth - S(14));
                bool storageInline = storageSummaryWidth >= S(160);
                int storageSummaryRight = storageInline ? storageSummaryWidth :
                    Math.Max(1, cardWidth - S(63) - insideRight);
                int storageTitleTop = S(7);
                int storageTitleHeight = Measure(storageTitle, storageSummaryRight, S(28));
                int storageUsageTop = storageTitleTop + storageTitleHeight;
                int storageUsageHeight = Measure(storageUsage, storageSummaryRight, S(30));
                int storageSummaryBottom = storageUsageTop + storageUsageHeight;
                int storageButtonsTop;
                int storageOpenX;
                int storageClearX;
                int storageOpenDisplayedWidth;
                int storageClearDisplayedWidth;
                int storageHeight;
                int storageClearTop;

                if (storageInline)
                {
                    storageButtonsTop = Math.Max(S(14),
                        (storageSummaryBottom - storageActionHeight) / 2);
                    storageOpenX = cardWidth - insideRight - storageActionsWidth;
                    storageClearX = storageOpenX + storageOpenWidth + storageButtonGap;
                    storageOpenDisplayedWidth = storageOpenWidth;
                    storageClearDisplayedWidth = storageClearWidth;
                    storageClearTop = storageButtonsTop;
                    storageHeight = Math.Max(S(88), Math.Max(storageSummaryBottom,
                        storageButtonsTop + storageActionHeight) + S(10));
                }
                else
                {
                    storageButtonsTop = storageSummaryBottom + S(8);
                    int rowWidth = cardWidth - S(26);
                    bool buttonsInOneRow = storageActionsWidth <= rowWidth;
                    if (buttonsInOneRow)
                    {
                        storageOpenX = S(13);
                        storageClearX = storageOpenX + storageOpenWidth + storageButtonGap;
                        storageOpenDisplayedWidth = storageOpenWidth;
                        storageClearDisplayedWidth = storageClearWidth;
                        storageClearTop = storageButtonsTop;
                    }
                    else
                    {
                        // In a narrow dialog, stack actions instead of shrinking
                        // their text rectangles below the caption's actual width.
                        storageOpenX = S(13);
                        storageClearX = S(13);
                        storageOpenDisplayedWidth = Math.Max(1, rowWidth);
                        storageClearDisplayedWidth = Math.Max(1, rowWidth);
                        storageClearTop = storageButtonsTop + storageActionHeight + S(8);
                    }
                    storageHeight = Math.Max(S(88), storageClearTop +
                        storageActionHeight + S(12));
                }

                storage.Bounds = new Rectangle(left, top, cardWidth, storageHeight);
                storageIcon.Bounds = new Rectangle(S(15), S(16), S(36), S(36));
                storageTitle.Bounds = new Rectangle(S(63), storageTitleTop,
                    storageSummaryRight, storageTitleHeight);
                storageUsage.Bounds = new Rectangle(S(63), storageUsageTop,
                    storageSummaryRight, storageUsageHeight);
                openLogs.Bounds = new Rectangle(storageOpenX, storageButtonsTop,
                    storageOpenDisplayedWidth, storageActionHeight);
                clearLogs.Bounds = new Rectangle(storageClearX, storageClearTop,
                    storageClearDisplayedWidth, storageActionHeight);
                top += storageHeight + cardGap;

                // Privacy/retention may span several localized lines. Neither is
                // constrained to a fixed 85/69 logical-pixel card height anymore.
                int privacyTextHeight = Measure(privacyText, textWidth, S(67));
                int privacyHeight = Math.Max(S(85), privacyTextHeight + S(18));
                privacy.Bounds = new Rectangle(left, top, cardWidth, privacyHeight);
                privacyGlyph.Bounds = new Rectangle(S(16), S(24), S(36), S(36));
                privacyText.Bounds = new Rectangle(textLeft, S(9),
                    textWidth, privacyTextHeight);
                top += privacyHeight + cardGap;

                int retentionTextHeight = Measure(retentionText, textWidth, S(56));
                int retentionHeight = Math.Max(S(69), retentionTextHeight + S(14));
                retention.Bounds = new Rectangle(left, top, cardWidth, retentionHeight);
                retentionGlyph.Bounds = new Rectangle(S(16), S(16), S(36), S(36));
                retentionText.Bounds = new Rectangle(textLeft, S(7),
                    textWidth, retentionTextHeight);
                top += retentionHeight + S(4);

                int termsHeight = Math.Max(S(24),
                    TextRenderer.MeasureText(terms.Text, terms.Font,
                        new Size(Math.Max(1, cardWidth - S(10)), int.MaxValue), wrap).Height + S(4));
                terms.Bounds = new Rectangle(left + S(5), top,
                    Math.Max(1, cardWidth - S(10)), termsHeight);
                top += termsHeight + S(5);

                footerDivider.Bounds = new Rectangle(left, top, cardWidth, S(1));
                top += S(14);
                int closeWidth = ActionWidth(close, S(166));
                int closeHeight = ActionHeight(close);
                close.Bounds = new Rectangle(left + Math.Max(0, cardWidth - closeWidth),
                    top, Math.Min(cardWidth, closeWidth), closeHeight);
                top += closeHeight + S(16);

                if (canvas.Height != top)
                    canvas.Height = top;

                // A larger-DPI layout may have created native scroll ranges and a
                // negative canvas offset. Re-evaluate from the freshly measured
                // content height, not from the old scrollbar state. The viewport's
                // Height (rather than ClientSize.Height) excludes stale horizontal
                // scrollbar bookkeeping from this decision.
                bool needsVerticalScroll = top > viewport.Height;
                if (needsVerticalScroll)
                {
                    if (!viewport.AutoScroll)
                        viewport.AutoScroll = true;
                    Size scrollExtent = new(0, top);
                    if (viewport.AutoScrollMinSize != scrollExtent)
                        viewport.AutoScrollMinSize = scrollExtent;
                }
                else
                {
                    // Remove BOTH stale vertical and horizontal scrollbars when
                    // the content fits again, including after 175% -> 100% DPI.
                    // Clear the old range/position and put the canvas at the origin.
                    if (viewport.AutoScroll)
                        viewport.AutoScroll = false;
                    if (viewport.AutoScrollMinSize != Size.Empty)
                        viewport.AutoScrollMinSize = Size.Empty;
                    if (viewport.AutoScrollPosition != Point.Empty)
                        viewport.AutoScrollPosition = Point.Empty;
                    if (canvas.Location != Point.Empty)
                        canvas.Location = Point.Empty;
                }
            }
            finally
            {
                diagnosticsLayoutInProgress = false;
            }
            // Track content-height changes (e.g. a translated string or a
            // status change) so the themed overlay never becomes stale.
            if (viewport.IsHandleCreated)
                themedScrollBar.RefreshState();
        }
        viewport.SizeChanged += (_, _) => LayoutDiagnostics();
        LayoutDiagnostics();

        bool maintenanceBusy = false;
        close.Click += (_, _) => modal.Close();
        openLogs.Click += (_, _) =>
        {
            UpdateStorage();
            try
            {
                if (!Directory.Exists(BetaDiagnostics.LogsFolder))
                    Directory.CreateDirectory(BetaDiagnostics.LogsFolder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = BetaDiagnostics.LogsFolder, UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show(modal, L("BetaDiagnosticsOpenFolderError"),
                    L("BetaDiagnosticsDialogTitle"), MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };
        // Reuse the HBM-themed consent dialog in read-only mode. Reviewing
        // existing terms never changes the user's consent or logging state.
        terms.LinkClicked += (_, _) =>
        {
            using BetaConsentDialog review = new(_selectedLanguage, _selectedTheme,
                reviewOnly: true);
            review.ShowDialog(modal);
            UpdateStorage();
        };
        modal.CancelButton = close;

        int UpdateStorage()
        {
            var (files, bytes) = BetaDiagnostics.GetStorageUsage();
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            string size;
            string unitKey;
            if (bytes < 1024)
            {
                size = bytes.ToString("N0", culture);
                unitKey = "BetaDiagnosticsStorageUnitBytes";
            }
            else if (bytes < 1024L * 1024L)
            {
                size = (bytes / 1024d).ToString("0.#", culture);
                unitKey = "BetaDiagnosticsStorageUnitKilobytes";
            }
            else
            {
                size = (bytes / (1024d * 1024d)).ToString("0.#", culture);
                unitKey = "BetaDiagnosticsStorageUnitMegabytes";
            }

            storageUsage.Text = string.Format(culture, L("BetaDiagnosticsStorageUsage"),
                files, size, L(unitKey));
            LayoutDiagnostics();
            clearLogs.Enabled = !maintenanceBusy;
            return files;
        }

        // The timer updates only the remaining capture time. Storage is refreshed
        // on modal opening and explicit user actions, never by periodic disk scans.
        using System.Windows.Forms.Timer captureStatusTimer = new() { Interval = 1000 };
        bool handling = false;
        void ShowState()
        {
            bool active = BetaDiagnostics.IsEssentialLoggingEnabled;
            statusText.Text = L("BetaDiagnosticsStatus") + "  " +
                (active ? "●  " + L("BetaDiagnosticsActive") : "○  " + L("BetaDiagnosticsInactive"));
            statusText.ForeColor = active ? (dark ? Color.FromArgb(123, 224, 145) : Color.FromArgb(0, 130, 62)) : subtle;
            if (toggle.Checked != active)
            {
                handling = true;
                toggle.Checked = active;
                handling = false;
            }
            DateTimeOffset? ends = BetaDiagnostics.DetailedCaptureEndsAtUtc;
            bool detailedActive = BetaDiagnostics.IsDetailedCaptureActive && ends.HasValue;
            startDetailed.Visible = !detailedActive;
            startDetailed.Enabled = !detailedActive && !maintenanceBusy;
            stopDetailed.Visible = detailedActive;
            stopDetailed.Enabled = detailedActive && !maintenanceBusy;
            toggle.Enabled = !maintenanceBusy;
            string newDetailedState = detailedActive
                ? string.Format(L("BetaDiagnosticsDetailedRemaining"),
                    Math.Max(1, (int)Math.Ceiling((ends!.Value - DateTimeOffset.UtcNow).TotalMinutes)))
                : L("BetaDiagnosticsDetailedInactive");
            if (detailedState.Text != newDetailedState)
            {
                detailedState.Text = newDetailedState;
                LayoutDiagnostics();
            }
        }
        startDetailed.Click += (_, _) =>
        {
            if (maintenanceBusy) return;
            UpdateStorage();
            int? hours = ShowDetailedCaptureDurationDialog(modal, dark,
                background, cardFill, fg, subtle, border);
            UpdateStorage();
            if (!hours.HasValue) return;
            bool temporaryConsent = !BetaDiagnostics.IsEssentialLoggingEnabled;
            if (temporaryConsent)
            {
                // Ephemeral permission: never write consent or Settings here.
                // Decline closes the duration flow and returns to Diagnostics.
                using BetaConsentDialog consent = new(_selectedLanguage, _selectedTheme,
                    temporaryCaptureHours: hours.Value);
                DialogResult consentResult = consent.ShowDialog(modal);
                UpdateStorage();
                if (consentResult != DialogResult.OK)
                {
                    ShowState();
                    return;
                }
            }
            if (!BetaDiagnostics.StartDetailedCapture(hours.Value,
                allowTemporary: temporaryConsent))
                ShowDiagnosticsResultDialog(modal, L("BetaDiagnosticsCaptureStartFailure"),
                    false, dark, background, cardFill, fg, border);
            ShowState();
            UpdateStorage();
        };
        stopDetailed.Click += (_, _) =>
        {
            BetaDiagnostics.StopDetailedCapture();
            ShowState();
            UpdateStorage();
        };
        clearLogs.Click += async (_, _) =>
        {
            if (maintenanceBusy) return;
            if (UpdateStorage() == 0)
            {
                ShowDiagnosticsResultDialog(modal, L("BetaDiagnosticsNoLogsToClear"),
                    true, dark, background, cardFill, fg, border);
                UpdateStorage();
                return;
            }
            if (!ShowDiagnosticsConfirmationDialog(modal, L("BetaDiagnosticsClearLogs"),
                L("BetaDiagnosticsClearConfirm"), dark, background, cardFill, fg, subtle, border,
                L("Yes"), L("No"), destructive: true))
            {
                UpdateStorage();
                return;
            }
            maintenanceBusy = true;
            clearLogs.Enabled = false;
            toggle.Enabled = false;
            startDetailed.Enabled = false;
            stopDetailed.Enabled = false;
            bool success;
            try { success = await BetaDiagnostics.ClearLogsAsync(); }
            catch { success = false; }
            finally { maintenanceBusy = false; }
            if (modal.IsDisposed) return;
            UpdateStorage();
            ShowState();
            ShowDiagnosticsResultDialog(modal,
                L(success ? "BetaDiagnosticsClearSuccess" : "BetaDiagnosticsClearFailure"),
                success, dark, background, cardFill, fg, border);
        };
        captureStatusTimer.Tick += (_, _) => ShowState();
        modal.Shown += (_, _) => captureStatusTimer.Start();
        modal.FormClosed += (_, _) => captureStatusTimer.Stop();
        toggle.CheckedChanged += (_, _) =>
        {
            if (handling) return;
            UpdateStorage();
            bool enable = toggle.Checked;
            if (enable)
            {
                using BetaConsentDialog consent = new(_selectedLanguage, _selectedTheme);
                if (consent.ShowDialog(modal) != DialogResult.OK)
                {
                    ShowState();
                    UpdateStorage();
                    return;
                }
            }
            try
            {
                // A fresh disk profile avoids saving unrelated unapplied Settings UI edits.
                SettingsManager manager = new();
                AppSettings persisted = manager.Load();
                persisted.BetaDiagnosticsConsentVersion = enable
                    ? BetaDiagnostics.ConsentVersion : -BetaDiagnostics.ConsentVersion;
                manager.Save(persisted);
                _settings.BetaDiagnosticsConsentVersion = persisted.BetaDiagnosticsConsentVersion;
                if (enable)
                {
                    BetaDiagnostics.Start();
                    BetaDiagnostics.Write("Diagnostics.EnabledFromAbout",
                        new { Version = AppReleaseInfo.SemanticVersion });
                }
                else
                {
                    BetaDiagnostics.Write("Diagnostics.DisabledFromAbout");
                    BetaDiagnostics.DisableEssentialLogging();
                }
            }
            catch
            {
                MessageBox.Show(modal, L("BetaDiagnosticsConsentSaveError"),
                    L("BetaDiagnosticsConsentTitle"), MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            ShowState();
            UpdateStorage();
        };
        ShowState();
        UpdateStorage();
        // Recompute the preferred logical size on *every* DPI transition,
        // including 175% -> 100%. A one-way Min(oldSize, workingArea) clamp
        // would keep the window oversized after Windows scaling is reduced.
        void FitModalToDpi(int nextDpi, Rectangle? targetRectangle = null)
        {
            if (modal.IsDisposed || modal.Disposing)
                return;

            dpi = Math.Clamp(nextDpi, LogicalDpi, 768);
            Rectangle workArea = targetRectangle is { Width: > 0, Height: > 0 } suggested
                ? Screen.FromRectangle(suggested).WorkingArea
                : Screen.FromControl(modal).WorkingArea;
            int width = Math.Min(S(680), Math.Max(1, workArea.Width - S(24)));
            int height = Math.Min(S(715), Math.Max(1, workArea.Height - S(24)));
            modal.Bounds = new Rectangle(
                workArea.Left + (workArea.Width - width) / 2,
                workArea.Top + (workArea.Height - height) / 2,
                width, height);
            RoundWindow(); // The corner radius scales even if Bounds remain unchanged.
            // A DPI transition must invalidate the previous scroll ranges BEFORE
            // content measurement; otherwise WinForms may retain a phantom bar.
            viewport.AutoScroll = false;
            viewport.AutoScrollMinSize = Size.Empty;
            viewport.AutoScrollPosition = Point.Empty;
            canvas.Location = Point.Empty;
            LayoutDiagnostics();
            if (viewport.IsHandleCreated)
                ApplyNativeScrollTheme(viewport, dark); // Fallback if overlay is hidden.
            themedScrollBar.RefreshState();
        }

        int CurrentWindowDpi()
        {
            if (modal.IsHandleCreated)
            {
                try
                {
                    uint current = GetDiagnosticsWindowDpi(modal.Handle);
                    if (current is >= 96 and <= 768)
                        return (int)current;
                }
                catch (EntryPointNotFoundException) { /* Fallback to WinForms DPI. */ }
            }
            return Math.Max(LogicalDpi, modal.DeviceDpi);
        }

        // DPI and shell preference notifications may originate on other threads.
        // Queue resize until WinForms has processed WM_DPICHANGED, to avoid the
        // framework's suggested window rectangle overriding our logical size.
        void QueueDpiRefresh(int? eventDpi = null, Rectangle? suggestedRectangle = null,
            bool forceFit = false)
        {
            if (modal.IsDisposed || modal.Disposing || !modal.IsHandleCreated)
                return;
            try
            {
                modal.BeginInvoke((MethodInvoker)(() =>
                {
                    if (modal.IsDisposed || modal.Disposing)
                        return;
                    int current = eventDpi ?? CurrentWindowDpi();
                    if (forceFit || current != dpi || suggestedRectangle.HasValue)
                        FitModalToDpi(current, suggestedRectangle);
                    else
                    {
                        LayoutDiagnostics();
                        themedScrollBar.RefreshState();
                    }
                }));
            }
            catch (InvalidOperationException) { /* Modal is closing. */ }
        }

        modal.DpiChanged += (_, e) =>
            QueueDpiRefresh(e.DeviceDpiNew, e.SuggestedRectangle);
        void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
            QueueDpiRefresh(forceFit: true);
        void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) =>
            QueueDpiRefresh();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        modal.Shown += (_, _) => FitModalToDpi(CurrentWindowDpi());

        overlay.Show(this);
        overlay.BringToFront();
        try { modal.ShowDialog(overlay); }
        finally
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            overlay.Close();
        }
    }

    private static void OpenExternalUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell launch failures; the About page remains usable.
        }
    }

    private static void OpenLocalFile(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell launch failures; the About page remains usable.
        }
    }


    private int? ShowDetailedCaptureDurationDialog(Form owner, bool dark,
        Color background, Color cardFill, Color foreground, Color subtle, Color border)
    {
        // Measure the localized captions, but never force a minimum dialog width
        // larger than the actual monitor work area (notably at 200% DPI).
        string cancelCaption = L("Cancel");
        string startCaption = L("BetaDiagnosticsDetailedStart");
        string promptCaption = L("BetaDiagnosticsDurationPrompt");
        string privacyCaption = L("BetaDiagnosticsDetailedPrivacyNote");
        using Font actionFont = new("Segoe UI", 8.8f);
        using Font promptFont = new("Segoe UI", 9.2f);
        using Font privacyFont = new("Segoe UI", 8.4f);
        int ButtonWidth(string caption, int minimumLogicalWidth) => Math.Max(
            ScaleUi(minimumLogicalWidth),
            TextRenderer.MeasureText(caption, actionFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + ScaleUi(30));

        int cancelWidth = ButtonWidth(cancelCaption, 100);
        int startWidth = ButtonWidth(startCaption, 128);
        int footerMargin = ScaleUi(24);
        int buttonGap = ScaleUi(12);
        int neededWidth = footerMargin * 2 + cancelWidth + buttonGap + startWidth;
        Rectangle workArea = Screen.FromControl(owner).WorkingArea;
        int screenMargin = ScaleUi(12);
        int availableWidth = Math.Max(1, workArea.Width - screenMargin * 2);
        int availableHeight = Math.Max(1, workArea.Height - screenMargin * 2);
        int dialogWidth = Math.Min(Math.Max(ScaleUi(448), neededWidth), availableWidth);

        // Preserve the existing 448x250 logical layout when it fits. Reflow the
        // duration choices, text and actions only on constrained displays.
        // If the content is taller than the work area, let users scroll to the
        // bottom actions rather than cropping them off-screen.
        int cardLeft = ScaleUi(18);
        int cardTop = ScaleUi(58);
        int chipGap = ScaleUi(10);
        int chipHeight = ScaleUi(34);
        int[] allowedHours = { 1, 3, 6, 12, 24 };

        (int CardWidth, int CardHeight, int InnerWidth, int PromptHeight,
            int ChipsTop, int ChipWidth, int ChipColumns, int ChipRows,
            int NoteTop, int NoteHeight, int ActionWidth, int ActionHeight,
            int ActionsTop, int NeededHeight, bool StackActions) MeasureLayout(int contentWidth)
        {
            int cardWidth = Math.Max(1, contentWidth - ScaleUi(36));
            int innerWidth = Math.Max(1, cardWidth - ScaleUi(32));
            TextFormatFlags wrap = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
            int promptHeight = Math.Max(ScaleUi(28),
                TextRenderer.MeasureText(promptCaption, promptFont,
                    new Size(innerWidth, int.MaxValue), wrap).Height + ScaleUi(2));
            int chipColumns = allowedHours.Length;
            while (chipColumns > 1 &&
                (innerWidth - chipGap * (chipColumns - 1)) / chipColumns < ScaleUi(50))
                chipColumns--;
            int chipWidth = Math.Min(ScaleUi(68),
                Math.Max(1, (innerWidth - chipGap * (chipColumns - 1)) / chipColumns));
            int chipRows = (allowedHours.Length + chipColumns - 1) / chipColumns;
            int chipsTop = ScaleUi(14) + promptHeight;
            int noteTop = chipsTop + chipRows * chipHeight + (chipRows - 1) * chipGap + ScaleUi(4);
            int noteHeight = Math.Max(ScaleUi(28),
                TextRenderer.MeasureText(privacyCaption, privacyFont,
                    new Size(innerWidth, int.MaxValue), wrap).Height + ScaleUi(2));
            int cardHeight = Math.Max(ScaleUi(122), noteTop + noteHeight + ScaleUi(14));

            int actionWidth = Math.Max(1, contentWidth - footerMargin * 2);
            int actionHeight = Math.Max(ScaleUi(32), Math.Max(
                TextRenderer.MeasureText(cancelCaption, actionFont,
                    new Size(Math.Max(1, Math.Min(cancelWidth, actionWidth) - ScaleUi(30)),
                        int.MaxValue), wrap).Height + ScaleUi(12),
                TextRenderer.MeasureText(startCaption, actionFont,
                    new Size(Math.Max(1, Math.Min(startWidth, actionWidth) - ScaleUi(30)),
                        int.MaxValue), wrap).Height + ScaleUi(12)));
            bool stackActions = neededWidth > contentWidth;
            int actionsTop = cardTop + cardHeight + ScaleUi(20);
            int neededHeight = actionsTop + actionHeight * (stackActions ? 2 : 1) +
                (stackActions ? ScaleUi(10) : 0) + ScaleUi(18);
            return (cardWidth, cardHeight, innerWidth, promptHeight, chipsTop,
                chipWidth, chipColumns, chipRows, noteTop, noteHeight,
                actionWidth, actionHeight, actionsTop, neededHeight, stackActions);
        }

        int contentWidth = dialogWidth;
        var layout = MeasureLayout(contentWidth);
        if (layout.NeededHeight > availableHeight)
        {
            // The vertical scrollbar consumes horizontal space; remeasure once
            // so its presence cannot push the last chip/button out of view.
            contentWidth = Math.Max(1, dialogWidth - SystemInformation.VerticalScrollBarWidth);
            layout = MeasureLayout(contentWidth);
        }
        int dialogHeight = Math.Min(layout.NeededHeight, availableHeight);
        using Form dialog = CreateDiagnosticsDialogShell(L("BetaDiagnosticsDurationTitle"),
            dark, background, foreground, new Size(dialogWidth, dialogHeight));
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(
            workArea.Left + (workArea.Width - dialog.Width) / 2,
            workArea.Top + (workArea.Height - dialog.Height) / 2);
        if (layout.NeededHeight > dialogHeight)
        {
            dialog.AutoScroll = true;
            dialog.AutoScrollMinSize = new Size(0, layout.NeededHeight);
        }

        RoundedPanel card = new()
        {
            Parent = dialog,
            BackColor = cardFill,
            OutsideBackColor = background,
            BorderColor = border,
            Bounds = new Rectangle(cardLeft, cardTop,
                layout.CardWidth, layout.CardHeight)
        };
        _ = new Label
        {
            Parent = card,
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = foreground,
            Font = new Font("Segoe UI", 9.2f),
            Text = promptCaption,
            Bounds = new Rectangle(ScaleUi(16), ScaleUi(14), layout.InnerWidth, layout.PromptHeight)
        };
        _ = new Label
        {
            Parent = card,
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = subtle,
            Font = new Font("Segoe UI", 8.4f),
            Text = privacyCaption,
            Bounds = new Rectangle(ScaleUi(16), layout.NoteTop,
                layout.InnerWidth, layout.NoteHeight)
        };

        int selectedIndex = 0;
        List<DurationChoiceButton> choiceButtons = new();
        void RefreshDurationButtons()
        {
            for (int i = 0; i < choiceButtons.Count; i++)
                choiceButtons[i].SelectedChoice = i == selectedIndex;
        }

        for (int i = 0; i < allowedHours.Length; i++)
        {
            int index = i;
            int row = i / layout.ChipColumns;
            int col = i % layout.ChipColumns;
            int rowCount = Math.Min(layout.ChipColumns, allowedHours.Length - row * layout.ChipColumns);
            int rowWidth = rowCount * layout.ChipWidth + (rowCount - 1) * chipGap;
            int chipsLeft = ScaleUi(16) + Math.Max(0, (layout.InnerWidth - rowWidth) / 2);
            DurationChoiceButton button = new()
            {
                Parent = card,
                DarkMode = dark,
                Text = $"{allowedHours[i]} h",
                Bounds = new Rectangle(chipsLeft + col * (layout.ChipWidth + chipGap),
                    layout.ChipsTop + row * (chipHeight + chipGap),
                    layout.ChipWidth, chipHeight),
                TabStop = true,
                SelectedChoice = i == selectedIndex
            };
            button.Click += (_, _) =>
            {
                selectedIndex = index;
                RefreshDurationButtons();
            };
            choiceButtons.Add(button);
        }
        RefreshDurationButtons();

        int actionsRight = contentWidth - footerMargin;
        int cancelY = layout.ActionsTop;
        int startY = layout.ActionsTop + (layout.StackActions ? layout.ActionHeight + ScaleUi(10) : 0);
        ActionButton cancelButton = CreateDialogActionButton(cancelCaption, dark, false, background);
        cancelButton.Parent = dialog;
        cancelButton.Bounds = new Rectangle(
            actionsRight - (layout.StackActions ? Math.Min(cancelWidth, layout.ActionWidth) :
                Math.Min(startWidth, layout.ActionWidth) + buttonGap + cancelWidth),
            cancelY, Math.Min(cancelWidth, layout.ActionWidth), layout.ActionHeight);
        cancelButton.DialogResult = DialogResult.Cancel;

        ActionButton startButton = CreateDialogActionButton(startCaption, dark, true, background);
        startButton.Parent = dialog;
        startButton.Bounds = new Rectangle(
            actionsRight - Math.Min(startWidth, layout.ActionWidth),
            startY, Math.Min(startWidth, layout.ActionWidth), layout.ActionHeight);
        startButton.Click += (_, _) =>
        {
            dialog.Tag = allowedHours[selectedIndex];
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };
        dialog.AcceptButton = startButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog(owner) == DialogResult.OK && dialog.Tag is int hours
            ? hours
            : null;
    }

    private bool ShowDiagnosticsConfirmationDialog(Form owner, string title, string message,
        bool dark, Color background, Color cardFill, Color foreground, Color subtle,
        Color border, string confirmText, string cancelText, bool destructive)
    {
        using Form dialog = CreateDiagnosticsDialogShell(title, dark, background, foreground,
            new Size(ScaleUi(452), ScaleUi(214)));
        RoundedPanel card = new()
        {
            Parent = dialog,
            BackColor = cardFill,
            OutsideBackColor = background,
            BorderColor = border,
            Bounds = new Rectangle(ScaleUi(18), ScaleUi(58), dialog.ClientSize.Width - ScaleUi(36),
                ScaleUi(92))
        };

        _ = new WarningGlyphPanel
        {
            Parent = card,
            ForeColor = destructive ? Color.FromArgb(234, 179, 8) : foreground,
            BackColor = Color.Transparent,
            Bounds = new Rectangle(ScaleUi(16), ScaleUi(20), ScaleUi(28), ScaleUi(28))
        };
        _ = new Label
        {
            Parent = card,
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = foreground,
            Font = new Font("Segoe UI", 9f),
            Text = message,
            Bounds = new Rectangle(ScaleUi(56), ScaleUi(16), card.Width - ScaleUi(72), ScaleUi(48))
        };

        ActionButton cancelButton = CreateDialogActionButton(cancelText, dark, false, background);
        cancelButton.Parent = dialog;
        cancelButton.Bounds = new Rectangle(dialog.ClientSize.Width - ScaleUi(290),
            dialog.ClientSize.Height - ScaleUi(50), ScaleUi(128), ScaleUi(32));
        cancelButton.DialogResult = DialogResult.Cancel;

        ActionButton confirmButton = CreateDialogActionButton(confirmText, dark, true, background);
        confirmButton.Parent = dialog;
        confirmButton.Bounds = new Rectangle(dialog.ClientSize.Width - ScaleUi(152),
            dialog.ClientSize.Height - ScaleUi(50), ScaleUi(128), ScaleUi(32));
        confirmButton.Click += (_, _) =>
        {
            dialog.DialogResult = DialogResult.Yes;
            dialog.Close();
        };

        dialog.AcceptButton = confirmButton;
        dialog.CancelButton = cancelButton;
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }

    private void ShowDiagnosticsResultDialog(Form owner, string message, bool success,
        bool dark, Color background, Color cardFill, Color foreground, Color border)
    {
        using Form dialog = CreateDiagnosticsDialogShell(L("BetaDiagnosticsDialogTitle"),
            dark, background, foreground, new Size(ScaleUi(452), ScaleUi(214)));
        RoundedPanel card = new()
        {
            Parent = dialog,
            BackColor = cardFill,
            OutsideBackColor = background,
            BorderColor = border,
            Bounds = new Rectangle(ScaleUi(18), ScaleUi(58),
                dialog.ClientSize.Width - ScaleUi(36), ScaleUi(92))
        };

        _ = new Label
        {
            Parent = card,
            AutoSize = false,
            BackColor = Color.Transparent,
            Text = success ? "✓" : "!",
            Font = new Font("Segoe UI Symbol", 18f, FontStyle.Bold),
            ForeColor = success
                ? (dark ? Color.FromArgb(123, 224, 145) : Color.FromArgb(0, 130, 62))
                : Color.FromArgb(234, 179, 8),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(ScaleUi(16), ScaleUi(20), ScaleUi(28), ScaleUi(40))
        };
        _ = new Label
        {
            Parent = card,
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = foreground,
            Font = new Font("Segoe UI", 9f),
            Text = message,
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(ScaleUi(56), ScaleUi(12),
                card.Width - ScaleUi(72), ScaleUi(68))
        };

        ActionButton okButton = CreateDialogActionButton(L("Ok"), dark, true, background);
        okButton.Parent = dialog;
        okButton.Bounds = new Rectangle(dialog.ClientSize.Width - ScaleUi(152),
            dialog.ClientSize.Height - ScaleUi(50), ScaleUi(128), ScaleUi(32));
        okButton.DialogResult = DialogResult.OK;
        dialog.AcceptButton = okButton;
        dialog.CancelButton = okButton;
        dialog.ShowDialog(owner);
    }

    private Form CreateDiagnosticsDialogShell(string title, bool dark, Color background,
        Color foreground, Size clientSize)
    {
        Form dialog = new()
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.None,
            ClientSize = clientSize,
            AutoScaleMode = AutoScaleMode.None,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ShowIcon = false,
            BackColor = background,
            ForeColor = foreground,
            Font = new Font("Segoe UI", 9f)
        };
        dialog.SizeChanged += (_, _) => ApplyRoundedDialogRegion(dialog);
        ApplyRoundedDialogRegion(dialog);

        _ = new PngIconControl(_iconCache, "diagnostics")
        {
            Parent = dialog,
            DarkMode = dark,
            Size = new Size(ScaleUi(28), ScaleUi(28)),
            Location = new Point(ScaleUi(20), ScaleUi(16)),
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        _ = new Label
        {
            Parent = dialog,
            Text = title,
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = foreground,
            Font = new Font("Segoe UI Semibold", 10.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(ScaleUi(56), ScaleUi(14), dialog.ClientSize.Width - ScaleUi(76),
                ScaleUi(30))
        };
        return dialog;
    }

    private void ApplyRoundedDialogRegion(Form dialog)
    {
        if (dialog.Width <= 0 || dialog.Height <= 0)
            return;

        dialog.Region?.Dispose();
        using GraphicsPath rounded = GraphicsExtensions.CreateRoundedPath(
            new RectangleF(0.5f, 0.5f, Math.Max(1, dialog.Width - 1f),
                Math.Max(1, dialog.Height - 1f)), ScaleUi(10));
        dialog.Region = new Region(rounded);
    }

    private ActionButton CreateDialogActionButton(string text, bool dark, bool primary,
        Color outsideBackColor)
    {
        return new ActionButton(_iconCache)
        {
            Text = text,
            DarkMode = dark,
            Primary = primary,
            OutsideBackColor = outsideBackColor,
            Font = new Font("Segoe UI", 8.8f),
            Margin = new Padding(0)
        };
    }

    private sealed class DurationChoiceButton : Button
    {
        private bool _hovered;
        private bool _pressed;
        private bool _selectedChoice;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool DarkMode { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool SelectedChoice
        {
            get => _selectedChoice;
            set
            {
                if (_selectedChoice == value)
                    return;
                _selectedChoice = value;
                Invalidate();
            }
        }

        public DurationChoiceButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            TabStop = true;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            ForeColor = Color.White;
            Font = new Font("Segoe UI Semibold", 8.8f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Region?.Dispose();
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1f), Math.Max(1, Height - 1f)),
                Math.Max(1, Math.Min(Height / 2, 18)));
            Region = new Region(path);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rect = new(0.5f, 0.5f, Math.Max(1, Width - 1f), Math.Max(1, Height - 1f));
            Color border = DarkMode ? Color.FromArgb(82, 92, 112) : Color.FromArgb(187, 197, 214);
            Color fill = DarkMode ? Color.FromArgb(33, 38, 48) : Color.FromArgb(242, 246, 252);
            Color hoverFill = DarkMode ? Color.FromArgb(44, 51, 63) : Color.FromArgb(234, 241, 250);
            Color activeFill = DarkMode ? Color.FromArgb(35, 99, 132) : Color.FromArgb(221, 241, 252);
            Color activeBorder = DarkMode ? Color.FromArgb(64, 160, 214) : Color.FromArgb(88, 163, 209);
            Color text = DarkMode ? Color.WhiteSmoke : Color.FromArgb(35, 42, 52);
            Color activeText = DarkMode ? Color.White : Color.FromArgb(19, 67, 100);
            if (_selectedChoice)
            {
                fill = activeFill;
                border = activeBorder;
                text = activeText;
            }
            else if (_pressed)
            {
                fill = DarkMode ? Color.FromArgb(28, 33, 43) : Color.FromArgb(224, 232, 242);
            }
            else if (_hovered)
            {
                fill = hoverFill;
            }

            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(rect,
                Math.Max(1, Math.Min(Height / 2, 18)));
            using SolidBrush brush = new(fill);
            using Pen pen = new(border, 1f);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);

            TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Round(rect), text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            // WinForms' default focus rectangle is square and dotted; render a smooth
            // pill-shaped keyboard focus indicator only on choices not yet selected.
            // Selected choices are already clearly indicated by their fill and border.
            if (Focused && !_selectedChoice)
            {
                RectangleF focusBounds = RectangleF.Inflate(rect, -3f, -3f);
                using GraphicsPath focusPath = GraphicsExtensions.CreateRoundedPath(
                    focusBounds, Math.Max(1, Math.Min((Height - 6) / 2, 15)));
                using Pen focusPen = new(activeBorder, 1.25f);
                e.Graphics.DrawPath(focusPen, focusPath);
            }
        }
    }

    private sealed class WarningGlyphPanel : Panel
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF triangle = new(Width * 0.14f, Height * 0.06f, Width * 0.72f, Height * 0.72f);
            using GraphicsPath path = new();
            path.AddPolygon(new[]
            {
                new PointF(triangle.Left + (triangle.Width / 2f), triangle.Top),
                new PointF(triangle.Right, triangle.Bottom),
                new PointF(triangle.Left, triangle.Bottom)
            });
            using SolidBrush fill = new(ForeColor);
            e.Graphics.FillPath(fill, path);
            using SolidBrush mark = new(Color.FromArgb(38, 38, 38));
            float centerX = Width / 2f;
            float top = Height * 0.30f;
            e.Graphics.FillRectangle(mark, centerX - (Width * 0.04f), top,
                Width * 0.08f, Height * 0.28f);
            e.Graphics.FillEllipse(mark, centerX - (Width * 0.05f), Height * 0.70f,
                Width * 0.10f, Height * 0.10f);
        }
    }

}
