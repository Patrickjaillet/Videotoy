namespace Videotoy.Media;

/// <summary>
/// Une texture volume ("Texture 3D" sur shadertoy.com) décodée depuis son
/// image atlas (bande horizontale de tranches carrées, voir
/// <see cref="Videotoy.Core.ShaderModel.volumeSliceCountFromAtlasDimensions"/>)
/// en tranches BGRA32 individuelles, prêtes à être empilées dans un
/// <c>ID3D11Texture3D</c>.
/// </summary>
public sealed class VolumeAsset
{
    public required int SliceWidth { get; init; }

    public required int SliceHeight { get; init; }

    public required int SliceCount { get; init; }

    /// <summary>
    /// Les tranches concaténées dans l'ordre Z croissant, chacune
    /// <c>SliceWidth * SliceHeight * 4</c> octets BGRA32.
    /// </summary>
    public required byte[] SlicesBgra { get; init; }
}
