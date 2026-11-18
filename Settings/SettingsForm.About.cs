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
            ClientSize = new Size(S(680), S(542)),
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
            AutoScroll = true,
            BackColor = background,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        modal.Controls.Add(viewport);
        Panel canvas = new()
        {
            Location = Point.Empty,
            Size = new Size(S(680), S(528)),
            BackColor = background,
            Margin = new Padding(0)
        };
        viewport.Controls.Add(canvas);

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

        RoundedPanel statusCard = Card();
        Label statusText = Text(string.Empty, 10f, true);
        ToggleSwitchControl toggle = new()
        {
            Size = new Size(S(44), S(25)),
            DarkMode = dark,
            Checked = _settings.BetaDiagnosticsConsentVersion == BetaDiagnostics.ConsentVersion
                && BetaDiagnostics.IsEnabled,
            AccessibleName = L("BetaDiagnosticsDialogTitle")
        };
        statusCard.Controls.Add(statusText);
        statusCard.Controls.Add(toggle);

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

        RoundedPanel actions = Card();
        PngIconControl actionsIcon = Icon("config", 25);
        Label actionsTitle = Text(L("BetaDiagnosticsActions"), 9.5f, true);
        ActionButton openLogs = Button(L("BetaDiagnosticsOpenLogsShort"), false);
        ActionButton close = Button(L("BetaDiagnosticsClose"), true);
        actions.Controls.Add(actionsIcon);
        actions.Controls.Add(actionsTitle);
        actions.Controls.Add(openLogs);
        actions.Controls.Add(close);

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

        // Position only direct children of the cards. The previous nested
        // TableLayoutPanels computed preferred heights beyond their fixed rows,
        // which clipped descriptions and put glyphs outside the cards.
        void LayoutDiagnostics()
        {
            // Reserve the vertical scrollbar width if a smaller display needs it.
            int scrollbar = canvas.Height > viewport.ClientSize.Height
                ? SystemInformation.VerticalScrollBarWidth : 0;
            int canvasWidth = Math.Max(S(400), viewport.ClientSize.Width - scrollbar);
            if (canvas.Width != canvasWidth) canvas.Width = canvasWidth;
            int left = S(18);
            int cardWidth = Math.Max(S(1), canvas.Width - S(36));
            int textLeft = S(66);
            int textWidth = Math.Max(1, cardWidth - textLeft - S(16));

            headerIcon.Bounds = new Rectangle(left + S(3), S(18), S(36), S(36));
            headerTitle.Bounds = new Rectangle(left + S(52), S(12),
                Math.Max(1, cardWidth - S(52)), S(47));

            intro.Bounds = new Rectangle(left, S(68), cardWidth, S(74));
            introIcon.Bounds = new Rectangle(S(19), S(23), S(25), S(25));
            introTitle.Bounds = new Rectangle(textLeft, S(8), textWidth, S(27));
            introDescription.Bounds = new Rectangle(textLeft, S(34), textWidth, S(34));

            statusCard.Bounds = new Rectangle(left, S(150), cardWidth, S(50));
            statusText.Bounds = new Rectangle(S(19), S(8),
                Math.Max(1, cardWidth - S(100)), S(34));
            toggle.Bounds = new Rectangle(Math.Max(S(4), cardWidth - S(62)), S(12),
                S(44), S(25));

            privacy.Bounds = new Rectangle(left, S(208), cardWidth, S(87));
            privacyGlyph.Bounds = new Rectangle(S(16), S(24), S(36), S(36));
            privacyText.Bounds = new Rectangle(textLeft, S(9), textWidth, S(68));

            retention.Bounds = new Rectangle(left, S(303), cardWidth, S(80));
            retentionGlyph.Bounds = new Rectangle(S(16), S(21), S(36), S(36));
            retentionText.Bounds = new Rectangle(textLeft, S(9), textWidth, S(62));

            actions.Bounds = new Rectangle(left, S(391), cardWidth, S(84));
            actionsIcon.Bounds = new Rectangle(S(14), S(7), S(25), S(25));
            actionsTitle.Bounds = new Rectangle(S(47), S(8),
                Math.Max(1, cardWidth - S(58)), S(24));
            int buttonGap = S(12);
            int buttonPad = S(14);
            int buttonWidth = Math.Max(S(1), (cardWidth - 2 * buttonPad - buttonGap) / 2);
            openLogs.Bounds = new Rectangle(buttonPad, S(41), buttonWidth, S(34));
            close.Bounds = new Rectangle(buttonPad + buttonWidth + buttonGap, S(41),
                Math.Max(1, cardWidth - (buttonPad + buttonWidth + buttonGap) - buttonPad), S(34));

            terms.Bounds = new Rectangle(left + S(5), S(489),
                Math.Max(1, cardWidth - S(10)), S(28));
        }
        viewport.SizeChanged += (_, _) => LayoutDiagnostics();
        LayoutDiagnostics();

        close.Click += (_, _) => modal.Close();
        openLogs.Click += (_, _) =>
        {
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
        };
        modal.CancelButton = close;


        bool handling = false;
        void ShowState()
        {
            bool active = BetaDiagnostics.IsEnabled;
            statusText.Text = L("BetaDiagnosticsStatus") + "  " +
                (active ? "●  " + L("BetaDiagnosticsActive") : "○  " + L("BetaDiagnosticsInactive"));
            statusText.ForeColor = active ? (dark ? Color.FromArgb(123, 224, 145) : Color.FromArgb(0, 130, 62)) : subtle;
            if (toggle.Checked != active)
            {
                handling = true;
                toggle.Checked = active;
                handling = false;
            }
        }
        toggle.CheckedChanged += (_, _) =>
        {
            if (handling) return;
            bool enable = toggle.Checked;
            if (enable)
            {
                using BetaConsentDialog consent = new(_selectedLanguage, _selectedTheme);
                if (consent.ShowDialog(modal) != DialogResult.OK)
                {
                    ShowState();
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
                    BetaDiagnostics.Stop();
                }
            }
            catch
            {
                MessageBox.Show(modal, L("BetaDiagnosticsConsentSaveError"),
                    L("BetaDiagnosticsConsentTitle"), MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            ShowState();
        };
        ShowState();
        modal.Shown += (_, _) =>
        {
            Rectangle bounds = Screen.FromControl(modal).WorkingArea;
            modal.Width = Math.Min(modal.Width, Math.Max(S(400), bounds.Width - S(24)));
            modal.Height = Math.Min(modal.Height, Math.Max(S(320), bounds.Height - S(24)));
            modal.Location = new Point(
                bounds.Left + (bounds.Width - modal.Width) / 2,
                bounds.Top + (bounds.Height - modal.Height) / 2);
            LayoutDiagnostics();
        };
        overlay.Show(this);
        overlay.BringToFront();
        try { modal.ShowDialog(overlay); }
        finally { overlay.Close(); }
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
}
