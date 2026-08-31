using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Videotoy.Ffmpeg;

public sealed class FfmpegService
{
    private readonly FfmpegLocator _locator;

    private Process? _process;
    private Stream? _stdin;
    private Task? _stderrPumpTask;
    private readonly List<string> _stderrTail = new();
    private readonly object _stderrLock = new();

    public FfmpegService(FfmpegLocator locator)
    {
        _locator = locator;
    }

    public bool IsRunning => _process is { HasExited: false };

    public void Start(FfmpegEncodingOptions options)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("An FFmpeg export is already running.");
        }

        var outputDirectory = Path.GetDirectoryName(options.OutputFilePath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _locator.ResolveExecutablePath(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in BuildArguments(options))
        {
            startInfo.ArgumentList.Add(argument);
        }

        _process = new Process { StartInfo = startInfo };
        _process.Start();

        _stdin = _process.StandardInput.BaseStream;

        lock (_stderrLock)
        {
            _stderrTail.Clear();
        }

        _stderrPumpTask = PumpStderrAsync(_process);
    }

    /// <summary>
    /// Construit la liste d'arguments FFmpeg. La vidéo brute (BGRA,
    /// une frame par appel de <see cref="WriteFrameAsync"/>) arrive toujours
    /// sur <c>pipe:0</c> (stdin) comme entrée 0. Quand
    /// <see cref="FfmpegEncodingOptions.AudioTrack"/> est renseigné, le
    /// fichier audio source est ajouté comme entrée 1 : les deux flux sont
    /// alors mixés (<c>-map</c>) et encodés dans le même processus FFmpeg,
    /// en une seule passe — aucun fichier intermédiaire, aucun second appel
    /// à FFmpeg pour le muxage.
    /// </summary>
    private static IEnumerable<string> BuildArguments(FfmpegEncodingOptions options)
    {
        var frameRateText = options.FrameRate.ToString(CultureInfo.InvariantCulture);

        yield return "-y";

        // Entrée 0 : flux vidéo brut, streamé via stdin.
        yield return "-f";
        yield return "rawvideo";
        yield return "-pix_fmt";
        yield return "bgra";
        yield return "-video_size";
        yield return $"{options.Width}x{options.Height}";
        yield return "-framerate";
        yield return frameRateText;
        yield return "-i";
        yield return "pipe:0";

        var audioTrack = options.AudioTrack;

        if (audioTrack is not null)
        {
            // Entrée 1 : fichier audio source (WAV/MP3/OGG), décodé par
            // FFmpeg lui-même — aucun pré-traitement ni fichier temporaire.
            yield return "-i";
            yield return audioTrack.SourceFilePath;
        }

        yield return "-map";
        yield return "0:v";

        if (audioTrack is not null)
        {
            yield return "-map";
            yield return "1:a";
        }

        yield return "-c:v";
        yield return options.VideoCodecName;

        if (options.ConstantRateFactor is { } crf)
        {
            yield return "-crf";
            yield return crf.ToString(CultureInfo.InvariantCulture);
        }
        else if (options.TargetBitrateKbps is { } kbps)
        {
            yield return "-b:v";
            yield return $"{kbps}k";
        }

        yield return "-pix_fmt";
        yield return "yuv420p";
        yield return "-r";
        yield return frameRateText;

        if (audioTrack is not null)
        {
            // Encodage AAC standard pour un conteneur MP4. audioTrack.DurationSeconds
            // est la durée effective de la vidéo (nombre de frames réellement
            // rendu / fps, calculée par VideoExportPipeline — jamais la durée
            // brute demandée), donc -t aligne la piste audio exactement sur
            // la dernière frame vidéo, avec la même origine t = 0 que la
            // timeline de rendu déterministe ; -shortest garantit qu'aucune
            // des deux pistes ne dépasse l'autre si le fichier source est
            // plus court que la vidéo exportée.
            yield return "-c:a";
            yield return "aac";
            yield return "-b:a";
            yield return "192k";
            yield return "-t";
            yield return audioTrack.DurationSeconds.ToString(CultureInfo.InvariantCulture);
            yield return "-shortest";
        }

        yield return "-movflags";
        yield return "+faststart";

        yield return options.OutputFilePath;
    }

    public async Task WriteFrameAsync(byte[] pixelsBgra, CancellationToken cancellationToken = default)
    {
        if (_stdin is null || _process is null || _process.HasExited)
        {
            throw new InvalidOperationException("FFmpeg process is not running; call Start first.");
        }

        await _stdin.WriteAsync(pixelsBgra, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> FinishAsync(CancellationToken cancellationToken = default)
    {
        if (_process is null)
        {
            throw new InvalidOperationException("FFmpeg process was never started.");
        }

        if (_stdin is not null)
        {
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
            _stdin.Close();
            _stdin = null;
        }

        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (_stderrPumpTask is not null)
        {
            await _stderrPumpTask.ConfigureAwait(false);
        }

        var exitCode = _process.ExitCode;

        if (exitCode != 0)
        {
            var diagnosis = FfmpegStderrParser.Diagnose(GetStderrTail());
            _process.Dispose();
            _process = null;
            throw new FfmpegEncodingException(exitCode, diagnosis);
        }

        _process.Dispose();
        _process = null;

        return exitCode;
    }

    /// <summary>
    /// Ferme le pipe stdin et tue l'arbre de processus FFmpeg (process lui-même
    /// et tout enfant qu'il aurait pu créer), sans lever d'exception si le
    /// processus est déjà terminé ou si le pipe est déjà rompu. Toujours
    /// dispose et efface le handle de processus ensuite, afin qu'un export
    /// annulé ne laisse jamais de <c>ffmpeg.exe</c> orphelin en cours
    /// d'exécution et ne bloque pas un export ultérieur. Idempotent : peut
    /// être appelé même si aucun export n'est en cours.
    /// </summary>
    public void Cancel()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            _stdin?.Close();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _stdin = null;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private async Task PumpStderrAsync(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                lock (_stderrLock)
                {
                    _stderrTail.Add(line);
                    if (_stderrTail.Count > FfmpegStderrParser.EffectiveMaxTailLines)
                    {
                        _stderrTail.RemoveAt(0);
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private IReadOnlyList<string> GetStderrTail()
    {
        lock (_stderrLock)
        {
            return _stderrTail.ToArray();
        }
    }
}
