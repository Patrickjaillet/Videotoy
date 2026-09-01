namespace Videotoy.Ffmpeg;

/// <summary>
/// Métadonnées d'une vidéo source utilisée comme <c>iChannel</c>, extraites
/// une seule fois par <see cref="VideoProber"/> à partir de la bannière de
/// démarrage de FFmpeg (aucun ffprobe n'est embarqué avec ce projet).
/// </summary>
public sealed record VideoProbeResult(int Width, int Height, double DurationSeconds, double FrameRate);
