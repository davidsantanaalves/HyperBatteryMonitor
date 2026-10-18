using System.Drawing;
using System.Drawing.Drawing2D;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm : Form
{
    private enum AboutActionIcon { GitHub, Support, Documentation, External }


    private sealed class CustomizeDynamicIconColorsDialog : Form
    {
        // Win32 WS_EX_COMPOSITED. The modal contains several nested WinForms/native
        // child windows (including TextBox instances). Form.DoubleBuffered buffers the
        // form itself, but not the entire child-window hierarchy. Compositing the dialog
        // makes Windows present that hierarchy as a completed frame instead of exposing
        // child-by-child painting during the first display.
        private const int WsExComposited = 0x02000000;

        private const int DialogLogicalWidth = 500;
        private const int DialogInitialLogicalHeight = 560;
        private const int DialogMinimumLogicalHeight = 500;
        private const int HeaderLogicalHeight = 46;
        private const int FooterLogicalHeight = 58;
        private const int SeparatorLogicalHeight = 1;
        private const int WorkingAreaMarginLogical = 24;
        private const int ContentHorizontalPaddingLogical = 18;
        private const int ContentVerticalPaddingLogical = 10;
        private const int CardInnerPaddingLogical = 12;
        private const int SwatchColumnLogicalWidth = 54;
        private const int ThresholdColumnLogicalWidth = 116;
        private const int NumericLogicalWidth = 72;
        private const int NumericLogicalHeight = 26;
        private const int ToggleColumnLogicalWidth = 60;
        private const int ToggleLogicalWidth = 54;
        private const int ToggleLogicalHeight = 28;
        private const int PreviewLogicalHeight = 58;
        private const int IntroTextMaximumLogicalWidth = 456;
        private const int ColorTextMaximumLogicalWidth = 250;
        private const int WideTextMaximumLogicalWidth = 350;
        private const int FooterButtonLogicalHeight = 36;
        private const int FooterButtonGapLogical = 10;

        private readonly PngIconCache _iconCache;
        private readonly bool _dark;
        private readonly AppLanguage _language;
        private readonly List<BatteryColorSettings> _colors;
        private readonly List<ColorSwatchControl> _swatches = new();
        private readonly List<CriticalBatteryNumericControl> _levels = new();
        private Panel _contentHost = null!;
        private ThemedVerticalScrollBar _contentScrollBar = null!;
        private TableLayoutPanel _contentLayout = null!;
        private ToggleSwitchControl _gradientToggle = null!;
        private CriticalBatteryNumericControl _gradientStepInput = null!;
        private DynamicColorPreviewControl _preview = null!;

        public IReadOnlyList<BatteryColorSettings> BatteryColors =>
            _colors.Select(c => new BatteryColorSettings
            {
                Name = c.Name,
                MinimumPercent = c.MinimumPercent,
                Argb = c.Argb
            }).ToList();

        public bool UseGradient => _gradientToggle.Checked;
        public int GradientPercent => _gradientStepInput.Value;

        public CustomizeDynamicIconColorsDialog(
            PngIconCache iconCache,
            bool dark,
            AppLanguage language,
            IEnumerable<BatteryColorSettings>? colors,
            bool useGradient,
            int gradientPercent)
        {
            _iconCache = iconCache;
            _dark = dark;
            _language = language;
            _colors = (colors ?? Enumerable.Empty<BatteryColorSettings>())
                .OrderByDescending(c => c.MinimumPercent)
                .Select(c => new BatteryColorSettings
                {
                    Name = c.Name,
                    MinimumPercent = Math.Clamp(c.MinimumPercent, 0, 100),
                    Argb = c.Argb
                })
                .ToList();

            if (_colors.Count != 3)
                _colors.Clear();

            if (_colors.Count == 0)
            {
                _colors.Add(new BatteryColorSettings { Name = "Green", MinimumPercent = 60, Color = Color.LimeGreen });
                _colors.Add(new BatteryColorSettings { Name = "Yellow", MinimumPercent = 30, Color = Color.Gold });
                _colors.Add(new BatteryColorSettings { Name = "Red", MinimumPercent = 0, Color = Color.Red });
            }

            Text = L("CustomizeDynamicIconColors");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(DialogLogicalWidth, DialogInitialLogicalHeight);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            // ShowDialog(settingsForm) keeps this modal above its owner only.
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            AutoScaleDimensions = new SizeF(LogicalDpi, LogicalDpi);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = _dark ? DarkBackground : LightBackground;
            ForeColor = _dark ? Color.WhiteSmoke : LightText;
            Font = new Font("Segoe UI", 9f);

            BuildLayout(useGradient, gradientPercent);
            _preview.RefreshPreview();
        }

        private string L(string key) => Localization.Get(key, _language);

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExComposited;
                return parameters;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Complete the DPI-aware responsive layout before the first visible paint.
            // Running this from OnShown would expose the intermediate control layout to the user.
            FitToWorkingArea();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _contentScrollBar.RefreshState();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            if (Width <= 0 || Height <= 0)
                return;

            Region?.Dispose();
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f)),
                10);
            Region = new Region(path);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using Pen border = new(
                _dark ? Color.FromArgb(66, 71, 76) : Color.FromArgb(210, 216, 224),
                1f);
            RectangleF rect = new(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(rect, 10);
            e.Graphics.DrawPath(border, path);
        }

        private void BuildLayout(bool useGradient, int gradientPercent)
        {
            TableLayoutPanel shell = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderLogicalHeight));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, SeparatorLogicalHeight));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, SeparatorLogicalHeight));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterLogicalHeight));
            Controls.Add(shell);

            shell.Controls.Add(BuildHeader(), 0, 0);
            shell.Controls.Add(CreateSeparator(), 0, 1);

            _contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0),
                AutoScroll = true,
                BackColor = _dark ? DarkBackground : LightBackground
            };

            _contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                Margin = new Padding(0),
                Padding = new Padding(
                    ContentHorizontalPaddingLogical,
                    ContentVerticalPaddingLogical,
                    ContentHorizontalPaddingLogical,
                    ContentVerticalPaddingLogical),
                BackColor = Color.Transparent
            };
            _contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _contentHost.Controls.Add(_contentLayout);
            Panel contentWrapper = new()
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = _dark ? DarkBackground : LightBackground
            };
            contentWrapper.Controls.Add(_contentHost);
            shell.Controls.Add(contentWrapper, 0, 2);
            _contentScrollBar = new ThemedVerticalScrollBar(_contentHost, _dark);

            BuildContent(useGradient, gradientPercent);

            shell.Controls.Add(CreateSeparator(), 0, 3);
            shell.Controls.Add(BuildFooter(), 0, 4);
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(16, 0, 8, 0),
                BackColor = Color.Transparent
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            PngIconControl icon = new(_iconCache, "theme")
            {
                Anchor = AnchorStyles.Left,
                Size = new Size(27, 27),
                Margin = new Padding(0),
                DarkMode = _dark
            };
            header.Controls.Add(icon, 0, 0);

            Label title = new()
            {
                Text = L("CustomizeDynamicIconColors"),
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 12.2f),
                ForeColor = _dark ? Color.WhiteSmoke : LightText,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            header.Controls.Add(title, 1, 0);

            Button close = new()
            {
                Text = "×",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                BackColor = Color.Transparent,
                ForeColor = _dark ? Color.WhiteSmoke : LightText,
                Font = new Font("Segoe UI", 16f),
                Cursor = Cursors.Hand,
                TabStop = false,
                Margin = new Padding(0)
            };
            close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            header.Controls.Add(close, 2, 0);

            return header;
        }

        private Panel CreateSeparator()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = _dark ? Color.FromArgb(53, 58, 63) : Color.FromArgb(220, 225, 232)
            };
        }

        private void BuildContent(bool useGradient, int gradientPercent)
        {
            AddContentControl(CreateWrappedLabel(
                L("CustomizeDynamicIconColorsDescription"),
                new Font("Segoe UI", 9.7f),
                _dark ? Color.WhiteSmoke : LightText,
                new Padding(4, 0, 4, 3),
                IntroTextMaximumLogicalWidth));

            AddContentControl(CreateWrappedLabel(
                L("CustomizeDynamicIconColorsDescription2"),
                new Font("Segoe UI", 8.5f),
                _dark ? DarkSecondary : LightSecondary,
                new Padding(4, 0, 4, 10),
                IntroTextMaximumLogicalWidth));

            AddContentControl(BuildSettingsCard(useGradient, gradientPercent));
            AddContentControl(BuildPreviewCard());
        }

        private RoundedPanel BuildSettingsCard(bool useGradient, int gradientPercent)
        {
            RoundedPanel settingsCard = new()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 10),
                BorderColor = _dark ? DarkBorder : LightBorder,
                OutsideBackColor = _dark ? DarkBackground : LightBackground,
                BackColor = _dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251)
            };

            TableLayoutPanel layout = new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                Margin = new Padding(0),
                Padding = new Padding(CardInnerPaddingLogical),
                BackColor = Color.Transparent
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settingsCard.Controls.Add(layout);

            string[] titles =
            {
                L("HighBatteryColor"),
                L("MediumBatteryColor"),
                L("LowBatteryColor")
            };

            for (int i = 0; i < 3; i++)
                AddAutoSizeRow(layout, BuildColorRow(i, titles[i]));

            Panel separator = new()
            {
                Dock = DockStyle.Top,
                Height = 1,
                Margin = new Padding(0, 6, 0, 8),
                BackColor = _dark ? Color.FromArgb(68, 73, 79) : Color.FromArgb(220, 225, 232)
            };
            AddAutoSizeRow(layout, separator);

            AddAutoSizeRow(layout, BuildGradientRow(useGradient));
            AddAutoSizeRow(layout, BuildTransitionRow(gradientPercent));

            return settingsCard;
        }

        private Control BuildColorRow(int index, string titleText)
        {
            TableLayoutPanel row = new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                MinimumSize = new Size(0, 52),
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(0, 2, 0, 2),
                BackColor = Color.Transparent
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SwatchColumnLogicalWidth));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ThresholdColumnLogicalWidth));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            ColorSwatchControl swatch = new(_colors[index].Color, _dark)
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Size = new Size(48, 48),
                Margin = new Padding(0)
            };
            swatch.ColorChanged += (_, _) =>
            {
                _colors[index].Color = swatch.Color;
                _preview.RefreshPreview();
            };
            row.Controls.Add(swatch, 0, 0);
            _swatches.Add(swatch);

            TableLayoutPanel textStack = CreateTextStack(
                titleText,
                index == 2 ? L("UsedBelowThisLevel") : L("UsedFromThisLevelAndAbove"),
                9.1f,
                7.8f,
                ColorTextMaximumLogicalWidth);
            textStack.Margin = new Padding(4, 1, 8, 0);
            row.Controls.Add(textStack, 1, 0);

            TableLayoutPanel thresholdStack = new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            thresholdStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            thresholdStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            thresholdStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label levelTitle = new()
            {
                Text = L("BatteryLevel"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 7.8f),
                ForeColor = _dark ? Color.WhiteSmoke : LightText,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 2)
            };
            thresholdStack.Controls.Add(levelTitle, 0, 0);

            TableLayoutPanel inputRow = new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NumericLogicalWidth));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            CriticalBatteryNumericControl levelInput = new()
            {
                Size = new Size(NumericLogicalWidth, NumericLogicalHeight),
                Minimum = 0,
                Maximum = 100,
                Value = _colors[index].MinimumPercent,
                DarkMode = _dark,
                Margin = new Padding(0)
            };
            levelInput.ValueChanged += (_, _) =>
            {
                _colors[index].MinimumPercent = levelInput.Value;
                _preview.RefreshPreview();
            };
            inputRow.Controls.Add(levelInput, 0, 0);
            _levels.Add(levelInput);

            Label percent = new()
            {
                Text = "%",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = _dark ? Color.WhiteSmoke : LightText,
                BackColor = Color.Transparent,
                Margin = new Padding(6, 0, 0, 0)
            };
            inputRow.Controls.Add(percent, 1, 0);
            thresholdStack.Controls.Add(inputRow, 0, 1);

            row.Controls.Add(thresholdStack, 2, 0);
            return row;
        }

        private Control BuildGradientRow(bool useGradient)
        {
            TableLayoutPanel row = new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ToggleColumnLogicalWidth));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            TableLayoutPanel textStack = CreateTextStack(
                L("UseGradient"),
                L("UseGradientDescription"),
                8.9f,
                7.8f,
                WideTextMaximumLogicalWidth);
            textStack.Margin = new Padding(0, 0, 10, 0);
            row.Controls.Add(textStack, 0, 0);

            _gradientToggle = new ToggleSwitchControl
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Size = new Size(ToggleLogicalWidth, ToggleLogicalHeight),
                Checked = useGradient,
                DarkMode = _dark,
                Margin = new Padding(0, 1, 0, 0)
            };
            row.Controls.Add(_gradientToggle, 1, 0);

            return row;
        }

        private Control BuildTransitionRow(int gradientPercent)
        {
            TableLayoutPanel row = new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ThresholdColumnLogicalWidth));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            TableLayoutPanel textStack = CreateTextStack(
                L("GradientTransitionStep"),
                L("GradientTransitionStepDescription"),
                8.9f,
                7.7f,
                WideTextMaximumLogicalWidth);
            textStack.Margin = new Padding(0, 0, 10, 0);
            row.Controls.Add(textStack, 0, 0);

            TableLayoutPanel inputRow = new()
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NumericLogicalWidth));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _gradientStepInput = new CriticalBatteryNumericControl
            {
                Size = new Size(NumericLogicalWidth, NumericLogicalHeight),
                Minimum = 0,
                Maximum = 50,
                Value = Math.Clamp(gradientPercent, 0, 50),
                DarkMode = _dark,
                Margin = new Padding(0)
            };
            inputRow.Controls.Add(_gradientStepInput, 0, 0);

            Label stepPercent = new()
            {
                Text = "%",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = _dark ? Color.WhiteSmoke : LightText,
                BackColor = Color.Transparent,
                Margin = new Padding(6, 0, 0, 0)
            };
            inputRow.Controls.Add(stepPercent, 1, 0);
            row.Controls.Add(inputRow, 1, 0);

            return row;
        }

        private RoundedPanel BuildPreviewCard()
        {
            RoundedPanel previewCard = new()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                BorderColor = _dark ? DarkBorder : LightBorder,
                OutsideBackColor = _dark ? DarkBackground : LightBackground,
                BackColor = _dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251)
            };

            TableLayoutPanel layout = new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(CardInnerPaddingLogical, 8, CardInnerPaddingLogical, 8),
                BackColor = Color.Transparent
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, PreviewLogicalHeight));
            previewCard.Controls.Add(layout);

            Label previewTitle = new()
            {
                Text = L("Preview"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 8.9f),
                ForeColor = _dark ? Color.WhiteSmoke : LightText,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 3)
            };
            layout.Controls.Add(previewTitle, 0, 0);

            _preview = new DynamicColorPreviewControl(_dark, _language, _colors, _gradientToggle, _gradientStepInput)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            layout.Controls.Add(_preview, 0, 1);

            _gradientToggle.CheckedChanged += (_, _) => _preview.RefreshPreview();
            _gradientStepInput.ValueChanged += (_, _) => _preview.RefreshPreview();

            return previewCard;
        }

        private Control BuildFooter()
        {
            TableLayoutPanel footer = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(16, 10, 16, 10),
                BackColor = _dark ? DarkBackground : LightBackground
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            ActionButton reset = CreateFooterButton(L("ResetToDefaults"), primary: false, showResetIcon: true);
            reset.Anchor = AnchorStyles.Left;
            reset.Click += (_, _) =>
            {
                AppSettings defaults = AppSettings.CreateDefault();
                for (int i = 0; i < 3; i++)
                {
                    _colors[i].Name = defaults.BatteryColors[i].Name;
                    _colors[i].MinimumPercent = defaults.BatteryColors[i].MinimumPercent;
                    _colors[i].Argb = defaults.BatteryColors[i].Argb;
                    _swatches[i].Color = _colors[i].Color;
                    _levels[i].Value = _colors[i].MinimumPercent;
                }

                _gradientToggle.Checked = defaults.UseGradient;
                _gradientStepInput.Value = defaults.GradientPercent;
                _preview.RefreshPreview();
            };
            footer.Controls.Add(reset, 0, 0);

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

            ActionButton ok = CreateFooterButton(L("Ok"), primary: true);
            ok.Margin = new Padding(0, 0, FooterButtonGapLogical, 0);
            ok.Click += (_, _) =>
            {
                if (_colors[0].MinimumPercent <= _colors[1].MinimumPercent ||
                    _colors[1].MinimumPercent <= _colors[2].MinimumPercent)
                {
                    MessageBox.Show(
                        this,
                        L("ColorOrderError"),
                        L("InvalidSettings"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            };
            actions.Controls.Add(ok);

            ActionButton cancel = CreateFooterButton(L("Cancel"), primary: false);
            cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            actions.Controls.Add(cancel);

            footer.Controls.Add(actions, 1, 0);
            return footer;
        }

        private ActionButton CreateFooterButton(string text, bool primary, bool showResetIcon = false)
        {
            return new ActionButton(_iconCache)
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(showResetIcon ? 124 : 92, FooterButtonLogicalHeight),
                Padding = showResetIcon
                    ? new Padding(32, 0, 14, 0)
                    : new Padding(18, 0, 18, 0),
                Font = new Font("Segoe UI", 8.7f),
                Primary = primary,
                ShowResetIcon = showResetIcon,
                DarkMode = _dark,
                OutsideBackColor = _dark ? DarkBackground : LightBackground,
                Margin = new Padding(0)
            };
        }

        private TableLayoutPanel CreateTextStack(
            string title,
            string description,
            float titleFontSize,
            float descriptionFontSize,
            int maximumLogicalWidth)
        {
            TableLayoutPanel stack = new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label titleLabel = CreateWrappedLabel(
                title,
                new Font("Segoe UI Semibold", titleFontSize),
                _dark ? Color.WhiteSmoke : LightText,
                new Padding(0, 0, 0, 2),
                maximumLogicalWidth);
            Label descriptionLabel = CreateWrappedLabel(
                description,
                new Font("Segoe UI", descriptionFontSize),
                _dark ? DarkSecondary : LightSecondary,
                new Padding(0),
                maximumLogicalWidth);

            stack.Controls.Add(titleLabel, 0, 0);
            stack.Controls.Add(descriptionLabel, 0, 1);
            return stack;
        }

        private static Label CreateWrappedLabel(
            string text,
            Font font,
            Color color,
            Padding margin,
            int maximumLogicalWidth)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Dock = DockStyle.Fill,
                AutoEllipsis = false,
                MaximumSize = new Size(maximumLogicalWidth, 0),
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent,
                Margin = margin,
                TextAlign = ContentAlignment.TopLeft
            };
        }

        private void AddContentControl(Control control)
        {
            int row = _contentLayout.RowCount++;
            _contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _contentLayout.Controls.Add(control, 0, row);
        }

        private static void AddAutoSizeRow(TableLayoutPanel layout, Control control)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control, 0, row);
        }

        private int ScaleDialogLogical(int logicalValue) =>
            PngIconCache.ScaleLogicalToInt(logicalValue, DeviceDpi);

        private void FitToWorkingArea()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            Screen screen = Screen.FromHandle(Handle);
            Rectangle workingArea = screen.WorkingArea;
            int margin = ScaleDialogLogical(WorkingAreaMarginLogical);
            int availableWidth = Math.Max(1, workingArea.Width - margin * 2);
            int minimumWidth = Math.Min(ScaleDialogLogical(360), availableWidth);
            int targetWidth = Math.Clamp(ClientSize.Width, minimumWidth, availableWidth);

            if (ClientSize.Width != targetWidth)
                ClientSize = new Size(targetWidth, ClientSize.Height);

            PerformLayout();
            _contentHost.PerformLayout();
            _contentLayout.PerformLayout();

            int contentHeight = _contentLayout.GetPreferredSize(
                new Size(Math.Max(1, _contentHost.ClientSize.Width), 0)).Height;
            int chromeHeight = ScaleDialogLogical(
                HeaderLogicalHeight + FooterLogicalHeight + SeparatorLogicalHeight * 2);
            int desiredClientHeight = chromeHeight + contentHeight;
            int availableHeight = Math.Max(1, workingArea.Height - margin * 2);
            int minimumHeight = Math.Min(ScaleDialogLogical(DialogMinimumLogicalHeight), availableHeight);
            int targetHeight = Math.Clamp(desiredClientHeight, minimumHeight, availableHeight);

            _contentHost.AutoScroll = desiredClientHeight > availableHeight;
            ClientSize = new Size(targetWidth, targetHeight);
            _contentScrollBar.RefreshState();
            CenterToParent();

            int clampedX = Math.Clamp(Left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - Width));
            int clampedY = Math.Clamp(Top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - Height));
            Location = new Point(clampedX, clampedY);
        }
    }
}
