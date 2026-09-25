namespace Videotoy.Media;

/// <summary>
/// Les 6 faces d'une texture cubemap chargées en BGRA32, dans l'ordre D3D11
/// attendu pour <c>ID3D11Texture2D</c> <c>ArraySize=6</c>/
/// <c>MiscFlags.TextureCube</c> (+X, -X, +Y, -Y, +Z, -Z) — voir
/// <see cref="Videotoy.Core.ShaderModel.cubemapFacePaths"/> pour la
/// convention de dérivation des 6 chemins de fichier à partir du chemin de
/// base déclaré par le shader. Toutes les faces doivent avoir les mêmes
/// dimensions (contrainte D3D11 sur une cubemap) ; <see cref="TextureLoader"/>
/// valide cette contrainte au chargement.
/// </summary>
public sealed class CubemapAsset
{
    public required int FaceWidth { get; init; }

    public required int FaceHeight { get; init; }

    /// <summary>
    /// Les 6 faces, chacune <c>FaceWidth * FaceHeight * 4</c> octets BGRA32,
    /// dans l'ordre +X, -X, +Y, -Y, +Z, -Z.
    /// </summary>
    public required byte[][] FacesBgra { get; init; }
}
