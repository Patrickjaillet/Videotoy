using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Videotoy.Media;

/// <summary>
/// Persists, per shader file, the last-used "Duration Mode" selection
/// (manual duration vs. seamless loop) and the last loop duration used —
/// so reopening a shader restores the duration mode state the user left it
/// in, rather than always resetting to the application defaults. Mirrors
/// the storage pattern already used by <see cref="RecentFilesService"/> and
/// <see cref="ExportPresetService"/>.
/// </summary>
public sealed class LoopSettingsService
{
    private const int MaxEntries = 200;
    private readonly string _storageFilePath;

    public LoopSettingsService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Videotoy");

        Directory.CreateDirectory(appDataDirectory);
        _storageFilePath = Path.Combine(appDataDirectory, "loop-settings.json");
    }

    private IReadOnlyList<LoopSettingsEntry> LoadAll()
    {
        if (!File.Exists(_storageFilePath))
        {
            return Array.Empty<LoopSettingsEntry>();
        }

        try
        {
            var json = File.ReadAllText(_storageFilePath);
            var entries = JsonSerializer.Deserialize<List<LoopSettingsEntry>>(json);
            return entries ?? new List<LoopSettingsEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<LoopSettingsEntry>();
        }
    }

    /// <summary>
    /// Returns the persisted entry for <paramref name="shaderFilePath"/>, or
    /// <c>null</c> if this shader has never had its duration mode saved
    /// before (first time it's opened, or it was opened only with an older
    /// version predating this feature).
    /// </summary>
    public LoopSettingsEntry? TryGet(string shaderFilePath)
    {
        return LoadAll().FirstOrDefault(entry =>
            string.Equals(entry.ShaderFilePath, shaderFilePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Saves or replaces the entry for <paramref name="shaderFilePath"/>.
    /// Entries beyond <see cref="MaxEntries"/> are pruned, oldest
    /// (<see cref="LoopSettingsEntry.LastUpdatedUtc"/>) first, so this file
    /// never grows unbounded across a long history of distinct shader files.
    /// </summary>
    public void SaveOrReplace(string shaderFilePath, bool isSeamlessLoopModeEnabled, double loopDurationSeconds)
    {
        var entries = LoadAll()
            .Where(entry => !string.Equals(entry.ShaderFilePath, shaderFilePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        entries.Add(new LoopSettingsEntry
        {
            ShaderFilePath = shaderFilePath,
            IsSeamlessLoopModeEnabled = isSeamlessLoopModeEnabled,
            LoopDurationSeconds = loopDurationSeconds
        });

        var trimmed = entries
            .OrderByDescending(entry => entry.LastUpdatedUtc)
            .Take(MaxEntries)
            .ToList();

        var json = JsonSerializer.Serialize(trimmed, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storageFilePath, json);
    }
}
