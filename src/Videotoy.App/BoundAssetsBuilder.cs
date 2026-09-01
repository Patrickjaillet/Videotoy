using System.Collections.Generic;
using System.IO;
using System.Linq;
using Videotoy.Ffmpeg;
using Videotoy.Media;
using Videotoy.Rendering;

namespace Videotoy.App;

/// <summary>
/// Convertit <see cref="LoadedShader.Textures"/>/<c>.AudioTracks</c>/
/// <c>.VideoSources</c> vers les types neutres attendus par
/// <see cref="MultiPassRenderer.Initialize"/> (<see cref="BoundImageAsset"/>/
/// <see cref="BoundAudioAsset"/>/<see cref="BoundVideoAsset"/>) — cette
/// conversion existe uniquement pour que <c>Videotoy.Rendering</c> n'ait
/// jamais besoin de référencer <c>Videotoy.Media</c>/<c>Videotoy.Ffmpeg</c>
/// (cycle de dépendances, puisque ces deux projets référencent déjà
/// <c>Videotoy.Rendering</c>). Extrait de <see cref="ViewModels.MainWindowViewModel"/>
/// afin d'être réutilisable par <see cref="RenderQueueProcessor"/> sans
/// dépendre du ViewModel.
/// </summary>
public sealed class BoundAssetsBuilder
{
    private readonly AudioSpectrumTextureGenerator _audioSpectrumTextureGenerator;
    private readonly VideoTextureLoader _videoTextureLoader;

    public BoundAssetsBuilder(
        AudioSpectrumTextureGenerator audioSpectrumTextureGenerator,
        VideoTextureLoader videoTextureLoader)
    {
        _audioSpectrumTextureGenerator = audioSpectrumTextureGenerator;
        _videoTextureLoader = videoTextureLoader;
    }

    public (
        IReadOnlyDictionary<string, BoundImageAsset> Images,
        IReadOnlyDictionary<string, BoundAudioAsset> AudioTracks,
        IReadOnlyDictionary<string, BoundVideoAsset> VideoSources)
        Build(LoadedShader loadedShader)
    {
        var images = loadedShader.Textures.ToDictionary(
            pair => pair.Key,
            pair => new BoundImageAsset(pair.Value.Width, pair.Value.Height, pair.Value.PixelDataBgra));

        var audioTracks = loadedShader.AudioTracks.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var track = pair.Value;
                return new BoundAudioAsset(timeSeconds => _audioSpectrumTextureGenerator.Generate(track, timeSeconds).PixelDataBgra);
            });

        var videoSources = loadedShader.VideoSources.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var source = pair.Value;
                return new BoundVideoAsset((renderTimeSeconds, targetWidth, targetHeight) =>
                {
                    var playbackTimeSeconds = Videotoy.Core.VideoTimeMapping.resolveVideoPlaybackTimeSeconds(
                        source.TimeMapping, source.Probe.DurationSeconds, renderTimeSeconds);

                    return _videoTextureLoader
                        .GetFramePixelsBgraAsync(source.FilePath, playbackTimeSeconds, targetWidth, targetHeight)
                        .GetAwaiter()
                        .GetResult();
                });
            });

        return (images, audioTracks, videoSources);
    }

    /// <summary>
    /// Résout le chemin absolu du fichier audio source à muxer avec la vidéo
    /// exportée, lorsque le shader chargé possède un <c>iChannel</c> audio
    /// (type <c>Music</c> ou <c>MusicStream</c>). Le chemin déclaré dans le
    /// shader (<see cref="Videotoy.Core.ShaderModel.firstAudioChannelPath"/>)
    /// est résolu par rapport au dossier du fichier shader, exactement comme
    /// le fait déjà <see cref="ShaderFileService"/> au chargement. Ne
    /// détermine délibérément aucune durée : c'est
    /// <see cref="VideoExportPipeline.RunAsync"/> qui calcule la durée
    /// effective à partir du nombre de frames réellement rendu, pour rester
    /// strictement aligné sur la timeline de rendu déterministe même en mode
    /// boucle parfaite. Retourne <c>null</c> si le shader n'utilise aucune
    /// entrée audio, ou si le fichier résolu n'existe plus sur disque.
    /// </summary>
    public static string? ResolveExportAudioSourceFilePath(LoadedShader loadedShader)
    {
        var declaredPath = Videotoy.Core.ShaderModel.firstAudioChannelPath(loadedShader.Project);
        if (declaredPath is null)
        {
            return null;
        }

        var baseDirectory = Path.GetDirectoryName(loadedShader.Project.SourceFilePath) ?? string.Empty;
        var resolvedPath = Path.IsPathRooted(declaredPath.Value)
            ? declaredPath.Value
            : Path.Combine(baseDirectory, declaredPath.Value);

        return File.Exists(resolvedPath) ? resolvedPath : null;
    }
}
