namespace Videotoy.Ffmpeg;

public sealed record VideoExportProgress(int FramesCompleted, int TotalFrameCount, double ElapsedSeconds)
{
    public int CurrentFrameNumber => FramesCompleted;

    public int TotalFrames => TotalFrameCount;

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
