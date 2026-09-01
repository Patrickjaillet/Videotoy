using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Videotoy.Media;

/// <summary>
/// Persists a browsable log of recent video exports to
/// <c>%AppData%\Videotoy\export-history.json</c>, mirroring
/// <see cref="LoopSettingsService"/>'s prune-by-age pattern (rather than
/// <see cref="ExportPresetService"/>'s named-replace pattern): this is an
/// unbounded, ever-growing log of one entry per completed export, so it is
/// capped at <see cref="MaxEntries"/>, pruning the oldest entries first.
/// </summary>
public sealed class ExportHistoryService
{
    private const int MaxEntries = 200;
    private readonly string _storageFilePath;

    public ExportHistoryService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Videotoy");

        Directory.CreateDirectory(appDataDirectory);
        _storageFilePath = Path.Combine(appDataDirectory, "export-history.json");
    }

    public IReadOnlyList<ExportHistoryEntry> Load()
    {
        if (!File.Exists(_storageFilePath))
        {
            return Array.Empty<ExportHistoryEntry>();
        }

        try
        {
            var json = File.ReadAllText(_storageFilePath);
            var entries = JsonSerializer.Deserialize<List<ExportHistoryEntry>>(json);

            return entries?.OrderByDescending(entry => entry.CompletedUtc).ToList()
                   ?? new List<ExportHistoryEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<ExportHistoryEntry>();
        }
    }

    /// <summary>
    /// Appends <paramref name="entry"/> and prunes entries beyond
    /// <see cref="MaxEntries"/>, oldest (<see cref="ExportHistoryEntry.CompletedUtc"/>)
    /// first, so this file never grows unbounded across a long export
    /// history. Returns the resulting list, newest first.
    /// </summary>
    public IReadOnlyList<ExportHistoryEntry> Append(ExportHistoryEntry entry)
    {
        var entries = Load().ToList();
        entries.Add(entry);

        var trimmed = entries
            .OrderByDescending(e => e.CompletedUtc)
            .Take(MaxEntries)
            .ToList();

        Save(trimmed);
        return trimmed;
    }

    public void Clear()
    {
        Save(Array.Empty<ExportHistoryEntry>());
    }

    private void Save(IReadOnlyList<ExportHistoryEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storageFilePath, json);
    }
}
