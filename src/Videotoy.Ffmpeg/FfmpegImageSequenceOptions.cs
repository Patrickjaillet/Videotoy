using Videotoy.Core.Domain;

namespace Videotoy.Ffmpeg;

/// <summary>
/// Options FFmpeg résolues pour un export séquence d'images (PNG 8/16-bit,
/// TIFF 16-bit, EXR 16-bit half-float), analogue à
/// <see cref="FfmpegAnimatedImageOptions"/> mais sans notion de boucle ni de
/// palette : chaque frame est encodée par une invocation FFmpeg indépendante
/// (voir <see cref="ImageSequenceExportPipeline"/>), une entrée <c>rawvideo</c>
/// BGRA sur stdin produisant un seul fichier image en sortie.
/// <see cref="PixelFormatName"/>/<see cref="VideoCodecName"/> encodent le
/// format de sortie effectif ; voir <see cref="ImageSequenceExportPipeline.BuildImageSequenceArguments"/>
/// pour comment ils sont assemblés en ligne de commande.
/// </summary>
public sealed record FfmpegImageSequenceOptions(
    int Width,
    int Height,
    double FrameRate,
    string OutputDirectory,
    ImageSequenceFormat Format,
    string VideoCodecName,
    string PixelFormatName,
    bool UseTiffLzwCompression)
{
    public static FfmpegImageSequenceOptions FromExportSettings(ImageSequenceExportSettings settings)
    {
        if (!Videotoy.Core.ImageSequenceExportSettingsValidator.isValid(settings))
        {
            var issues = Videotoy.Core.ImageSequenceExportSettingsValidator.validate(settings);
            throw new ArgumentException(
                $"Invalid image sequence export settings: {string.Join(", ", issues)}", nameof(settings));
        }

        var (codecName, pixelFormatName) = ResolveCodecAndPixelFormat(settings.Format);

        return new FfmpegImageSequenceOptions(
            settings.Resolution.Width,
            settings.Resolution.Height,
            settings.FrameRate.Value,
            settings.OutputDirectory,
            settings.Format,
            codecName,
            pixelFormatName,
            settings.TiffUseLzwCompression);
    }

    /// <summary>
    /// Résout le couple (codec, format de pixel) FFmpeg pour chaque format de
    /// séquence : PNG 8-bit -> <c>png</c>/<c>rgba</c> ; PNG 16-bit ->
    /// <c>png</c>/<c>rgba64be</c> (l'encodeur PNG de FFmpeg accepte le 16-bit
    /// via ce format de pixel big-endian) ; TIFF 16-bit -> <c>tiff</c>/
    /// <c>rgba64le</c> (compression LZW appliquée séparément via
    /// <c>-compression_algo lzw</c>, voir <see cref="ImageSequenceExportPipeline"/>) ;
    /// EXR 16-bit half-float -> <c>exr</c>/<c>gbrapf16le</c>. Ce dernier
    /// format de pixel est celui documenté pour l'encodeur EXR de FFmpeg
    /// (plans Vert/Bleu/Rouge/Alpha en demi-flottant peu-endian) ; il n'a pas
    /// pu être re-vérifié contre un binaire ffmpeg.exe réel dans cet
    /// environnement de développement (absent de <c>tools/ffmpeg/</c> ici) —
    /// à valider avec `ffmpeg -h encoder=exr` / `-pix_fmts` avant une release
    /// si des artefacts de couleur étaient constatés en usage réel.
    /// </summary>
    private static (string CodecName, string PixelFormatName) ResolveCodecAndPixelFormat(ImageSequenceFormat format)
    {
        if (format.Equals(ImageSequenceFormat.Png16))
        {
            return ("png", "rgba64be");
        }

        if (format.Equals(ImageSequenceFormat.Tiff16))
        {
            return ("tiff", "rgba64le");
        }

        if (format.Equals(ImageSequenceFormat.Exr16))
        {
            return ("exr", "gbrapf16le");
        }

        // Png8, and the default for match exhaustiveness.
        return ("png", "rgba");
    }
}
