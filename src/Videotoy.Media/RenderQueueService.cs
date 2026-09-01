using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Videotoy.Media;

/// <summary>
/// Persists the render queue (batch export jobs) to
/// <c>%AppData%\Videotoy\render-queue.json</c>, mirroring
/// <see cref="ExportPresetService"/>'s "replace and re-save the whole list"
/// shape — unlike <see cref="ExportHistoryService"/>'s append-only log, the
/// queue is a small, actively-mutated ordered list (add/remove/reorder/
/// status changes), so every mutation simply re-saves the full list.
/// </summary>
public sealed class RenderQueueService
{
    private readonly string _storageFilePath;

    public RenderQueueService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Videotoy");

        Directory.CreateDirectory(appDataDirectory);
        _storageFilePath = Path.Combine(appDataDirectory, "render-queue.json");
    }

    /// <summary>
    /// Loads the queue ordered by <see cref="RenderQueueItem.SortOrder"/>.
    /// Any item found with <see cref="RenderQueueItemStatus.Running"/> is
    /// coerced back to <see cref="RenderQueueItemStatus.Pending"/> and
    /// persisted immediately: the process that was running it no longer
    /// exists (the app was closed or killed mid-export), so a stale
    /// "Running" status would otherwise linger forever and never be
    /// retried. Empty on a missing or corrupt file.
    /// </summary>
    public IReadOnlyList<RenderQueueItem> Load()
    {
        var entries = LoadRaw();

        var hadStaleRunningItem = false;
        foreach (var entry in entries)
        {
            if (entry.Status == RenderQueueItemStatus.Running)
            {
                entry.Status = RenderQueueItemStatus.Pending;
                hadStaleRunningItem = true;
            }
        }

        var ordered = entries.OrderBy(entry => entry.SortOrder).ToList();

        if (hadStaleRunningItem)
        {
            Save(ordered);
        }

        return ordered;
    }

    /// <summary>
    /// Appends <paramref name="item"/> with a <see cref="RenderQueueItem.SortOrder"/>
    /// placing it last, saves, and returns the resulting ordered list.
    /// </summary>
    public IReadOnlyList<RenderQueueItem> Add(RenderQueueItem item)
    {
        var entries = LoadRaw();
        var nextSortOrder = entries.Count == 0 ? 0 : entries.Max(entry => entry.SortOrder) + 1;
        item.SortOrder = nextSortOrder;

        entries.Add(item);
        var ordered = entries.OrderBy(entry => entry.SortOrder).ToList();
        Save(ordered);
        return ordered;
    }

    public IReadOnlyList<RenderQueueItem> Remove(Guid itemId)
    {
        var entries = LoadRaw()
            .Where(entry => entry.Id != itemId)
            .ToList();

        var ordered = entries.OrderBy(entry => entry.SortOrder).ToList();
        Save(ordered);
        return ordered;
    }

    /// <summary>
    /// Rewrites <see cref="RenderQueueItem.SortOrder"/> so the queue's
    /// persisted order matches <paramref name="orderedItemIds"/>. Items not
    /// present in <paramref name="orderedItemIds"/> (should not normally
    /// happen) are appended after, in their previous relative order.
    /// </summary>
    public IReadOnlyList<RenderQueueItem> Reorder(IReadOnlyList<Guid> orderedItemIds)
    {
        var entries = LoadRaw();
        var entriesById = entries.ToDictionary(entry => entry.Id);

        var sortOrder = 0;
        var reordered = new List<RenderQueueItem>();

        foreach (var itemId in orderedItemIds)
        {
            if (entriesById.Remove(itemId, out var entry))
            {
                entry.SortOrder = sortOrder++;
                reordered.Add(entry);
            }
        }

        foreach (var remaining in entries.Where(entry => entriesById.ContainsKey(entry.Id)))
        {
            remaining.SortOrder = sortOrder++;
            reordered.Add(remaining);
        }

        Save(reordered);
        return reordered;
    }

    public IReadOnlyList<RenderQueueItem> UpdateStatus(Guid itemId, RenderQueueItemStatus status, string? errorSummary = null)
    {
        var entries = LoadRaw();
        var entry = entries.FirstOrDefault(entry => entry.Id == itemId);

        if (entry is not null)
        {
            entry.Status = status;
            entry.ErrorSummary = errorSummary;
        }

        var ordered = entries.OrderBy(entry => entry.SortOrder).ToList();
        Save(ordered);
        return ordered;
    }

    private List<RenderQueueItem> LoadRaw()
    {
        if (!File.Exists(_storageFilePath))
        {
            return new List<RenderQueueItem>();
        }

        try
        {
            var json = File.ReadAllText(_storageFilePath);
            var entries = JsonSerializer.Deserialize<List<RenderQueueItem>>(json);
            return entries ?? new List<RenderQueueItem>();
        }
        catch (JsonException)
        {
            return new List<RenderQueueItem>();
        }
    }

    private void Save(IReadOnlyList<RenderQueueItem> entries)
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storageFilePath, json);
    }
}
