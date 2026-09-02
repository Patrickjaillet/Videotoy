using Videotoy.Core.Domain;
using Videotoy.Media;

namespace Videotoy.App.ViewModels;

/// <summary>
/// Converts between a persisted <see cref="RenderQueueItem"/> (flattened
/// primitives) and the real F# domain settings the export pipelines expect
/// (<see cref="ExportSettings"/>/<see cref="AnimatedImageExportSettings"/>),
/// plus the reverse "capture the current render settings panel state into a
/// queue item" direction used by "Add current export to queue". This is the
/// exact inverse of <see cref="MainWindowViewModel.LoadExportPreset"/>'s
/// assignments and <see cref="MainWindowViewModel.SaveExportPreset"/>'s
/// field-copy logic, redirected to/from <see cref="RenderQueueItem"/>
/// instead of <see cref="ExportPreset"/>.
/// </summary>
public static class RenderQueueSettingsBuilder
{
    public static ExportSettings BuildExportSettings(RenderQueueItem item)
    {
        var resolutionPreset = ResolutionPresetOption.FromKey(item.ResolutionPresetName);
        var resolution = resolutionPreset.IsCustom
            ? new Resolution(Math.Max(0, item.CustomResolutionWidth), Math.Max(0, item.CustomResolutionHeight))
            : new Resolution(resolutionPreset.Width, resolutionPreset.Height);

        var frameRatePreset = FrameRatePresetOption.FromKey(item.FrameRatePresetName);
        var frameRate = new FrameRate(frameRatePreset.IsCustom ? item.CustomFrameRateValue : frameRatePreset.Value);

        var durationMode = BuildDurationMode(item, frameRate);

        var rateControl = item.IsTargetBitrateModeEnabled
            ? RateControlMode.NewTargetBitrate(item.TargetBitrateKbps)
            : RateControlMode.NewConstantRateFactor(item.ConstantRateFactorValue);

        var performance = item.IsLowSpecModeEnabled
            ? PerformanceMode.NewLowSpec(item.LowSpecThrottleMillisecondsPerFrame)
            : PerformanceMode.Normal;

        var encoding = new EncodingOptions(
            SpeedPresetOption.FromKey(item.SpeedPresetKey).Value,
            VideoProfileOption.FromKey(item.VideoProfileKey).Value,
            item.IsGopSizeEnabled
                ? Microsoft.FSharp.Core.FSharpOption<int>.Some(item.GopSizeValue)
                : Microsoft.FSharp.Core.FSharpOption<int>.None,
            item.IsTwoPassEnabled ? EncodingPassMode.TwoPass : EncodingPassMode.SinglePass,
            HardwareEncoderOption.FromKey(item.HardwareEncoderKey).Value,
            AudioCodecOption.FromKey(item.AudioCodecKey).Value,
            item.AudioBitrateKbps);

        return new ExportSettings(
            resolution,
            frameRate,
            durationMode,
            item.OutputDirectory,
            item.OutputFileName,
            VideoCodecOption.FromKey(item.VideoCodecKey).Value,
            rateControl,
            ContainerFormatOption.FromKey(item.ContainerFormatKey).Value,
            performance,
            encoding,
            AlphaModeOption.FromKey(item.AlphaModeKey).Value);
    }

    public static AnimatedImageExportSettings BuildAnimatedImageExportSettings(RenderQueueItem item)
    {
        var resolutionPreset = ResolutionPresetOption.FromKey(item.ResolutionPresetName);
        var resolution = resolutionPreset.IsCustom
            ? new Resolution(Math.Max(0, item.CustomResolutionWidth), Math.Max(0, item.CustomResolutionHeight))
            : new Resolution(resolutionPreset.Width, resolutionPreset.Height);

        var frameRatePreset = FrameRatePresetOption.FromKey(item.FrameRatePresetName);
        var frameRate = new FrameRate(frameRatePreset.IsCustom ? item.CustomFrameRateValue : frameRatePreset.Value);

        var encoding = new AnimatedImageEncodingOptions(
            item.GifColorCount,
            GifDitherOption.FromKey(item.GifDitherKey).Value,
            item.WebPQuality,
            item.IsWebPLosslessEnabled);

        return new AnimatedImageExportSettings(
            resolution,
            frameRate,
            item.LoopDurationSeconds,
            item.IsLoopEndFrameExclusive,
            item.OutputDirectory,
            item.OutputFileName,
            AnimatedImageFormatOption.FromKey(item.AnimatedImageFormatKey).Value,
            encoding);
    }

