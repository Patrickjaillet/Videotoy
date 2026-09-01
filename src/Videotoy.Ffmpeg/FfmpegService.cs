using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Videotoy.Ffmpeg;

public sealed class FfmpegService
{
    private readonly FfmpegLocator _locator;
    private readonly HardwareEncoderProbe _hardwareEncoderProbe;

    private Process? _process;
    private Stream? _stdin;
    private Task? _stderrPumpTask;
    private readonly List<string> _stderrTail = new();
    private readonly object _stderrLock = new();

    public FfmpegService(FfmpegLocator locator, HardwareEncoderProbe hardwareEncoderProbe)
    {
        _locator = locator;
        _hardwareEncoderProbe = hardwareEncoderProbe;
    }

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// Démarre le process FFmpeg pour <paramref name="options"/>. Suffixe
    /// <c>Async</c> : la résolution de l'encodeur matériel effectif
    /// (<see cref="HardwareEncoderProbe"/>) nécessite de lancer FFmpeg une ou
    /// deux fois au préalable (liste des encodeurs compilés, encodage de
    /// test), donc démarrer l'export réel ne peut plus être une opération
    /// synchrone. <paramref name="passNumber"/> vaut <c>null</c> pour un
    /// encodage en une seule passe ; <see cref="VideoExportPipeline"/> passe
    /// <c>1</c> puis <c>2</c> lorsque <see cref="FfmpegEncodingOptions.IsTwoPass"/>
    /// est actif, en ré-exécutant le rendu des frames pour chaque passe (le
    /// pipeline de rendu étant déterministe, les deux passes reçoivent des
    /// pixels strictement identiques).
    /// </summary>
    public async Task StartAsync(
        FfmpegEncodingOptions options,
        CancellationToken cancellationToken = default,
        int? passNumber = null)
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

        var effectiveVideoCodecName = await _hardwareEncoderProbe.ResolveEncoderNameAsync(
            options.HardwareEncoderKey,
            options.Codec,
            options.VideoCodecName,
            cancellationToken).ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = _locator.ResolveExecutablePath(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in BuildArguments(options, effectiveVideoCodecName, passNumber))
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
    /// Un encodeur matériel (suffixe <c>_nvenc</c>/<c>_qsv</c>/<c>_amf</c>)
    /// n'accepte pas les mêmes options que les encodeurs logiciels
    /// x264/x265 : ni <c>-preset</c> (vocabulaire de presets différent, non
    /// géré ici), ni <c>-crf</c> (NVENC/AMF utilisent <c>-rc constqp -qp</c>,
    /// Quick Sync utilise <c>-global_quality</c>). Cette distinction pilote
    /// le choix du flag de contrôle qualité dans <see cref="BuildArguments"/>.
    /// </summary>
    private static bool IsHardwareEncoderName(string videoCodecName) =>
        videoCodecName.EndsWith("_nvenc", StringComparison.Ordinal)
        || videoCodecName.EndsWith("_qsv", StringComparison.Ordinal)
        || videoCodecName.EndsWith("_amf", StringComparison.Ordinal);

