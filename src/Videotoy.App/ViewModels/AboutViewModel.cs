using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videotoy.Core;
using Videotoy.Media;

namespace Videotoy.App.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly LocalizationService _localizationService;
    private bool _isApplyingCurrentLanguage;

    public string ApplicationName => "Videotoy";

    public string Version => $"v{Videotoy.Core.Version.SemVer}";

    public string Copyright => "Copyright © 2026 Patrick JAILLET";

    public string Email => "sandefjord.development@proton.me";

    public string Website => "https://patrickjaillet.github.io/Videotoy";

    public ObservableCollection<LanguageOptionViewModel> AvailableLanguages { get; } = new();

    [ObservableProperty]
    private LanguageOptionViewModel? _selectedLanguage;

    public AboutViewModel(LocalizationService localizationService)
    {
        _localizationService = localizationService;

        foreach (var language in LocalizationService.AvailableLanguages)
        {
            AvailableLanguages.Add(new LanguageOptionViewModel(language, DisplayNameFor(language)));
        }

        SyncSelectedLanguageFromService();
    }

    partial void OnSelectedLanguageChanged(LanguageOptionViewModel? value)
    {
        if (_isApplyingCurrentLanguage || value is null)
        {
            return;
        }

        _localizationService.SetLanguage(value.Language);
    }

    private void SyncSelectedLanguageFromService()
    {
        _isApplyingCurrentLanguage = true;

        foreach (var option in AvailableLanguages)
        {
            if (option.Language == _localizationService.CurrentLanguage)
            {
                SelectedLanguage = option;
                break;
            }
        }

        _isApplyingCurrentLanguage = false;
    }

    /// <summary>
    /// Each language's name is shown in that language itself (never
    /// translated into the currently active UI language), so the selector
    /// stays legible to someone switching away from a language they can't
    /// read.
    /// </summary>
    private static string DisplayNameFor(AppLanguage language) => language switch
    {
        AppLanguage.French => "Français",
        _ => "English"
    };

    [RelayCommand]
    private void OpenWebsite()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Website,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void CopyEmail()
    {
        System.Windows.Clipboard.SetText(Email);
    }
}
