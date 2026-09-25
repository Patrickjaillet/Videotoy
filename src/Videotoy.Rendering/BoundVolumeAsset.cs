namespace Videotoy.Rendering;

/// <summary>
/// Une texture volume ("Texture 3D" Shadertoy) liée à un <c>iChannel</c>,
/// sous une forme neutre — même raison de cycle de dépendances que
/// <see cref="BoundImageAsset"/>. <see cref="SlicesBgra"/> concatène les
/// tranches Z dans l'ordre croissant, chacune déjà ré-extraite en un bloc
/// contigu (voir <c>Videotoy.Media.TextureLoader.LoadVolume</c>).
/// </summary>
public sealed record BoundVolumeAsset(int SliceWidth, int SliceHeight, int SliceCount, byte[] SlicesBgra);
