using Videotoy.Core.Domain;

namespace Videotoy.Ffmpeg;

/// <summary>
/// Options FFmpeg résolues pour un export image animée (GIF/WebP), analogue
/// à <see cref="FfmpegEncodingOptions"/> pour l'export vidéo mais sans
/// aucune notion de conteneur/codec vidéo/piste audio. <see cref="PaletteFilePath"/>
/// n'est utilisé que pour GIF (fichier PNG intermédiaire produit par la
/// passe <see cref="AnimatedImagePass.GifPaletteGen"/>, consommé par
/// <see cref="AnimatedImagePass.GifPaletteUse"/>, puis supprimé par
/// <see cref="AnimatedImageExportPipeline"/> après l'export).
/// </summary>
public sealed record FfmpegAnimatedImageOptions(
    int Width,
    int Height,
    double FrameRate,
    string OutputFilePath,
    string PaletteFilePath,
    int GifColorCount,
    string GifDitherName,
    int WebPQuality,
    bool WebPLossless)
{
    public static FfmpegAnimatedImageOptions FromExportSettings(AnimatedImageExportSettings settings)
    {
        if (!Videotoy.Core.AnimatedImageExportSettingsValidator.isValid(settings))
        {
            var issues = Videotoy.Core.AnimatedImageExportSettingsValidator.validate(settings);
            throw new ArgumentException(
                $"Invalid animated image export settings: {string.Join(", ", issues)}", nameof(settings));
        }

        var outputFilePath = Videotoy.Core.AnimatedImageExportSettingsValidator.resolveOutputFilePath(settings);

        return new FfmpegAnimatedImageOptions(
            settings.Resolution.Width,
            settings.Resolution.Height,
            settings.FrameRate.Value,
            outputFilePath,
            outputFilePath + ".palette.png",
            settings.Encoding.GifColorCount,
            Videotoy.Core.AnimatedImageExportSettingsValidator.resolveGifDitherName(settings.Encoding.GifDither),
            settings.Encoding.WebPQuality,
            settings.Encoding.WebPLossless);
    }
}
