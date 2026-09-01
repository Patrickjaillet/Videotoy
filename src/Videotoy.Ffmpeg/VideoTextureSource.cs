namespace Videotoy.Ffmpeg;

/// <summary>
/// Source vidéo résolue pour un <c>iChannel</c> vidéo : chemin de fichier,
/// métadonnées sondées une fois au chargement, et mode de correspondance
/// temporelle. <see cref="TimeMapping"/> est volontairement une propriété
/// mutable (pas <c>init</c>) : le panneau de paramètres peut la modifier en
/// direct sans recharger le shader ni ré-initialiser le renderer — chaque
/// <c>RenderFrame</c> la relit à la volée.
/// </summary>
public sealed class VideoTextureSource
{
    public required string FilePath { get; set; }

    public required VideoProbeResult Probe { get; set; }

    public Core.VideoTimeMapping.VideoTimeMappingMode TimeMapping { get; set; } =
        Core.VideoTimeMapping.VideoTimeMappingMode.Looped;
}
