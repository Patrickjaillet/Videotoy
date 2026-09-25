namespace Videotoy.Rendering;

/// <summary>
/// Les 6 faces d'une texture cubemap liée à un <c>iChannel</c>, sous une
/// forme neutre — même raison de cycle de dépendances que
/// <see cref="BoundImageAsset"/>. Faces dans l'ordre D3D11 attendu (+X, -X,
/// +Y, -Y, +Z, -Z), toutes de dimensions identiques.
/// </summary>
public sealed record BoundCubemapAsset(int FaceWidth, int FaceHeight, byte[][] FacesBgra);
