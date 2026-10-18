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
        TableLayoutPanel identityLayout = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0), BackColor = Color.Transparent };
        identityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(78)));
        identityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
