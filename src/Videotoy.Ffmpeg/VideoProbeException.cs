namespace Videotoy.Ffmpeg;

/// <summary>
/// Levée lorsque la bannière de démarrage de FFmpeg pour un fichier vidéo
/// source ne peut pas être interprétée (format non reconnu, fichier
/// corrompu, etc.). Porte la fin du flux stderr brut pour diagnostic,
/// même convention que <see cref="FfmpegEncodingException"/>.
/// </summary>
public sealed class VideoProbeException : Exception
{
    public string RawStderrTail { get; }

    public VideoProbeException(string message, string rawStderrTail)
        : base(message)
    {
        RawStderrTail = rawStderrTail;
    }
}
