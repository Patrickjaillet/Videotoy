namespace Videotoy.Media;

/// <summary>
/// Serializable snapshot of the user's language preference, persisted in
/// <c>%AppData%\Videotoy\language-settings.json</c>.
/// </summary>
public sealed class LanguageSettingsEntry
{
    public string LanguageCode { get; set; } = "en";

    public bool IsUserSelected { get; set; }
}
