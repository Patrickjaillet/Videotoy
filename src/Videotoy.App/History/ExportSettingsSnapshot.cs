using Videotoy.App.ViewModels;

namespace Videotoy.App.History;

/// <summary>
/// Copie immuable de tous les paramètres de rendu/export undoable de
/// <see cref="MainWindowViewModel"/> — jamais le contenu du shader lui-même.
/// L'égalité par valeur (record) permet de détecter une transaction sans
/// effet réel (Before == After) et d'éviter de pousser une entrée d'historique
/// inutile. <see cref="ApplyTo"/> réaffecte chaque champ via les vraies
/// propriétés (pas les champs de sauvegarde) afin que les cascades existantes
/// (ex. changer le codec vidéo réinitialise le profil vidéo) et
/// <c>RecalculateExportPreview</c> continuent de s'exécuter normalement.
/// </summary>
public sealed record ExportSettingsSnapshot(
    ResolutionPresetOption SelectedResolutionPreset,
    int CustomResolutionWidth,
    int CustomResolutionHeight,
    FrameRatePresetOption SelectedFrameRatePreset,
    double CustomFrameRateValue,
    DurationUnit ManualDurationUnit,
    double ManualDurationValue,
    bool IsSeamlessLoopModeEnabled,
    double LoopDurationSeconds,
    bool IsLoopEndFrameExclusive,
    ExportKindOption SelectedExportKind,
    AnimatedImageFormatOption SelectedAnimatedImageFormat,
    int GifColorCount,
    GifDitherOption SelectedGifDither,
    int WebPQuality,
    bool IsWebPLosslessEnabled,
    ContainerFormatOption SelectedContainerFormat,
    VideoCodecOption SelectedVideoCodec,
    bool IsTargetBitrateModeEnabled,
    int TargetBitrateKbps,
    int ConstantRateFactorValue,
    SpeedPresetOption SelectedSpeedPreset,
    VideoProfileOption SelectedVideoProfile,
    bool IsGopSizeEnabled,
    int GopSizeValue,
    bool IsTwoPassEnabled,
    HardwareEncoderOption SelectedHardwareEncoder,
    AudioCodecOption SelectedAudioCodec,
    int AudioBitrateKbps,
    bool IncludeAudioInExport)
{
    public static ExportSettingsSnapshot Capture(MainWindowViewModel viewModel) => new(
        viewModel.SelectedResolutionPreset,
        viewModel.CustomResolutionWidth,
        viewModel.CustomResolutionHeight,
        viewModel.SelectedFrameRatePreset,
        viewModel.CustomFrameRateValue,
        viewModel.ManualDurationUnit,
        viewModel.ManualDurationValue,
        viewModel.IsSeamlessLoopModeEnabled,
        viewModel.LoopDurationSeconds,
        viewModel.IsLoopEndFrameExclusive,
        viewModel.SelectedExportKind,
        viewModel.SelectedAnimatedImageFormat,
        viewModel.GifColorCount,
        viewModel.SelectedGifDither,
        viewModel.WebPQuality,
        viewModel.IsWebPLosslessEnabled,
        viewModel.SelectedContainerFormat,
        viewModel.SelectedVideoCodec,
        viewModel.IsTargetBitrateModeEnabled,
        viewModel.TargetBitrateKbps,
        viewModel.ConstantRateFactorValue,
        viewModel.SelectedSpeedPreset,
        viewModel.SelectedVideoProfile,
        viewModel.IsGopSizeEnabled,
        viewModel.GopSizeValue,
        viewModel.IsTwoPassEnabled,
        viewModel.SelectedHardwareEncoder,
        viewModel.SelectedAudioCodec,
        viewModel.AudioBitrateKbps,
        viewModel.IncludeAudioInExport);

    public void ApplyTo(MainWindowViewModel viewModel)
    {
        viewModel.SelectedResolutionPreset = SelectedResolutionPreset;
        viewModel.CustomResolutionWidth = CustomResolutionWidth;
        viewModel.CustomResolutionHeight = CustomResolutionHeight;
        viewModel.SelectedFrameRatePreset = SelectedFrameRatePreset;
        viewModel.CustomFrameRateValue = CustomFrameRateValue;
        viewModel.ManualDurationUnit = ManualDurationUnit;
        viewModel.ManualDurationValue = ManualDurationValue;
        viewModel.IsSeamlessLoopModeEnabled = IsSeamlessLoopModeEnabled;
        viewModel.LoopDurationSeconds = LoopDurationSeconds;
        viewModel.IsLoopEndFrameExclusive = IsLoopEndFrameExclusive;
        viewModel.SelectedExportKind = SelectedExportKind;
        viewModel.SelectedAnimatedImageFormat = SelectedAnimatedImageFormat;
        viewModel.GifColorCount = GifColorCount;
        viewModel.SelectedGifDither = SelectedGifDither;
        viewModel.WebPQuality = WebPQuality;
        viewModel.IsWebPLosslessEnabled = IsWebPLosslessEnabled;
        viewModel.SelectedContainerFormat = SelectedContainerFormat;
        viewModel.SelectedVideoCodec = SelectedVideoCodec;
        viewModel.IsTargetBitrateModeEnabled = IsTargetBitrateModeEnabled;
        viewModel.TargetBitrateKbps = TargetBitrateKbps;
        viewModel.ConstantRateFactorValue = ConstantRateFactorValue;
        viewModel.SelectedSpeedPreset = SelectedSpeedPreset;
        viewModel.SelectedVideoProfile = SelectedVideoProfile;
        viewModel.IsGopSizeEnabled = IsGopSizeEnabled;
        viewModel.GopSizeValue = GopSizeValue;
        viewModel.IsTwoPassEnabled = IsTwoPassEnabled;
        viewModel.SelectedHardwareEncoder = SelectedHardwareEncoder;
        viewModel.SelectedAudioCodec = SelectedAudioCodec;
        viewModel.AudioBitrateKbps = AudioBitrateKbps;
        viewModel.IncludeAudioInExport = IncludeAudioInExport;
    }
}
