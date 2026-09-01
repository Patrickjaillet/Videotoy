namespace Videotoy.Rendering;

/// <summary>
/// Source vidéo liée à un <c>iChannel</c> vidéo, sous une forme neutre :
/// <see cref="GetFramePixelsBgra"/> délègue le décodage (résolution du
/// mapping temporel, cache LRU) au type concret fourni par l'appelant
/// (<c>Videotoy.App</c>, via <c>Videotoy.Ffmpeg.VideoTextureLoader</c>),
/// même raison de cycle de dépendances que <see cref="BoundImageAsset"/>.
/// Doit rester une fonction pure de <paramref name="renderTimeSeconds"/>
/// pour un appelant donné, pour rester compatible avec le pipeline de
/// rendu déterministe.
/// </summary>
public sealed record BoundVideoAsset(Func<double, int, int, byte[]> GetFramePixelsBgra);
