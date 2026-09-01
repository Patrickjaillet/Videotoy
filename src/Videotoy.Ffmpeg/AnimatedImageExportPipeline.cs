using System.Diagnostics;
using Videotoy.Core.Domain;
using Videotoy.Rendering;

namespace Videotoy.Ffmpeg;

/// <summary>
/// Exécute un export image animée (GIF/WebP) complet, structurellement
/// parallèle à <see cref="VideoExportPipeline"/> : GIF ré-exécute le rendu
/// de la boucle deux fois (passe <c>palettegen</c> puis passe
/// <c>paletteuse</c>, exactement comme le mode deux passes vidéo — le
/// pipeline de rendu étant déterministe, les deux passes reçoivent des
/// pixels strictement identiques), WebP ne le rend qu'une seule fois.
/// </summary>
public sealed class AnimatedImageExportPipeline
{
    private readonly FrameSequenceRenderer _frameSequenceRenderer;
    private readonly FfmpegService _ffmpegService;

    public AnimatedImageExportPipeline(FrameSequenceRenderer frameSequenceRenderer, FfmpegService ffmpegService)
    {
        _frameSequenceRenderer = frameSequenceRenderer;
        _ffmpegService = ffmpegService;
    }

    public async Task RunAsync(
        AnimatedImageExportSettings settings,
        IProgress<VideoExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var durationMode = Videotoy.Core.AnimatedImageExportSettingsValidator.resolveDurationMode(settings);
        var frameCount = Videotoy.Core.LoopCalculator.computeFrameCount(durationMode, settings.FrameRate);
        var totalFrameCount = frameCount.FrameCount;

        var options = FfmpegAnimatedImageOptions.FromExportSettings(settings);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (settings.Format == AnimatedImageFormat.Gif)
            {
                // La passe palettegen ne produit qu'un PNG de palette, la
                // passe paletteuse produit le GIF final : la progression
                // totale considère donc le nombre de frames comme doublé,
                // comme pour le mode deux passes vidéo.
                var totalWorkUnits = totalFrameCount * 2;

                await RunPassAsync(
                    options, durationMode, settings.FrameRate, progress, cancellationToken, stopwatch,
                    AnimatedImagePass.GifPaletteGen, framesCompletedOffset: 0, totalWorkUnits).ConfigureAwait(false);

                await RunPassAsync(
                    options, durationMode, settings.FrameRate, progress, cancellationToken, stopwatch,
                    AnimatedImagePass.GifPaletteUse, framesCompletedOffset: totalFrameCount, totalWorkUnits).ConfigureAwait(false);

                DeletePaletteFileBestEffort(options.PaletteFilePath);
            }
            else
            {
                await RunPassAsync(
                    options, durationMode, settings.FrameRate, progress, cancellationToken, stopwatch,
                    AnimatedImagePass.WebP, framesCompletedOffset: 0, totalFrameCount).ConfigureAwait(false);
            }
        }
        catch
        {
            // Couvre l'annulation et tout échec d'encodage : le PNG de
            // palette intermédiaire (s'il a été produit) ne doit jamais
            // survivre à un export GIF interrompu ou en échec.
            DeletePaletteFileBestEffort(options.PaletteFilePath);
            throw;
        }
    }

    private async Task RunPassAsync(
        FfmpegAnimatedImageOptions options,
        DurationMode durationMode,
        FrameRate frameRate,
        IProgress<VideoExportProgress>? progress,
        CancellationToken cancellationToken,
        Stopwatch stopwatch,
        AnimatedImagePass pass,
        int framesCompletedOffset,
        int totalWorkUnits)
    {
        await _ffmpegService.StartAsync(options, pass, cancellationToken).ConfigureAwait(false);

        try
        {
            var framesCompleted = 0;

            foreach (var frame in _frameSequenceRenderer.RenderSequence(durationMode, frameRate, cancellationToken))
            {
                await _ffmpegService.WriteFrameAsync(frame.PixelsRgba, cancellationToken).ConfigureAwait(false);

                framesCompleted++;

                progress?.Report(new VideoExportProgress(
                    framesCompletedOffset + framesCompleted, totalWorkUnits, stopwatch.Elapsed.TotalSeconds));
            }

            await _ffmpegService.FinishAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _ffmpegService.Cancel();
            throw;
        }
    }

    private static void DeletePaletteFileBestEffort(string paletteFilePath)
    {
        try
        {
            if (File.Exists(paletteFilePath))
            {
                File.Delete(paletteFilePath);
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
