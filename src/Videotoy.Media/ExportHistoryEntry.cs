namespace Videotoy.Media;

public enum ExportHistoryResult
{
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>
/// Record of a single completed (successful, failed, or cancelled) video
/// export, persisted by <see cref="ExportHistoryService"/>. Deliberately
/// made of plain primitives (not the <c>Videotoy.Core.Domain</c>
/// discriminated unions directly) so it serializes losslessly with
/// <c>System.Text.Json</c> and stays stable across refactors of the F#
/// domain types — mirrors <see cref="ExportPreset"/>'s stated rationale.
/// </summary>
public sealed class ExportHistoryEntry
{
    public required string ShaderFilePath { get; init; }

    public required string ShaderDisplayName { get; init; }

    public required string OutputFilePath { get; init; }

    public required int ResolutionWidth { get; init; }

    public required int ResolutionHeight { get; init; }

    public required double FrameRateValue { get; init; }

    public required double DurationSeconds { get; init; }

    public required string CodecName { get; init; }

    public required string RateControlSummary { get; init; }

    public required string SpeedPresetName { get; init; }

    public required string HardwareEncoderKey { get; init; }

    public required TimeSpan EncodingDuration { get; init; }

    public required ExportHistoryResult Result { get; init; }

    public string? ErrorSummary { get; init; }

    public DateTime CompletedUtc { get; init; } = DateTime.UtcNow;
}
