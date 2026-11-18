using HyperXBatteryTray.Updates;
using HyperXBatteryTray.Diagnostics;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm
{
    private const int AboutUpdatesCardLogicalHeight = 204;
    private Label? _updatesStatusLabel;
    private LinkLabel? _updatesNotesLink;
    private PngIconControl? _updatesStateIcon;
    private RoundedPanel? _updatesStateBanner;
    private ActionButton? _updatesCheckButton;
    private ActionButton? _updatesDownloadButton;
    private ActionButton? _updatesInstallButton;
    private ActionButton? _updatesLaterButton;
    private ProgressBar? _updatesProgress;
    private Panel? _updatesButtonsPanel;
    private bool _updatesShowNotes;
    private bool _updatesShowProgress;
    private bool _installLaunchInProgress;

    private void BuildAboutUpdatesCard()
    {
        bool dark = EffectiveTheme == AppTheme.Dark;
        Color foreground = dark ? Color.WhiteSmoke : LightText;
        Color secondary = dark ? DarkSecondary : LightSecondary;
        bool store = _updateCoordinator?.IsStoreManaged ?? WindowsPackageDetector.IsPackaged();

        int height = store ? 96 : AboutUpdatesCardLogicalHeight;
        RoundedPanel card = CreateResponsiveCard(height);
        AddResponsiveRow(card, height, 0);
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1,
            Padding = ScaleUiPadding(12, 6, 12, 6),
            BackColor = Color.Transparent, Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(30)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(CreateTableIcon("update", StandardUiIconLogicalSize), 0, 0);
        header.Controls.Add(CreateUpdateLabel("UpdatesTitle", 9.5f, foreground, true), 1, 0);
        AddUpdatesRow(layout, header, 24);

        if (store)
        {
            AddUpdatesRow(layout, CreateUpdateLabel("UpdatesStoreManaged", 8.8f, secondary), 44);
            return;
        }

        // Compact rows prevent the original 760 x 520 Settings window from growing.
        // Long option descriptions remain available in accessible tooltips.
        TableLayoutPanel channel = CreateCompactUpdatesOptionsRow("UpdatesChannel", foreground);
        RadioButton stable = CreateUpdateRadio("UpdatesStable", _pendingUpdateChannel == UpdateChannel.Stable, foreground);
        RadioButton beta = CreateUpdateRadio("UpdatesBeta", _pendingUpdateChannel == UpdateChannel.Beta, foreground);
        channel.Controls.Add(stable, 1, 0);
        channel.Controls.Add(beta, 2, 0);
        stable.CheckedChanged += (_, _) =>
        {
            if (!stable.Checked) return;
            _pendingUpdateChannel = UpdateChannel.Stable;
            _pendingUpdateChannelUserSelected = true;
            RefreshUpdatesUi();
        };
        beta.CheckedChanged += (_, _) =>
        {
            if (!beta.Checked) return;
            _pendingUpdateChannel = UpdateChannel.Beta;
            _pendingUpdateChannelUserSelected = true;
            RefreshUpdatesUi();
        };
        AddUpdatesRow(layout, channel, 29);

        TableLayoutPanel preference = CreateCompactUpdatesOptionsRow("UpdatesPreference", foreground);
        RadioButton automatic = CreateUpdateRadio("UpdatesAutomaticShort",
            _pendingUpdateDownloadMode == UpdateDownloadMode.DownloadAutomatically, foreground);
        automatic.AccessibleDescription = L("UpdatesAutomatic");
        RadioButton notify = CreateUpdateRadio("UpdatesNotifyShort",
            _pendingUpdateDownloadMode == UpdateDownloadMode.NotifyBeforeDownload, foreground);
        notify.AccessibleDescription = L("UpdatesNotify");
        preference.Controls.Add(automatic, 1, 0);
        preference.Controls.Add(notify, 2, 0);
        automatic.CheckedChanged += (_, _) =>
        {
            if (automatic.Checked)
            {
                _pendingUpdateDownloadMode = UpdateDownloadMode.DownloadAutomatically;
                RefreshUpdatesUi();
            }
        };
        notify.CheckedChanged += (_, _) =>
        {
            if (notify.Checked)
            {
                _pendingUpdateDownloadMode = UpdateDownloadMode.NotifyBeforeDownload;
                RefreshUpdatesUi();
            }
        };
        AddUpdatesRow(layout, preference, 32);

        _updatesStateBanner = new RoundedPanel
        {
            Dock = DockStyle.Fill, Margin = ScaleUiPadding(0, 2, 0, 2),
            OutsideBackColor = dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251),
            BackColor = dark ? Color.FromArgb(43, 48, 54) : Color.FromArgb(240, 244, 248),
            BorderColor = dark ? DarkBorder : LightBorder
        };
        // Position banner contents explicitly inside its fixed-height row.
        // Nested TableLayoutPanels here previously measured a taller preferred
        // height and displaced the icon below the banner on 100% DPI.
        _updatesStateIcon = CreateTableIcon("update", LargeUiIconLogicalSize);
        _updatesStateIcon.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _updatesStateIcon.Margin = new Padding(0);
        _updatesStateBanner.Controls.Add(_updatesStateIcon);

        _updatesStatusLabel = CreateUpdateLabel("UpdatesIdle", 8.2f, foreground, true);
        _updatesStatusLabel.Dock = DockStyle.None;
        _updatesStatusLabel.AutoEllipsis = true;
        _updatesStateBanner.Controls.Add(_updatesStatusLabel);

        _updatesNotesLink = new LinkLabel
        {
            Text = L("UpdatesWhatsNew"), Dock = DockStyle.None,
            Font = new Font("Segoe UI", 8.3f), BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            LinkBehavior = LinkBehavior.AlwaysUnderline,
            LinkColor = dark ? Color.FromArgb(95, 174, 255) : Color.FromArgb(0, 102, 204),
            ActiveLinkColor = dark ? Color.FromArgb(140, 197, 255) : Color.FromArgb(0, 75, 165),
            VisitedLinkColor = dark ? Color.FromArgb(95, 174, 255) : Color.FromArgb(0, 102, 204),
            Visible = false
        };
        _updatesNotesLink.LinkClicked += (_, _) => ShowAvailableReleaseNotes();
        _updatesStateBanner.Controls.Add(_updatesNotesLink);
        _updatesProgress = new ProgressBar
        {
            Dock = DockStyle.None, Maximum = 100, Visible = false,
            Style = ProgressBarStyle.Continuous, Margin = new Padding(0)
        };
        _updatesStateBanner.Controls.Add(_updatesProgress);
        _updatesStateBanner.SizeChanged += (_, _) => LayoutUpdatesBanner();
        AddUpdatesRow(layout, _updatesStateBanner, 59);

        // Owner-drawn ActionButtons must not be Dock.Fill in nested tables:
        // their preferred height could exceed the 40px row and clip all text.
        _updatesButtonsPanel = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent,
            Margin = new Padding(0), Padding = new Padding(0)
        };
        _updatesCheckButton = CreateUpdateButton("UpdatesCheck", dark);
        _updatesCheckButton.IconKey = "update";
        _updatesCheckButton.IconLogicalSize = StandardUiIconLogicalSize;
        _updatesDownloadButton = CreateUpdateButton("UpdatesDownload", dark);
        _updatesInstallButton = CreateUpdateButton("UpdatesInstall", dark);
        _updatesDownloadButton.IconKey = "install";
        _updatesInstallButton.IconKey = "install";
        _updatesDownloadButton.Primary = true;
        _updatesInstallButton.Primary = true;
        _updatesLaterButton = CreateUpdateButton("UpdatesLater", dark);
        _updatesLaterButton.IconKey = "clock";

        foreach (ActionButton button in new ActionButton[]
        {
            _updatesCheckButton!, _updatesDownloadButton!, _updatesInstallButton!, _updatesLaterButton!
        })
        {
            button.Dock = DockStyle.None;
            button.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            button.Margin = new Padding(0);
            _updatesButtonsPanel.Controls.Add(button);
        }
        _updatesButtonsPanel.SizeChanged += (_, _) => LayoutUpdatesButtons();
        AddUpdatesRow(layout, _updatesButtonsPanel, 40);

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
        RefreshUpdatesUi();
    }

    // Explicit geometry prevents status icons and owner-drawn buttons from
    // being placed outside the fixed 204 logical-pixel updates card.
    private void LayoutUpdatesBanner()
    {
        if (_updatesStateBanner is not { IsDisposed: false } host ||
            _updatesStateIcon is null || _updatesStatusLabel is null ||
            _updatesNotesLink is null || _updatesProgress is null)
            return;

        int size = ScaleUi(LargeUiIconLogicalSize);
        int left = ScaleUi(10);
        int textLeft = left + size + ScaleUi(12);
        int textWidth = Math.Max(1, host.ClientSize.Width - textLeft - ScaleUi(12));
        int height = host.ClientSize.Height;
        if (height <= 0) return;

        _updatesStateIcon.Bounds = new Rectangle(left,
            Math.Max(0, (height - size) / 2), size, size);

        bool notes = _updatesShowNotes;
        bool progress = _updatesShowProgress;
        int statusHeight = notes || progress ? ScaleUi(21) : ScaleUi(36);
        int statusTop = notes || progress ? ScaleUi(3) :
            Math.Max(0, (height - statusHeight) / 2);
        _updatesStatusLabel.Bounds = new Rectangle(textLeft, statusTop,
            textWidth, statusHeight);
        _updatesNotesLink.Bounds = new Rectangle(textLeft, ScaleUi(25),
            textWidth, ScaleUi(17));
        _updatesProgress.Bounds = new Rectangle(textLeft,
            Math.Max(0, height - ScaleUi(9)), textWidth, ScaleUi(5));
    }

    private void LayoutUpdatesButtons()
    {
        if (_updatesButtonsPanel is not { IsDisposed: false } host ||
            _updatesCheckButton is null || _updatesDownloadButton is null ||
            _updatesInstallButton is null || _updatesLaterButton is null)
            return;

        int width = host.ClientSize.Width;
        int availableHeight = host.ClientSize.Height;
        if (width <= 0 || availableHeight <= 0) return;

        int buttonHeight = Math.Min(ScaleUi(32), Math.Max(1, availableHeight - ScaleUi(4)));
        int top = (availableHeight - buttonHeight) / 2;
        int gap = ScaleUi(8);
        UpdateStatus status = _updateCoordinator?.Snapshot.Status ?? UpdateStatus.Idle;
        bool mainVisible = status is UpdateStatus.Available or UpdateStatus.Downloaded or
            UpdateStatus.InstallationFailed;
        bool laterVisible = status is UpdateStatus.Downloaded or UpdateStatus.InstallationFailed;

        if (!mainVisible && !laterVisible)
        {
            // The single Check button is not stretched to the full card width.
            int textWidth = TextRenderer.MeasureText(_updatesCheckButton.Text,
                _updatesCheckButton.Font).Width;
            int checkWidth = Math.Min(width, Math.Max(ScaleUi(145),
                textWidth + ScaleUi(52)));
            _updatesCheckButton.Bounds = new Rectangle(0, top, checkWidth, buttonHeight);
            return;
        }

        // Two/three independently positioned actions: check on the left,
        // download/install in the middle and reminder on the right.
        int leftWidth = (int)Math.Round(width * (laterVisible ? 0.38 : 0.50));
        int middleWidth = (int)Math.Round(width * (laterVisible ? 0.32 : 0.50));
        int rightWidth = Math.Max(0, width - leftWidth - middleWidth);
        _updatesCheckButton.Bounds = new Rectangle(0, top,
            Math.Max(1, leftWidth - gap), buttonHeight);
        Rectangle mainBounds = new(leftWidth, top,
            Math.Max(1, middleWidth - (laterVisible ? gap : 0)), buttonHeight);
        _updatesDownloadButton.Bounds = mainBounds;
        _updatesInstallButton.Bounds = mainBounds;
        _updatesLaterButton.Bounds = new Rectangle(leftWidth + middleWidth, top,
            Math.Max(1, rightWidth), buttonHeight);
    }

    private TableLayoutPanel CreateCompactUpdatesOptionsRow(string labelKey, Color foreground)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = Color.Transparent, Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(135)));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.Controls.Add(CreateUpdateLabel(labelKey, 8.2f, foreground, true), 0, 0);
        return row;
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
        Font = new Font("Segoe UI", 8.1f),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
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
        if (_updatesStatusLabel is null || _updatesStatusLabel.IsDisposed ||
            _updateCoordinator is null || _currentPage != "About")
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

        _updatesStatusLabel.Text = ApplicationRuntime.TestIndicator +
            string.Format(L(key), snapshot.Version ?? string.Empty, snapshot.Percent);
        _updatesStatusLabel.AccessibleDescription = _updatesStatusLabel.Text;
        bool hasNotes = !pendingChanged &&
            (snapshot.Status is UpdateStatus.Available or UpdateStatus.Downloading or
                UpdateStatus.Downloaded or UpdateStatus.InstallationFailed) &&
            _updateCoordinator.ReleaseForSnapshot is not null;
        _updatesShowNotes = hasNotes;
        if (_updatesNotesLink is { IsDisposed: false })
            _updatesNotesLink.Visible = hasNotes;

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
        _updatesShowProgress = snapshot.Status == UpdateStatus.Downloading;
        if (_updatesProgress is { IsDisposed: false })
        {
            _updatesProgress.Visible = _updatesShowProgress;
            _updatesProgress.Value = Math.Clamp(snapshot.Percent, 0, 100);
        }
        if (_updatesInstallButton is { IsDisposed: false })
        {
            _updatesInstallButton.Visible = snapshot.Status is UpdateStatus.Downloaded or
                UpdateStatus.InstallationFailed;
            _updatesInstallButton.Enabled = ApplicationRuntime.CanInstallUpdates &&
                !pendingChanged && !_installLaunchInProgress &&
                _installDownloadedUpdateAsync is not null;
        }
        if (_updatesLaterButton is { IsDisposed: false })
        {
            _updatesLaterButton.Visible = snapshot.Status is UpdateStatus.Downloaded or
                UpdateStatus.InstallationFailed;
            _updatesLaterButton.Enabled = !pendingChanged && !_installLaunchInProgress;
        }

        bool good = !pendingChanged && snapshot.Status == UpdateStatus.Downloaded;
        bool problem = !pendingChanged && snapshot.Status is (UpdateStatus.Error or
            UpdateStatus.InstallationFailed);
        bool action = !pendingChanged && snapshot.Status is (UpdateStatus.Available or
            UpdateStatus.Downloading);
        if (_updatesStateIcon is { IsDisposed: false })
            _updatesStateIcon.IconKey = pendingChanged ? "update" : snapshot.Status switch
            {
                UpdateStatus.Available => "download_available",
                UpdateStatus.Downloading => "downloading",
                UpdateStatus.UpToDate => "install",
                UpdateStatus.Downloaded => "downloaded",
                UpdateStatus.InstallationFailed or UpdateStatus.Error => "download_error",
                _ => "update"
            };
        if (_updatesStateBanner is { IsDisposed: false })
        {
            bool dark = EffectiveTheme == AppTheme.Dark;
            _updatesStateBanner.BackColor = good
                ? (dark ? Color.FromArgb(29, 67, 48) : Color.FromArgb(226, 248, 235))
                : problem ? (dark ? Color.FromArgb(78, 41, 44) : Color.FromArgb(255, 238, 239))
                : action ? (dark ? Color.FromArgb(34, 60, 83) : Color.FromArgb(230, 243, 255))
                : (dark ? Color.FromArgb(43, 48, 54) : Color.FromArgb(240, 244, 248));
            _updatesStateBanner.BorderColor = good
                ? (dark ? Color.FromArgb(49, 129, 80) : Color.FromArgb(118, 193, 148))
                : problem ? (dark ? Color.FromArgb(181, 75, 80) : Color.FromArgb(223, 132, 136))
                : action ? (dark ? Color.FromArgb(55, 137, 214) : Color.FromArgb(111, 176, 232))
                : (dark ? DarkBorder : LightBorder);
            _updatesStateBanner.Invalidate();
        }
        LayoutUpdatesBanner();
        LayoutUpdatesButtons();
    }
}
