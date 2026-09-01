namespace Videotoy.Ffmpeg;

/// <summary>
/// Distingue les erreurs FFmpeg dignes d'une nouvelle tentative
/// automatique (voir <see cref="VideoExportPipeline.RunAsync"/>) des erreurs
/// définitives, qu'une reprise ne résoudrait jamais. Volontairement
/// conservateur : un disque plein, un chemin de sortie inaccessible ou un
/// codec non supporté ne sont jamais transitoires — les retenter ferait
/// perdre du temps à l'utilisateur pour un échec garanti.
/// </summary>
public static class TransientFfmpegErrorClassifier
{
    public static bool IsTransient(Exception exception) =>
        exception switch
        {
            IOException => true,
            FfmpegEncodingException encodingException => encodingException.Diagnosis.Category
                is FfmpegErrorCategory.Unknown or FfmpegErrorCategory.InvalidInputStream,
            _ => false
        };
}
