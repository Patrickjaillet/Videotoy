namespace Videotoy.Media;

public sealed class TextureAsset
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required byte[] PixelDataBgra { get; init; }
}
