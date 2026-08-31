using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Videotoy.Media;

public sealed class TextureLoader
{
    public TextureAsset Load(string filePath)
    {
        using var stream = File.OpenRead(filePath);

        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = frame;
        converted.DestinationFormat = PixelFormats.Bgra32;
        converted.EndInit();

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        return new TextureAsset
        {
            Width = width,
            Height = height,
            PixelDataBgra = pixels
        };
    }
}
