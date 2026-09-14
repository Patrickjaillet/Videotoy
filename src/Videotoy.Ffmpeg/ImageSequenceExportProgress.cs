namespace Videotoy.Ffmpeg;

/// <summary>
/// Progression d'un export séquence d'images, structurellement identique à
/// <see cref="VideoExportProgress"/> (mêmes conventions
/// <c>ProgressFraction</c>/<c>EstimatedRemainingSeconds</c> dérivées de
/// <see cref="Videotoy.Core.ExportProgressEstimator"/>) mais son propre type
/// : une séquence d'images n'a pas de notion de "passe" (contrairement au
/// mode deux passes vidéo ou GIF palettegen/paletteuse), donc
/// <see cref="TotalFrameCount"/> est toujours le nombre réel de frames de la
/// séquence, jamais un nombre d'"unités de travail" doublé.
/// </summary>
public sealed record ImageSequenceExportProgress(int FramesCompleted, int TotalFrameCount, double ElapsedSeconds)
{
    public double ProgressFraction =>
        Videotoy.Core.ExportProgressEstimator.progressFraction(FramesCompleted, TotalFrameCount);

    public double? EstimatedRemainingSeconds
    {
        get
        {
            var estimate = Videotoy.Core.ExportProgressEstimator.estimateRemainingSeconds(
                ElapsedSeconds, FramesCompleted, TotalFrameCount);
            return estimate.HasValue ? estimate.Value : null;
        }
    }
}
