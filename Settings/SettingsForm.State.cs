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
    private void LanguageComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_updatingLanguage || _languageComboBox.SelectedIndex < 0) return;
        _selectedLanguage = Localization.LanguageAt(_languageComboBox.SelectedIndex);
        ApplyLocalizedText();
        ShowPage(_currentPage);
        ApplyTheme(_selectedTheme);
    }

    private void ThemeOption_Click(object? sender, EventArgs e)
    {
        if (sender is not ThemeOptionControl option) return;
        _selectedTheme = option.Theme;
        _lightThemeOption.Selected = _selectedTheme == AppTheme.Light;
        _darkThemeOption.Selected = _selectedTheme == AppTheme.Dark;
        _systemThemeOption.Selected = _selectedTheme == AppTheme.System;
        ApplyTheme(_selectedTheme);
    }

    private void ApplyLocalizedText()
    {
        Text = GetWindowTitle();
        _resetButton.Text = L("ResetToDefaults");
        _okButton.Text = L("Ok");
        _cancelButton.Text = L("Cancel");
        _applyButton.Text = L("Apply");
        _footer?.PerformLayout();
        PositionFooter();
        foreach ((string key, SidebarItem item) in _navButtons)
            item.Text = L(GetSidebarLocalizationKey(key));
        if (_sidebarStatusTitleLabel != null)
            _sidebarStatusTitleLabel.Text = L("SidebarStatus");
        if (_sidebarBatteryTitleLabel != null)
            _sidebarBatteryTitleLabel.Text = L("SidebarBattery");
        if (_sidebarMicrophoneTitleLabel != null)
            _sidebarMicrophoneTitleLabel.Text = L("SidebarMicrophone");
        if (_deviceSelector != null)
        {
            _deviceSelector.SetLanguage(_selectedLanguage);
            _deviceSelector.SetPlaceholder(L("LocateDevice"));
        }
        UpdateDeviceInformation();

        if (_lightThemeOption != null) _lightThemeOption.LabelText = ThemeText(AppTheme.Light);
        if (_darkThemeOption != null) _darkThemeOption.LabelText = ThemeText(AppTheme.Dark);
        if (_systemThemeOption != null) _systemThemeOption.LabelText = ThemeText(AppTheme.System);
        if (_startupToggle != null) _startupToggle.Invalidate();

        if (_languageComboBox != null &&
            _languageComboBox.Items.Count == Localization.SupportedLanguages.Count)
        {
            _updatingLanguage = true;
            for (int i = 0; i < Localization.SupportedLanguages.Count; i++)
            {
                _languageComboBox.Items[i] =
                    Localization.LanguageDisplay(Localization.SupportedLanguages[i]);
            }
            _languageComboBox.SelectedIndex = Localization.LanguageIndex(_selectedLanguage);
            _updatingLanguage = false;
        }
    }

    private void ApplyTheme(AppTheme theme)
    {
        // Theme changes restyle existing controls only. Keep the page host visible
        // and suspend layout until all properties have been updated. Hiding it here
        // can leave the entire content area invisible during navigation/theme events.
        _pageHost.SuspendLayout();
        try
        {
        bool dark = ResolveTheme(theme) == AppTheme.Dark;
        Color background = dark ? DarkBackground : LightBackground;
        Color sidebar = dark ? DarkSidebar : LightSidebar;
        Color foreground = dark ? Color.WhiteSmoke : LightText;
        Color secondary = dark ? DarkSecondary : LightSecondary;

        BackColor = background;
        ApplyTitleBarTheme(dark);
        _sidebar.BackColor = sidebar;
        _pageHost.BackColor = background;
        _pageHost.ForeColor = foreground;
        if (_footer != null) _footer.BackColor = background;
        _versionLabel.ForeColor = secondary;
        _applicationNameLabel.ForeColor = foreground;
        _versionLabel.Text = "v" + AppReleaseInfo.DisplayVersion;

        Icon = LoadApplicationIcon();
        WarmUpIcons();
        ApplyThemeRecursive(this, foreground, dark);
        ApplyDeviceCardTheme(dark);

        if (_sidebarDeviceCard != null)
        {
            _sidebarDeviceCard.BackColor = dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251);
            _sidebarDeviceCard.BorderColor = dark ? DarkBorder : LightBorder;
            _sidebarDeviceCard.OutsideBackColor = sidebar;
        }

        if (_lightThemeOption != null) _lightThemeOption.DarkMode = dark;
        if (_darkThemeOption != null) _darkThemeOption.DarkMode = dark;
        if (_systemThemeOption != null) _systemThemeOption.DarkMode = dark;
        if (_startupToggle != null) _startupToggle.DarkMode = dark;
        if (_notifyOnLowBatteryToggle != null) _notifyOnLowBatteryToggle.DarkMode = dark;
        if (_notifyWhenFullyChargedToggle != null) _notifyWhenFullyChargedToggle.DarkMode = dark;
        if (_blinkOnCriticalBatteryToggle != null) _blinkOnCriticalBatteryToggle.DarkMode = dark;
        if (_criticalBatteryPercentInput != null)
            _criticalBatteryPercentInput.DarkMode = dark;
        UpdateBatteryMonitorModeCards();

        foreach (SidebarItem item in _navButtons.Values)
        {
            item.DarkMode = dark;
            item.Invalidate();
        }

        if (_sidebarDeviceCard != null)
        {
            _sidebarDeviceCard.BackColor = dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251);
            _sidebarDeviceCard.BorderColor = dark ? DarkBorder : LightBorder;
            _sidebarDeviceCard.OutsideBackColor = sidebar;
        }
        if (_sidebarDeviceNameLabel != null)
            _sidebarDeviceNameLabel.ForeColor = foreground;
        if (_sidebarStatusTitleLabel != null)
            _sidebarStatusTitleLabel.ForeColor = secondary;
        if (_sidebarStatusLabel != null)
            _sidebarStatusLabel.ForeColor = secondary;
        if (_sidebarBatteryTitleLabel != null)
            _sidebarBatteryTitleLabel.ForeColor = secondary;
        if (_sidebarBatteryLabel != null)
            _sidebarBatteryLabel.ForeColor = secondary;
        if (_sidebarBatteryIcon != null)
        {
            _sidebarBatteryIcon.DarkMode = dark;
            _sidebarBatteryIcon.Invalidate();
        }
        if (_sidebarMicrophoneTitleLabel != null)
            _sidebarMicrophoneTitleLabel.ForeColor = secondary;
        if (_sidebarMicrophoneLabel != null)
            _sidebarMicrophoneLabel.ForeColor = secondary;
        UpdateSidebarDeviceStatus(
            _deviceSelector != null && _deviceSelector.SelectedIndex > 0,
            _device != null && _device.IsConnected && _device.Battery >= 0 && _device.Battery <= 100);

        if (_deviceStatusLabel != null)
        {
            _deviceStatusLabel.ForeColor = foreground;
            _deviceStatusLabel.BackColor = Color.Transparent;
        }
        if (_deviceStatusDescriptionLabel != null)
        {
            _deviceStatusDescriptionLabel.ForeColor = secondary;
            _deviceStatusDescriptionLabel.BackColor = Color.Transparent;
        }
        if (_batteryValueLabel != null)
        {
            _batteryValueLabel.ForeColor = foreground;
            _batteryValueLabel.BackColor = Color.Transparent;
        }
        if (_chargingLabel != null)
        {
            _chargingLabel.ForeColor = secondary;
            _chargingLabel.BackColor = Color.Transparent;
        }

        if (_deviceSelector != null)
        {
            _deviceSelector.DarkMode = dark;
            _deviceSelector.Invalidate();
        }

        if (_autoDetectButton != null)
        {
            _autoDetectButton.DarkMode = dark;
            _autoDetectButton.OutsideBackColor = dark
                ? Color.FromArgb(42, 45, 48)
                : Color.FromArgb(248, 249, 251);
            _autoDetectButton.Invalidate();
        }

        StyleFooterButton(_resetButton, dark, false);
        StyleFooterButton(_cancelButton, dark, false);
        StyleFooterButton(_applyButton, dark, false);
        StyleFooterButton(_okButton, dark, true);
        _sidebar.Invalidate();
        ApplyCurrentScrollThemes(dark);
        _pageHost.Invalidate(true);
        UpdateDeviceInformation();
        RefreshDeviceStatus();
        }
        finally
        {
            _pageHost.ResumeLayout(true);
            _pageHost.Invalidate(true);
        }
    }

    private void ApplyDeviceCardTheme(bool dark)
    {
        Color cardBackground = dark
            ? Color.FromArgb(42, 45, 48)
            : Color.FromArgb(248, 249, 251);
        Color outsideBackground = dark ? DarkBackground : LightBackground;

        foreach (Control control in _pageHost.Controls)
        {
            if (control is not RoundedPanel panel || panel.Tag is not string tag || tag != "device-card")
                continue;

            panel.BackColor = cardBackground;
            panel.OutsideBackColor = outsideBackground;
            panel.BorderColor = dark ? DarkBorder : LightBorder;
            panel.Invalidate();
        }
    }

    private void ApplyThemeRecursive(Control parent, Color foreground, bool dark)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is PngIconControl pngIcon)
            {
                pngIcon.DarkMode = dark;
                continue;
            }

            if (c is BatteryPreviewItem previewItem)
            {
                previewItem.DarkMode = dark;
                continue;
            }

            if (c is BatteryModeCard modeCard)
            {
                modeCard.DarkMode = dark;
                continue;
            }

            if (c is SidebarItem || c is Button || c is DeviceSelector || c is StatusDotControl || c is BatteryIconControl || c == _footer)
                continue;

            if (c.Tag is string tag && tag == "device-divider")
            {
                c.BackColor = dark ? DarkBorder : LightBorder;
                continue;
            }

            c.ForeColor = foreground;

            if (c is RoundedPanel panel)
            {
                bool isDeviceCard = panel.Tag is string cardTag && cardTag == "device-card";
                panel.BackColor = isDeviceCard
                    ? (dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251))
                    : panel.Tag is string s && s == "outer"
                        ? (dark ? Color.FromArgb(34, 37, 40) : Color.White)
                        : (dark ? Color.FromArgb(42, 45, 48) : Color.FromArgb(248, 249, 251));
                panel.BorderColor = dark ? DarkBorder : LightBorder;
                panel.OutsideBackColor = isDeviceCard
                    ? (dark ? Color.FromArgb(32, 35, 38) : LightBackground)
                    : panel.Tag is string innerTag && innerTag == "inner"
                        ? (dark ? Color.FromArgb(34, 37, 40) : Color.White)
                        : (dark ? Color.FromArgb(32, 35, 38) : LightBackground);
            }
            else if (c is RoundedLanguageSelector languageSelector)
            {
                languageSelector.DarkMode = dark;
                languageSelector.ForeColor = foreground;
            }
            else if (c is ComboBox combo)
            {
                combo.BackColor = dark ? Color.FromArgb(38, 41, 44) : Color.White;
                combo.ForeColor = foreground;
            }
            else if (c is RadioButton || c is CheckBox || c is Label)
            {
                c.BackColor = Color.Transparent;
            }
            else if (c is Panel panel2)
            {
                panel2.BackColor = Color.Transparent;
            }

            ApplyThemeRecursive(c, foreground, dark);
        }
    }

    private void StyleFooterButton(Button button, bool dark, bool primary)
    {
        if (button is ActionButton actionButton)
        {
            actionButton.DarkMode = dark;
            actionButton.Primary = primary;
            actionButton.OutsideBackColor = dark ? Color.FromArgb(32, 35, 38) : LightBackground;
            actionButton.Invalidate();
            return;
        }

        button.BackColor = primary ? Accent : (dark ? Color.FromArgb(38, 41, 44) : Color.White);
        button.ForeColor = primary ? Color.White : (dark ? Color.WhiteSmoke : LightText);
    }

    private void ComboBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool dark = EffectiveTheme == AppTheme.Dark;
        e.DrawBackground();
        using Brush brush = new SolidBrush(dark ? Color.WhiteSmoke : LightText);
        Font font = e.Font ?? Control.DefaultFont;
        TextRenderer.DrawText(e.Graphics, _languageComboBox.Items[e.Index]?.ToString() ?? string.Empty, font, e.Bounds, ((SolidBrush)brush).Color, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        e.DrawFocusRectangle();
    }

    private void ResetButton_Click(object? sender, EventArgs e)
    {
        using RestoreDefaultsDialog dialog = new(
            L("RestoreDefaults"),
            L("RestoreDefaultsQuestion"),
            L("Yes"),
            L("No"),
            EffectiveTheme == AppTheme.Dark);

        DialogResult result = dialog.ShowDialog(this);
        if (result != DialogResult.Yes) return;

        AppSettings defaults = AppSettings.CreateDefault();
        _pendingSelectedDevice = defaults.SelectedDevice;
        _pendingUpdateChannel = defaults.UpdateChannel;
        _pendingUpdateChannelUserSelected = false;
        _pendingUpdateDownloadMode = defaults.UpdateDownloadMode;
        _pendingStartupEnabled = !ApplicationRuntime.IsTestMode &&
            AppSettings.DefaultStartWithWindows;
        _pendingNotifyOnLowBattery = defaults.NotifyOnLowBattery;
        _pendingNotifyWhenFullyCharged = defaults.NotifyWhenFullyCharged;
        _pendingBlinkOnCriticalBattery = defaults.BlinkOnCriticalBattery;
        _pendingShowMicrophoneMuteInSystray = defaults.ShowMicrophoneMuteInSystray;
        _pendingCriticalBatteryPercent = defaults.CriticalBatteryPercent;
        _pendingDisplayMode = defaults.DisplayMode;
        _pendingAdvancedDisplayMode = defaults.AdvancedDisplayMode;
        _pendingBatteryColors = CloneBatteryColors(defaults.BatteryColors);
        _pendingUseGradient = defaults.UseGradient;
        _pendingGradientPercent = defaults.GradientPercent;
        _selectedLanguage = defaults.Language;
        _selectedTheme = defaults.Theme;

        if (_deviceSelector != null) _deviceSelector.SelectedIndex = 0;
        if (_languageComboBox != null) _languageComboBox.SelectedIndex = (int)_selectedLanguage;
        if (_lightThemeOption != null) _lightThemeOption.Selected = false;
        if (_darkThemeOption != null) _darkThemeOption.Selected = false;
        if (_systemThemeOption != null) _systemThemeOption.Selected = true;

        ApplyLocalizedText();
        ShowPage(_currentPage);
        ApplyTheme(_selectedTheme);
        RefreshDeviceStatus();
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        if (ApplySettings()) Close();
    }

    private void ApplyButton_Click(object? sender, EventArgs e) => ApplySettings();

    private static List<BatteryColorSettings> CloneBatteryColors(IEnumerable<BatteryColorSettings>? colors)
    {
        return (colors ?? Enumerable.Empty<BatteryColorSettings>())
            .Where(c => c != null)
            .Select(c => new BatteryColorSettings
            {
                Name = c.Name,
                MinimumPercent = Math.Clamp(c.MinimumPercent, 0, 100),
                Argb = c.Argb
            })
            .ToList();
    }

    private bool ApplySettings()
    {
        _settings.SelectedDevice = _pendingSelectedDevice;
        _settings.UpdateChannel = _pendingUpdateChannel;
        _settings.UpdateChannelUserSelected = _pendingUpdateChannelUserSelected;
        _settings.UpdateDownloadMode = _pendingUpdateDownloadMode;
        _settings.DisplayMode = _pendingDisplayMode;
        _settings.AdvancedDisplayMode = _pendingAdvancedDisplayMode;
        _settings.BatteryColors = CloneBatteryColors(_pendingBatteryColors);
        _settings.UseGradient = _pendingUseGradient;
        _settings.GradientPercent = Math.Clamp(_pendingGradientPercent, 0, 50);
        _settings.NotifyOnLowBattery = _pendingNotifyOnLowBattery;
        _settings.NotifyWhenFullyCharged = _pendingNotifyWhenFullyCharged;
        _settings.BlinkOnCriticalBattery = _pendingBlinkOnCriticalBattery;
        _settings.ShowMicrophoneMuteInSystray = _pendingShowMicrophoneMuteInSystray;
        _settings.CriticalBatteryPercent = Math.Clamp(_pendingCriticalBatteryPercent, 1, 100);
        _settings.Language = _selectedLanguage;
        _settings.Theme = _selectedTheme;
        _settings.ThemeConfigured = true;
        try
        {
            if (!ApplicationRuntime.IsTestMode)
            {
                if (_pendingStartupEnabled) _startupManager.Enable();
                else _startupManager.Disable();
            }

            _settings.IsNewSettingsProfile = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(L("StartupError"), ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        StopDevicePreview();
        SettingsApplied?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void SettingsForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        if (_updateCoordinator is not null)
            _updateCoordinator.Changed -= OnUpdateStatusChanged;
        StopDevicePreview();
        UnsubscribeFromDeviceStatusEvents(_device);

        if (_sidebarMicrophoneIcon != null)
        {
            Image? microphoneIcon = _sidebarMicrophoneIcon.Image;
            _sidebarMicrophoneIcon.Image = null;
            microphoneIcon?.Dispose();
            _sidebarMicrophoneIconDark = null;
            _sidebarMicrophoneIconUsesMuteAsset = null;
        }

        _deviceAutoDetectToolTip?.Dispose();
        _deviceAutoDetectToolTip = null;
        _iconCache.Dispose();
    }

    private void WarmUpIcons()
    {
        _iconCache.Prewarm(new[]
        {
            new PngIconRequest("device", 25),
            new PngIconRequest("interface", 25),
            new PngIconRequest("battery_monitor", 25),
            new PngIconRequest("notification", 25),
            new PngIconRequest("about", 25),
            new PngIconRequest("device", 36),
            new PngIconRequest("interface", 36),
            new PngIconRequest("battery_monitor", 36),
            new PngIconRequest("notification", 36),
            new PngIconRequest("about", 36),
            new PngIconRequest("language", 25),
            new PngIconRequest("theme", 25),
            new PngIconRequest("windows", 25),
            new PngIconRequest("light", 36),
            new PngIconRequest("dark", 36),
            new PngIconRequest("interface", 36),
            new PngIconRequest("reset", 20),
            new PngIconRequest("git", 25),
            new PngIconRequest("support", 25),
            new PngIconRequest("documentation", 25),
            new PngIconRequest("external", 25),
            new PngIconRequest("legal", 25),
            new PngIconRequest("auto-detect", 36),
            new PngIconRequest("supported", 25),
            new PngIconRequest("unsupported", 25)
        }, EffectiveTheme == AppTheme.Dark, DeviceDpi);
    }

    private string ThemeText(AppTheme theme) => theme switch
    {
        AppTheme.Dark => L("ThemeDark"),
        AppTheme.System => L("ThemeSystem"),
        _ => L("ThemeLight")
    };

    private string GetWindowTitle() => ApplicationRuntime.TestIndicator + string.Format(
        L("WindowTitle"),
        Application.ProductName,
        AppReleaseInfo.DisplayVersion);

    private string L(string key) => Localization.Get(key, _selectedLanguage);
    private AppTheme EffectiveTheme => ResolveTheme(_selectedTheme);

    private static AppTheme ResolveTheme(AppTheme theme)
    {
        if (theme != AppTheme.System) return theme;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            if (value is int i) return i == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch { }
        return AppTheme.Light;
    }

    private static Icon? LoadApplicationIcon()
    {
        string path = AssetPaths.GetBrandingAssetPath("hxbm-logo.ico");
        if (!File.Exists(path)) return null;
        using FileStream stream = File.OpenRead(path);
        return new Icon(stream);
    }

    private static Icon? LoadThemeIcon(AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark;
        string path = AssetPaths.GetThemeIconPath(dark, dark ? "dark.ico" : "light.ico");
        if (!File.Exists(path)) return null;
        using FileStream stream = File.OpenRead(path);
        return new Icon(stream);
    }

    private void PositionWindowAtTop()
    {
        if (Owner != null)
        {
            StartPosition = FormStartPosition.CenterParent;
            return;
        }
        Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(workArea.Left + Math.Max(0, (workArea.Width - Width) / 2), workArea.Top + Math.Max(0, (workArea.Height - Height) / 2));
    }
}
