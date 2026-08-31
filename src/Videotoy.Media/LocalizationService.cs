using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Videotoy.Media;

/// <summary>
/// Loads the flat key/value UI string tables from
/// <c>Resources/Localization/{code}.json</c>, exposes the currently active
/// <see cref="AppLanguage"/>, and raises <see cref="LanguageChanged"/> so
/// bound UI can refresh instantly when <see cref="SetLanguage"/> is called
/// (no application restart required). The last explicit user selection is
/// persisted in <c>%AppData%\Videotoy\language-settings.json</c>, mirroring
/// the storage pattern used by <see cref="RecentFilesService"/> and
/// <see cref="LoopSettingsService"/>. When no explicit selection has ever
/// been made, the operating system UI language is used as the initial
/// language on first launch.
/// </summary>
public sealed class LocalizationService
{
    private static readonly AppLanguage[] SupportedLanguages =
        { AppLanguage.English, AppLanguage.French };

    private readonly string _resourcesDirectory;
    private readonly string _storageFilePath;
    private readonly Dictionary<AppLanguage, IReadOnlyDictionary<string, string>> _cache = new();

    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();

    public event EventHandler? LanguageChanged;

    public AppLanguage CurrentLanguage { get; private set; }

    public LocalizationService()
        : this(ResolveDefaultResourcesDirectory())
    {
    }

    /// <summary>
    /// Overload used by tests to point at an arbitrary resources directory
    /// instead of the one shipped next to the application executable.
    /// </summary>
    public LocalizationService(string resourcesDirectory)
    {
        _resourcesDirectory = resourcesDirectory;

        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Videotoy");

        Directory.CreateDirectory(appDataDirectory);
        _storageFilePath = Path.Combine(appDataDirectory, "language-settings.json");

        CurrentLanguage = ResolveInitialLanguage();
        _strings = LoadLanguage(CurrentLanguage);
    }

    public static IReadOnlyList<AppLanguage> AvailableLanguages => SupportedLanguages;

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>, or the key
    /// itself (so missing translations remain visible/debuggable instead of
    /// silently disappearing) when no entry is found.
    /// </summary>
    public string GetString(string key)
    {
        return _strings.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>
    /// Returns the localized string for <paramref name="key"/> used as a
    /// composite format string (e.g. <c>"Frame {0}"</c>), formatted with
    /// <paramref name="args"/>.
    /// </summary>
    public string GetFormattedString(string key, params object?[] args)
    {
        var format = GetString(key);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    /// <summary>
    /// Switches the active language immediately and raises
    /// <see cref="LanguageChanged"/> so every <c>loc:Loc</c> /
    /// <c>loc:LocFormat</c> binding in the UI re-evaluates without a
    /// restart. The selection is persisted as an explicit user choice.
    /// </summary>
    public void SetLanguage(AppLanguage language, bool persistAsUserSelection = true)
    {
        if (language == CurrentLanguage && _strings.Count > 0)
        {
            return;
        }

        CurrentLanguage = language;
        _strings = LoadLanguage(language);

        if (persistAsUserSelection)
        {
            Save(new LanguageSettingsEntry
            {
                LanguageCode = language.ToCode(),
                IsUserSelected = true
            });
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private AppLanguage ResolveInitialLanguage()
    {
        var saved = Load();

        if (saved is { IsUserSelected: true })
        {
            return AppLanguageExtensions.FromCode(saved.LanguageCode);
        }

        var detected = DetectSystemLanguage();

        Save(new LanguageSettingsEntry
        {
            LanguageCode = detected.ToCode(),
            IsUserSelected = false
        });

        return detected;
    }

    /// <summary>
    /// Maps the current OS UI culture to a supported <see cref="AppLanguage"/>,
    /// falling back to English when the system language isn't one Videotoy
    /// ships translations for.
    /// </summary>
    private static AppLanguage DetectSystemLanguage()
    {
        var systemTwoLetterCode = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;

        return SupportedLanguages.FirstOrDefault(
            language => string.Equals(language.ToCode(), systemTwoLetterCode, StringComparison.OrdinalIgnoreCase),
            AppLanguage.English);
    }

    private IReadOnlyDictionary<string, string> LoadLanguage(AppLanguage language)
    {
        if (_cache.TryGetValue(language, out var cached))
        {
            return cached;
        }

        var filePath = Path.Combine(_resourcesDirectory, $"{language.ToCode()}.json");

        var loaded = File.Exists(filePath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(filePath))
              ?? new Dictionary<string, string>()
            : new Dictionary<string, string>();

        _cache[language] = loaded;
        return loaded;
    }

    private LanguageSettingsEntry? Load()
    {
        if (!File.Exists(_storageFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_storageFilePath);
            return JsonSerializer.Deserialize<LanguageSettingsEntry>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Save(LanguageSettingsEntry entry)
    {
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storageFilePath, json);
    }

    private static string ResolveDefaultResourcesDirectory()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDirectory, "Resources", "Localization");
    }
}
