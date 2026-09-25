namespace Videotoy.Rendering;

/// <summary>
/// Source vidéo liée à un <c>iChannel</c> vidéo, sous une forme neutre :
/// <see cref="GetFramePixelsBgra"/> délègue le décodage (résolution du
/// mapping temporel, cache LRU) au type concret fourni par l'appelant
/// (<c>Videotoy.App</c>, via <c>Videotoy.Ffmpeg.VideoTextureLoader</c>),
/// même raison de cycle de dépendances que <see cref="BoundImageAsset"/>.
/// Doit rester une fonction pure de <paramref name="renderTimeSeconds"/>
/// pour un appelant donné, pour rester compatible avec le pipeline de
/// rendu déterministe. <see cref="ResolvePlaybackTimeSeconds"/> expose la
/// même résolution de mapping temporel (linéaire/bouclé/figé) que
/// <see cref="GetFramePixelsBgra"/> utilise en interne pour choisir la
/// frame à décoder, mais comme valeur nue plutôt que des pixels décodés —
/// c'est exactement la valeur `iChannelTime[n]` attendue par un shader
/// Shadertoy pour ce canal.
/// </summary>
public sealed record BoundVideoAsset(Func<double, int, int, byte[]> GetFramePixelsBgra, Func<double, double> ResolvePlaybackTimeSeconds);
