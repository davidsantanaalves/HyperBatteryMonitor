using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperXBatteryTray.Devices;

namespace HyperXBatteryTray.Settings;

public static class Localization
{
    private const string ResourcePrefix = "HyperXBatteryTray.Languages.";

    private static readonly Regex FormatPlaceholderRegex = new(
        @"(?:\{\{|\}\})|\{([0-9]+) *(?:,[^}:]+)?(?::[^}]*)?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly LanguageDefinition[] LanguageDefinitions =
    {
        new(AppLanguage.English, "en-US.json", "🇺🇸", "English", "en"),
        new(AppLanguage.PortugueseBrazil, "pt-BR.json", "🇧🇷", "Português (Brasil)", "pt"),
        new(AppLanguage.Spanish, "es.json", "🇪🇸", "Español", "es"),

        // Ukrainian localization contributed by sladkOy and reviewed against the v2.3.0 key set.
        new(AppLanguage.Ukrainian, "uk-UA.json", "🇺🇦", "Українська", "uk"),

        new(AppLanguage.German, "de-DE.json", "🇩🇪", "Deutsch", "de"),
        new(AppLanguage.French, "fr-FR.json", "🇫🇷", "Français", "fr"),
        new(AppLanguage.Polish, "pl-PL.json", "🇵🇱", "Polski", "pl"),
        new(AppLanguage.Russian, "ru-RU.json", "🇷🇺", "Русский", "ru"),
        new(
            AppLanguage.ChineseSimplified,
            "zh-CN.json",
            "🇨🇳",
            "简体中文",
            null,
            "zh-CN",
            "zh-SG",
            "zh-Hans"),
        new(AppLanguage.Japanese, "ja-JP.json", "🇯🇵", "日本語", "ja"),
        new(AppLanguage.Korean, "ko-KR.json", "🇰🇷", "한국어", "ko")
    };

    private static readonly AppLanguage[] SupportedLanguageValues =
        LanguageDefinitions
            .Select(definition => definition.Language)
            .ToArray();

    private static readonly IReadOnlyDictionary<AppLanguage, LanguageDefinition>
        DefinitionsByLanguage = LanguageDefinitions.ToDictionary(
            definition => definition.Language);

    private static readonly IReadOnlyDictionary<
        AppLanguage,
        IReadOnlyDictionary<string, string>> TranslationCatalogs =
            LoadTranslationCatalogs();

    public static IReadOnlyList<AppLanguage> SupportedLanguages => SupportedLanguageValues;

    public static string Get(string key, AppLanguage language)
    {
        if (TranslationCatalogs.TryGetValue(language, out IReadOnlyDictionary<string, string>? catalog) &&
            catalog.TryGetValue(key, out string? localized))
        {
            return localized;
        }

        return GetEnglishOrKey(key);
    }

    public static string LanguageDisplay(AppLanguage language)
    {
        return DefinitionsByLanguage.TryGetValue(language, out LanguageDefinition? definition)
            ? definition.DisplayName
            : DefinitionsByLanguage[AppLanguage.English].DisplayName;
    }

    public static int LanguageIndex(AppLanguage language)
    {
        int index = Array.IndexOf(SupportedLanguageValues, language);
        return index >= 0 ? index : 0;
    }

    public static AppLanguage LanguageAt(int index)
    {
        return index >= 0 && index < SupportedLanguageValues.Length
            ? SupportedLanguageValues[index]
            : AppLanguage.English;
    }

    public static AppLanguage DetectLanguage(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        foreach (LanguageDefinition definition in LanguageDefinitions)
        {
            foreach (string prefix in definition.CulturePrefixes)
            {
                if (culture.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return definition.Language;
            }
        }

        foreach (LanguageDefinition definition in LanguageDefinitions)
        {
            if (definition.TwoLetterIsoCode != null &&
                string.Equals(
                    culture.TwoLetterISOLanguageName,
                    definition.TwoLetterIsoCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return definition.Language;
            }
        }

        return AppLanguage.English;
    }

    public static string DeviceDisplayName(string? deviceName, AppLanguage language)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return string.Empty;

        string canonicalName = HyperXDeviceManager.NormalizeSupportedDeviceName(deviceName);

        // Keep the persisted device identity unchanged; simplify only the user-visible label.
        // Hide connection-specific suffixes without changing persisted device identity.
        if (string.Equals(canonicalName, "HyperX Cloud Alpha 2 Wireless", StringComparison.OrdinalIgnoreCase))
            return "HyperX Cloud Alpha 2";
        if (string.Equals(canonicalName, "HyperX Cloud Stinger 3 Wireless", StringComparison.OrdinalIgnoreCase))
            return "HyperX Cloud Stinger 3";
        if (string.Equals(canonicalName, "HyperX Cloud Stinger Core Wireless + 7.1", StringComparison.OrdinalIgnoreCase))
            return "HyperX Cloud Stinger Core + 7.1";

        return HyperXDeviceManager.RequiresDedicatedDongle(canonicalName)
            ? string.Format(Get("DedicatedDongleDeviceFormat", language), canonicalName)
            : canonicalName;
    }

    private static IReadOnlyDictionary<
        AppLanguage,
        IReadOnlyDictionary<string, string>> LoadTranslationCatalogs()
    {
        Dictionary<AppLanguage, IReadOnlyDictionary<string, string>> catalogs = new();

        LanguageDefinition englishDefinition =
            DefinitionsByLanguage[AppLanguage.English];

        Dictionary<string, string> english = LoadCatalog(englishDefinition);
        foreach ((string key, string value) in english)
        {
            try
            {
                CompositeFormat.Parse(value);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"Invalid canonical English format string: {englishDefinition.ResourceFileName} / {key}",
                    ex);
            }
        }
        catalogs[AppLanguage.English] = english;

        foreach (LanguageDefinition definition in LanguageDefinitions)
        {
            if (definition.Language == AppLanguage.English)
                continue;

            Dictionary<string, string> localized = LoadCatalog(definition);
            ValidateCatalog(definition, localized, english);
            catalogs[definition.Language] = localized;
        }

        return catalogs;
    }

    private static Dictionary<string, string> LoadCatalog(LanguageDefinition definition)
    {
        string resourceName = ResourcePrefix + definition.ResourceFileName;

        try
        {
            Assembly assembly = typeof(Localization).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                Debug.WriteLine(
                    $"Localization resource was not found: {resourceName}");
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            using JsonDocument document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                Debug.WriteLine(
                    $"Localization resource must contain a JSON object: {resourceName}");
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            Dictionary<string, string> catalog = new(StringComparer.Ordinal);

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    Debug.WriteLine(
                        $"Localization value must be a string: {resourceName} / {property.Name}");
                    continue;
                }

                string value = property.Value.GetString() ?? string.Empty;

                if (!catalog.TryAdd(property.Name, value))
                {
                    Debug.WriteLine(
                        $"Duplicate localization key ignored: {resourceName} / {property.Name}");
                }
            }

            return catalog;
        }
        catch (Exception ex) when (
            ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(
                $"Could not load localization resource {resourceName}: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void ValidateCatalog(
        LanguageDefinition definition,
        Dictionary<string, string> localized,
        IReadOnlyDictionary<string, string> english)
    {
        foreach (string key in localized.Keys.ToArray())
        {
            if (!english.TryGetValue(key, out string? englishValue))
            {
                Debug.WriteLine(
                    $"Localization key is not defined in English and will be ignored: " +
                    $"{definition.ResourceFileName} / {key}");
                localized.Remove(key);
                continue;
            }

            if (!HasMatchingFormatPlaceholders(englishValue, localized[key]))
            {
                Debug.WriteLine(
                    $"Localization format string is malformed or placeholders do not match English; English fallback will be used: " +
                    $"{definition.ResourceFileName} / {key}");
                localized.Remove(key);
            }
        }

        foreach (string key in english.Keys)
        {
            if (!localized.ContainsKey(key))
            {
                Debug.WriteLine(
                    $"Localization key is missing; English fallback will be used: " +
                    $"{definition.ResourceFileName} / {key}");
            }
        }
    }

    private static bool HasMatchingFormatPlaceholders(string english, string localized)
    {
        // Syntax belongs to the native parser; the regex only extracts argument indices.
        CompositeFormat englishFormat = CompositeFormat.Parse(english);
        CompositeFormat localizedFormat;
        try
        {
            localizedFormat = CompositeFormat.Parse(localized);
        }
        catch (FormatException)
        {
            return false;
        }

        if (englishFormat.MinimumArgumentCount != localizedFormat.MinimumArgumentCount)
            return false;

        int[] englishPlaceholders = ExtractFormatPlaceholders(english);
        int[] localizedPlaceholders = ExtractFormatPlaceholders(localized);

        return englishPlaceholders.SequenceEqual(localizedPlaceholders);
    }

    private static int[] ExtractFormatPlaceholders(string text)
    {
        return FormatPlaceholderRegex
            .Matches(text)
            .Cast<Match>()
            .Where(match => match.Groups[1].Success)
            .Select(match => int.Parse(
                match.Groups[1].Value,
                CultureInfo.InvariantCulture))
            .OrderBy(index => index)
            .ToArray();
    }

    private static string GetEnglishOrKey(string key)
    {
        return TranslationCatalogs.TryGetValue(
                   AppLanguage.English,
                   out IReadOnlyDictionary<string, string>? english) &&
               english.TryGetValue(key, out string? value)
            ? value
            : key;
    }
}
