using System;
using System.IO;
using System.Text.Json;

namespace Videotoy.Media;

/// <summary>
/// Persists the preview viewport background preference (Phase v2.1.0's
/// deferred item), mirroring the storage pattern already used by
/// <see cref="OnboardingStateService"/>: a single flat state, not a keyed
/// collection, since it is a whole-application concept rather than
/// per-shader state.
/// </summary>
public sealed class ViewportBackgroundSettingsService
{
    private readonly string _storageFilePath;

    public ViewportBackgroundSettingsService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Videotoy");

        Directory.CreateDirectory(appDataDirectory);
        _storageFilePath = Path.Combine(appDataDirectory, "viewport-background.json");
    }

    public ViewportBackgroundState Load()
    {
        if (!File.Exists(_storageFilePath))
        {
            return new ViewportBackgroundState();
        }

        try
        {
            var json = File.ReadAllText(_storageFilePath);
            return JsonSerializer.Deserialize<ViewportBackgroundState>(json) ?? new ViewportBackgroundState();
        }
        catch (JsonException)
        {
            return new ViewportBackgroundState();
        }
    }

    public void Save(ViewportBackgroundState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storageFilePath, json);
    }
}
