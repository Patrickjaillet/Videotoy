using System;

namespace Videotoy.Media;

public enum RenderQueueItemKind
{
    Video,
    AnimatedImage
}

public enum RenderQueueItemStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>
/// Serializable snapshot of a single render-queue export job, persisted to
/// <c>%AppData%\Videotoy\render-queue.json</c> by <see cref="RenderQueueService"/>.
/// Deliberately made of plain primitives (not the <c>Videotoy.Core.Domain</c>
/// discriminated unions directly), exactly mirroring <see cref="ExportPreset"/>'s
/// flattening convention — each item owns a full copy of its export settings
/// rather than referencing a named preset, so it stays valid even if the
/// user later edits or deletes the preset it may have started from.
/// <see cref="Kind"/> selects which of the video-only/animated-image-only
/// field groups actually apply; the other group's fields are simply unused.
/// </summary>
public sealed class RenderQueueItem
{
    public required Guid Id { get; init; }

    public required string ShaderFilePath { get; init; }

    public required string ShaderDisplayName { get; init; }

    public required RenderQueueItemKind Kind { get; init; }

    // Shared settings — mirrors ExportPreset's shared fields exactly.
    public required string ResolutionPresetName { get; init; }

    public required int CustomResolutionWidth { get; init; }

    public required int CustomResolutionHeight { get; init; }

    public required string FrameRatePresetName { get; init; }

    public required double CustomFrameRateValue { get; init; }

    public required bool IsSeamlessLoopModeEnabled { get; init; }

    public required string ManualDurationUnit { get; init; }

    public required double ManualDurationValue { get; init; }

    public required double LoopDurationSeconds { get; init; }

    public bool IsLoopEndFrameExclusive { get; init; } = true;

    public required string OutputDirectory { get; init; }

    public required string OutputFileName { get; init; }

    // Video-only settings — meaningless when Kind = AnimatedImage.
    public bool IsLowSpecModeEnabled { get; init; }

    public int LowSpecThrottleMillisecondsPerFrame { get; init; } = 50;

    public bool IncludeAudioInExport { get; init; }

    public string VideoCodecKey { get; init; } = "H264";

    public bool IsTargetBitrateModeEnabled { get; init; }

    public int TargetBitrateKbps { get; init; } = 8000;

    public int ConstantRateFactorValue { get; init; } = 18;

    public string SpeedPresetKey { get; init; } = "Medium";

    public string VideoProfileKey { get; init; } = "None";

    public string AlphaModeKey { get; init; } = "Opaque";

    public bool IsGopSizeEnabled { get; init; }

    public int GopSizeValue { get; init; } = 250;

    public bool IsTwoPassEnabled { get; init; }

    public string HardwareEncoderKey { get; init; } = "Software";

    public string AudioCodecKey { get; init; } = "Aac";

    public int AudioBitrateKbps { get; init; } = 192;

    public string ContainerFormatKey { get; init; } = "Mp4";

    // AnimatedImage-only settings — meaningless when Kind = Video.
    public string AnimatedImageFormatKey { get; init; } = "Gif";

    public int GifColorCount { get; init; } = 256;

    public string GifDitherKey { get; init; } = "FloydSteinberg";

    public int WebPQuality { get; init; } = 90;

    public bool IsWebPLosslessEnabled { get; init; }

    // Mutable queue-management state, persisted so order/status survive a
    // restart. Per-item live progress (current frame/percent) is
    // deliberately NOT here — it is transient, owned in memory by
    // RenderQueueProcessor, and always starts at 0 on load.
    public int SortOrder { get; set; }

    public RenderQueueItemStatus Status { get; set; } = RenderQueueItemStatus.Pending;

    public string? ErrorSummary { get; set; }

    public DateTime AddedUtc { get; init; } = DateTime.UtcNow;
}