    private static DurationMode BuildDurationMode(RenderQueueItem item, FrameRate frameRate)
    {
        if (item.IsSeamlessLoopModeEnabled)
        {
            return DurationMode.NewSeamlessLoop(item.LoopDurationSeconds, item.IsLoopEndFrameExclusive);
        }

        var seconds = item.ManualDurationUnit == DurationUnit.Frames.ToString() && frameRate.Value > 0.0
            ? item.ManualDurationValue / frameRate.Value
            : item.ManualDurationValue;

        return DurationMode.NewManual(seconds);
    }

    /// <summary>
    /// Captures the render settings panel's current state into a new
    /// <see cref="RenderQueueItem"/> — the reverse direction of
    /// <see cref="BuildExportSettings"/>/<see cref="BuildAnimatedImageExportSettings"/>,
    /// used by "Add current export to queue". Mirrors
    /// <see cref="MainWindowViewModel.SaveExportPreset"/>'s field-copy logic.
    /// </summary>
    public static RenderQueueItem CaptureFromCurrentPanelState(
        MainWindowViewModel viewModel,
        string shaderFilePath,
        string shaderDisplayName,
        RenderQueueItemKind kind,
        Guid id)
    {
        return new RenderQueueItem
        {
            Id = id,
            ShaderFilePath = shaderFilePath,
            ShaderDisplayName = shaderDisplayName,
            Kind = kind,
            ResolutionPresetName = viewModel.SelectedResolutionPreset.Key,
            CustomResolutionWidth = viewModel.CustomResolutionWidth,
            CustomResolutionHeight = viewModel.CustomResolutionHeight,
            FrameRatePresetName = viewModel.SelectedFrameRatePreset.Key,
            CustomFrameRateValue = viewModel.CustomFrameRateValue,
            IsSeamlessLoopModeEnabled = viewModel.IsSeamlessLoopModeEnabled,
            ManualDurationUnit = viewModel.ManualDurationUnit.ToString(),
            ManualDurationValue = viewModel.ManualDurationValue,
            LoopDurationSeconds = viewModel.LoopDurationSeconds,
            IsLoopEndFrameExclusive = viewModel.IsLoopEndFrameExclusive,
            OutputDirectory = viewModel.OutputDirectory,
            OutputFileName = viewModel.OutputFileName,
            IsLowSpecModeEnabled = viewModel.IsLowSpecModeEnabled,
            IncludeAudioInExport = viewModel.IncludeAudioInExport,
            ContainerFormatKey = viewModel.SelectedContainerFormat.Key,
            VideoCodecKey = viewModel.SelectedVideoCodec.Key,
            IsTargetBitrateModeEnabled = viewModel.IsTargetBitrateModeEnabled,
            TargetBitrateKbps = viewModel.TargetBitrateKbps,
            ConstantRateFactorValue = viewModel.ConstantRateFactorValue,
            SpeedPresetKey = viewModel.SelectedSpeedPreset.Key,
            VideoProfileKey = viewModel.SelectedVideoProfile.Key,
            AlphaModeKey = viewModel.SelectedAlphaMode.Key,
            IsGopSizeEnabled = viewModel.IsGopSizeEnabled,
            GopSizeValue = viewModel.GopSizeValue,
            IsTwoPassEnabled = viewModel.IsTwoPassEnabled,
            HardwareEncoderKey = viewModel.SelectedHardwareEncoder.Key,
            AudioCodecKey = viewModel.SelectedAudioCodec.Key,
            AudioBitrateKbps = viewModel.AudioBitrateKbps,
            AnimatedImageFormatKey = viewModel.SelectedAnimatedImageFormat.Key,
            GifColorCount = viewModel.GifColorCount,
            GifDitherKey = viewModel.SelectedGifDither.Key,
            WebPQuality = viewModel.WebPQuality,
            IsWebPLosslessEnabled = viewModel.IsWebPLosslessEnabled
        };
    }
}
