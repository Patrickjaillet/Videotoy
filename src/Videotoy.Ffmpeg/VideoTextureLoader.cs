namespace Videotoy.Ffmpeg;

/// <summary>
/// Façade publique du décodage vidéo déterministe utilisé pour les
/// <c>iChannel</c> de type vidéo : sondage des métadonnées (une fois par
/// fichier, mise en cache par instance) et lecture de frame par timestamp,
/// via <see cref="VideoFrameCache"/> pour éviter de relancer FFmpeg à
/// chaque appel pour la même frame résolue.
/// </summary>
public sealed class VideoTextureLoader
{
    private readonly VideoProber _prober;
    private readonly VideoFrameDecoder _decoder;
    private readonly VideoFrameCache _cache = new();
    private readonly Dictionary<string, VideoProbeResult> _probeCache = new(StringComparer.OrdinalIgnoreCase);

    public VideoTextureLoader(VideoProber prober, VideoFrameDecoder decoder)
    {
        _prober = prober;
        _decoder = decoder;
    }

    public async Task<VideoProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (_probeCache.TryGetValue(filePath, out var cached))
        {
            return cached;
        }

        var probe = await _prober.ProbeAsync(filePath, cancellationToken).ConfigureAwait(false);
        _probeCache[filePath] = probe;
        return probe;
    }

    /// <summary>
    /// Retourne les pixels BGRA (redimensionnés à <paramref name="targetWidth"/>
    /// x <paramref name="targetHeight"/>) de la frame la plus proche de
    /// <paramref name="timestampSeconds"/>, telle que résolue par le fps
    /// sondé du fichier. Nécessite un appel préalable à
    /// <see cref="ProbeAsync"/> pour ce fichier (le fps sondé pilote la
    /// résolution de l'index de frame utilisé comme clé de cache).
    /// </summary>
    public async Task<byte[]> GetFramePixelsBgraAsync(
        string filePath,
        double timestampSeconds,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken = default)
    {
        var probe = await ProbeAsync(filePath, cancellationToken).ConfigureAwait(false);
        var frameIndex = (int)Math.Round(timestampSeconds * probe.FrameRate);
        var key = new VideoFrameKey(filePath, frameIndex);

        return await _cache.GetOrDecodeAsync(key, () =>
            _decoder.DecodeFrameBgraAsync(filePath, timestampSeconds, targetWidth, targetHeight, cancellationToken)
        ).ConfigureAwait(false);
    }
}
