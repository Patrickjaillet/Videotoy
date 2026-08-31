using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Videotoy.Media;

/// <summary>
/// Persists named <see cref="ExportPreset"/> snapshots of the render
/// settings panel to <c>%AppData%\Videotoy\export-presets.json</c>, mirroring
/// the storage pattern already used by <see cref="RecentFilesService"/> for
/// recently opened shaders.
/// </summary>
public sealed class ExportPresetService
{
    private readonly string _storageFilePath;

    public ExportPresetService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Videotoy");

        Directory.CreateDirectory(appDataDirectory);
        _storageFilePath = Path.Combine(appDataDirectory, "export-presets.json");
    }

    public IReadOnlyList<ExportPreset> Load()
    {
        if (!File.Exists(_storageFilePath))
        {
            return Array.Empty<ExportPreset>();
        }

        try
        {
            var json = File.ReadAllText(_storageFilePath);
            var entries = JsonSerializer.Deserialize<List<ExportPreset>>(json);

            return entries?.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList()
                   ?? new List<ExportPreset>();
        }
        catch (JsonException)
        {
            return Array.Empty<ExportPreset>();
        }
    }

    /// <summary>
    /// Saves <paramref name="preset"/> under its <see cref="ExportPreset.Name"/>,
    /// replacing any existing preset with the same name (case-insensitive)
    /// rather than accumulating duplicates.
    /// </summary>
    public IReadOnlyList<ExportPreset> SaveOrReplace(ExportPreset preset)
    {
        var entries = Load()
            .Where(entry => !string.Equals(entry.Name, preset.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        entries.Add(preset);

        var sorted = entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Save(sorted);
        return sorted;
    }

    public IReadOnlyList<ExportPreset> Delete(string presetName)
    {
        var entries = Load()
            .Where(entry => !string.Equals(entry.Name, presetName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Save(entries);
        return entries;
    }

    private void Save(IReadOnlyList<ExportPreset> entries)
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storageFilePath, json);
    }
}
