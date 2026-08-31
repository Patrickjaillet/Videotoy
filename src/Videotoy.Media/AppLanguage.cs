namespace Videotoy.Media;

/// <summary>
/// A language the Videotoy UI can be displayed in. The <see cref="Code"/>
/// matches the base file name of the corresponding resource dictionary
/// under <c>Resources/Localization</c> (e.g. <c>"fr"</c> for
/// <c>fr.json</c>).
/// </summary>
public enum AppLanguage
{
    English,
    French
}

public static class AppLanguageExtensions
{
    public static string ToCode(this AppLanguage language) => language switch
    {
        AppLanguage.French => "fr",
        _ => "en"
    };

    public static AppLanguage FromCode(string? code) => code?.ToLowerInvariant() switch
    {
        "fr" => AppLanguage.French,
        _ => AppLanguage.English
    };
}
