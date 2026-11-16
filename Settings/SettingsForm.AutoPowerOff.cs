using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using HyperXBatteryTray.Devices;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm
{
    private static readonly int[] AutoPowerOffMinutes = { 0, 10, 20, 30 };
    private RoundedLanguageSelector? _autoPowerOffSelector;
    private Label? _autoPowerOffTitle;
    private Label? _autoPowerOffDescription;
    private IHyperXDevice? _autoPowerOffIdentity;
    private string? _autoPowerOffModel;
    private int? _autoPowerOffCurrent;
    private int? _autoPowerOffPending;
    private bool _autoPowerOffDirty;
    private bool _autoPowerOffReading;
    private bool _autoPowerOffReadAttempted;
    private bool _autoPowerOffBinding;
    private bool _autoPowerOffApplying;
    private CancellationTokenSource? _autoPowerOffReadCts;

    private void BuildAutoPowerOffRow(TableLayoutPanel batteryLayout)
    {
        // Keep the divider in place and center both columns in the content band.
        // Spacing is DPI-scaled and fits the existing fixed-height Battery card.
        int dividerHeight = ScaleUi(1);
        int contentTopSpacing = ScaleUi(8);
        int contentHeight = ScaleUi(DeviceAutoPowerOffRowLogicalHeight) -
            dividerHeight - contentTopSpacing;

        TableLayoutPanel row = new()
        {
            ColumnCount = 2,
            RowCount = 3,
            Dock = DockStyle.Fill,
            Margin = ScaleUiPadding(14, 0, 14, 0),
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(174)));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, dividerHeight));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, contentTopSpacing));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, contentHeight));

        Panel divider = new() { Dock = DockStyle.Fill, Tag = "device-divider", Margin = new Padding(0) };
        divider.BackColor = EffectiveTheme == AppTheme.Dark ? DarkBorder : LightBorder;
        row.Controls.Add(divider, 0, 0);
        row.SetColumnSpan(divider, 2);

        TableLayoutPanel labels = new()
        {
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = ScaleUiPadding(0, 0, 8, 0),
            BackColor = Color.Transparent
        };
        labels.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(20)));
        labels.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _autoPowerOffTitle = CreateWrappedLabel(L("AutoPowerOff"), true, 9.1f);
        _autoPowerOffTitle.TextAlign = ContentAlignment.MiddleLeft;
        _autoPowerOffDescription = CreateWrappedLabel(L("AutoPowerOffDescription"), false, 8.1f);
        _autoPowerOffDescription.ForeColor = EffectiveTheme == AppTheme.Dark ? DarkSecondary : LightSecondary;
        _autoPowerOffDescription.TextAlign = ContentAlignment.MiddleLeft;
        labels.Controls.Add(_autoPowerOffTitle, 0, 0);
        labels.Controls.Add(_autoPowerOffDescription, 0, 1);
        row.Controls.Add(labels, 0, 2);

        _autoPowerOffSelector = new RoundedLanguageSelector
        {
            // Match the compact rounded selector on Interface; do not stretch
            // the control to fill the entire remaining card row.
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Right,
            Size = ScaleUiSize(168, 36),
            Margin = new Padding(0),
            DarkMode = EffectiveTheme == AppTheme.Dark,
            Font = new Font("Segoe UI", 9f),
            AccessibleName = L("AutoPowerOff"),
            Enabled = false,
            PlaceholderText = L("AutoPowerOffUnavailable")
        };
        PopulateAutoPowerOffOptions();
        _autoPowerOffSelector.SelectedIndexChanged += AutoPowerOffSelectionChanged;
        row.Controls.Add(_autoPowerOffSelector, 1, 2);
        batteryLayout.Controls.Add(row, 0, 2);
        RefreshAutoPowerOffState();
    }

    private void PopulateAutoPowerOffOptions()
    {
        if (_autoPowerOffSelector == null) return;
        string[] choices =
        {
            L("AutoPowerOffNever"), L("AutoPowerOff10"),
            L("AutoPowerOff20"), L("AutoPowerOff30")
        };
        _autoPowerOffSelector.Items.Clear();
        foreach (string choice in choices)
            _autoPowerOffSelector.Items.Add(choice);
        _autoPowerOffSelector.PlaceholderText = L("AutoPowerOffUnavailable");
    }

    private void UpdateAutoPowerOffLanguage()
    {
        if (_autoPowerOffSelector == null || _autoPowerOffSelector.IsDisposed) return;
        if (_autoPowerOffTitle != null) _autoPowerOffTitle.Text = L("AutoPowerOff");
        if (_autoPowerOffDescription != null)
            _autoPowerOffDescription.Text = L("AutoPowerOffDescription");
        _autoPowerOffBinding = true;
        try
        {
            PopulateAutoPowerOffOptions();
            _autoPowerOffSelector.SelectedIndex = Array.IndexOf(
                AutoPowerOffMinutes, _autoPowerOffPending ?? -1);
            _autoPowerOffSelector.AccessibleName = L("AutoPowerOff");
        }
        finally { _autoPowerOffBinding = false; }
    }

    private void AutoPowerOffSelectionChanged(object? sender, EventArgs e)
    {
        if (_autoPowerOffBinding || _autoPowerOffSelector == null ||
            !_autoPowerOffSelector.Enabled) return;
        int index = _autoPowerOffSelector.SelectedIndex;
        if (index < 0 || index >= AutoPowerOffMinutes.Length) return;
        _autoPowerOffPending = AutoPowerOffMinutes[index];
        _autoPowerOffDirty = _autoPowerOffCurrent != _autoPowerOffPending;
    }

    private void InvalidateAutoPowerOffForDeviceChange()
    {
        CancelAutoPowerOffRead();
        _autoPowerOffIdentity = null;
        _autoPowerOffModel = null;
        _autoPowerOffCurrent = null;
        _autoPowerOffPending = null;
        _autoPowerOffDirty = false;
        _autoPowerOffReadAttempted = false;
    }

    private void CancelAutoPowerOffRead()
    {
        CancellationTokenSource? cts = _autoPowerOffReadCts;
        _autoPowerOffReadCts = null;
        cts?.Cancel();
        // CTS is disposed by its read task after it finishes.
        _autoPowerOffReading = false;
    }

    private void RefreshAutoPowerOffState()
    {
        if (_currentPage != "Device" || _autoPowerOffSelector == null ||
            _autoPowerOffSelector.IsDisposed) return;

        // A new receiver/preview or device selection cannot inherit a prior value.
        if (!ReferenceEquals(_device, _autoPowerOffIdentity) ||
            !string.Equals(_pendingSelectedDevice, _autoPowerOffModel, StringComparison.Ordinal))
        {
            InvalidateAutoPowerOffForDeviceChange();
            _autoPowerOffIdentity = _device;
            _autoPowerOffModel = _pendingSelectedDevice;
        }

        bool supported = _device is IAutoPowerOffDevice { SupportsAutoPowerOff: true };
        bool available = supported && _device?.IsConnected == true &&
            _device.Battery is >= 0 and <= 100;
        _autoPowerOffBinding = true;
        try
        {
            _autoPowerOffSelector.Enabled = available &&
                !_autoPowerOffApplying && (_autoPowerOffCurrent.HasValue || _autoPowerOffDirty);
            _autoPowerOffSelector.SelectedIndex = Array.IndexOf(
                AutoPowerOffMinutes, _autoPowerOffPending ?? -1);
            _autoPowerOffSelector.PlaceholderText = !supported && _device != null &&
                _device.Battery is >= 0 and <= 100
                ? L("AutoPowerOffUnsupported")
                : _autoPowerOffReading ? L("AutoPowerOffReading")
                : L("AutoPowerOffUnavailable");
        }
        finally { _autoPowerOffBinding = false; }

        if (!available)
        {
            // Re-read the actual device setting on the next verified connection.
            // Do not carry a stale pending write across a disconnect.
            if (_autoPowerOffReadCts != null) CancelAutoPowerOffRead();
            _autoPowerOffReadAttempted = false;
            _autoPowerOffCurrent = null;
            _autoPowerOffPending = null;
            _autoPowerOffDirty = false;
            _autoPowerOffSelector.SelectedIndex = -1;
            return;
        }

        if (!_autoPowerOffReadAttempted && !_autoPowerOffReading &&
            !_autoPowerOffDirty && IsHandleCreated)
        {
            _autoPowerOffReadAttempted = true;
            _ = ReadAutoPowerOffAsync(_device!, (IAutoPowerOffDevice)_device!);
        }
    }

    private async Task ReadAutoPowerOffAsync(IHyperXDevice identity, IAutoPowerOffDevice control)
    {
        CancellationTokenSource cts = new();
        _autoPowerOffReadCts = cts;
        _autoPowerOffReading = true;
        try
        {
            RefreshAutoPowerOffPlaceholder();
            int? actual = await control.QueryAutoPowerOffAsync(cts.Token);
            if (cts.IsCancellationRequested || IsDisposed || Disposing ||
                !ReferenceEquals(identity, _device) ||
                !ReferenceEquals(cts, _autoPowerOffReadCts)) return;
            _autoPowerOffCurrent = actual;
            if (!_autoPowerOffDirty) _autoPowerOffPending = actual;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _autoPowerOffCurrent = null; }
        finally
        {
            if (ReferenceEquals(cts, _autoPowerOffReadCts))
            {
                _autoPowerOffReadCts = null;
                _autoPowerOffReading = false;
                RefreshAutoPowerOffPlaceholder();
            }
            cts.Dispose();
        }
    }

    private void RefreshAutoPowerOffPlaceholder()
    {
        if (_autoPowerOffSelector == null || _autoPowerOffSelector.IsDisposed) return;
        _autoPowerOffBinding = true;
        try
        {
            _autoPowerOffSelector.Enabled = !_autoPowerOffReading &&
                !_autoPowerOffApplying && _autoPowerOffCurrent.HasValue;
            _autoPowerOffSelector.SelectedIndex = Array.IndexOf(
                AutoPowerOffMinutes, _autoPowerOffPending ?? -1);
            _autoPowerOffSelector.PlaceholderText = _autoPowerOffReading
                ? L("AutoPowerOffReading") : L("AutoPowerOffUnavailable");
        }
        finally { _autoPowerOffBinding = false; }
    }

    private void RevertAutoPowerOffPending()
    {
        _autoPowerOffPending = _autoPowerOffCurrent;
        _autoPowerOffDirty = false;
        RefreshAutoPowerOffState();
    }

    private async Task<bool> CommitAutoPowerOffAsync()
    {
        if (_autoPowerOffApplying) return false;
        if (!_autoPowerOffDirty) return true;
        if (_autoPowerOffPending is not int desired ||
            _device is not IAutoPowerOffDevice control ||
            !control.SupportsAutoPowerOff || _device.Battery is < 0 or > 100)
        {
            ShowAutoPowerOffError();
            return false;
        }

        IHyperXDevice identity = _device;
        _autoPowerOffApplying = true;
        try
        {
            RefreshAutoPowerOffPlaceholder();
            bool success = await control.SetAutoPowerOffAsync(desired);
            if (!success || !ReferenceEquals(identity, _device) || IsDisposed)
            {
                ShowAutoPowerOffError();
                return false;
            }
            _autoPowerOffCurrent = desired;
            _autoPowerOffDirty = false;
            return true;
        }
        catch (Exception)
        {
            ShowAutoPowerOffError();
            return false;
        }
        finally
        {
            _autoPowerOffApplying = false;
            RefreshAutoPowerOffPlaceholder();
        }
    }

    private void ShowAutoPowerOffError()
    {
        if (!IsDisposed)
            MessageBox.Show(this, L("AutoPowerOffApplyError"),
                Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void DisposeAutoPowerOffState()
    {
        CancelAutoPowerOffRead();
        _autoPowerOffSelector?.ClosePopup();
        _autoPowerOffSelector = null;
    }

    private void LimitWindowToWorkingArea()
    {
        Rectangle work = Screen.FromControl(this).WorkingArea;
        if (Width > work.Width || Height > work.Height)
            Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
        // The existing themed AutoScroll layout keeps the footer accessible.
    }
}
