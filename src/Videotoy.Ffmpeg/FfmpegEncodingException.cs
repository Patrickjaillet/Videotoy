namespace Videotoy.Ffmpeg;

public sealed class FfmpegEncodingException : Exception
{
    public int ExitCode { get; }

    public FfmpegStderrDiagnosis Diagnosis { get; }

    public FfmpegEncodingException(int exitCode, FfmpegStderrDiagnosis diagnosis)
        : base(diagnosis.Summary)
    {
        ExitCode = exitCode;
        Diagnosis = diagnosis;
    }
}
