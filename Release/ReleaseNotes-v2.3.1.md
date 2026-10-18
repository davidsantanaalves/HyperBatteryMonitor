# Hyper Battery Monitor v2.3.1

## ✨ New

- The application has been renamed from **HyperX Battery Monitor** to **Hyper Battery Monitor**.
- Existing desktop installations are migrated to the new application folder: `C:\Program Files\Hyper Battery Monitor`.
- Existing settings and battery history are migrated from `%LOCALAPPDATA%\HyperXBatteryTray` to `%LOCALAPPDATA%\Hyper Battery Monitor` during a normal upgrade.
- Added an optional **Clean Installation** mode to the Windows installer. When selected, existing settings and battery history are removed so the installed version starts with a fresh user profile.
- New installations automatically select a supported language based on the Windows display language. Unsupported Windows languages fall back to English.

## 🌐 Localization

Hyper Battery Monitor now supports **11 languages**:

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
- The Windows installer now explicitly detects running application instances before install/uninstall operations, reducing file-in-use failures during upgrades.
- Startup registration is migrated to the renamed executable while preserving whether Start with Windows was enabled.

## 🐛 Fixes

- Fixed text overlap and clipping in the **Dynamic Icon Colors** dialog when using languages with longer labels.
- Fixed localized descriptions being truncated because of fixed-height layout areas.
- Fixed truncated labels in battery preview tiles.
- Fixed Settings footer buttons being too narrow for some translations.
- Fixed the language popup becoming too tall after the addition of the new languages.
- Fixed the visible “curtain” / progressive painting effect when opening the Dynamic Icon Colors dialog.
- Fixed inconsistent visual-resource paths after the asset structure cleanup.
- Fixed legacy application-name references in the interface and project documentation.
- Fixed upgrades that could leave the previous application running while files were being replaced.
- Fixed uninstallations leaving current or legacy **Start with Windows** registry entries behind, which could make Windows keep trying to launch the application after it had been removed.
- Fixed excess empty space inside the About page's Legal card.

## Third-party notices

The historical development reference and original MIT notice are preserved in `THIRD_PARTY_NOTICES.md`, which is now included with the application. The technical provenance audit remains in the source repository.

## Upgrade notes

A normal upgrade preserves your settings and battery history, moves them to the new **Hyper Battery Monitor** data directory, updates the Start with Windows registration when enabled, and removes the previous application directory after the migration succeeds.

The Microsoft Store/MSIX package keeps its existing technical package identity so updates remain compatible while displaying the new **Hyper Battery Monitor** name.
