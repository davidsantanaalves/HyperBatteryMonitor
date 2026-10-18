using HyperXBatteryTray;
using System.Drawing;
using System.Drawing.Drawing2D;
using HyperXBatteryTray.Devices;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm : Form
{
    // The device search may use the full time limit; fast success still remains visible
    // long enough for users to register that automatic detection actually ran.
    private static readonly TimeSpan AutoDetectionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AutoDetectionMinimumVisibleTime = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan AutoDetectionRetryDelay = TimeSpan.FromMilliseconds(500);
    private const double AutoDetectionOverlayOpacity = 0.72;
    private const string DetectingImageFileName = "detecting.png";
    private const string NoDeviceImageFileName = "detecting-error.png";
    private const string UnsupportedReceiverImageFileName = "detecting-unsupported.png";

    private sealed record DeviceAutoDetectionResult(
        IReadOnlyList<string> DetectedDevices,
        bool UnsupportedThreeInOneReceiverDetected);

    private ActionButton? _autoDetectButton;
    private PngIconControl? _batteryCapabilityIcon;
    private PngIconControl? _chargingCapabilityIcon;
    private PngIconControl? _microphoneCapabilityIcon;
    private ToolTip? _deviceAutoDetectToolTip;
    private bool _deviceAutoDetectionInProgress;

    private void BuildDeviceSelectorCard(RoundedPanel selectorCard)
    {
        if (_autoDetectButton != null)
            _deviceAutoDetectToolTip?.SetToolTip(_autoDetectButton, string.Empty);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = ScaleUiPadding(12, 6, 12, 6),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(92)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(62)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(56)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        selectorCard.Controls.Add(layout);

        Label selectorLabel = CreateWrappedLabel(L("DeviceLabelShort"), true, 9.5f);
        selectorLabel.Dock = DockStyle.Fill;
        selectorLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(selectorLabel, 0, 0);

        _deviceSelector = new DeviceSelector
        {
            Dock = DockStyle.Fill,
            Margin = ScaleUiPadding(0, 0, 8, 0),
            SelectedIndex = 0,
            DarkMode = EffectiveTheme == AppTheme.Dark
        };
        layout.Controls.Add(_deviceSelector, 1, 0);

        bool dark = EffectiveTheme == AppTheme.Dark;
        _autoDetectButton = new ActionButton(_iconCache)
        {
            Dock = DockStyle.None,
            Anchor = AnchorStyles.None,
            Size = ScaleUiSize(54, 54),
            Margin = new Padding(0),
            Text = string.Empty,
            IconKey = "auto-detect",
            IconLogicalSize = 36,
            IconOnly = true,
            DarkMode = dark,
            Primary = false,
            OutsideBackColor = dark
                ? Color.FromArgb(42, 45, 48)
                : Color.FromArgb(248, 249, 251),
            AccessibleName = L("AutoDetectTooltip"),
            TabStop = true
        };
        _autoDetectButton.Click += AutoDetectButton_Click;
        layout.Controls.Add(_autoDetectButton, 2, 0);

        _deviceAutoDetectToolTip ??= new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 450,
            ReshowDelay = 100,
            AutoPopDelay = 5000
        };
        _deviceAutoDetectToolTip.SetToolTip(_autoDetectButton, L("AutoDetectTooltip"));

        TableLayoutPanel capabilities = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = ScaleUiPadding(0, 6, 0, 0),
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        capabilities.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(20)));
        capabilities.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label capabilitiesTitle = CreateWrappedLabel(L("SupportedFeatures"), true, 8.7f);
        capabilitiesTitle.Dock = DockStyle.Fill;
        capabilitiesTitle.TextAlign = ContentAlignment.MiddleLeft;
        capabilities.Controls.Add(capabilitiesTitle, 0, 0);

        TableLayoutPanel capabilityRow = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        capabilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        capabilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        capabilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        capabilityRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        capabilityRow.Controls.Add(
            CreateCapabilityItem(L("BatteryMonitoringFeature"), out _batteryCapabilityIcon),
            0,
            0);
        capabilityRow.Controls.Add(
            CreateCapabilityItem(L("ChargingStatusFeature"), out _chargingCapabilityIcon),
            1,
            0);
        capabilityRow.Controls.Add(
            CreateCapabilityItem(L("MicrophoneStatusFeature"), out _microphoneCapabilityIcon),
            2,
            0);

        capabilities.Controls.Add(capabilityRow, 0, 1);
        layout.Controls.Add(capabilities, 0, 1);
        layout.SetColumnSpan(capabilities, 3);

        _deviceSelector.SetLanguage(_selectedLanguage);
        _deviceSelector.SetPlaceholder(L("LocateDevice"));
        _deviceSelector.SelectedDeviceName = _pendingSelectedDevice;
        _deviceSelector.SelectionChanged += DeviceSelector_SelectionChanged;
    }

    private TableLayoutPanel CreateCapabilityItem(
        string text,
        out PngIconControl icon)
    {
        TableLayoutPanel item = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = ScaleUiPadding(0, 0, 4, 0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(31)));
        item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        item.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        icon = CreateTableIcon("unsupported", StandardUiIconLogicalSize, 4);
        item.Controls.Add(icon, 0, 0);

        Label label = CreateWrappedLabel(text, false, 8.0f);
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(0);
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoEllipsis = true;
        item.Controls.Add(label, 1, 0);

        return item;
    }

    private void UpdateDeviceCapabilities()
    {
        if (_batteryCapabilityIcon == null ||
            _chargingCapabilityIcon == null ||
            _microphoneCapabilityIcon == null)
        {
            return;
        }

        string selectedDevice = _deviceSelector?.SelectedDeviceName ?? string.Empty;
        bool selected = !string.IsNullOrWhiteSpace(selectedDevice);
        bool usingBluetooth = _device is BluetoothFallbackDevice bluetooth && bluetooth.IsUsingBluetooth;

        _batteryCapabilityIcon.IconKey = selected ? "supported" : "unsupported";
        _chargingCapabilityIcon.IconKey =
            selected && !usingBluetooth && HyperXDeviceManager.SupportsChargingMonitoring(selectedDevice)
                ? "supported"
                : "unsupported";
        _microphoneCapabilityIcon.IconKey =
            selected && !usingBluetooth && HyperXDeviceManager.SupportsMicrophoneMuteMonitoring(selectedDevice)
                ? "supported"
                : "unsupported";
    }

    private async void AutoDetectButton_Click(object? sender, EventArgs e)
    {
        if (_deviceAutoDetectionInProgress)
            return;

        _deviceAutoDetectionInProgress = true;
        if (_autoDetectButton != null)
            _autoDetectButton.Enabled = false;

        try
        {
            bool dark = EffectiveTheme == AppTheme.Dark;
            using DeviceDetectionOverlayForm overlay = new(this);
            using DeviceDetectionDialog dialog = new(
                _iconCache,
                dark,
                _selectedLanguage,
                DeviceDpi,
                DetectSupportedDevicesWithTimeoutAsync);

            overlay.Show(this);
            overlay.BringToFront();

            DialogResult result = dialog.ShowDialog(overlay);
            IReadOnlyList<string> detectedDevices = dialog.DetectedDevices;

            overlay.Close();

            if (result != DialogResult.OK)
                return;

            if (detectedDevices.Count == 1)
            {
                _deviceSelector.SelectedDeviceName = detectedDevices[0];
                return;
            }

            if (detectedDevices.Count == 0)
            {
                _deviceSelector.SelectedDeviceName = string.Empty;
                return;
            }

            MessageBox.Show(
                this,
                L("AutoDetectionMultipleDevices"),
                L("AutoDetectionTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        finally
        {
            _deviceAutoDetectionInProgress = false;
            if (_autoDetectButton != null && !_autoDetectButton.IsDisposed)
                _autoDetectButton.Enabled = true;
        }
    }

    private async Task<DeviceAutoDetectionResult> DetectSupportedDevicesWithTimeoutAsync(
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AutoDetectionTimeout);

        bool unsupportedThreeInOneReceiverDetected = false;

        try
        {
            while (true)
            {
                // HID enumeration/handle inspection is synchronous Win32 work.
                // Keep it off the UI thread so the overlay countdown and Cancel
                // remain responsive even with many receiver collections present.
                unsupportedThreeInOneReceiverDetected |=
                    await Task.Run(HyperXDeviceManager.IsThreeInOneReceiverPresent,
                        timeout.Token);

                IReadOnlyList<string> detected = await Task.Run(
                    () => HyperXDeviceManager.ProbeResponsiveDevicesAsync(timeout.Token),
                    timeout.Token);

                if (detected.Count > 0)
                {
                    return new DeviceAutoDetectionResult(
                        detected,
                        unsupportedThreeInOneReceiverDetected);
                }

                await Task.Delay(AutoDetectionRetryDelay, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DeviceAutoDetectionResult(
                Array.Empty<string>(),
                unsupportedThreeInOneReceiverDetected);
        }
    }

    private sealed class DeviceDetectionOverlayForm : Form
    {
        public DeviceDetectionOverlayForm(Form owner)
        {
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            ControlBox = false;
            Bounds = owner.Bounds;
            BackColor = Color.Black;
            Opacity = AutoDetectionOverlayOpacity;
            // Owned by SettingsForm (overlay.Show(owner)); no global topmost.
            AutoScaleMode = AutoScaleMode.None;
        }
    }

    private sealed class DeviceDetectionDialog : Form
    {
        private readonly PngIconCache _iconCache;
        private readonly bool _dark;
        private readonly AppLanguage _language;
        private readonly int _dpi;
        private readonly Func<CancellationToken, Task<DeviceAutoDetectionResult>> _detectionOperation;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly PictureBox _detectionImageBox;
        private readonly Label _progressLabel;
        private readonly Label _instructionLabel;
        private readonly ActionButton _actionButton;
        private readonly System.Windows.Forms.Timer _countdownTimer;
        private Image? _detectionImage;
        private DateTime _detectionDeadlineUtc;
        private bool _cancelledByUser;
        private bool _showingNoDeviceError;

        public IReadOnlyList<string> DetectedDevices { get; private set; } = Array.Empty<string>();

        public DeviceDetectionDialog(
            PngIconCache iconCache,
            bool dark,
            AppLanguage language,
            int dpi,
            Func<CancellationToken, Task<DeviceAutoDetectionResult>> detectionOperation)
        {
            _iconCache = iconCache;
            _dark = dark;
            _language = language;
            _dpi = Math.Max(LogicalDpi, dpi);
            _detectionOperation = detectionOperation;

            Text = L("AutoDetectionTitle");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = ScaleSize(440, 250);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            // ShowDialog(overlay) maintains dialog-over-owner stacking.
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = _dark ? DarkBackground : Color.White;
            ForeColor = _dark ? Color.WhiteSmoke : LightText;
            Font = new Font("Segoe UI", 9f);

            PngIconControl titleIcon = new(_iconCache, "auto-detect")
            {
                Location = ScalePoint(18, 12),
                Size = ScaleSize(36, 36),
                DarkMode = _dark
            };
            Controls.Add(titleIcon);

            Label title = new()
            {
                Text = L("AutoDetectionTitle"),
                Location = ScalePoint(62, 15),
                Size = ScaleSize(350, 32),
                Font = new Font("Segoe UI Semibold", 15f),
                ForeColor = ForeColor,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(title);

            _detectionImageBox = new PictureBox
            {
                Location = ScalePoint(45, 52),
                Size = ScaleSize(350, 92),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            SetDetectionImage(DetectingImageFileName);
            Controls.Add(_detectionImageBox);

            _progressLabel = new Label
            {
                Text = FormatDetectionProgress(GetInitialCountdownSeconds()),
                Location = ScalePoint(20, 150),
                Size = ScaleSize(400, 26),
                Font = new Font("Segoe UI Semibold", 10.3f),
                ForeColor = ForeColor,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_progressLabel);

            _instructionLabel = new Label
            {
                Text = L("AutoDetectionInstruction"),
                Location = ScalePoint(20, 176),
                Size = ScaleSize(400, 24),
                Font = new Font("Segoe UI", 8.7f),
                ForeColor = _dark ? DarkSecondary : LightSecondary,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(_instructionLabel);

            _actionButton = new ActionButton(_iconCache)
            {
                Text = L("Cancel"),
                Location = ScalePoint(160, 207),
                Size = ScaleSize(120, 34),
                Font = new Font("Segoe UI", 9f),
                DarkMode = _dark,
                Primary = false,
                OutsideBackColor = BackColor,
                TabStop = true
            };
            _actionButton.Click += (_, _) => HandleActionButtonClick();
            Controls.Add(_actionButton);
            CancelButton = _actionButton;

            _countdownTimer = new System.Windows.Forms.Timer
            {
                Interval = 250
            };
            _countdownTimer.Tick += CountdownTimer_Tick;

            Shown += DeviceDetectionDialog_Shown;
        }

        private string L(string key) => Localization.Get(key, _language);

        private int ScaleValue(int logicalValue) =>
            PngIconCache.ScaleLogicalToInt(logicalValue, _dpi);

        private Point ScalePoint(int x, int y) =>
            new(ScaleValue(x), ScaleValue(y));

        private Size ScaleSize(int width, int height) =>
            new(ScaleValue(width), ScaleValue(height));

        private async void DeviceDetectionDialog_Shown(object? sender, EventArgs e)
        {
            // Measure from when the dialog is shown, not from when detection finishes.
            // This minimum affects presentation only, not the HID search timeout.
            System.Diagnostics.Stopwatch visibleDuration = System.Diagnostics.Stopwatch.StartNew();
            _detectionDeadlineUtc = DateTime.UtcNow + AutoDetectionTimeout;
            UpdateCountdownText();
            _countdownTimer.Start();

            try
            {
                DeviceAutoDetectionResult result =
                    await _detectionOperation(_cancellation.Token);

                if (_cancelledByUser || IsDisposed || Disposing)
                    return;

                DetectedDevices = result.DetectedDevices;

                if (result.DetectedDevices.Count == 0)
                {
                    if (result.UnsupportedThreeInOneReceiverDetected)
                        ShowUnsupportedReceiverState();
                    else
                        ShowNoDeviceDetectedState();

                    return;
                }

                // A device may be found almost instantly. Keep the modal visible
                // for at least 1.5 seconds in total without delaying the HID probe.
                TimeSpan remainingDisplayTime =
                    AutoDetectionMinimumVisibleTime - visibleDuration.Elapsed;
                if (remainingDisplayTime > TimeSpan.Zero)
                {
                    // Cancelling the dialog must remain immediate during this wait.
                    await Task.Delay(remainingDisplayTime, _cancellation.Token);

                    if (_cancelledByUser || IsDisposed || Disposing)
                        return;
                }

                // Keep the countdown running until the dialog actually closes,
                // including the minimum-visible-time interval after a quick success.
                _countdownTimer.Stop();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                if (_cancelledByUser || IsDisposed || Disposing)
                    return;

                _countdownTimer.Stop();
                DetectedDevices = Array.Empty<string>();
                ShowNoDeviceDetectedState();
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing)
                    return;

                _countdownTimer.Stop();
                System.Diagnostics.Debug.WriteLine(
                    $"Automatic device detection failed: {ex.Message}");

                DetectedDevices = Array.Empty<string>();
                ShowNoDeviceDetectedState();
            }
        }

        private int GetInitialCountdownSeconds() =>
            Math.Max(0, (int)Math.Ceiling(AutoDetectionTimeout.TotalSeconds));

        private string FormatDetectionProgress(int secondsRemaining) =>
            string.Format(L("AutoDetectionProgress"), secondsRemaining);

        private void CountdownTimer_Tick(object? sender, EventArgs e) =>
            UpdateCountdownText();

        private void UpdateCountdownText()
        {
            if (_showingNoDeviceError)
                return;

            TimeSpan remaining = _detectionDeadlineUtc - DateTime.UtcNow;
            int secondsRemaining = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            _progressLabel.Text = FormatDetectionProgress(secondsRemaining);
        }

        private void ShowNoDeviceDetectedState()
        {
            if (_showingNoDeviceError || IsDisposed || Disposing)
                return;

            _showingNoDeviceError = true;
            _countdownTimer.Stop();
            SetDetectionImage(NoDeviceImageFileName);

            _progressLabel.Text = L("AutoDetectionNoDevice");
            _progressLabel.Size = ScaleSize(400, 50);
            _instructionLabel.Visible = false;

            _actionButton.Text = L("Ok");
            AcceptButton = _actionButton;
        }

        private void ShowUnsupportedReceiverState()
        {
            if (_showingNoDeviceError || IsDisposed || Disposing)
                return;

            _showingNoDeviceError = true;
            _countdownTimer.Stop();
            SetDetectionImage(
                UnsupportedReceiverImageFileName,
                NoDeviceImageFileName);

            _progressLabel.Text = L("AutoDetectionUnsupportedReceiver");
            _progressLabel.Size = ScaleSize(400, 52);
            _instructionLabel.Visible = false;

            _actionButton.Text = L("Ok");
            AcceptButton = _actionButton;
        }

        private void SetDetectionImage(
            string fileName,
            string? fallbackFileName = null)
        {
            _detectionImageBox?.SuspendLayout();
            if (_detectionImageBox != null)
                _detectionImageBox.Image = null;

            _detectionImage?.Dispose();
            _detectionImage = null;

            string imagePath = AssetPaths.GetDeviceImagePath(fileName);

            if (!File.Exists(imagePath) &&
                !string.IsNullOrWhiteSpace(fallbackFileName))
            {
                imagePath = AssetPaths.GetDeviceImagePath(fallbackFileName);
            }

            if (File.Exists(imagePath))
            {
                try
                {
                    using Image source = Image.FromFile(imagePath);
                    _detectionImage = new Bitmap(source);
                    if (_detectionImageBox != null)
                        _detectionImageBox.Image = _detectionImage;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Could not load automatic-detection image '{imagePath}': {ex.Message}");
                }
            }

            _detectionImageBox?.ResumeLayout();
        }

        private void HandleActionButtonClick()
        {
            if (_showingNoDeviceError)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            CancelDetection();
        }

        private void CancelDetection()
        {
            if (_cancelledByUser)
                return;

            _cancelledByUser = true;
            _cancellation.Cancel();
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            if (Width <= 0 || Height <= 0)
                return;

            Region?.Dispose();
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(
                    0.5f,
                    0.5f,
                    Math.Max(1f, Width - 1f),
                    Math.Max(1f, Height - 1f)),
                ScaleValue(10));
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using Pen border = new(
                _dark ? DarkBorder : Color.FromArgb(210, 216, 224),
                1f);
            using GraphicsPath path = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(
                    0.5f,
                    0.5f,
                    Math.Max(1f, Width - 1f),
                    Math.Max(1f, Height - 1f)),
                ScaleValue(10));
            e.Graphics.DrawPath(border, path);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _countdownTimer.Stop();
                _countdownTimer.Tick -= CountdownTimer_Tick;
                _countdownTimer.Dispose();
                _cancellation.Cancel();
                _cancellation.Dispose();
                _detectionImageBox.Image = null;
                _detectionImage?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
