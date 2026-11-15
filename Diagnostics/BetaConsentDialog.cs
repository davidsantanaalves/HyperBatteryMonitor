using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using HyperXBatteryTray.Settings;

namespace HyperXBatteryTray.Diagnostics;

/// <summary>Explicit, localized opt-in shown only by compiled Beta builds.</summary>
internal sealed class BetaConsentDialog : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    internal BetaConsentDialog(AppLanguage language, AppTheme theme)
    {
        string T(string key) => Localization.Get(key, language);
        bool dark = IsDarkTheme(theme);
        Color background = dark ? Color.FromArgb(31, 34, 37) : Color.FromArgb(250, 251, 253);
        Color foreground = dark ? Color.WhiteSmoke : Color.FromArgb(24, 32, 45);

        Text = T("BetaDiagnosticsConsentTitle");
        // Startup has no owner; CenterParent would leave the window near the top-left.
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(630, 390);
        MinimumSize = new Size(470, 350);
        Font = new Font("Segoe UI", 10f);
        BackColor = background;
        ForeColor = foreground;

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Padding = new Padding(20, 16, 20, 14), BackColor = background
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(layout);

        Label title = new()
        {
            Text = T("BetaDiagnosticsConsentTitle"),
            Dock = DockStyle.Fill, AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = foreground, BackColor = background,
            Margin = new Padding(0, 0, 0, 12)
        };
        layout.Controls.Add(title, 0, 0);

        // RichTextBoxScrollBars.Vertical is conditional: unlike the original TextBox
        // scrollbar, it is only visible when the localized text actually overflows.
        RichTextBox description = new()
        {
            Text = T("BetaDiagnosticsConsentBody") + Environment.NewLine + Environment.NewLine +
                T("BetaDiagnosticsCloudSyncNotice"),
            ReadOnly = true, Multiline = true, ScrollBars = RichTextBoxScrollBars.Vertical,
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            TabStop = false, DetectUrls = false, ShortcutsEnabled = true,
            BackColor = background, ForeColor = foreground,
            Margin = new Padding(0)
        };
        layout.Controls.Add(description, 0, 1);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 8, 0, 0),
            BackColor = background
        };

        Button CreateButton(string text, string symbol, DialogResult result, bool primary)
        {
            // Measure the full localized label, including its left-side glyph.
            string caption = symbol + "  " + text;
            int textWidth = TextRenderer.MeasureText(caption, Font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine).Width;
            RoundedConsentButton button = new(dark, primary, background)
            {
                Text = caption,
                Size = new Size(Math.Max(175, textWidth + 36), 36),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                MinimumSize = new Size(Math.Max(175, textWidth + 36), 36),
                DialogResult = result,
                Font = Font,
                Margin = new Padding(8, 0, 0, 0)
            };
            return button;
        }

        Button agree = CreateButton(T("BetaDiagnosticsAccept"), "✓", DialogResult.OK, true);
        Button decline = CreateButton(T("BetaDiagnosticsDecline"), "✕", DialogResult.Cancel, false);
        buttons.Controls.Add(agree);
        buttons.Controls.Add(decline);
        layout.Controls.Add(buttons, 0, 2);

        // If a localization needs more width, expand the dialog before display.
        int requiredClientWidth = layout.Padding.Horizontal + agree.Width + decline.Width +
            agree.Margin.Horizontal + decline.Margin.Horizontal + buttons.Padding.Horizontal;
        if (ClientSize.Width < requiredClientWidth)
            ClientSize = new Size(requiredClientWidth, ClientSize.Height);

        AcceptButton = agree;
        CancelButton = decline;
        Shown += (_, _) =>
        {
            // When opened from About, center over Settings; startup remains screen-centered.
            if (Owner is not null)
                CenterToParent();

            if (IsHandleCreated)
            {
                try
                {
                    int darkMode = dark ? 1 : 0;
                    _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode,
                        ref darkMode, sizeof(int));
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }
            description.SelectionStart = 0;
            description.SelectionLength = 0;
            description.ScrollToCaret();
            agree.Focus();
        };
    }


    // Match SettingsForm.ActionButton: 7px radius, flat fill, subtle secondary
    // border and hover/pressed states, without changing dialog result handling.
    private sealed class RoundedConsentButton : Button
    {
        private readonly bool _dark;
        private readonly bool _primary;
        private readonly Color _outsideColor;
        private bool _hover;
        private bool _pressed;

        internal RoundedConsentButton(bool dark, bool primary, Color outsideColor)
        {
            _dark = dark;
            _primary = primary;
            _outsideColor = outsideColor;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = outsideColor;
            ForeColor = primary ? Color.White : dark ? Color.WhiteSmoke : Color.FromArgb(24, 32, 45);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) =>
            e.Graphics.Clear(_outsideColor);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(_outsideColor);

            Color fill = _primary ? Color.FromArgb(0, 122, 255) :
                (_dark ? Color.FromArgb(39, 43, 47) : Color.White);
            if (_hover && !_primary)
                fill = _dark ? Color.FromArgb(48, 53, 58) : Color.FromArgb(247, 249, 252);
            if (_pressed)
                fill = _primary ? Color.FromArgb(0, 105, 220) :
                    (_dark ? Color.FromArgb(32, 36, 40) : Color.FromArgb(239, 243, 248));

            Color border = _dark ? Color.FromArgb(82, 88, 95) : Color.FromArgb(198, 205, 214);
            RectangleF rect = new(1f, 1f, Math.Max(1f, Width - 2f), Math.Max(1f, Height - 2f));
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(rect, 7);
            using Brush brush = new SolidBrush(fill);
            using Pen pen = new(border, 1f) { LineJoin = LineJoin.Round };
            e.Graphics.FillPath(brush, path);
            if (!_primary)
                e.Graphics.DrawPath(pen, path);

            TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Round(rect),
                ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    private static bool IsDarkTheme(AppTheme theme)
    {
        if (theme != AppTheme.System)
            return theme == AppTheme.Dark;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}
