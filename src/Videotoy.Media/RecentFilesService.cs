using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Videotoy.Media;

public sealed class RecentFilesService
{
    private const int MaxEntries = 10;
    private readonly string _storageFilePath;

    public RecentFilesService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Videotoy");

        Directory.CreateDirectory(appDataDirectory);
        _storageFilePath = Path.Combine(appDataDirectory, "recent-shaders.json");
    }

    public IReadOnlyList<RecentShaderFile> Load()
    {
        if (!File.Exists(_storageFilePath))
        {
            return Array.Empty<RecentShaderFile>();
        }

        try
        {
            var json = File.ReadAllText(_storageFilePath);
            var entries = JsonSerializer.Deserialize<List<RecentShaderFile>>(json);

            return entries?.Where(entry => File.Exists(entry.FilePath)).ToList()
                   ?? new List<RecentShaderFile>();
        }
        catch (JsonException)
        {
            return Array.Empty<RecentShaderFile>();
        }
    }

    public IReadOnlyList<RecentShaderFile> AddOrPromote(string filePath)
    {
        var entries = Load()
            .Where(entry => !string.Equals(entry.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        entries.Insert(0, new RecentShaderFile
        {
            FilePath = filePath,
            LastOpenedUtc = DateTime.UtcNow
        });

        var trimmed = entries.Take(MaxEntries).ToList();
        Save(trimmed);
        return trimmed;
    }

    private void Save(IReadOnlyList<RecentShaderFile> entries)
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storageFilePath, json);
    }
}
