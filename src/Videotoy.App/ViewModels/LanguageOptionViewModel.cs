using Videotoy.Media;

namespace Videotoy.App.ViewModels;

/// <summary>
/// A single entry in the About window's language selector. The
/// <see cref="DisplayName"/> is always shown in its own language (e.g.
/// "Français", "English") rather than being translated, so the option
/// remains readable to someone who doesn't yet understand the currently
/// active UI language.
/// </summary>
public sealed class LanguageOptionViewModel
{
    public LanguageOptionViewModel(AppLanguage language, string displayName)
    {
        Language = language;
        DisplayName = displayName;
    }

    public AppLanguage Language { get; }

    public string DisplayName { get; }
}
