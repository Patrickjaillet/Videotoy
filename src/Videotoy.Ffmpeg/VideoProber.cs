using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Videotoy.Ffmpeg;

/// <summary>
/// Extrait largeur/hauteur/durée/fps d'un fichier vidéo source en analysant
/// la bannière de démarrage de FFmpeg (aucun ffprobe n'est embarqué avec ce
/// projet, seul <c>ffmpeg.exe</c> l'est). <c>ffmpeg -i &lt;fichier&gt;</c> sans
/// argument de sortie se termine toujours en code non nul — attendu et sans
/// rapport avec l'échec du probe, seul le texte de stderr importe ici.
/// </summary>
public sealed class VideoProber
{
    private static readonly Regex DurationPattern = new(
        @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private static readonly Regex VideoStreamPattern = new(
        @"Stream #\d+:\d+.*Video:.*?(\d{2,5})x(\d{2,5})[^,]*,.*?(\d+(?:\.\d+)?)\s*fps", RegexOptions.Compiled);

    private readonly FfmpegLocator _locator;

    public VideoProber(FfmpegLocator locator)
    {
        _locator = locator;
    }

    public async Task<VideoProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
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

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stderrText = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var durationMatch = DurationPattern.Match(stderrText);
        var videoStreamMatch = VideoStreamPattern.Match(stderrText);

        if (!durationMatch.Success || !videoStreamMatch.Success)
        {
            throw new VideoProbeException(
                $"Could not determine video stream info for '{filePath}'.", stderrText);
        }

        var hours = int.Parse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(durationMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(durationMatch.Groups[3].Value, CultureInfo.InvariantCulture);
        var durationSeconds = hours * 3600.0 + minutes * 60.0 + seconds;

        var width = int.Parse(videoStreamMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var height = int.Parse(videoStreamMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        var frameRate = double.Parse(videoStreamMatch.Groups[3].Value, CultureInfo.InvariantCulture);

        return new VideoProbeResult(width, height, durationSeconds, frameRate);
    }
}
