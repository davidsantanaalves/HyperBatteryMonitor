# HyperX Battery Monitor v2.3.1 Beta 1

> 🧪 **Public Beta / Testing Release**
>
> This is a pre-release version intended for public testing. It may contain bugs or unexpected behavior.
>
> Users who prefer maximum reliability should remain on the latest stable release.

## ✨ New

- Added an optional **Clean Installation** mode to the Windows installer. When selected, existing HyperX Battery Monitor settings and battery history are removed during setup so the installed version starts with a fresh user profile.
- New installations can now automatically select a supported language based on the Windows display language. Unsupported Windows languages fall back to English.

## 🌐 Localization

HyperX Battery Monitor now supports **11 languages**:

- English
- Portuguese (Brazil)
- Spanish
- Ukrainian
- German
- French
- Polish
- Russian
- Simplified Chinese
- Japanese
- Korean

Additional localization improvements include:

- Reworked the localization system so translations are stored in individual JSON files instead of large hardcoded C# dictionaries.
- Translation files are embedded into the application at build time, keeping deployment self-contained while making translations easier to maintain and contribute.
- English is used as the canonical fallback language when a translated entry is unavailable or invalid.
- Translation format strings and placeholders such as `{0}` and `{1}` are validated against the English entries. Malformed or mismatched translations fall back to English.
- Improved the language selector to handle the larger language list with scrolling and better popup positioning.
- Corrected references to the former **HyperX Battery Tray** name so the interface consistently uses **HyperX Battery Monitor**.

## 🔧 Improvements

- Renamed the **Battery Monitor** Settings section to the shorter and clearer **Battery** label in both the sidebar and page title.
- Reworked several Settings layouts to better handle longer translated text without breaking the interface.
- Improved support for different Windows DPI and display scaling levels.
- Reworked the **Dynamic Icon Colors** dialog with a responsive, DPI-aware layout.
- Buttons and text areas can now adapt to localized content instead of depending on fixed English-sized dimensions.
- Battery preview labels can use multiple lines when necessary instead of being prematurely truncated.
- Improved the Settings footer layout so translated button labels can resize correctly.
- Improved the Dynamic Icon Colors dialog opening behavior so its controls are presented as a completed layout instead of visibly appearing one after another.
- Consolidated application image resources under the `Assets` structure and centralized runtime asset-path resolution.
- Removed confirmed unused, duplicate, legacy, and backup visual resources from the application package.

## 🐛 Fixes

- Fixed text overlap and clipping in the **Dynamic Icon Colors** dialog when using languages with longer labels.
- Fixed localized descriptions being truncated because of fixed-height layout areas.
- Fixed truncated labels in battery preview tiles.
- Fixed Settings footer buttons being too narrow for some translations.
- Fixed the language popup becoming too tall after the addition of the new languages.
- Fixed the visible “curtain” / progressive painting effect when opening the Dynamic Icon Colors dialog.
- Fixed inconsistent visual-resource paths after the asset structure cleanup.
- Fixed a legacy startup description that still referred to the application as **HyperX Battery Tray**.

## 🧪 What to test

Beta testers are especially encouraged to verify:

### Languages

- Switch between all 11 available languages.
- Restart the application and confirm the selected language is preserved.
- On a clean installation, verify that the Windows display language is detected correctly.
- Check for untranslated text, incorrect wording, clipped labels, overlapping controls, or missing characters.

### Display scaling

If possible, test the Settings window at different Windows scaling levels, especially:

- 100%
- 125%
- 150%
- 175%
- 200%

Pay special attention to:

- Battery page
- Language selector
- Settings footer buttons
- Dynamic Icon Colors dialog
- Battery preview labels

### Dynamic Icon Colors

Verify that:

- the dialog opens without visible progressive rendering;
- text does not overlap the percentage controls;
- longer translations wrap correctly;
- buttons remain fully visible;
- the preview is displayed correctly;
- scrolling appears only when required by the available screen space.

### Installer

Please test both installation modes:

**Normal upgrade**
- Existing settings should be preserved.
- Existing battery history should be preserved.

**Clean Installation**
- Existing settings should be removed.
- Existing battery history should be removed.
- The application should behave like a first installation.
- The Windows language should be detected again.

### Visual assets

Because the internal asset structure was reorganized, please verify that no images or icons are missing, including:

- system tray icons;
- charging icons;
- disconnected icons;
- microphone status icons;
- battery-state icons;
- light and dark themes;
- Settings icons;
- headset images;
- device-detection images;
- Windows notifications;
- application and installer icons.

## 💬 Feedback and bug reports

If you find a problem, please report it through the project's **GitHub Issues** page.

When reporting a visual or localization issue, please include:

- the selected language;
- Windows display scaling percentage;
- Windows version;
- a screenshot if possible;
- the steps needed to reproduce the issue.

Thank you for helping test HyperX Battery Monitor.
