namespace Videotoy.Ffmpeg;

public sealed class FfmpegIntegrityException : Exception
{
    public FfmpegIntegrityException(string message)
        : base(message)
    {
    }

    public FfmpegIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
