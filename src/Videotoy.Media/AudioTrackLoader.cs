using System.IO;
using System.Linq;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Videotoy.Media;

public sealed class AudioTrackLoader
{
    public AudioTrack Load(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        using WaveStream reader = extension switch
        {
            ".ogg" => new VorbisWaveReader(filePath),
            _ => new AudioFileReader(filePath)
        };

        var sampleProvider = reader.ToSampleProvider();
        var monoProvider = sampleProvider.WaveFormat.Channels > 1
            ? sampleProvider.ToMono()
            : sampleProvider;

        var samples = new List<float>();
        var buffer = new float[monoProvider.WaveFormat.SampleRate];

        int samplesRead;
        while ((samplesRead = monoProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.Take(samplesRead));
        }

        var sampleRate = monoProvider.WaveFormat.SampleRate;

        return new AudioTrack
        {
            MonoSamples = samples.ToArray(),
            SampleRate = sampleRate,
            DurationSeconds = sampleRate > 0 ? samples.Count / (double)sampleRate : 0.0
        };
    }
}
