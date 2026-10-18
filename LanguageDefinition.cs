namespace HyperXBatteryTray.Settings;

public enum AppLanguage
{
    English = 0,
    PortugueseBrazil = 1,
    Spanish = 2,
    Ukrainian = 3,
    German = 4,
    French = 5,
    Polish = 6,
    Russian = 7,
    ChineseSimplified = 8,
    Japanese = 9,
    Korean = 10
}

internal sealed class LanguageDefinition
{
    public LanguageDefinition(
        AppLanguage language,
        string resourceFileName,
        string flag,
        string nativeName,
        string? twoLetterIsoCode = null,
        params string[] culturePrefixes)
    {
        Language = language;
        ResourceFileName = resourceFileName;
        Flag = flag;
        NativeName = nativeName;
        TwoLetterIsoCode = twoLetterIsoCode;
        CulturePrefixes = culturePrefixes;
    }

    public AppLanguage Language { get; }
    public string ResourceFileName { get; }
    public string Flag { get; }
    public string NativeName { get; }
    public string? TwoLetterIsoCode { get; }
    public IReadOnlyList<string> CulturePrefixes { get; }
    public string DisplayName => $"{Flag} {NativeName}";
}