    /// <summary>
    /// Construit la liste d'arguments FFmpeg. La vidéo brute (BGRA,
    /// une frame par appel de <see cref="WriteFrameAsync"/>) arrive toujours
    /// sur <c>pipe:0</c> (stdin) comme entrée 0. Quand
    /// <see cref="FfmpegEncodingOptions.AudioTrack"/> est renseigné, le
    /// fichier audio source est ajouté comme entrée 1 : les deux flux sont
    /// alors mixés (<c>-map</c>) et encodés dans le même processus FFmpeg,
    /// en une seule passe — aucun fichier intermédiaire, aucun second appel
    /// à FFmpeg pour le muxage. <paramref name="effectiveVideoCodecName"/>
    /// est le nom d'encodeur réellement résolu par
    /// <see cref="HardwareEncoderProbe"/> (peut différer de
    /// <see cref="FfmpegEncodingOptions.VideoCodecName"/> si un encodeur
    /// matériel a été retenu). <paramref name="passNumber"/> vaut <c>null</c>
    /// pour un encodage en une seule passe, <c>1</c> ou <c>2</c> pour une
    /// passe du mode deux passes (<see cref="FfmpegEncodingOptions.IsTwoPass"/>) :
    /// la passe 1 n'écrit qu'un fichier de statistiques (sortie <c>NUL</c>,
    /// sans audio), la passe 2 produit le fichier final.
    /// </summary>
    private static IEnumerable<string> BuildArguments(
        FfmpegEncodingOptions options,
        string effectiveVideoCodecName,
        int? passNumber)
    {
        var frameRateText = options.FrameRate.ToString(CultureInfo.InvariantCulture);
        var isFirstPass = passNumber == 1;
        var isHardwareEncoder = IsHardwareEncoderName(effectiveVideoCodecName);

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

        // La passe 1 du mode deux passes ne produit qu'un fichier de
        // statistiques : ni piste audio en entrée, ni muxage, ne sont
        // nécessaires (ni même valides, puisque le fichier audio source
        // n'a pas vocation à être ré-analysé deux fois).
        var audioTrack = isFirstPass ? null : options.AudioTrack;

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
        yield return effectiveVideoCodecName;

        if (!isHardwareEncoder && !string.IsNullOrEmpty(options.SpeedPreset))
        {
            yield return "-preset";
            yield return options.SpeedPreset;
        }

        if (!string.IsNullOrEmpty(options.VideoProfileName))
        {
            yield return "-profile:v";
            yield return options.VideoProfileName;
        }

        if (options.GopSize is { } gopSize)
        {
            yield return "-g";
            yield return gopSize.ToString(CultureInfo.InvariantCulture);
        }

        if (options.ConstantRateFactor is { } crf)
        {
            if (isHardwareEncoder)
            {
                if (effectiveVideoCodecName.EndsWith("_qsv", StringComparison.Ordinal))
                {
                    yield return "-global_quality";
                    yield return crf.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    // NVENC / AMF : équivalent qualité constante de -crf.
                    yield return "-rc";
                    yield return "constqp";
                    yield return "-qp";
                    yield return crf.ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                yield return "-crf";
                yield return crf.ToString(CultureInfo.InvariantCulture);
            }
        }
        else if (options.TargetBitrateKbps is { } kbps)
        {
            // -b:v est reconnu uniformément par x264/x265 et par les trois
            // familles d'encodeurs matériels.
            yield return "-b:v";
            yield return $"{kbps}k";
        }

        if (options.IsTwoPass && passNumber is { } pass)
        {
            yield return "-pass";
            yield return pass.ToString(CultureInfo.InvariantCulture);
            yield return "-passlogfile";
            yield return options.OutputFilePath + ".passlog";
        }

        yield return "-pix_fmt";
        yield return "yuv420p";
        yield return "-r";
        yield return frameRateText;

        if (audioTrack is not null)
        {
            // audioTrack.DurationSeconds est la durée effective de la vidéo
            // (nombre de frames réellement rendu / fps, calculée par
            // VideoExportPipeline — jamais la durée brute demandée), donc -t
            // aligne la piste audio exactement sur la dernière frame vidéo,
            // avec la même origine t = 0 que la timeline de rendu
            // déterministe ; -shortest garantit qu'aucune des deux pistes ne
            // dépasse l'autre si le fichier source est plus court que la
            // vidéo exportée. Le codec/débit audio (AAC par défaut, ou
            // "copy" pour ré-utiliser tel quel la piste source) sont
            // configurables via FfmpegEncodingOptions plutôt que fixés en dur.
            yield return "-c:a";
            yield return options.AudioCodecName;

            if (!string.Equals(options.AudioCodecName, "copy", StringComparison.Ordinal))
            {
                yield return "-b:a";
                yield return $"{options.AudioBitrateKbps}k";
            }

            yield return "-t";
            yield return audioTrack.DurationSeconds.ToString(CultureInfo.InvariantCulture);
            yield return "-shortest";
        }

        yield return "-movflags";
        yield return "+faststart";

        if (isFirstPass)
        {
            // La passe 1 n'écrit aucun fichier réel : la sortie est
            // discardée (NUL sous Windows), seul le fichier de statistiques
            // -passlogfile importe pour la passe 2.
            yield return "-f";
            yield return "null";
            yield return "NUL";
        }
        else
        {
            yield return options.OutputFilePath;
        }
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
