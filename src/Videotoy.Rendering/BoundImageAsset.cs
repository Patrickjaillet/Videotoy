namespace Videotoy.Rendering;

/// <summary>
/// Données pixel d'une texture image statique liée à un <c>iChannel</c>,
/// telle que fournie à <see cref="MultiPassRenderer.Initialize"/>. Type
/// neutre défini dans <c>Videotoy.Rendering</c> plutôt que de référencer
/// <c>Videotoy.Media.TextureAsset</c> directement : <c>Videotoy.Ffmpeg</c>
/// référence déjà <c>Videotoy.Rendering</c>, donc l'inverse créerait un
/// cycle de dépendances. L'appelant (<c>Videotoy.App</c>) convertit.
/// </summary>
public sealed record BoundImageAsset(int Width, int Height, byte[] PixelsBgra);
