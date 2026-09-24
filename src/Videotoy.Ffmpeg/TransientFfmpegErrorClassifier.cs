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
            // FfmpegService traduit désormais toute rupture du pipe stdin
            // causée par la fin (le plus souvent en échec) du process FFmpeg
            // en FfmpegEncodingException diagnostiquée (voir
            // FfmpegService.ThrowDiagnosedFailureAsync) : une IOException brute
            // qui atteint encore ce classifieur ne provient donc plus d'un
            // disque plein déjà identifié, mais d'une cause non qualifiée —
            // rester conservateur et ne jamais la retenter automatiquement.
            IOException => false,
            FfmpegEncodingException encodingException => encodingException.Diagnosis.Category
                is FfmpegErrorCategory.Unknown or FfmpegErrorCategory.InvalidInputStream,
            _ => false
        };
}
