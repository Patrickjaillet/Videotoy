using System;
using NAudio.Dsp;

namespace Videotoy.Media;

public sealed class AudioSpectrumTextureGenerator
{
    private const int TextureWidth = 512;
    private const int TextureHeight = 2;
    private const int FftSize = 1024;

    public TextureAsset Generate(AudioTrack track, double timeSeconds)
    {
        var pixels = new byte[TextureWidth * TextureHeight * 4];

        var centerSample = (int)(timeSeconds * track.SampleRate);
        var startSample = centerSample - FftSize / 2;

        var fftBuffer = new Complex[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            var sampleIndex = startSample + i;
            var sampleValue = sampleIndex >= 0 && sampleIndex < track.MonoSamples.Length
                ? track.MonoSamples[sampleIndex]
                : 0f;

            var window = 0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * i / (FftSize - 1));
            fftBuffer[i].X = sampleValue * window;
            fftBuffer[i].Y = 0f;
        }

        FastFourierTransform.FFT(true, (int)Math.Log2(FftSize), fftBuffer);

        for (var x = 0; x < TextureWidth; x++)
        {
            var binIndex = Math.Min(x, FftSize / 2 - 1);
            var magnitude = (float)Math.Sqrt(
                fftBuffer[binIndex].X * fftBuffer[binIndex].X +
                fftBuffer[binIndex].Y * fftBuffer[binIndex].Y);

            var spectrumValue = ToByte(magnitude * 4f);
            WritePixel(pixels, x, 0, spectrumValue);

            var waveSampleIndex = startSample + x * 2;
            var waveSample = waveSampleIndex >= 0 && waveSampleIndex < track.MonoSamples.Length
                ? track.MonoSamples[waveSampleIndex]
                : 0f;

            var waveValue = ToByte(waveSample * 0.5f + 0.5f);
            WritePixel(pixels, x, 1, waveValue);
        }

        return new TextureAsset
        {
            Width = TextureWidth,
            Height = TextureHeight,
            PixelDataBgra = pixels
        };
    }

    private static byte ToByte(float value)
    {
        var clamped = Math.Clamp(value, 0f, 1f);
        return (byte)Math.Round(clamped * 255f);
    }

    private static void WritePixel(byte[] pixels, int x, int y, byte value)
    {
        var offset = (y * TextureWidth + x) * 4;
        pixels[offset] = value;
        pixels[offset + 1] = value;
        pixels[offset + 2] = value;
        pixels[offset + 3] = 255;
    }
}
