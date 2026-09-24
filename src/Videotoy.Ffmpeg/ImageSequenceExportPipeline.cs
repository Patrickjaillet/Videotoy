using System.Diagnostics;
using System.Text.Json;
using Videotoy.Core.Domain;
using Videotoy.Rendering;

namespace Videotoy.Ffmpeg;

/// <summary>
/// Métadonnées de séquence d'images écrites en fin d'export réussi
/// (<c>&lt;sequence-name&gt;.metadata.json</c> dans le dossier de sortie),
/// pour import direct dans un logiciel de montage/compositing tiers.
/// <see cref="ColorspaceNote"/> est honnête sur la portée du format : les
/// formats 16-bit de cette phase (v2.2.0) sont produits en étendant la
/// source 8-bit sRGB du pipeline de rendu actuel, pas une vraie HDR linéaire
/// — celle-ci arrivera avec le pipeline de rendu flottant de la v2.3.0.
/// </summary>
public sealed record ImageSequenceMetadata(
    int Width,
    int Height,
    double FrameRate,
    int FirstFrameIndex,
    int LastFrameIndex,
    string ColorspaceNote,
    string AlphaMode,
    string Format);

/// <summary>
/// Résultat d'un pré-vol de collision/reprise pour un export séquence
/// d'images : liste les fichiers de frame déjà présents dans le dossier de
/// sortie, correspondant au motif de nommage résolu. La couche ViewModel
/// s'appuie sur ce résultat pour décider d'afficher une confirmation
/// d'écrasement ou une proposition de reprise, plutôt que ce pipeline ne
/// décide lui-même silencieusement.
/// </summary>
public sealed record ImageSequencePreflightResult(
    IReadOnlyList<int> ExistingFrameIndices,
    int FirstMissingFrameIndex)
{
    public bool HasExistingFrames => ExistingFrameIndices.Count > 0;
}

