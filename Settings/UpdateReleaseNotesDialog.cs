using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm
{
    /// <summary>
    /// Theme-aware release notes modal. Shares the overlay, geometry, palette,
    /// icons and ActionButton controls with the automatic device detection UI.
    /// Only trusted release text is displayed, never an embedded web page.
    /// </summary>
    private sealed class UpdateReleaseNotesDialog : Form
    {
        private static readonly Regex MarkdownLink = new(
            @"\[([^\]\r\n]+)\]\(https?://[^)\r\n]+\)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(200));

        private readonly bool _dark;
        private readonly int _dpi;
        private readonly ThemedVerticalScrollBar _notesScrollBar;

        internal UpdateReleaseNotesDialog(
            PngIconCache iconCache, int dpi, string tag, string? releaseNotes,
            Uri releasePageUrl, AppLanguage language, bool dark)
        {
            _dark = dark;
            _dpi = Math.Max(LogicalDpi, dpi);
            string L(string key) => Localization.Get(key, language);
            Color background = dark ? DarkBackground : Color.White;
            Color foreground = dark ? Color.WhiteSmoke : LightText;
            Color accent = dark ? Color.FromArgb(95, 174, 255) : Accent;

            Text = string.Format(L("UpdatesReleaseNotesTitle"), tag);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = ScaleSize(620, 500);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            // ShowDialog(overlay) guarantees owner-relative stacking;
            // global TopMost would cover the browser opened by View on GitHub.
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = background;
            ForeColor = foreground;
            Font = new Font("Segoe UI", 9f);

            TableLayoutPanel layout = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(Scale(20), Scale(12), Scale(20), Scale(12)),
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(58)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(62)));
            Controls.Add(layout);

            Panel header = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            PngIconControl titleIcon = new(iconCache, "update")
            {
                DarkMode = dark,
                Location = new Point(0, Scale(5)),
                Size = ScaleSize(36, 36)
            };
            Label titleLabel = new()
            {
                Text = Text,
                Location = new Point(Scale(48), 0),
                Size = new Size(Math.Max(Scale(190), ClientSize.Width - Scale(140)), Scale(50)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Segoe UI Semibold", 14f),
                ForeColor = foreground,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            header.Controls.Add(titleIcon);
            header.Controls.Add(titleLabel);
            layout.Controls.Add(header, 0, 0);

            RichTextBox body = new()
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                DetectUrls = false,
                TabStop = true,
                BackColor = background,
                ForeColor = foreground,
                Font = new Font("Segoe UI", 9.5f),
                Margin = new Padding(Scale(2), Scale(2), Scale(2), Scale(9))
            };
            Panel notesHost = new()
            {
                Dock = DockStyle.Fill,
                BackColor = background,
                Margin = new Padding(0)
            };
            notesHost.Controls.Add(body);
            layout.Controls.Add(notesHost, 0, 1);
            RenderMarkdown(body,
                string.IsNullOrWhiteSpace(releaseNotes)
                    ? L("UpdatesReleaseNotesUnavailable") : releaseNotes,
                foreground, accent);
            _notesScrollBar = new ThemedVerticalScrollBar(body, dark);

            Panel footer = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            footer.Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = Scale(1),
                BackColor = dark ? DarkBorder : LightBorder
            });
            FlowLayoutPanel actions = new()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0, Scale(12), 0, 0)
            };
            footer.Controls.Add(actions);
            layout.Controls.Add(footer, 0, 2);

            ActionButton close = CreateModalAction(iconCache, L("UpdatesClose"), background, false);
            close.DialogResult = DialogResult.Cancel;
            ActionButton viewOnline = CreateModalAction(iconCache, L("UpdatesViewOnGitHub"), background, true);
            viewOnline.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(releasePageUrl.AbsoluteUri)
                    {
                        UseShellExecute = true
                    });
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            };
            actions.Controls.Add(close);
            actions.Controls.Add(viewOnline);
            AcceptButton = close;
            CancelButton = close;
        }

        private ActionButton CreateModalAction(
            PngIconCache iconCache, string text, Color background, bool primary)
        {
            using Font measureFont = new("Segoe UI", 9f);
            int textWidth = TextRenderer.MeasureText(text, measureFont).Width;
            int buttonWidth = Math.Max(Scale(114), Math.Min(Scale(250), textWidth + Scale(28)));
            return new ActionButton(iconCache)
            {
                Text = text,
                Size = new Size(buttonWidth, Scale(34)),
                Font = new Font("Segoe UI", 9f),
                DarkMode = _dark,
                Primary = primary,
                OutsideBackColor = background,
                Margin = new Padding(Scale(8), 0, 0, 0),
                TabStop = true
            };
        }

        private int Scale(int logicalValue) =>
            PngIconCache.ScaleLogicalToInt(logicalValue, _dpi);

        private Size ScaleSize(int width, int height) =>
            new(Scale(width), Scale(height));

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Width = Math.Min(Width, Math.Max(Scale(420), area.Width - Scale(24)));
            Height = Math.Min(Height, Math.Max(Scale(300), area.Height - Scale(24)));
            CenterToParent();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _notesScrollBar.RefreshState();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 0 || Height <= 0 || _dpi <= 0)
                return;
            Region?.Dispose();
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f),
                    Math.Max(1f, Height - 1f)), Scale(10));
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using Pen border = new(_dark ? DarkBorder : Color.FromArgb(210, 216, 224), 1f);
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f),
                    Math.Max(1f, Height - 1f)), Scale(10));
            e.Graphics.DrawPath(border, path);
        }

        private static void RenderMarkdown(RichTextBox box, string markdown,
            Color foreground, Color accent)
        {
            using Font normal = new("Segoe UI", 9.5f);
            using Font strong = new("Segoe UI Semibold", 9.5f);
            using Font heading = new("Segoe UI Semibold", 11f);
            using Font code = new("Consolas", 9f);

            box.SuspendLayout();
            foreach (string line in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string text = line.TrimEnd();
                bool isHeading = false;
                int hashes = 0;
                while (hashes < text.Length && hashes < 6 && text[hashes] == '#') hashes++;
                if (hashes > 0 && hashes < text.Length && text[hashes] == ' ')
                {
                    isHeading = true;
                    text = text[(hashes + 1)..];
                }
                else if (text.StartsWith("- ", StringComparison.Ordinal) ||
                         text.StartsWith("* ", StringComparison.Ordinal))
                    text = "  • " + text[2..];
                else if (text is "---" or "***")
                    text = "────────────────────";

                // Show readable labels without treating GitHub-provided links as active content.
                try { text = MarkdownLink.Replace(text, "$1"); }
                catch (RegexMatchTimeoutException) { }

                int index = 0;
                bool bold = false;
                bool mono = false;
                while (index < text.Length)
                {
                    if (index + 1 < text.Length && text.AsSpan(index).StartsWith("**"))
                    {
                        bold = !bold;
                        index += 2;
                        continue;
                    }
                    if (text[index] == '`')
                    {
                        mono = !mono;
                        index++;
                        continue;
                    }
                    int next = index + 1;
                    while (next < text.Length && text[next] != '`' &&
                           !(next + 1 < text.Length && text.AsSpan(next).StartsWith("**"))) next++;
                    box.SelectionStart = box.TextLength;
                    box.SelectionLength = 0;
                    box.SelectionFont = mono ? code : isHeading ? heading : bold ? strong : normal;
                    box.SelectionColor = isHeading ? accent : foreground;
                    box.AppendText(text[index..next]);
                    index = next;
                }
                box.SelectionStart = box.TextLength;
                box.SelectionLength = 0;
                box.SelectionFont = normal;
                box.AppendText("\n");
            }
            box.SelectionStart = 0;
            box.SelectionLength = 0;
            box.ResumeLayout();
        }

    }
}
