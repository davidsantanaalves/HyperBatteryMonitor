using HyperXBatteryTray.Updates;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm
{
    private const int AboutUpdatesCardLogicalHeight = 204;
    private LinkLabel? _updatesStatusLabel;
    private ActionButton? _updatesCheckButton;
    private ActionButton? _updatesDownloadButton;
    private ActionButton? _updatesInstallButton;
    private ActionButton? _updatesLaterButton;
    private ProgressBar? _updatesProgress;
    private FlowLayoutPanel? _updatesActionRow;
    private bool _installLaunchInProgress;

    private void BuildAboutUpdatesCard()
    {
        bool dark = EffectiveTheme == AppTheme.Dark;
        Color foreground = dark ? Color.WhiteSmoke : LightText;
        Color secondary = dark ? DarkSecondary : LightSecondary;
        bool store = _updateCoordinator?.IsStoreManaged ?? WindowsPackageDetector.IsPackaged();

        int height = store ? 96 : AboutUpdatesCardLogicalHeight;
        RoundedPanel card = CreateResponsiveCard(height);
        // Last About section: no trailing margin is needed. Removing the unused
        // 6px prevents the almost-full page from activating WinForms AutoScroll.
        AddResponsiveRow(card, height, 0);
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = ScaleUiPadding(14, 7, 14, 7),
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(30)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // PngIconCache already supports dark_update / light_update asset names.
        // Older source trees without the new files retain the previous icon.
        string themePrefix = dark ? "dark" : "light";
        string updateIconPath = AssetPaths.GetThemeIconPath(dark,
            $"{themePrefix}_update-{StandardUiIconLogicalSize}x{StandardUiIconLogicalSize}.png");
        header.Controls.Add(CreateTableIcon(File.Exists(updateIconPath) ? "update" : "notification",
            StandardUiIconLogicalSize), 0, 0);
        header.Controls.Add(CreateUpdateLabel("UpdatesTitle", 9.5f, foreground, true), 1, 0);
        AddUpdatesRow(layout, header, 24);

        if (store)
        {
            AddUpdatesRow(layout, CreateUpdateLabel("UpdatesStoreManaged", 8.8f, secondary), 44);
            return;
        }

        // The redundant UpdatesDescription is omitted to keep the entire About
        // page visible at the standard window size. Controls retain their labels.

        AddUpdatesRow(layout, CreateUpdateLabel("UpdatesChannel", 8.8f, foreground, true), 18);
        TableLayoutPanel channelRow = CreateUpdatesOptionsRow();
        RadioButton stable = CreateUpdateRadio("UpdatesStable", _pendingUpdateChannel == UpdateChannel.Stable, foreground);
        RadioButton beta = CreateUpdateRadio("UpdatesBeta", _pendingUpdateChannel == UpdateChannel.Beta, foreground);
        channelRow.Controls.Add(stable, 0, 0);
        channelRow.Controls.Add(beta, 1, 0);
        stable.CheckedChanged += (_, _) => { if (stable.Checked) { _pendingUpdateChannel = UpdateChannel.Stable; _pendingUpdateChannelUserSelected = true; RefreshUpdatesUi(); } };
        beta.CheckedChanged += (_, _) => { if (beta.Checked) { _pendingUpdateChannel = UpdateChannel.Beta; _pendingUpdateChannelUserSelected = true; RefreshUpdatesUi(); } };
        AddUpdatesRow(layout, channelRow, 24);

        AddUpdatesRow(layout, CreateUpdateLabel("UpdatesPreference", 8.8f, foreground, true), 18);
        RadioButton automatic = CreateUpdateRadio("UpdatesAutomatic", _pendingUpdateDownloadMode == UpdateDownloadMode.DownloadAutomatically, foreground);
        RadioButton notify = CreateUpdateRadio("UpdatesNotify", _pendingUpdateDownloadMode == UpdateDownloadMode.NotifyBeforeDownload, foreground);
        // Reuse the release-channel layout: both options share one radio group.
        TableLayoutPanel modeRow = CreateUpdatesOptionsRow();
        modeRow.Controls.Add(automatic, 0, 0);
        modeRow.Controls.Add(notify, 1, 0);
        automatic.CheckedChanged += (_, _) => { if (automatic.Checked) { _pendingUpdateDownloadMode = UpdateDownloadMode.DownloadAutomatically; RefreshUpdatesUi(); } };
        notify.CheckedChanged += (_, _) => { if (notify.Checked) { _pendingUpdateDownloadMode = UpdateDownloadMode.NotifyBeforeDownload; RefreshUpdatesUi(); } };
        AddUpdatesRow(layout, modeRow, 26);

        // A single LinkLabel puts the clickable segment immediately after the
        // version status, including in translated languages and test mode.
        // Only the link text is interactive; the status remains ordinary text.
        _updatesStatusLabel = new LinkLabel
        {
            Text = L("UpdatesIdle"),
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = secondary,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            LinkBehavior = LinkBehavior.AlwaysUnderline,
            LinkColor = dark ? Color.FromArgb(95, 174, 255) : Color.FromArgb(0, 102, 204),
            ActiveLinkColor = dark ? Color.FromArgb(140, 197, 255) : Color.FromArgb(0, 75, 165),
            VisitedLinkColor = dark ? Color.FromArgb(95, 174, 255) : Color.FromArgb(0, 102, 204),
            AutoEllipsis = false,
            UseCompatibleTextRendering = false
        };
        _updatesStatusLabel.Links.Clear();
        _updatesStatusLabel.LinkArea = new LinkArea(0, 0);
        _updatesStatusLabel.LinkClicked += (_, _) => ShowAvailableReleaseNotes();
        AddUpdatesRow(layout, _updatesStatusLabel, 32);
        _updatesProgress = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Maximum = 100,
            Visible = false,
            Style = ProgressBarStyle.Continuous,
            Margin = ScaleUiPadding(0, 1, 0, 1)
        };
        AddUpdatesRow(layout, _updatesProgress, 10);

        // Use the existing ActionButton control and size each action to its text,
        // instead of stretching Check for updates to 50% of the card width.
        // Invisible actions do not reserve space; available actions flow together.
        _updatesActionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(0),
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        _updatesActionRow.SizeChanged += (_, _) => ResizeUpdateActionButtons();

        _updatesCheckButton = CreateUpdateButton("UpdatesCheck", dark);
        _updatesDownloadButton = CreateUpdateButton("UpdatesDownload", dark);
        _updatesInstallButton = CreateUpdateButton("UpdatesInstall", dark);
        _updatesLaterButton = CreateUpdateButton("UpdatesLater", dark);

        _updatesActionRow.Controls.Add(_updatesCheckButton);
        _updatesActionRow.Controls.Add(_updatesDownloadButton);
        _updatesActionRow.Controls.Add(_updatesInstallButton);
        _updatesActionRow.Controls.Add(_updatesLaterButton);

        _updatesCheckButton.Click += async (_, _) =>
        {
            if (_updateCoordinator is not null)
                await _updateCoordinator.CheckAsync(true);
        };
        _updatesDownloadButton.Click += async (_, _) =>
        {
            if (_updateCoordinator is null ||
                _pendingUpdateChannel != _settings.UpdateChannel ||
                _pendingUpdateDownloadMode != _settings.UpdateDownloadMode)
                return;

            UpdateSnapshot update = _updateCoordinator.Snapshot;
            if (update.IsStableRollback)
            {
                if (update.Status != UpdateStatus.Available ||
                    MessageBox.Show(this,
                        string.Format(L("UpdatesStableRollbackDownloadConfirm"),
                            update.Version ?? string.Empty),
                        Application.ProductName, MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }
            await _updateCoordinator.DownloadAvailableAsync();
        };
        _updatesInstallButton.Click += async (_, _) => await InstallUpdateFromAboutAsync();
        _updatesLaterButton.Click += (_, _) =>
        {
            _updateCoordinator?.RemindLater();
            RefreshUpdatesUi();
        };
        AddUpdatesRow(layout, _updatesActionRow, 38);

        RefreshUpdatesUi();
    }

    private void ShowAvailableReleaseNotes()
    {
        if (_updateCoordinator?.ReleaseForSnapshot is not { } release ||
            _pendingUpdateChannel != _settings.UpdateChannel ||
            _pendingUpdateDownloadMode != _settings.UpdateDownloadMode)
            return;

        // Match the existing auto-detection modal: theme-aware dialog, rounded
        // corners and buttons, and a darkened owner background. Using the
        // overlay as modal owner keeps it above HBM, not above other apps.
        using DeviceDetectionOverlayForm overlay = new(this);
        using UpdateReleaseNotesDialog dialog = new(
            _iconCache, DeviceDpi, release.Tag, release.ReleaseNotes,
            release.ReleasePageUrl, _selectedLanguage,
            EffectiveTheme == AppTheme.Dark);
        overlay.Show(this);
        overlay.BringToFront();
        try
        {
            dialog.ShowDialog(overlay);
        }
        finally
        {
            // The modal owner chain restores HBM naturally when appropriate.
            // Do not force Activate(): a browser opened from the dialog should
            // be allowed to remain the foreground application.
            overlay.Close();
        }
    }

    private async Task InstallUpdateFromAboutAsync()
    {
        if (!ApplicationRuntime.CanInstallUpdates || _installLaunchInProgress ||
            _installDownloadedUpdateAsync is null ||
            _updateCoordinator is null || _updateCoordinator.IsStoreManaged ||
            _pendingUpdateChannel != _settings.UpdateChannel ||
            _pendingUpdateDownloadMode != _settings.UpdateDownloadMode ||
            _updateCoordinator.Snapshot.Status is not (UpdateStatus.Downloaded or UpdateStatus.InstallationFailed))
            return;

        string version = _updateCoordinator.Snapshot.Version ?? string.Empty;
        string confirmationKey = _updateCoordinator.Snapshot.IsStableRollback
            ? "UpdatesStableRollbackInstallConfirm"
            : "UpdatesInstallConfirm";
        if (MessageBox.Show(this, string.Format(L(confirmationKey), version),
                Application.ProductName, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            _updateCoordinator.RemindLater();
            RefreshUpdatesUi();
            return;
        }

        // --test --allowinstall permits a real Inno Setup run. The release
        // comes from the test feed, but Setup still uses the production AppId.
        if (ApplicationRuntime.IsTestInstallationEnabled &&
            MessageBox.Show(this,
                "This is a REAL Windows installation using an asset from the HBM TEST repository. " +
                "The installer may replace your installed Hyper Battery Monitor. " +
                "Selecting Clean Installation in Setup will delete production settings and battery history. " +
                "Back up your data before proceeding. Continue?",
                "TEST MODE - REAL INSTALLATION",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _installLaunchInProgress = true;
        RefreshUpdatesUi();
        bool started;
        try
        {
            started = await _installDownloadedUpdateAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            started = false;
        }
        if (started)
            return; // The application context has already initiated a clean exit.

        _installLaunchInProgress = false;
        if (IsDisposed || Disposing) return;
        RefreshUpdatesUi();
        MessageBox.Show(this, L("UpdatesInstallStartError"), Application.ProductName,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void AddUpdatesRow(TableLayoutPanel layout, Control child, int logicalHeight)
    {
        int index = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(logicalHeight)));
        child.Margin = new Padding(0);
        layout.Controls.Add(child, 0, index);
    }

    private static TableLayoutPanel CreateUpdatesOptionsRow()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return row;
    }

    private Label CreateUpdateLabel(string key, float size, Color color, bool strong = false) => new()
    {
        Text = L(key),
        Dock = DockStyle.Fill,
        Font = new Font(strong ? "Segoe UI Semibold" : "Segoe UI", size),
        ForeColor = color,
        BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleLeft,
        UseCompatibleTextRendering = false,
        AutoEllipsis = false
    };

    private RadioButton CreateUpdateRadio(string key, bool selected, Color color) => new()
    {
        Text = L(key),
        Checked = selected,
        Dock = DockStyle.Fill,
        AutoSize = false,
        ForeColor = color,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", 8.5f),
        TextAlign = ContentAlignment.MiddleLeft,
        UseCompatibleTextRendering = false
    };

    private ActionButton CreateUpdateButton(string key, bool dark) => new(_iconCache)
    {
        Text = L(key),
        Dock = DockStyle.None,
        Size = ScaleUiSize(160, 32),
        Margin = ScaleUiPadding(0, 2, 8, 2),
        Font = new Font("Segoe UI", 8.3f),
        DarkMode = dark,
        Primary = false,
        OutsideBackColor = dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251)
    };

    private void ResizeUpdateActionButtons()
    {
        if (_updatesActionRow is null || _updatesActionRow.IsDisposed ||
            _updatesCheckButton is null || _updatesDownloadButton is null ||
            _updatesInstallButton is null || _updatesLaterButton is null ||
            _updatesActionRow.ClientSize.Width <= 0)
            return;

        ActionButton[] buttons =
        {
            _updatesCheckButton, _updatesDownloadButton,
            _updatesInstallButton, _updatesLaterButton
        };
        ActionButton[] visible = buttons
            .Where(button => button is { IsDisposed: false, Visible: true })
            .ToArray();
        if (visible.Length == 0)
            return;

        int gap = ScaleUi(8);
        int available = Math.Max(0, _updatesActionRow.ClientSize.Width - gap * (visible.Length - 1));
        int[] widths = visible.Select(button =>
            Math.Max(ScaleUi(100), TextRenderer.MeasureText(button.Text, button.Font).Width + ScaleUi(26)))
            .ToArray();
        int total = widths.Sum();

        // If a translated label is longer than the available row, shrink all
        // visible actions proportionally; smaller page widths can still scroll.
        if (total > available && total > 0)
        {
            double ratio = available / (double)total;
            for (int i = 0; i < widths.Length; i++)
                widths[i] = Math.Max(ScaleUi(72), (int)Math.Floor(widths[i] * ratio));
        }

        for (int i = 0; i < visible.Length; i++)
        {
            visible[i].Size = new Size(widths[i], ScaleUi(32));
            visible[i].Margin = ScaleUiPadding(0, 2, i < visible.Length - 1 ? 8 : 0, 2);
        }
    }

    private void OnUpdateStatusChanged(UpdateSnapshot snapshot)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                if (!IsDisposed && !Disposing && _currentPage == "About")
                    RefreshUpdatesUi();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void RefreshUpdatesUi()
    {
        if (_updatesStatusLabel is null || _updatesStatusLabel.IsDisposed || _updateCoordinator is null ||
            _currentPage != "About")
            return;

        UpdateSnapshot snapshot = _updateCoordinator.Snapshot;
        bool pendingChanged = _pendingUpdateChannel != _settings.UpdateChannel ||
            _pendingUpdateDownloadMode != _settings.UpdateDownloadMode;
        string key = pendingChanged ? "UpdatesApplyChanges" :
            snapshot.Status == UpdateStatus.Downloaded && _updateCoordinator.IsReminderScheduled
                ? "UpdatesReminderScheduled" : snapshot.Status switch
        {
            UpdateStatus.Checking => "UpdatesChecking",
            UpdateStatus.UpToDate => "UpdatesCurrent",
            UpdateStatus.Available => "UpdatesAvailable",
            UpdateStatus.Downloading => "UpdatesDownloading",
            UpdateStatus.Downloaded => "UpdatesDownloaded",
            UpdateStatus.InstallationFailed => "UpdatesInstallFailed",
            UpdateStatus.Error => "UpdatesError",
            UpdateStatus.StoreManaged => "UpdatesStoreManaged",
            _ => "UpdatesIdle"
        };
        if (!pendingChanged && snapshot.IsStableRollback)
        {
            key = snapshot.Status switch
            {
                UpdateStatus.Available => "UpdatesStableRollbackAvailable",
                UpdateStatus.Downloaded => "UpdatesStableRollbackDownloaded",
                UpdateStatus.InstallationFailed => "UpdatesStableRollbackInstallFailed",
                _ => key
            };
        }
        string statusText = ApplicationRuntime.TestIndicator +
            string.Format(L(key), snapshot.Version ?? string.Empty, snapshot.Percent);
        bool hasNotesLink = !pendingChanged &&
            (snapshot.Status is UpdateStatus.Available or UpdateStatus.Downloading or
                UpdateStatus.Downloaded or UpdateStatus.InstallationFailed) &&
            _updateCoordinator.ReleaseForSnapshot is not null;
        _updatesStatusLabel.Links.Clear();
        if (hasNotesLink)
        {
            // Remove sentence-ending punctuation before the inline link:
            // "Version ... is available (See what's new)".
            string inlineStatus = statusText.TrimEnd().TrimEnd('.', '。', '!', '！');
            string whatsNew = L("UpdatesWhatsNew");
            _updatesStatusLabel.Text = inlineStatus + " (" + whatsNew + ")";
            _updatesStatusLabel.Links.Add(inlineStatus.Length + 2, whatsNew.Length);
        }
        else
        {
            _updatesStatusLabel.Text = statusText;
            _updatesStatusLabel.LinkArea = new LinkArea(0, 0);
        }
        bool busy = snapshot.Status is UpdateStatus.Checking or UpdateStatus.Downloading;
        if (_updatesCheckButton is { IsDisposed: false })
            _updatesCheckButton.Enabled = !busy && !pendingChanged && !_installLaunchInProgress;
        if (_updatesDownloadButton is { IsDisposed: false })
        {
            _updatesDownloadButton.Visible = snapshot.Status == UpdateStatus.Available;
            _updatesDownloadButton.Text = L(snapshot.IsStableRollback
                ? "UpdatesStableRollbackDownload" : "UpdatesDownload");
            _updatesDownloadButton.Enabled = !pendingChanged && !_installLaunchInProgress;
        }
        if (_updatesProgress is { IsDisposed: false })
        {
            _updatesProgress.Visible = snapshot.Status == UpdateStatus.Downloading;
            _updatesProgress.Value = Math.Clamp(snapshot.Percent, 0, 100);
        }
        if (_updatesInstallButton is { IsDisposed: false })
        {
            _updatesInstallButton.Visible = snapshot.Status is UpdateStatus.Downloaded or UpdateStatus.InstallationFailed;
            _updatesInstallButton.Enabled = ApplicationRuntime.CanInstallUpdates &&
                !pendingChanged && !_installLaunchInProgress &&
                _installDownloadedUpdateAsync is not null;
        }
        if (_updatesLaterButton is { IsDisposed: false })
        {
            _updatesLaterButton.Visible = snapshot.Status is UpdateStatus.Downloaded or UpdateStatus.InstallationFailed;
            _updatesLaterButton.Enabled = !pendingChanged && !_installLaunchInProgress;
        }
        ResizeUpdateActionButtons();
    }
}
