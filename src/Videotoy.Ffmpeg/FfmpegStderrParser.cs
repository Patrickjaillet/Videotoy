namespace Videotoy.Ffmpeg;

public enum FfmpegErrorCategory
{
    Unknown,
    UnwritableOutputPath,
    DiskFull,
    UnsupportedCodec,
    InvalidResolution,
    InvalidInputStream
}

public sealed record FfmpegStderrDiagnosis(FfmpegErrorCategory Category, string Summary, string RawTail);

public static class FfmpegStderrParser
{
    private static readonly (string Needle, FfmpegErrorCategory Category, string Summary)[] KnownPatterns =
    {
        ("Permission denied", FfmpegErrorCategory.UnwritableOutputPath,
            "FFmpeg could not write to the output path (permission denied)."),
        ("No such file or directory", FfmpegErrorCategory.UnwritableOutputPath,
            "FFmpeg could not write to the output path (directory does not exist)."),
        ("No space left on device", FfmpegErrorCategory.DiskFull,
            "The destination disk is full."),
        ("Unknown encoder", FfmpegErrorCategory.UnsupportedCodec,
            "The selected video codec is not supported by this FFmpeg build."),
        ("Unrecognized option", FfmpegErrorCategory.UnsupportedCodec,
            "FFmpeg rejected one of the encoding options used for this export."),
        ("Invalid too large or too small dimension", FfmpegErrorCategory.InvalidResolution,
            "The requested export resolution is invalid."),
        ("dimensions not divisible by", FfmpegErrorCategory.InvalidResolution,
            "The requested export resolution is not compatible with the selected codec."),
        ("Invalid data found when processing input", FfmpegErrorCategory.InvalidInputStream,
            "FFmpeg could not process one of the input streams (corrupted or unsupported data)."),
        ("only supports even width and height", FfmpegErrorCategory.InvalidResolution,
            "VP9 requires even width and height for the selected export resolution."),
        ("Codec not currently supported in container", FfmpegErrorCategory.UnsupportedCodec,
            "The selected codec cannot be muxed into the selected container."),
        ("Unable to find a suitable output format", FfmpegErrorCategory.UnsupportedCodec,
            "FFmpeg could not determine an output format for the selected container/codec combination."),
    };

    private const int MaxTailLines = 20;

    public static FfmpegStderrDiagnosis Diagnose(IReadOnlyList<string> stderrTailLines)
    {
        var tail = string.Join(Environment.NewLine, stderrTailLines);

        foreach (var (needle, category, summary) in KnownPatterns)
        {
            if (tail.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return new FfmpegStderrDiagnosis(category, summary, tail);
            }
        }

        var fallbackSummary = stderrTailLines.Count > 0
            ? $"FFmpeg exited with an error: {stderrTailLines[^1]}"
            : "FFmpeg exited with an unspecified error.";

        return new FfmpegStderrDiagnosis(FfmpegErrorCategory.Unknown, fallbackSummary, tail);
    }

    public static int EffectiveMaxTailLines => MaxTailLines;
}
