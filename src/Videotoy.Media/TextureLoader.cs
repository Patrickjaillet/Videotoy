using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Videotoy.Media;

public sealed class TextureLoader
{
    /// <summary>
    /// Charge <paramref name="filePath"/> en BGRA32. <paramref name="verticalFlip"/>
    /// reflète l'attribut <c>sampler.vflip</c> d'un input Shadertoy JSON
    /// (Phase 3 du ROADMAP), <c>false</c> par défaut pour préserver le
    /// comportement historique de ce chargeur (aucun retournement, quel que
    /// soit l'appelant) tant qu'un export ne demande pas explicitement
    /// l'inverse — inverser les lignes ici, une fois au chargement, évite de
    /// complexifier l'échantillonnage HLSL par un flag par canal.
    /// </summary>
    public TextureAsset Load(string filePath, bool verticalFlip = false)
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

        if (verticalFlip)
        {
            FlipRowsInPlace(pixels, stride, height);
        }

        return new TextureAsset
        {
            Width = width,
            Height = height,
            PixelDataBgra = pixels
        };
    }

    private static void FlipRowsInPlace(byte[] pixels, int stride, int height)
    {
        var rowBuffer = new byte[stride];
        for (var row = 0; row < height / 2; row++)
        {
            var topOffset = row * stride;
            var bottomOffset = (height - 1 - row) * stride;

            Buffer.BlockCopy(pixels, topOffset, rowBuffer, 0, stride);
            Buffer.BlockCopy(pixels, bottomOffset, pixels, topOffset, stride);
            Buffer.BlockCopy(rowBuffer, 0, pixels, bottomOffset, stride);
        }
    }

    /// <summary>
    /// Charge les 6 faces d'une cubemap (chemins déjà résolus par
    /// <see cref="Videotoy.Core.ShaderModel.cubemapFacePaths"/>, dans l'ordre
    /// D3D11 +X/-X/+Y/-Y/+Z/-Z) — lève <see cref="InvalidDataException"/> si
    /// une face a des dimensions différentes de la première, contrainte
    /// D3D11 sur une cubemap qu'il vaut mieux valider ici avec un message
    /// clair plutôt que de laisser échouer la création de la ressource GPU
    /// avec un HRESULT opaque.
    /// </summary>
    public CubemapAsset LoadCubemap(IReadOnlyList<string> facePaths, bool verticalFlip = false)
    {
        var faces = new byte[facePaths.Count][];
        var faceWidth = 0;
        var faceHeight = 0;

        for (var i = 0; i < facePaths.Count; i++)
        {
            var face = Load(facePaths[i], verticalFlip);

            if (i == 0)
            {
                faceWidth = face.Width;
                faceHeight = face.Height;
            }
            else if (face.Width != faceWidth || face.Height != faceHeight)
            {
                throw new InvalidDataException(
                    $"Cubemap face '{facePaths[i]}' is {face.Width}x{face.Height}, but face 0 is {faceWidth}x{faceHeight}; all 6 faces of a cubemap must have identical dimensions.");
            }

            faces[i] = face.PixelDataBgra;
        }

        return new CubemapAsset
        {
            FaceWidth = faceWidth,
            FaceHeight = faceHeight,
            FacesBgra = faces
        };
    }

    /// <summary>
    /// Charge une texture volume depuis son image atlas (bande horizontale
    /// de tranches carrées) et la découpe en tranches BGRA32 individuelles —
    /// voir <see cref="Videotoy.Core.ShaderModel.volumeSliceCountFromAtlasDimensions"/>
    /// pour la convention de décodage (non vérifiée contre un export
    /// shadertoy.com réel, Phase 3 du ROADMAP). L'atlas chargé est en
    /// row-major sur toute sa largeur ; chaque tranche doit donc être
    /// ré-extraite ligne par ligne dans son propre bloc contigu (une
    /// ré-interprétation directe du buffer serait fausse : la tranche N
    /// n'est pas contiguë en mémoire dans l'atlas source).
    /// </summary>
    public VolumeAsset LoadVolume(string filePath, bool verticalFlip = false)
    {
        var atlas = Load(filePath, verticalFlip);
        var sliceCount = Videotoy.Core.ShaderModel.volumeSliceCountFromAtlasDimensions(atlas.Width, atlas.Height);
        var sliceHeight = atlas.Height;
        var sliceWidth = atlas.Width / sliceCount;

        const int bytesPerPixel = 4;
        var atlasStride = atlas.Width * bytesPerPixel;
        var sliceStride = sliceWidth * bytesPerPixel;
        var slices = new byte[sliceCount * sliceStride * sliceHeight];

        for (var slice = 0; slice < sliceCount; slice++)
        {
            var sliceXOffset = slice * sliceStride;
            var sliceDestinationBase = slice * sliceStride * sliceHeight;

            for (var row = 0; row < sliceHeight; row++)
            {
                var sourceOffset = row * atlasStride + sliceXOffset;
                var destinationOffset = sliceDestinationBase + row * sliceStride;
                Buffer.BlockCopy(atlas.PixelDataBgra, sourceOffset, slices, destinationOffset, sliceStride);
            }
        }

        return new VolumeAsset
        {
            SliceWidth = sliceWidth,
            SliceHeight = sliceHeight,
            SliceCount = sliceCount,
            SlicesBgra = slices
        };
    }
}
