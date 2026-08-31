namespace Videotoy.Media;

public sealed class AudioTrack
{
    public required float[] MonoSamples { get; init; }

    public required int SampleRate { get; init; }

    public required double DurationSeconds { get; init; }
}
