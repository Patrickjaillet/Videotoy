using System.Diagnostics;
using System.Globalization;

namespace Videotoy.Ffmpeg;

/// <summary>
/// Décode une unique frame BGRA d'un fichier vidéo source à un timestamp
/// donné, via un process <c>ffmpeg.exe</c> de courte durée (contrairement à
/// <see cref="FfmpegService"/>, qui possède un process persistant côté
/// encodage) : chaque décodage relance FFmpeg depuis zéro, <c>-ss</c> placé
/// après <c>-i</c> pour une recherche précise à la frame près plutôt que
/// rapide mais approximative — la précision prime sur la vitesse ici,
/// cohérent avec le reste de ce pipeline déterministe. <c>-vf scale=</c>
/// redimensionne directement à la taille cible pendant le décodage, pour
/// que le texel GPU n'ait jamais à gérer une taille source variable.
/// </summary>
public sealed class VideoFrameDecoder
{
    private readonly FfmpegLocator _locator;

    public VideoFrameDecoder(FfmpegLocator locator)
    {
        _locator = locator;
    }

    public async Task<byte[]> DecodeFrameBgraAsync(
        string filePath,
        double timestampSeconds,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _locator.ResolveExecutablePath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(filePath);
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(Math.Max(0.0, timestampSeconds).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add($"scale={width}:{height}");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("rawvideo");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("bgra");
        startInfo.ArgumentList.Add("pipe:1");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var expectedByteCount = width * height * 4;
        var pixels = new byte[expectedByteCount];

        var stderrDrainTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await ReadExactlyBestEffortAsync(process.StandardOutput.BaseStream, pixels, cancellationToken).ConfigureAwait(false);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stderrText = await stderrDrainTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new VideoProbeException(
                $"FFmpeg failed to decode a frame from '{filePath}' at {timestampSeconds:0.###}s.", stderrText);
        }

        return pixels;
    }

    /// <summary>
    /// Remplit <paramref name="destination"/> avec les octets disponibles
    /// sur <paramref name="stream"/>, sans lever si le flux se termine plus
    /// tôt que prévu (dernière frame d'une vidéo dont la durée réelle est
    /// légèrement inférieure à la durée annoncée) : le reliquat reste à
    /// zéro plutôt que de faire échouer tout le décodage pour un décalage
    /// de quelques microsecondes en fin de fichier.
    /// </summary>
    private static async Task ReadExactlyBestEffortAsync(Stream stream, byte[] destination, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < destination.Length)
        {
            var read = await stream.ReadAsync(destination.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }
    }
}
