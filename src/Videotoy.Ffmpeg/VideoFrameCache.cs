namespace Videotoy.Ffmpeg;

/// <summary>
/// Cache LRU (least-recently-used) de frames vidéo décodées, borné en
/// octets plutôt qu'en nombre de frames — la taille d'une frame varie avec
/// la résolution de rendu, donc une limite en octets reste sensée quelle
/// que soit la résolution utilisée. Non thread-safe : le rendu de ce
/// pipeline est strictement séquentiel, aucune synchronisation n'est
/// nécessaire.
/// </summary>
public sealed class VideoFrameCache
{
    private const long MaxTotalBytes = 256L * 1024 * 1024;

    private readonly Dictionary<VideoFrameKey, LinkedListNode<CacheEntry>> _entriesByKey = new();
    private readonly LinkedList<CacheEntry> _recencyOrder = new();
    private long _totalBytes;

    private readonly record struct CacheEntry(VideoFrameKey Key, byte[] Pixels);

    /// <summary>
    /// Retourne les pixels associés à <paramref name="key"/>, en les
    /// décodant via <paramref name="decode"/> lors d'un cache miss. Un
    /// cache miss doit toujours retourner exactement ce qu'un cache hit
    /// aurait retourné pour la même clé : <paramref name="decode"/> doit
    /// être une fonction pure de <paramref name="key"/>.
    /// </summary>
    public async Task<byte[]> GetOrDecodeAsync(VideoFrameKey key, Func<Task<byte[]>> decode)
    {
        if (_entriesByKey.TryGetValue(key, out var existingNode))
        {
            _recencyOrder.Remove(existingNode);
            _recencyOrder.AddFirst(existingNode);
            return existingNode.Value.Pixels;
        }

        var pixels = await decode().ConfigureAwait(false);

        var entry = new CacheEntry(key, pixels);
        var node = _recencyOrder.AddFirst(entry);
        _entriesByKey[key] = node;
        _totalBytes += pixels.LongLength;

        EvictUntilUnderBudget();

        return pixels;
    }

    private void EvictUntilUnderBudget()
    {
        while (_totalBytes > MaxTotalBytes && _recencyOrder.Last is { } lastNode)
        {
            _totalBytes -= lastNode.Value.Pixels.LongLength;
            _entriesByKey.Remove(lastNode.Value.Key);
            _recencyOrder.RemoveLast();
        }
    }
}
