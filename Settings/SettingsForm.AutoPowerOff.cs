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
    private IHyperXDevice? _autoPowerOffIdentity;
    private string? _autoPowerOffModel;
    private int? _autoPowerOffCurrent;
    private int? _autoPowerOffPending;
    private bool _autoPowerOffDirty;
    private bool _autoPowerOffReading;
    private bool _autoPowerOffReadAttempted;
    private bool _autoPowerOffApplying;
    private CancellationTokenSource? _autoPowerOffReadCts;

    // The four compact selection cards are owned by DeviceSettingsDialog.
    // Only a verified and connected device can change an option.
    private void SelectAutoPowerOff(int minutes)
    {
        if (_autoPowerOffReading || _autoPowerOffApplying ||
            _device is not IAutoPowerOffDevice { SupportsAutoPowerOff: true } ||
            _device?.IsConnected != true || _device.Battery is < 0 or > 100 ||
            !_autoPowerOffCurrent.HasValue || minutes is not (0 or 10 or 20 or 30)) return;
        _autoPowerOffPending = minutes;
        _autoPowerOffDirty = _autoPowerOffCurrent != minutes;
        RefreshAutoPowerOffPresentation();
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
        if (_currentPage != "Device" || _activeDeviceSettingsDialog is null ||
            _activeDeviceSettingsDialog.IsDisposed) return;

        if (!ReferenceEquals(_device, _autoPowerOffIdentity) ||
            !string.Equals(_pendingSelectedDevice, _autoPowerOffModel, StringComparison.Ordinal))
        {
            InvalidateAutoPowerOffForDeviceChange();
            _autoPowerOffIdentity = _device;
            _autoPowerOffModel = _pendingSelectedDevice;
        }

        bool supported = HyperXDeviceManager.SupportsAutoPowerOff(_pendingSelectedDevice) &&
            (_device == null || _device.IsConnected != true ||
                _device is IAutoPowerOffDevice { SupportsAutoPowerOff: true });
        bool available = supported && _device is IAutoPowerOffDevice { SupportsAutoPowerOff: true } &&
            _device.IsConnected && _device.Battery is >= 0 and <= 100;
        if (!available)
        {
            CancelAutoPowerOffRead();
            _autoPowerOffReadAttempted = false;
            _autoPowerOffCurrent = null;
            _autoPowerOffPending = null;
            _autoPowerOffDirty = false;
            RefreshAutoPowerOffPresentation();
            return;
        }

        if (!_autoPowerOffReadAttempted && !_autoPowerOffReading &&
            !_autoPowerOffDirty && IsHandleCreated)
        {
            if (ReferenceEquals(_device, _runtimeDevice) &&
                _settings.RuntimeAutoPowerOffMinutes is 0 or 10 or 20 or 30)
            {
                _autoPowerOffCurrent = _settings.RuntimeAutoPowerOffMinutes;
                _autoPowerOffPending = _autoPowerOffCurrent;
                _autoPowerOffReadAttempted = true;
            }
            else
            {
                _autoPowerOffReadAttempted = true;
                _ = ReadAutoPowerOffAsync(_device!, (IAutoPowerOffDevice)_device!);
            }
        }
        RefreshAutoPowerOffPresentation();
    }

    private void RefreshAutoPowerOffPresentation()
    {
        if (_activeDeviceSettingsDialog is null || _activeDeviceSettingsDialog.IsDisposed) return;
        bool supported = HyperXDeviceManager.SupportsAutoPowerOff(_pendingSelectedDevice) &&
            (_device == null || _device.IsConnected != true ||
                _device is IAutoPowerOffDevice { SupportsAutoPowerOff: true });
        bool available = supported && _device is IAutoPowerOffDevice { SupportsAutoPowerOff: true } &&
            _device.IsConnected && _device.Battery is >= 0 and <= 100;
        int? last = _settings.GetLastKnownAutoPowerOff(_pendingSelectedDevice);
        int? shown = available ? _autoPowerOffPending ?? last : last;
        bool editable = available && !_autoPowerOffReading && !_autoPowerOffApplying &&
            _autoPowerOffCurrent.HasValue;
        string note = !supported ? L("AutoPowerOffNotSupportedForDevice")
            : !available ? L("AutoPowerOffPowerOnToChange")
            : _autoPowerOffReading ? L("AutoPowerOffReading")
            : _autoPowerOffCurrent.HasValue ? string.Empty : L("AutoPowerOffUnavailable");
        _activeDeviceSettingsDialog.UpdateAutoPowerOff(supported, editable, shown, note);
    }

    private async Task ReadAutoPowerOffAsync(IHyperXDevice identity, IAutoPowerOffDevice control)
    {
        CancellationTokenSource cts = new();
        _autoPowerOffReadCts = cts;
        _autoPowerOffReading = true;
        try
        {
            RefreshAutoPowerOffPresentation();
            int? actual = await control.QueryAutoPowerOffAsync(cts.Token);
            if (cts.IsCancellationRequested || IsDisposed || Disposing ||
                !ReferenceEquals(identity, _device) ||
                !ReferenceEquals(cts, _autoPowerOffReadCts)) return;
            _autoPowerOffCurrent = actual;
            if (!_autoPowerOffDirty) _autoPowerOffPending = actual;
            PublishAutoPowerOffSnapshot(identity, actual);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _autoPowerOffCurrent = null; }
        finally
        {
            if (ReferenceEquals(cts, _autoPowerOffReadCts))
            {
                _autoPowerOffReadCts = null;
                _autoPowerOffReading = false;
                RefreshAutoPowerOffPresentation();
            }
            cts.Dispose();
        }
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
            RefreshAutoPowerOffPresentation();
            bool success = await control.SetAutoPowerOffAsync(desired);
            if (!success || !ReferenceEquals(identity, _device) || IsDisposed)
            {
                ShowAutoPowerOffError();
                return false;
            }
            _autoPowerOffCurrent = desired;
            _autoPowerOffDirty = false;
            PublishAutoPowerOffSnapshot(identity, desired);
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
            RefreshAutoPowerOffPresentation();
        }
    }

    private void PublishAutoPowerOffSnapshot(IHyperXDevice identity, int? value)
    {
        if (!ReferenceEquals(identity, _device) || value is not (0 or 10 or 20 or 30))
            return;
        if (_settings.RememberVerifiedAutoPowerOff(_pendingSelectedDevice, value.Value))
        {
            try { new SettingsManager().Save(_settings); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not persist verified Auto Power-Off: {ex.Message}");
            }
        }
        if (!ReferenceEquals(identity, _runtimeDevice)) return;
        _settings.RuntimeAutoPowerOffMinutes = value;
        RefreshRuntimeStatusSummary();
        DeviceAutoPowerOffSnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowAutoPowerOffError()
    {
        if (!IsDisposed)
            MessageBox.Show(_activeDeviceSettingsDialog ?? (Form)this,
                L("AutoPowerOffApplyError"), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void DisposeAutoPowerOffState()
    {
        CancelAutoPowerOffRead();
        _activeDeviceSettingsDialog = null;
    }

    private void LimitWindowToWorkingArea()
    {
        Rectangle work = Screen.FromControl(this).WorkingArea;
        if (Width > work.Width || Height > work.Height)
            Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
        // The existing themed AutoScroll layout keeps the footer accessible.
    }
}
