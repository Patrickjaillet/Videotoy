using System.Diagnostics;
using Videotoy.Core.Domain;
using Videotoy.Rendering;

namespace Videotoy.Ffmpeg;

public sealed class VideoExportPipeline
{
    private readonly FrameSequenceRenderer _frameSequenceRenderer;
    private readonly FfmpegService _ffmpegService;

    /// <summary>
    /// Nombre maximal de nouvelles tentatives après une erreur FFmpeg jugée
    /// transitoire (voir <see cref="TransientFfmpegErrorClassifier"/>) avant
    /// de remonter définitivement l'erreur à l'UI. Volontairement bas : une
    /// erreur transitoire réelle (pipe cassé, hoquet du process) se résout
    /// en général dès la première reprise, et un plafond bas évite de faire
    /// perdre un temps disproportionné à l'utilisateur sur une erreur qui ne
    /// se résoudra jamais.
    /// </summary>
    private const int MaxTransientRetries = 2;

    public VideoExportPipeline(FrameSequenceRenderer frameSequenceRenderer, FfmpegService ffmpegService)
    {
        _frameSequenceRenderer = frameSequenceRenderer;
        _ffmpegService = ffmpegService;
    }

    /// <summary>
    /// Exécute un export vidéo complet, frame par frame, de bout en bout,
    /// avec reprise automatique sur erreur FFmpeg transitoire (voir
    /// <see cref="TransientFfmpegErrorClassifier"/>) : une annulation
    /// utilisateur (<see cref="OperationCanceledException"/>) n'est en
    /// revanche jamais retentée. <paramref name="onRetry"/>, si fourni, est
    /// invoqué avec le numéro de la tentative (2, 3, ...) juste avant chaque
    /// nouvelle tentative, pour permettre à l'UI d'afficher un message de
    /// reprise.
    /// </summary>
    public async Task RunAsync(
        ExportSettings settings,
        IProgress<VideoExportProgress>? progress,
        CancellationToken cancellationToken,
        string? audioSourceFilePath = null,
        Action<int>? onRetry = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RunAttemptAsync(settings, progress, cancellationToken, audioSourceFilePath).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt <= MaxTransientRetries && TransientFfmpegErrorClassifier.IsTransient(ex))
            {
                onRetry?.Invoke(attempt + 1);
            }
        }
    }

    /// <summary>
    /// Une seule tentative d'export complet. Voir <see cref="RunAsync"/> pour
    /// la logique de reprise sur erreur transitoire qui l'enveloppe.
    /// </summary>
    private async Task RunAttemptAsync(
        ExportSettings settings,
        IProgress<VideoExportProgress>? progress,
        CancellationToken cancellationToken,
        string? audioSourceFilePath)
    {
        var frameCount = Videotoy.Core.LoopCalculator.computeFrameCount(settings.Duration, settings.FrameRate);
        var totalFrameCount = frameCount.FrameCount;

        FfmpegAudioTrackOptions? audioTrack = null;
        if (audioSourceFilePath is not null)
        {
            var muxedAudioDurationSeconds = Videotoy.Core.LoopCalculator.effectiveDurationSeconds(frameCount, settings.FrameRate);
            audioTrack = new FfmpegAudioTrackOptions(audioSourceFilePath, muxedAudioDurationSeconds);
        }

        var options = FfmpegEncodingOptions.FromExportSettings(settings, audioTrack);

        var stopwatch = Stopwatch.StartNew();

        if (options.IsTwoPass)
        {
            // Le mode deux passes ré-exécute intégralement le rendu des
            // frames pour chacune des deux passes FFmpeg plutôt que de
            // bufferiser les pixels rendus en mémoire (infaisable pour un
            // export long/haute résolution) : le pipeline de rendu étant
            // déterministe (invariant central du projet), les deux passes
            // reçoivent des pixels strictement identiques. La passe 1
            // n'écrit qu'un fichier de statistiques (voir
            // FfmpegService.BuildArguments) ; la progression totale
            // considère donc le nombre de frames comme doublé.
            await RunSinglePassAsync(
                options, settings, progress, cancellationToken, totalFrameCount, stopwatch,
                passNumber: 1, framesCompletedOffset: 0, totalWorkUnits: totalFrameCount * 2).ConfigureAwait(false);

            await RunSinglePassAsync(
                options, settings, progress, cancellationToken, totalFrameCount, stopwatch,
                passNumber: 2, framesCompletedOffset: totalFrameCount, totalWorkUnits: totalFrameCount * 2).ConfigureAwait(false);

            DeletePassLogFileBestEffort(options.OutputFilePath + ".passlog");
        }
        else
        {
            await RunSinglePassAsync(
                options, settings, progress, cancellationToken, totalFrameCount, stopwatch,
                passNumber: null, framesCompletedOffset: 0, totalWorkUnits: totalFrameCount).ConfigureAwait(false);
        }
    }

    private async Task RunSinglePassAsync(
        FfmpegEncodingOptions options,
        ExportSettings settings,
        IProgress<VideoExportProgress>? progress,
        CancellationToken cancellationToken,
        int totalFrameCount,
        Stopwatch stopwatch,
        int? passNumber,
        int framesCompletedOffset,
        int totalWorkUnits)
    {
        var throttleMilliseconds = Videotoy.Core.ExportSettingsValidator.resolveThrottleMilliseconds(settings.Performance);

        await _ffmpegService.StartAsync(options, cancellationToken, passNumber).ConfigureAwait(false);

        try
        {
            var framesCompleted = 0;

            foreach (var frame in _frameSequenceRenderer.RenderSequence(
                settings.Duration, settings.FrameRate, cancellationToken))
            {
                await _ffmpegService.WriteFrameAsync(frame.PixelsRgba, cancellationToken).ConfigureAwait(false);

                framesCompleted++;

                progress?.Report(new VideoExportProgress(
                    framesCompletedOffset + framesCompleted, totalWorkUnits, stopwatch.Elapsed.TotalSeconds));

                if (throttleMilliseconds > 0)
                {
                    await Task.Delay(throttleMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            }

            await _ffmpegService.FinishAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Couvre l'annulation (OperationCanceledException), un échec
            // d'encodage remonté par FFmpeg (FfmpegEncodingException) et
            // toute autre exception survenant pendant le rendu ou l'écriture
            // des frames : dans tous les cas, le process FFmpeg doit être
            // tué et nettoyé pour ne jamais laisser un ffmpeg.exe orphelin,
            // y compris avant qu'une nouvelle tentative (voir RunAsync) ne
            // démarre un nouveau process.
            _ffmpegService.Cancel();
            throw;
        }
    }

    private static void DeletePassLogFileBestEffort(string passLogFilePath)
    {
        try
        {
            if (File.Exists(passLogFilePath))
            {
                File.Delete(passLogFilePath);
            }

            var mbtreeFilePath = passLogFilePath + "-0.log.mbtree";
            if (File.Exists(mbtreeFilePath))
            {
                File.Delete(mbtreeFilePath);
            }

            var statsFilePath = passLogFilePath + "-0.log";
            if (File.Exists(statsFilePath))
            {
                File.Delete(statsFilePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
