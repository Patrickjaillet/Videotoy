using Videotoy.Core.Domain;

namespace Videotoy.Ffmpeg;

/// <summary>
/// Décrit la piste audio à muxer avec la vidéo générée, lorsque le shader
/// exporté possède un <c>iChannel</c> audio. <see cref="SourceFilePath"/> est
/// le fichier audio original (WAV/MP3/OGG) tel que résolu depuis le shader
/// chargé ; FFmpeg lui-même se charge du décodage et de l'encodage AAC lors
/// du muxage, en une seule passe avec le flux vidéo brut lu depuis stdin.
/// <see cref="DurationSeconds"/> doit toujours être la durée **effective**
/// de la vidéo exportée — dérivée du nombre de frames réellement rendu
/// (<see cref="Videotoy.Core.LoopCalculator.effectiveDurationSeconds"/>),
/// jamais la durée brute demandée par l'utilisateur — afin que la piste
/// audio muxée se termine exactement avec la dernière frame vidéo, avec la
/// même origine <c>t = 0</c>, y compris en mode boucle parfaite où un
/// arrondi du nombre de frames peut légèrement décaler la durée effective
/// par rapport à la durée de boucle demandée. Construite exclusivement par
/// <see cref="VideoExportPipeline.RunAsync"/>, qui seul connaît cette durée
/// effective ; ce type ne doit pas être instancié en dehors de
/// <c>Videotoy.Ffmpeg</c> avec une durée arbitraire.
/// </summary>
public sealed record FfmpegAudioTrackOptions(string SourceFilePath, double DurationSeconds);

public sealed record FfmpegEncodingOptions(
    int Width,
    int Height,
    double FrameRate,
    string OutputFilePath,
    VideoCodec Codec,
    string VideoCodecName,
    int? ConstantRateFactor,
    int? TargetBitrateKbps,
    FfmpegAudioTrackOptions? AudioTrack,
    string SpeedPreset,
    string VideoProfileName,
    int? GopSize,
    bool IsTwoPass,
    string HardwareEncoderKey,
    string AudioCodecName,
    int AudioBitrateKbps)
{
    public static FfmpegEncodingOptions FromExportSettings(
        ExportSettings settings,
        FfmpegAudioTrackOptions? audioTrack = null)
    {
        if (!Videotoy.Core.ExportSettingsValidator.isValid(settings))
        {
            var issues = Videotoy.Core.ExportSettingsValidator.validate(settings);
            throw new ArgumentException(
                $"Invalid export settings: {string.Join(", ", issues)}", nameof(settings));
        }

        var targetBitrateKbps = ToNullableInt(Videotoy.Core.ExportSettingsValidator.tryResolveTargetBitrateKbps(settings.RateControl));

        // Le mode deux passes n'a de sens qu'en contrôle de débit ciblé
        // (TargetBitrate) : en mode CRF, une seule passe suffit déjà à
        // atteindre la qualité demandée, donc TwoPass est silencieusement
        // ramené à SinglePass plutôt que de lever une erreur de validation.
        var isTwoPass = targetBitrateKbps.HasValue
            && Videotoy.Core.ExportSettingsValidator.resolvePassModeIsTwoPass(settings.Encoding.PassMode);

        return new FfmpegEncodingOptions(
            settings.Resolution.Width,
            settings.Resolution.Height,
            settings.FrameRate.Value,
            Videotoy.Core.ExportSettingsValidator.resolveOutputFilePath(settings),
            settings.Codec,
            Videotoy.Core.ExportSettingsValidator.resolveCodecName(settings.Codec),
            ToNullableInt(Videotoy.Core.ExportSettingsValidator.tryResolveConstantRateFactor(settings.RateControl)),
            targetBitrateKbps,
            audioTrack,
            Videotoy.Core.ExportSettingsValidator.resolveSpeedPresetName(settings.Encoding.Speed),
            Videotoy.Core.ExportSettingsValidator.tryResolveVideoProfileName(settings.Encoding.Profile),
            ToNullableInt(Videotoy.Core.ExportSettingsValidator.resolveGopSize(settings.Encoding.GopSize)),
            isTwoPass,
            Videotoy.Core.ExportSettingsValidator.resolveHardwareEncoderPreferenceKey(settings.Encoding.HardwareEncoder),
            Videotoy.Core.ExportSettingsValidator.resolveAudioCodecName(settings.Encoding.AudioCodec),
            settings.Encoding.AudioBitrateKbps);
    }

    private static int? ToNullableInt(System.Nullable<int> value) =>
        value.HasValue ? value.Value : null;
}
