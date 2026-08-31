using System;
using System.ComponentModel;
using Videotoy.Media;

namespace Videotoy.App.Localization;

/// <summary>
/// Bridges the DI-registered <see cref="LocalizationService"/> singleton to
/// XAML markup extensions, which are instantiated by the XAML parser
/// without going through the application's <see cref="IServiceProvider"/>.
/// <see cref="Attach"/> is called once from <c>App.OnStartup</c>, before
/// any window is constructed.
/// </summary>
public static class LocalizationRuntime
{
    private static LocalizationService? _service;

    public static LocalizationService Service =>
        _service ?? throw new InvalidOperationException(
            "LocalizationRuntime.Attach must be called during application startup, before any window is constructed.");

    public static void Attach(LocalizationService service)
    {
        _service = service;
    }
}

/// <summary>
/// Exposes every localization key as an indexer property
/// (<c>this["menu.file"]</c>) so a plain WPF <c>Binding</c> can target a
/// specific key and react to <see cref="LocalizationService.LanguageChanged"/>
/// via the WPF-standard "Item[]" indexer change notification — the same
/// mechanism used for <c>ObservableCollection</c> indexers.
/// </summary>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    public static readonly LocalizedStrings Instance = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// WPF's well-known indexer change notification name, understood by the
    /// binding engine as "any indexed value on this source may have changed".
    /// </summary>
    private const string IndexerPropertyName = "Item[]";

    private LocalizedStrings()
    {
        LocalizationRuntime.Service.LanguageChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerPropertyName));
    }

    public string this[string key] => LocalizationRuntime.Service.GetString(key);
}
