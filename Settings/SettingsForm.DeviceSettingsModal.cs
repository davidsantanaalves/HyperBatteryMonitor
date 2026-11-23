using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using HyperXBatteryTray.Devices;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm
{
    private DeviceSettingsDialog? _activeDeviceSettingsDialog;

    // Kept in the existing Battery card, below the unchanged battery statistics.
    // The physical auto-power-off setting and per-model monitor mode live in the modal.
    private void BuildDeviceSettingsRow(TableLayoutPanel batteryLayout)
    {
        TableLayoutPanel row = new()
        {
            ColumnCount = 2,
            RowCount = 3,
            Dock = DockStyle.Fill,
            Margin = ScaleUiPadding(14, 0, 14, 0),
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(154)));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(1)));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(6)));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Panel divider = new()
        {
            Dock = DockStyle.Fill,
            Tag = "device-divider",
            BackColor = EffectiveTheme == AppTheme.Dark ? DarkBorder : LightBorder,
            Margin = new Padding(0)
        };
        row.Controls.Add(divider, 0, 0);
        row.SetColumnSpan(divider, 2);

        Label caption = CreateWrappedLabel(L("DeviceSettingsDescription"), false, 8.1f);
        caption.TextAlign = ContentAlignment.MiddleLeft;
        caption.ForeColor = EffectiveTheme == AppTheme.Dark ? DarkSecondary : LightSecondary;
        caption.Margin = ScaleUiPadding(0, 0, 8, 0);
        row.Controls.Add(caption, 0, 2);

        _deviceSettingsButton = new ActionButton(_iconCache)
        {
            Text = "⚙  " + L("DeviceSettingsButton"),
            Size = ScaleUiSize(146, 34),
            MinimumSize = ScaleUiSize(138, 32),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
            Font = new Font("Segoe UI", 8.5f),
            DarkMode = EffectiveTheme == AppTheme.Dark,
            OutsideBackColor = EffectiveTheme == AppTheme.Dark
                ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251)
        };
        _deviceSettingsButton.Click += (_, _) => ShowDeviceSettingsModal();
        row.Controls.Add(_deviceSettingsButton, 1, 2);
        batteryLayout.Controls.Add(row, 0, 2);
    }

    private void ShowDeviceSettingsModal()
    {
        if (string.IsNullOrWhiteSpace(_pendingSelectedDevice))
        {
            MessageBox.Show(this, L("UnknownDeviceDescription"),
                Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string model = HyperXDeviceManager.GetSelectionModelName(_pendingSelectedDevice);
        bool eligible = HyperXDeviceManager.SupportsListenOnlyMonitoring(model);
        DeviceMonitoringMode savedMode = GetPendingDeviceMonitoringMode();
        bool modeChanged = false;

        async Task<bool> SaveAsync(DeviceMonitoringMode chosen)
        {
            // Do not save *either* modal setting if the headset rejects the
            // requested auto-power-off value. A failed HID confirmation keeps
            // the modal open so the user can retry or cancel.
            if (!await CommitAutoPowerOffAsync())
                return false;

            if (!eligible) chosen = DeviceMonitoringMode.Polling;
            Dictionary<string, DeviceMonitoringMode> before = new(
                _settings.DeviceMonitoringModes ?? new Dictionary<string, DeviceMonitoringMode>(),
                StringComparer.OrdinalIgnoreCase);
            DeviceMonitoringMode previousEffective = _settings.GetMonitoringMode(model);
            try
            {
                Dictionary<string, DeviceMonitoringMode> updated = new(before,
                    StringComparer.OrdinalIgnoreCase)
                {
                    [model] = chosen
                };
                _settings.DeviceMonitoringModes = updated;
                // Modal Save persists the device choice independently of the
                // main Settings OK/Apply buttons and any unrelated pending changes.
                new SettingsManager().Save(_settings);
                _pendingDeviceMonitoringModes[model] = chosen;
                modeChanged = previousEffective != chosen;
                return true;
            }
            catch (Exception ex)
            {
                _settings.DeviceMonitoringModes = before;
                MessageBox.Show(_activeDeviceSettingsDialog ?? (Form)this,
                    string.Format(L("SaveError"), ex.Message), Application.ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // Preserve a verified value (or an in-flight read) for this exact
        // connected preview session. RefreshAutoPowerOffState resets it when
        // device identity/model or the verified connection changes.
        if (_devicePreviewMonitor == null ||
            !ReferenceEquals(_device, _autoPowerOffIdentity) ||
            !string.Equals(_pendingSelectedDevice, _autoPowerOffModel, StringComparison.Ordinal))
            InvalidateAutoPowerOffForDeviceChange();
        using DeviceSettingsDialog dialog = new(this, eligible, savedMode, SaveAsync);
        _activeDeviceSettingsDialog = dialog;
        dialog.AutoPowerOffSelected += SelectAutoPowerOff;

        DialogResult result = DialogResult.Cancel;
        try
        {
            dialog.Shown += (_, _) => RefreshAutoPowerOffState();
            result = dialog.ShowDialog(this);
        }
        finally
        {
            // Preview reads also populate the sidebar. Do not cancel that
            // in-flight one-shot query merely because the modal was closed.
            if (_devicePreviewMonitor == null)
                CancelAutoPowerOffRead();
            _activeDeviceSettingsDialog = null;
            // Discard cancelled UI-only changes. Never write hardware on close.
            _autoPowerOffPending = _autoPowerOffCurrent;
            _autoPowerOffDirty = false;
        }

        if (result != DialogResult.OK || !modeChanged)
            return;

        // The runtime device must be reopened only when its persisted mode
        // actually changed. An unrelated device preview is restarted separately.
        DeviceMonitoringModeSaved?.Invoke(this, model);
        if (!HyperXDeviceManager.IsSameDevice(_settings.SelectedDevice, model) &&
            HyperXDeviceManager.IsSameDevice(_pendingSelectedDevice, model))
        {
            StartDevicePreviewForSelection();
        }
    }

    private sealed class DeviceSettingsDialog : Form
    {
        private readonly bool _dark;
        private readonly bool _eligible;
        private readonly Func<DeviceMonitoringMode, Task<bool>> _onSave;
        private readonly MonitoringChoiceCard _listenOnlyCard;
        private readonly MonitoringChoiceCard _pollingCard;
        private readonly ActionButton _saveButton;
        private readonly ActionButton _cancelButton;
        private bool _saving;
        private Action? _reflowLayout;

        private readonly AutoPowerOffChoiceCard[] _powerChoices;
        private readonly TableLayoutPanel _powerOptions;
        private readonly Label _powerNote;
        private readonly Label _powerUnsupported;
        private bool _powerEditable;
        public event Action<int>? AutoPowerOffSelected;
        public DeviceMonitoringMode SelectedMode { get; private set; }

        public DeviceSettingsDialog(SettingsForm owner, bool eligible,
            DeviceMonitoringMode selectedMode,
            Func<DeviceMonitoringMode, Task<bool>> onSave)
        {
            _dark = owner.EffectiveTheme == AppTheme.Dark;
            _eligible = eligible;
            _onSave = onSave;
            SelectedMode = eligible ? selectedMode : DeviceMonitoringMode.Polling;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(556, 487);
            ShowInTaskbar = false;
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9f);
            BackColor = _dark ? DarkBackground : LightBackground;
            ForeColor = _dark ? Color.WhiteSmoke : LightText;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;

            Color cardColor = _dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251);
            Color border = _dark ? DarkBorder : LightBorder;
            Color secondary = _dark ? DarkSecondary : LightSecondary;

            TableLayoutPanel root = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(16, 14, 16, 12),
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // These sizes are determined by the real text measurements at the current DPI.
            // Fixed TableLayoutPanel rows left the descriptions clipped at 150% scaling.
            for (int i = 0; i < 5; i++)
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
            root.AutoScroll = true;
            Controls.Add(root);

            Panel header = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            Label title = TextLabel(owner.L("DeviceSettingsTitle"), 14f, true, ForeColor);
            title.Dock = DockStyle.None;
            title.SetBounds(4, 0, 460, 28);
            Label subtitle = TextLabel(owner.L("DeviceSettingsSubtitle"), 8.8f, false, secondary);
            subtitle.Dock = DockStyle.None;
            subtitle.SetBounds(4, 30, 480, 23);
            Button close = new()
            {
                Text = "×",
                Font = new Font("Segoe UI", 17f),
                Size = new Size(34, 32),
                Location = new Point(490, -2),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = ForeColor,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            close.FlatAppearance.BorderSize = 0;
            close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(close);
            root.Controls.Add(header, 0, 0);

            RoundedPanel monitoring = new()
            {
                Dock = DockStyle.Fill,
                BackColor = cardColor,
                OutsideBackColor = BackColor,
                BorderColor = border,
                Margin = new Padding(0)
            };
            root.Controls.Add(monitoring, 0, 1);
            TableLayoutPanel monitorLayout = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Margin = new Padding(0),
                Padding = new Padding(14, 12, 14, 10),
                BackColor = Color.Transparent
            };
            monitorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            monitorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < 5; i++)
                monitorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 4 ? 0 : 1));
            monitoring.Controls.Add(monitorLayout);
            // Reuse HBM's existing theme-aware magnifier asset (the same one
            // used by device auto-detection), instead of a font-dependent glyph.
            TableLayoutPanel monitoringHeader = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            monitoringHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            monitoringHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            monitoringHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            PngIconControl monitoringIcon = new(owner._iconCache, "auto-detect")
            {
                Size = new Size(22, 22),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0),
                DarkMode = _dark
            };
            monitoringHeader.Controls.Add(monitoringIcon, 0, 0);
            Label monitoringTitle = TextLabel(owner.L("DeviceMonitoringMode"), 10.1f, true, ForeColor);
            monitoringTitle.Margin = new Padding(0);
            monitoringHeader.Controls.Add(monitoringTitle, 1, 0);
            monitorLayout.Controls.Add(monitoringHeader, 0, 0);
            monitorLayout.SetColumnSpan(monitoringHeader, 2);
            Label monitoringDescription = TextLabel(owner.L("DeviceMonitoringChoiceDescription"), 8.5f, false, secondary);
            monitorLayout.Controls.Add(monitoringDescription, 0, 1);
            monitorLayout.SetColumnSpan(monitoringDescription, 2);
            _listenOnlyCard = new MonitoringChoiceCard(
                DeviceMonitoringMode.ListenOnly,
                owner.L("MonitoringModeListenOnly"), owner.L("MonitoringModeListenOnlyShortDescription"), _dark)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 6, 0),
                Enabled = eligible
            };
            _pollingCard = new MonitoringChoiceCard(
                DeviceMonitoringMode.Polling,
                owner.L("MonitoringModePolling"), owner.L("MonitoringModePollingShortDescription"), _dark)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 0, 0, 0)
            };
            _listenOnlyCard.SelectedRequested += (_, _) => SetMode(DeviceMonitoringMode.ListenOnly);
            _pollingCard.SelectedRequested += (_, _) => SetMode(DeviceMonitoringMode.Polling);
            monitorLayout.Controls.Add(_listenOnlyCard, 0, 2);
            monitorLayout.Controls.Add(_pollingCard, 1, 2);
            Label note = TextLabel(owner.L(eligible
                ? "DeviceMonitoringExperimentalNote" : "MonitoringModeNotSupportedDescription"),
                7.8f, false, secondary);
            note.Margin = new Padding(0, 4, 0, 0);
            monitorLayout.Controls.Add(note, 0, 3);
            monitorLayout.SetColumnSpan(note, 2);
            SetMode(SelectedMode);

            RoundedPanel powerOff = new()
            {
                Dock = DockStyle.Fill,
                BackColor = cardColor,
                OutsideBackColor = BackColor,
                BorderColor = border,
                Margin = new Padding(0)
            };
            root.Controls.Add(powerOff, 0, 3);
            TableLayoutPanel powerLayout = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(14, 10, 14, 9),
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            powerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++)
                powerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
            powerOff.Controls.Add(powerLayout);
            Label powerTitle = TextLabel("⏻  " + owner.L("AutoPowerOff"), 10.1f, true, ForeColor);
            Label powerDescription = TextLabel(owner.L("AutoPowerOffDescription"), 8.4f, false, secondary);
            powerLayout.Controls.Add(powerTitle, 0, 0);
            powerLayout.Controls.Add(powerDescription, 0, 1);

            _powerOptions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            _powerOptions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++)
                _powerOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            string[] powerLabels =
            {
                owner.L("AutoPowerOffNever"), owner.L("AutoPowerOff10"),
                owner.L("AutoPowerOff20"), owner.L("AutoPowerOff30")
            };
            _powerChoices = new AutoPowerOffChoiceCard[4];
            int[] minutes = { 0, 10, 20, 30 };
            for (int i = 0; i < 4; i++)
            {
                int minutesForCard = minutes[i];
                AutoPowerOffChoiceCard option = new(powerLabels[i], _dark)
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(i == 0 ? 0 : 4, 1, i == 3 ? 0 : 4, 1)
                };
                option.SelectedRequested += (_, _) =>
                {
                    if (_powerEditable) AutoPowerOffSelected?.Invoke(minutesForCard);
                };
                _powerOptions.Controls.Add(option, i, 0);
                _powerChoices[i] = option;
            }
            // A panel hosts the mutually exclusive choices/message; a table
            // cell itself must contain only one control.
            Panel optionsHost = new() { Dock = DockStyle.Fill, Margin = new Padding(0),
                BackColor = Color.Transparent };
            optionsHost.Controls.Add(_powerOptions);
            _powerUnsupported = TextLabel(owner.L("AutoPowerOffNotSupportedForDevice"),
                9f, false, secondary);
            _powerUnsupported.Visible = false;
            optionsHost.Controls.Add(_powerUnsupported);
            powerLayout.Controls.Add(optionsHost, 0, 2);
            _powerNote = TextLabel(string.Empty, 8.1f, false, secondary);
            _powerNote.Margin = new Padding(0, 2, 0, 0);
            powerLayout.Controls.Add(_powerNote, 0, 3);

            TableLayoutPanel footer = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            Label footnote = TextLabel(owner.L("DeviceSettingsOnlySelected"), 8f, false, secondary);
            footnote.Margin = new Padding(3, 0, 10, 0);
            footnote.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(footnote, 0, 0);

            _cancelButton = DialogAction(owner, owner.L("Cancel"), false, _dark);
            _cancelButton.Margin = new Padding(6, 0, 6, 0);
            _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(_cancelButton, 1, 0);
            _saveButton = DialogAction(owner, owner.L("DeviceSettingsSave"), true, _dark);
            _saveButton.Margin = new Padding(6, 0, 0, 0);
            _saveButton.Click += SaveClick;
            footer.Controls.Add(_saveButton, 2, 0);
            root.Controls.Add(footer, 0, 4);

            AcceptButton = _saveButton;
            CancelButton = _cancelButton;

            bool reflowing = false;
            void ReflowForCurrentDpi()
            {
                if (reflowing || IsDisposed || Disposing) return;
                reflowing = true;
                try
                {
                    // All measurements below are device pixels. Fonts are measured using
                    // WinForms' actual rendered size; logical spacing is scaled exactly once.
                    int Px(int logical) => Math.Max(1,
                        (int)Math.Ceiling(logical * Math.Max(96, DeviceDpi) / 96f));
                    int TextHeight(Label control, int width) =>
                        TextRenderer.MeasureText(control.Text, control.Font,
                            new Size(Math.Max(1, width), 2048),
                            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + Px(4);

                    Rectangle work = Screen.FromControl(this).WorkingArea;
                    int dialogWidth = Math.Max(1, Math.Min(Px(556), work.Width - Px(24)));
                    int contentWidth = Math.Max(Px(120), dialogWidth - Px(32));
                    int contentInnerWidth = Math.Max(Px(100), contentWidth - Px(28));
                    bool narrow = contentInnerWidth < Px(460);

                    root.Padding = new Padding(Px(16), Px(14), Px(16), Px(12));
                    monitorLayout.Padding = new Padding(Px(14), Px(12), Px(14), Px(10));
                    powerLayout.Padding = new Padding(Px(14), Px(10), Px(14), Px(9));

                    int headerTextWidth = Math.Max(Px(70), contentWidth - Px(48));
                    int titleHeight = Math.Max(Px(30), TextHeight(title, headerTextWidth));
                    int subtitleHeight = Math.Max(Px(23), TextHeight(subtitle, headerTextWidth));
                    title.Bounds = new Rectangle(Px(4), Px(1), headerTextWidth, titleHeight);
                    subtitle.Bounds = new Rectangle(Px(4), titleHeight + Px(2),
                        headerTextWidth, subtitleHeight);
                    close.Bounds = new Rectangle(Math.Max(0, contentWidth - Px(36)), 0,
                        Px(34), Px(32));
                    int headerHeight = titleHeight + subtitleHeight + Px(8);

                    monitoringHeader.ColumnStyles[0].Width = Px(26);
                    monitoringIcon.Size = new Size(Px(22), Px(22));
                    int monitorHeaderHeight = Math.Max(Px(28),
                        TextHeight(monitoringTitle, contentInnerWidth - Px(26)));
                    int monitorDescriptionHeight = Math.Max(Px(23),
                        TextHeight(monitoringDescription, contentInnerWidth));
                    int warningHeight = Math.Max(Px(22),
                        TextHeight(note, contentInnerWidth) + Px(4));

                    int cardWidth = narrow ? contentInnerWidth : (contentInnerWidth - Px(12)) / 2;
                    int listenHeight = _listenOnlyCard.GetPreferredHeight(cardWidth);
                    int pollingHeight = _pollingCard.GetPreferredHeight(cardWidth);
                    int cardHeight = Math.Max(Px(96), Math.Max(listenHeight, pollingHeight));
                    monitorLayout.RowStyles[0].Height = monitorHeaderHeight;
                    monitorLayout.RowStyles[1].Height = monitorDescriptionHeight;
                    monitorLayout.RowStyles[2].Height = cardHeight;
                    monitorLayout.RowStyles[3].Height = narrow ? cardHeight + Px(8) : warningHeight;
                    monitorLayout.RowStyles[4].Height = narrow ? warningHeight : 0;
                    monitorLayout.SetCellPosition(_listenOnlyCard,
                        new TableLayoutPanelCellPosition(0, 2));
                    monitorLayout.SetCellPosition(_pollingCard,
                        new TableLayoutPanelCellPosition(narrow ? 0 : 1, narrow ? 3 : 2));
                    monitorLayout.SetCellPosition(note,
                        new TableLayoutPanelCellPosition(0, narrow ? 4 : 3));
                    monitorLayout.SetColumnSpan(_listenOnlyCard, narrow ? 2 : 1);
                    monitorLayout.SetColumnSpan(_pollingCard, narrow ? 2 : 1);
                    monitorLayout.SetColumnSpan(note, 2);
                    _listenOnlyCard.Margin = narrow ? new Padding(0, 0, 0, Px(4)) :
                        new Padding(0, 0, Px(6), 0);
                    _pollingCard.Margin = narrow ? new Padding(0, Px(4), 0, 0) :
                        new Padding(Px(6), 0, 0, 0);
                    note.Margin = new Padding(0, Px(4), 0, 0);
                    int monitoringHeight = monitorLayout.Padding.Vertical + monitorHeaderHeight +
                        monitorDescriptionHeight + (narrow ? cardHeight * 2 + Px(8) : cardHeight) +
                        warningHeight + Px(3);

                    int powerTitleHeight = Math.Max(Px(27), TextHeight(powerTitle, contentInnerWidth));
                    int powerDescriptionHeight = Math.Max(Px(23),
                        TextHeight(powerDescription, contentInnerWidth));
                    powerLayout.RowStyles[0].Height = powerTitleHeight;
                    powerLayout.RowStyles[1].Height = powerDescriptionHeight;
                    int maxOptionWidth = 0;
                    foreach (AutoPowerOffChoiceCard option in _powerChoices)
                        maxOptionWidth = Math.Max(maxOptionWidth, option.GetPreferredWidth());
                    bool stackPower = contentInnerWidth < maxOptionWidth * 4 + Px(12);
                    // Keep all four columns allocated even in the two-row layout.
                    // Reducing ColumnCount while occupied can displace controls.
                    int visiblePowerColumns = stackPower ? 2 : 4;
                    _powerOptions.RowCount = stackPower ? 2 : 1;
                    for (int i = 0; i < _powerOptions.ColumnStyles.Count; i++)
                    {
                        _powerOptions.ColumnStyles[i].SizeType = SizeType.Percent;
                        _powerOptions.ColumnStyles[i].Width = stackPower
                            ? (i < 2 ? 50f : 0f) : 25f;
                    }
                    _powerOptions.RowStyles.Clear();
                    for (int i = 0; i < _powerOptions.RowCount; i++)
                        _powerOptions.RowStyles.Add(new RowStyle(SizeType.Percent,
                            100f / _powerOptions.RowCount));
                    for (int i = 0; i < _powerChoices.Length; i++)
                    {
                        _powerOptions.SetCellPosition(_powerChoices[i],
                            new TableLayoutPanelCellPosition(i % visiblePowerColumns,
                                i / visiblePowerColumns));
                        _powerChoices[i].Margin = new Padding(i % visiblePowerColumns == 0 ? 0 : Px(4),
                            Px(1), i % visiblePowerColumns == visiblePowerColumns - 1 ? 0 : Px(4), Px(1));
                    }
                    int optionsHeight = stackPower ? Px(94) : Px(47);
                    int powerNoteHeight = _powerNote.Visible ?
                        Math.Max(Px(25), TextHeight(_powerNote, contentInnerWidth) + Px(2)) : Px(6);
                    if (!_powerOptions.Visible)
                        optionsHeight = Math.Max(Px(47),
                            TextHeight(_powerUnsupported, contentInnerWidth));
                    powerLayout.RowStyles[2].Height = optionsHeight;
                    powerLayout.RowStyles[3].Height = powerNoteHeight;
                    int powerHeight = powerLayout.Padding.Vertical + powerTitleHeight +
                        powerDescriptionHeight + optionsHeight + powerNoteHeight + Px(3);

                    int saveWidth = Math.Max(Px(92), TextRenderer.MeasureText(
                        _saveButton.Text, _saveButton.Font).Width + Px(26));
                    int cancelWidth = Math.Max(Px(92), TextRenderer.MeasureText(
                        _cancelButton.Text, _cancelButton.Font).Width + Px(26));
                    footer.ColumnStyles[1].Width = cancelWidth + Px(12);
                    footer.ColumnStyles[2].Width = saveWidth + Px(6);
                    _saveButton.Size = new Size(saveWidth, Px(35));
                    _cancelButton.Size = new Size(cancelWidth, Px(35));
                    _cancelButton.Margin = new Padding(Px(6), 0, Px(6), 0);
                    _saveButton.Margin = new Padding(Px(6), 0, 0, 0);
                    int noteWidth = contentWidth - (int)footer.ColumnStyles[1].Width -
                        (int)footer.ColumnStyles[2].Width - Px(16);
                    bool stackedFooter = noteWidth < Px(115) ||
                        TextHeight(footnote, Math.Max(Px(20), noteWidth)) > Px(43);
                    int footerHeight;
                    if (stackedFooter)
                    {
                        footer.RowStyles[0].Height = Math.Max(Px(25),
                            TextHeight(footnote, contentWidth));
                        footer.RowStyles[1].Height = Px(42);
                        footer.SetCellPosition(footnote, new TableLayoutPanelCellPosition(0, 0));
                        footer.SetColumnSpan(footnote, 3);
                        footer.SetCellPosition(_cancelButton, new TableLayoutPanelCellPosition(1, 1));
                        footer.SetCellPosition(_saveButton, new TableLayoutPanelCellPosition(2, 1));
                        footerHeight = (int)footer.RowStyles[0].Height + Px(42);
                    }
                    else
                    {
                        footer.RowStyles[0].Height = Math.Max(Px(42),
                            TextHeight(footnote, noteWidth));
                        footer.RowStyles[1].Height = 0;
                        footer.SetCellPosition(footnote, new TableLayoutPanelCellPosition(0, 0));
                        footer.SetColumnSpan(footnote, 1);
                        footer.SetCellPosition(_cancelButton, new TableLayoutPanelCellPosition(1, 0));
                        footer.SetCellPosition(_saveButton, new TableLayoutPanelCellPosition(2, 0));
                        footerHeight = (int)footer.RowStyles[0].Height;
                    }
                    int spacer = Px(10);
                    root.RowStyles[0].Height = headerHeight;
                    root.RowStyles[1].Height = monitoringHeight;
                    root.RowStyles[2].Height = spacer;
                    root.RowStyles[3].Height = powerHeight;
                    root.RowStyles[4].Height = footerHeight;

                    int desiredHeight = root.Padding.Vertical + headerHeight + monitoringHeight +
                        spacer + powerHeight + footerHeight + Px(2);
                    int dialogHeight = Math.Max(1, Math.Min(desiredHeight,
                        work.Height - Px(24)));
                    root.AutoScrollMinSize = new Size(0, desiredHeight - root.Padding.Vertical);
                    ClientSize = new Size(dialogWidth, dialogHeight);
                    // Recenter after the measured size changes, without leaving the display.
                    Rectangle parentBounds = owner.Bounds;
                    Left = Math.Clamp(parentBounds.Left + (parentBounds.Width - Width) / 2,
                        work.Left, Math.Max(work.Left, work.Right - Width));
                    Top = Math.Clamp(parentBounds.Top + (parentBounds.Height - Height) / 2,
                        work.Top, Math.Max(work.Top, work.Bottom - Height));
                    UpdateRoundedShape();
                }
                finally { reflowing = false; }
            }
            _reflowLayout = ReflowForCurrentDpi;
            Shown += (_, _) =>
            {
                ReflowForCurrentDpi();
                UpdateRoundedShape();
                if (IsHandleCreated)
                {
                    try
                    {
                        int darkAttribute = _dark ? 1 : 0;
                        _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode,
                            ref darkAttribute, sizeof(int));
                    }
                    catch { /* DWM attribute is optional. */ }
                }
            };
            Resize += (_, _) => UpdateRoundedShape();
            DpiChanged += (_, _) => BeginInvoke(new Action(ReflowForCurrentDpi));
        }

        public void UpdateAutoPowerOff(bool supported, bool editable,
            int? selectedMinutes, string note)
        {
            _powerEditable = editable;
            _powerOptions.Visible = supported;
            _powerUnsupported.Visible = !supported;
            _powerNote.Text = supported ? note : string.Empty;
            _powerNote.Visible = supported && !string.IsNullOrWhiteSpace(note);
            int[] minutes = { 0, 10, 20, 30 };
            for (int i = 0; i < _powerChoices.Length; i++)
            {
                _powerChoices[i].Selected = supported && selectedMinutes == minutes[i];
                _powerChoices[i].Enabled = supported && editable;
            }
            if (Visible) _reflowLayout?.Invoke();
        }

        private void SetMode(DeviceMonitoringMode mode)
        {
            SelectedMode = !_eligible ? DeviceMonitoringMode.Polling : mode;
            _listenOnlyCard.Selected = SelectedMode == DeviceMonitoringMode.ListenOnly;
            _pollingCard.Selected = SelectedMode == DeviceMonitoringMode.Polling;
        }

        private async void SaveClick(object? sender, EventArgs e)
        {
            if (_saving) return;
            _saving = true;
            _saveButton.Enabled = false;
            _cancelButton.Enabled = false;
            _listenOnlyCard.Enabled = false;
            _pollingCard.Enabled = false;
            foreach (AutoPowerOffChoiceCard card in _powerChoices) card.Enabled = false;
            try
            {
                if (await _onSave(SelectedMode))
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            finally
            {
                _saving = false;
                if (!IsDisposed && DialogResult != DialogResult.OK)
                {
                    _saveButton.Enabled = true;
                    _cancelButton.Enabled = true;
                    _listenOnlyCard.Enabled = _eligible;
                    _pollingCard.Enabled = true;
                    foreach (AutoPowerOffChoiceCard card in _powerChoices)
                        card.Enabled = _powerEditable;
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_saving && DialogResult != DialogResult.OK)
                e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath rounded = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2)), 12);
            using Pen pen = new(_dark ? Color.FromArgb(95, 101, 110) : LightBorder, 1f);
            e.Graphics.DrawPath(pen, rounded);
        }

        private void UpdateRoundedShape()
        {
            if (Width <= 2 || Height <= 2) return;
            using GraphicsPath rounded = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(0, 0, Width, Height), 12);
            Region? old = Region;
            Region = new Region(rounded);
            old?.Dispose();
        }

        private static ActionButton DialogAction(SettingsForm owner, string label,
            bool primary, bool dark) => new(owner._iconCache)
        {
            Text = label,
            Primary = primary,
            DarkMode = dark,
            OutsideBackColor = dark ? DarkBackground : LightBackground,
            Size = new Size(92, 35),
            Anchor = AnchorStyles.Right,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand
        };

        private static Label TextLabel(string text, float size, bool bold, Color color) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size),
            ForeColor = color,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            UseCompatibleTextRendering = false
        };
    }

    // Compact version of the existing MonitoringChoiceCard. Uses precisely the
    // same HBM rounded-path/Accent visual style with no new image dependencies.
    private sealed class AutoPowerOffChoiceCard : Control
    {
        private readonly string _caption;
        private readonly bool _dark;
        private bool _selected;
        private bool _hovered;
        public event EventHandler? SelectedRequested;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        public AutoPowerOffChoiceCard(string caption, bool dark)
        {
            _caption = caption;
            _dark = dark;
            TabStop = true;
            Cursor = Cursors.Hand;
            BackColor = dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            AccessibleRole = AccessibleRole.RadioButton;
            AccessibleName = caption;
        }

        public int GetPreferredWidth()
        {
            int px = Math.Max(1, (int)Math.Ceiling(DeviceDpi / 96f));
            using Font font = new("Segoe UI", 8.3f);
            return TextRenderer.MeasureText(_caption, font,
                new Size(4096, 4096), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width
                + (int)Math.Ceiling(42 * DeviceDpi / 96f) + px;
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e); _hovered = true; Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e); _hovered = false; Invalidate();
        }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled) SelectedRequested?.Invoke(this, EventArgs.Empty);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
            {
                SelectedRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            float penWidth = _selected ? 2f : 1f;
            float inset = penWidth / 2f + 0.25f;
            using GraphicsPath shape = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(inset, inset, Math.Max(1, Width - inset * 2),
                    Math.Max(1, Height - inset * 2)), 8);
            Color fill = _selected
                ? (_dark ? Color.FromArgb(32, 56, 81) : Color.FromArgb(231, 242, 255))
                : _dark ? (_hovered && Enabled ? Color.FromArgb(52, 57, 63) : Color.FromArgb(37, 41, 45))
                : (_hovered && Enabled ? Color.FromArgb(240, 245, 251) : Color.White);
            Color border = _selected ? Accent : (_dark ? Color.FromArgb(95, 101, 110) : LightBorder);
            if (!Enabled && !_selected)
                fill = _dark ? Color.FromArgb(36, 38, 41) : Color.FromArgb(241, 243, 246);
            using Brush brush = new SolidBrush(fill);
            using Pen outline = new(border, penWidth) { LineJoin = LineJoin.Round };
            g.FillPath(brush, shape);
            g.DrawPath(outline, shape);

            float scale = DeviceDpi / 96f;
            float cy = Height / 2f;
            using Pen radioPen = new(_selected ? Accent : Color.Gray, Math.Max(1f, 1.5f * scale));
            g.DrawEllipse(radioPen, 10 * scale, cy - 7 * scale, 14 * scale, 14 * scale);
            if (_selected)
            {
                using Brush dot = new SolidBrush(Accent);
                g.FillEllipse(dot, 14 * scale, cy - 3 * scale, 6 * scale, 6 * scale);
            }
            Color textColor = Enabled || _selected
                ? (_dark ? Color.WhiteSmoke : LightText)
                : (_dark ? Color.FromArgb(156, 159, 165) : LightSecondary);
            using Font font = new("Segoe UI", 8.3f, _selected ? FontStyle.Bold : FontStyle.Regular);
            int left = (int)Math.Ceiling(29 * scale);
            int padding = (int)Math.Ceiling(5 * scale);
            TextRenderer.DrawText(g, _caption, font,
                new Rectangle(left, padding, Math.Max(1, Width - left - padding),
                    Math.Max(1, Height - 2 * padding)),
                textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                using GraphicsPath focus = GraphicsExtensions.CreateRoundedPath(
                    new RectangleF(4, 4, Math.Max(1, Width - 8), Math.Max(1, Height - 8)), 6);
                using Pen focusPen = new(textColor, 1f) { DashStyle = DashStyle.Dot };
                g.DrawPath(focusPen, focus);
            }
        }
    }

    // A mutually exclusive, fully keyboard-accessible radio-card pair. The
    // selected card uses HBM's existing blue Accent; no red/green accents.
    private sealed class MonitoringChoiceCard : Control
    {
        private readonly DeviceMonitoringMode _mode;
        private readonly string _title;
        private readonly string _description;
        private readonly bool _dark;
        private bool _selected;
        private bool _hovered;

        public event EventHandler? SelectedRequested;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        public MonitoringChoiceCard(DeviceMonitoringMode mode,
            string title, string description, bool dark)
        {
            _mode = mode;
            _title = title;
            _description = description;
            _dark = dark;
            TabStop = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            // Match the parent RoundedPanel's surface at the outer corners.
            // WinForms' default background would otherwise leave rectangular patches.
            BackColor = _dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251);
            AccessibleRole = AccessibleRole.RadioButton;
            AccessibleName = title;
            AccessibleDescription = description;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled) SelectedRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
            {
                SelectedRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        public int GetPreferredHeight(int cardWidth)
        {
            float scale = DeviceDpi / 96f;
            int left = (int)Math.Ceiling(47 * scale);
            int right = (int)Math.Ceiling(12 * scale);
            int width = Math.Max(1, cardWidth - left - right);
            using Font titleFont = new("Segoe UI Semibold", 10f);
            using Font descFont = new("Segoe UI", 8.5f);
            int titleHeight = TextRenderer.MeasureText(_title, titleFont, new Size(width, 2048),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            int descriptionHeight = TextRenderer.MeasureText(_description, descFont,
                new Size(width, 2048), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            return (int)Math.Ceiling(21 * scale) + titleHeight + descriptionHeight +
                (int)Math.Ceiling(7 * scale);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Same background-handling technique as HBM's RoundedPanel.
            e.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            float borderWidth = _selected ? 2f : 1f;
            // As in RoundedPanel, keep the stroke completely inside the control.
            float inset = borderWidth / 2f + 0.25f;
            RectangleF rect = new(inset, inset,
                Math.Max(1f, Width - inset * 2f),
                Math.Max(1f, Height - inset * 2f));
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(rect, 10);
            Color fill = _selected
                ? (_dark ? Color.FromArgb(32, 56, 81) : Color.FromArgb(231, 242, 255))
                : _dark ? (_hovered && Enabled ? Color.FromArgb(52, 57, 63) : Color.FromArgb(37, 41, 45))
                       : (_hovered && Enabled ? Color.FromArgb(240, 245, 251) : Color.White);
            Color border = _selected ? Accent : (_dark ? Color.FromArgb(95, 101, 110) : LightBorder);
            if (!Enabled) fill = _dark ? Color.FromArgb(36, 38, 41) : Color.FromArgb(241, 243, 246);
            using SolidBrush brush = new(fill);
            using Pen outline = new(border, borderWidth) { LineJoin = LineJoin.Round };
            g.FillPath(brush, path);
            g.DrawPath(outline, path);

            float scale = DeviceDpi / 96f;
            float cx = 25f * scale;
            float cy = 28f * scale;
            using Pen radioPen = new(Enabled ? (_selected ? Accent : (_dark ? Color.LightGray : LightSecondary))
                : Color.Gray, Math.Max(1f, 1.7f * scale));
            e.Graphics.DrawEllipse(radioPen, cx - 9 * scale, cy - 9 * scale, 18 * scale, 18 * scale);
            if (_selected)
            {
                using SolidBrush dot = new(Accent);
                e.Graphics.FillEllipse(dot, cx - 4.5f * scale, cy - 4.5f * scale, 9 * scale, 9 * scale);
            }

            Color textColor = !Enabled ? (_dark ? Color.Gray : LightSecondary)
                : (_dark ? Color.WhiteSmoke : LightText);
            Color descColor = !Enabled ? (_dark ? Color.Gray : LightSecondary)
                : (_dark ? DarkSecondary : LightSecondary);
            using Font titleFont = new("Segoe UI Semibold", 10f);
            using Font descFont = new("Segoe UI", 8.5f);
            int left = (int)Math.Ceiling(47 * scale);
            int top = (int)Math.Ceiling(10 * scale);
            int width = Math.Max(1, Width - left - (int)Math.Ceiling(12 * scale));
            int titleHeight = TextRenderer.MeasureText(_title, titleFont,
                new Size(width, 2048), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            Rectangle titleRect = new(left, top, width, titleHeight + (int)Math.Ceiling(3 * scale));
            Rectangle descRect = new(left, top + titleHeight + (int)Math.Ceiling(4 * scale),
                width, Math.Max(1, Height - top - titleHeight - (int)Math.Ceiling(9 * scale)));
            TextRenderer.DrawText(e.Graphics, _title, titleFont, titleRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, _description, descFont, descRect, descColor,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                // The old rectangular focus indicator overrode the rounded visual
                // on the focused card. Preserve keyboard focus with a rounded ring.
                const float focusInset = 5f;
                RectangleF focusRect = new(focusInset, focusInset,
                    Math.Max(1f, Width - focusInset * 2f),
                    Math.Max(1f, Height - focusInset * 2f));
                using GraphicsPath focusPath = GraphicsExtensions.CreateRoundedPath(focusRect, 8);
                using Pen focusPen = new(textColor, 1f)
                {
                    DashStyle = DashStyle.Dot,
                    LineJoin = LineJoin.Round
                };
                g.DrawPath(focusPen, focusPath);
            }
        }
    }
}
