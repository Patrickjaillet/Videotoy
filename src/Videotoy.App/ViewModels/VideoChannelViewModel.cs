using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Videotoy.Ffmpeg;

namespace Videotoy.App.ViewModels;

/// <summary>
/// Représente, pour affichage dans la carte "Video Channels" du panneau de
/// paramètres, un channel vidéo détecté dans le shader chargé — un par
/// (passe, index de channel) référençant une source vidéo effectivement
/// chargée. Mirroir de <see cref="CustomUniformGroupViewModel"/>'s "une
/// ligne d'UI par élément découvert dans le shader" pour ce nouveau type
/// d'élément.
/// </summary>
public sealed partial class VideoChannelViewModel : ObservableObject
{
    private readonly VideoTextureLoader _videoTextureLoader;

    public required string PassName { get; init; }

    public required int ChannelIndex { get; init; }

    public required VideoTextureSource Source { get; init; }

    public string DisplayLabel => $"{PassName} · iChannel{ChannelIndex}";

    public string FileName => Path.GetFileName(Source.FilePath);

    [ObservableProperty]
    private VideoTimeMappingOption _selectedTimeMapping = VideoTimeMappingOption.Looped;

    public IReadOnlyList<VideoTimeMappingOption> TimeMappingOptions => VideoTimeMappingOption.All;

    public VideoChannelViewModel(VideoTextureLoader videoTextureLoader)
    {
        _videoTextureLoader = videoTextureLoader;
    }

    partial void OnSelectedTimeMappingChanged(VideoTimeMappingOption value)
    {
        Source.TimeMapping = value.Value;
    }

    /// <summary>
    /// Ré-assigne le fichier vidéo source depuis un glisser-déposer :
    /// re-sonde ses métadonnées et met à jour <see cref="Source"/> en place
    /// (même objet, jamais recréé), sans nécessiter de recharger le shader
    /// ni de ré-initialiser le renderer — celui-ci relit <see cref="Source"/>
    /// à chaque frame déjà.
    /// </summary>
    public async Task HandleFileDroppedAsync(string newFilePath)
    {
        var probe = await _videoTextureLoader.ProbeAsync(newFilePath).ConfigureAwait(false);
        Source.FilePath = newFilePath;
        Source.Probe = probe;
        OnPropertyChanged(nameof(FileName));
    }
}