/// <summary>
/// Exécute un export séquence d'images complet : chaque frame rendue par
/// <see cref="FrameSequenceRenderer"/> est encodée par une invocation FFmpeg
/// indépendante (<see cref="FfmpegService.RunSingleFrameAsync"/>) plutôt que
/// streamée dans un unique process long-vivant comme
/// <see cref="VideoExportPipeline"/>/<see cref="AnimatedImageExportPipeline"/>
/// — ce choix est ce qui permet la reprise triviale d'un export interrompu
/// (<see cref="RunAsync"/>'s <c>resumeFromExisting</c>) : il suffit de sauter
/// les frames déjà présentes sur disque, sans jamais avoir à rouvrir un flux
/// stdin en cours de route.
/// </summary>
public sealed class ImageSequenceExportPipeline
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    private readonly FrameSequenceRenderer _frameSequenceRenderer;
    private readonly FfmpegService _ffmpegService;

    public ImageSequenceExportPipeline(FrameSequenceRenderer frameSequenceRenderer, FfmpegService ffmpegService)
    {
        _frameSequenceRenderer = frameSequenceRenderer;
        _ffmpegService = ffmpegService;
    }

    /// <summary>
    /// Scanne <see cref="ImageSequenceExportSettings.OutputDirectory"/> pour
    /// des fichiers de frame déjà présents correspondant au motif de nommage
    /// résolu, sans lancer aucun rendu ni encodage. Appelé par la couche
    /// ViewModel avant de démarrer l'export, pour décider si une
    /// confirmation d'écrasement ou une proposition de reprise doit être
    /// affichée.
    /// </summary>
    public ImageSequencePreflightResult Preflight(ImageSequenceExportSettings settings)
    {
        var frameCount = Videotoy.Core.LoopCalculator.computeFrameCount(settings.Duration, settings.FrameRate);
        var totalFrameCount = frameCount.FrameCount;
        var extension = Videotoy.Core.ImageSequenceExportSettingsValidator.resolveFileExtension(settings.Format);

        var existingIndices = new List<int>();

        if (Directory.Exists(settings.OutputDirectory))
        {
            var timeline = Videotoy.Core.LoopCalculator.buildFrameTimeline(totalFrameCount, settings.FrameRate);

            foreach (var frame in timeline)
            {
                var fileName = Videotoy.Core.ImageSequenceExportSettingsValidator.resolveFrameFileName(
                    settings.NamingPattern, frame.Index, frame.TimeSeconds, extension);
                var filePath = Path.Combine(settings.OutputDirectory, fileName);

                if (File.Exists(filePath))
                {
                    existingIndices.Add(frame.Index);
                }
            }
        }

        var firstMissing = 0;
        while (existingIndices.Contains(firstMissing) && firstMissing < totalFrameCount)
        {
            firstMissing++;
        }

        return new ImageSequencePreflightResult(existingIndices, firstMissing);
    }

    /// <summary>
    /// Exécute l'export complet. <paramref name="resumeFromExisting"/>,
    /// lorsque vrai, saute l'encodage de toute frame dont le fichier de
    /// sortie existe déjà sur disque (déterminé au moment du rendu de chaque
    /// frame, pas seulement au pré-vol initial) — le rendu de la frame reste
    /// exécuté dans tous les cas (voir <see cref="FrameSequenceRenderer.RenderSequence"/>,
    /// dont l'API n'expose pas de reprise à un index arbitraire), seul
    /// l'encodage/écriture disque est court-circuité, ce qui reste largement
    /// moins coûteux que l'invocation FFmpeg par frame qu'il évite. Écrit le
    /// fichier de métadonnées JSON uniquement en cas de succès complet (pas
    /// sur annulation/échec).
    /// </summary>
    public async Task RunAsync(
        ImageSequenceExportSettings settings,
        IProgress<ImageSequenceExportProgress>? progress,
        CancellationToken cancellationToken,
        bool resumeFromExisting = false)
    {
        var options = FfmpegImageSequenceOptions.FromExportSettings(settings);
        var extension = Videotoy.Core.ImageSequenceExportSettingsValidator.resolveFileExtension(settings.Format);

        var frameCount = Videotoy.Core.LoopCalculator.computeFrameCount(settings.Duration, settings.FrameRate);
        var totalFrameCount = frameCount.FrameCount;

        Directory.CreateDirectory(settings.OutputDirectory);

        var stopwatch = Stopwatch.StartNew();
        var framesCompleted = 0;
        var firstFrameIndexWritten = -1;
        var lastFrameIndexWritten = -1;

        await foreach (var frame in _frameSequenceRenderer.RenderSequence(settings.Duration, settings.FrameRate, cancellationToken).ConfigureAwait(false))
        {
            var fileName = Videotoy.Core.ImageSequenceExportSettingsValidator.resolveFrameFileName(
                settings.NamingPattern, frame.Index, frame.TimeSeconds, extension);
            var filePath = Path.Combine(settings.OutputDirectory, fileName);

            var alreadyExists = resumeFromExisting && File.Exists(filePath);

            if (!alreadyExists)
            {
                await _ffmpegService.RunSingleFrameAsync(options, filePath, frame.PixelsRgba, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (firstFrameIndexWritten < 0)
            {
                firstFrameIndexWritten = frame.Index;
            }
            lastFrameIndexWritten = frame.Index;

            framesCompleted++;

            progress?.Report(new ImageSequenceExportProgress(framesCompleted, totalFrameCount, stopwatch.Elapsed.TotalSeconds));
        }

        WriteMetadataFile(settings, firstFrameIndexWritten, lastFrameIndexWritten);
    }

    private static void WriteMetadataFile(ImageSequenceExportSettings settings, int firstFrameIndex, int lastFrameIndex)
    {
        var colorspaceNote = settings.Format == ImageSequenceFormat.Exr16
            ? "Linear half-float (expanded from 8-bit sRGB source — not true HDR; see v2.3.0)"
            : "sRGB (8-bit source, no HDR)";

        var metadata = new ImageSequenceMetadata(
            settings.Resolution.Width,
            settings.Resolution.Height,
            settings.FrameRate.Value,
            firstFrameIndex,
            lastFrameIndex,
            colorspaceNote,
            Videotoy.Core.ExportSettingsValidator.resolveAlphaModeKey(settings.AlphaMode),
            Videotoy.Core.ImageSequenceExportSettingsValidator.resolveFormatKey(settings.Format));

        var sequenceBaseName = ResolveSequenceBaseName(settings.NamingPattern);
        var metadataFilePath = Path.Combine(settings.OutputDirectory, $"{sequenceBaseName}.metadata.json");

        var json = JsonSerializer.Serialize(metadata, MetadataJsonOptions);
        File.WriteAllText(metadataFilePath, json);
    }

    /// <summary>
    /// Dérive un nom de base "propre" pour le fichier de métadonnées à partir
    /// du motif de nommage (ex. <c>frame_%05d.png</c> -> <c>frame</c>,
    /// <c>shot_{index}_{time}.png</c> -> <c>shot</c>) : tout ce qui précède le
    /// premier caractère de motif (<c>%</c> ou <c>{</c>), débarrassé des
    /// séparateurs de fin.
    /// </summary>
    private static string ResolveSequenceBaseName(ImageSequenceNamingMode namingMode)
    {
        var pattern = Videotoy.Core.ImageSequenceExportSettingsValidator.resolveNamingPatternText(namingMode);

        var cutIndex = pattern.IndexOfAny(['%', '{']);
        var baseName = cutIndex >= 0 ? pattern[..cutIndex] : Path.GetFileNameWithoutExtension(pattern);
        baseName = baseName.TrimEnd('_', '-', '.', ' ');

        return string.IsNullOrWhiteSpace(baseName) ? "sequence" : baseName;
    }
}
