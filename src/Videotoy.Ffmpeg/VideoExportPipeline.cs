using System.Diagnostics;
using Videotoy.Core.Domain;
using Videotoy.Rendering;

namespace Videotoy.Ffmpeg;

public sealed class VideoExportPipeline
{
    private readonly FrameSequenceRenderer _frameSequenceRenderer;
    private readonly FfmpegService _ffmpegService;

    public VideoExportPipeline(FrameSequenceRenderer frameSequenceRenderer, FfmpegService ffmpegService)
    {
        _frameSequenceRenderer = frameSequenceRenderer;
        _ffmpegService = ffmpegService;
    }

    /// <summary>
    /// Exécute un export vidéo complet, frame par frame, de bout en bout :
    /// <see cref="FrameSequenceRenderer"/> rend chaque frame déterministe sur
    /// le GPU et les pixels sont streamés directement dans le pipe stdin de
    /// FFmpeg, sans jamais écrire de fichier de frame intermédiaire sur
    /// disque. Quand <paramref name="audioSourceFilePath"/> est fourni (le
    /// shader exporté possède un <c>iChannel</c> audio), le fichier audio
    /// source est passé à FFmpeg comme seconde entrée et muxé avec la vidéo
    /// générée en une seule passe d'encodage (voir <see cref="FfmpegService"/>
    /// pour le détail de la construction des arguments FFmpeg). La durée
    /// muxée n'est jamais la durée brute demandée dans <paramref
    /// name="settings"/> : elle est calculée ici à partir du nombre de
    /// frames réellement rendu
    /// (<see cref="Videotoy.Core.LoopCalculator.effectiveDurationSeconds"/>),
    /// pour rester strictement alignée — même origine <c>t = 0</c>, même
    /// durée totale — sur la timeline de rendu déterministe, y compris en
    /// mode boucle parfaite où un arrondi du nombre de frames peut décaler
    /// très légèrement la durée effective par rapport à la durée de boucle
    /// demandée ; sans cette correction, un raccord de boucle audio pourrait
    /// présenter une micro-coupure ou un silence résiduel à la reprise.
    /// </summary>
    public async Task RunAsync(
        ExportSettings settings,
        IProgress<VideoExportProgress>? progress,
        CancellationToken cancellationToken,
        string? audioSourceFilePath = null)
    {
        var frameCount = Videotoy.Core.LoopCalculator.computeFrameCount(settings.Duration, settings.FrameRate);
        var totalFrameCount = frameCount.FrameCount;
        var throttleMilliseconds = Videotoy.Core.ExportSettingsValidator.resolveThrottleMilliseconds(settings.Performance);

        FfmpegAudioTrackOptions? audioTrack = null;
        if (audioSourceFilePath is not null)
        {
            var muxedAudioDurationSeconds = Videotoy.Core.LoopCalculator.effectiveDurationSeconds(frameCount, settings.FrameRate);
            audioTrack = new FfmpegAudioTrackOptions(audioSourceFilePath, muxedAudioDurationSeconds);
        }

        var options = FfmpegEncodingOptions.FromExportSettings(settings, audioTrack);

        var stopwatch = Stopwatch.StartNew();

        _ffmpegService.Start(options);

        try
        {
            var framesCompleted = 0;

            foreach (var frame in _frameSequenceRenderer.RenderSequence(
                settings.Duration, settings.FrameRate, cancellationToken))
            {
                await _ffmpegService.WriteFrameAsync(frame.PixelsRgba, cancellationToken).ConfigureAwait(false);

                framesCompleted++;

                progress?.Report(new VideoExportProgress(framesCompleted, totalFrameCount, stopwatch.Elapsed.TotalSeconds));

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
            // tué et nettoyé pour ne jamais laisser un ffmpeg.exe orphelin.
            _ffmpegService.Cancel();
            throw;
        }
    }
}
