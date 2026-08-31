using System;

namespace Videotoy.Media;

/// <summary>
/// Serializable snapshot of the render settings panel's export
/// configuration, persisted to <c>%AppData%\Videotoy\export-presets.json</c>
/// by <see cref="ExportPresetService"/>. Deliberately made of plain
/// primitives (not the <c>Videotoy.Core.Domain</c> discriminated unions
/// directly) so it serializes losslessly with <c>System.Text.Json</c> and
/// stays stable across refactors of the F# domain types.
/// </summary>
public sealed class ExportPreset
{
    public required string Name { get; init; }

    public required string ResolutionPresetName { get; init; }

    public required int CustomResolutionWidth { get; init; }

    public required int CustomResolutionHeight { get; init; }

    public required string FrameRatePresetName { get; init; }

    public required double CustomFrameRateValue { get; init; }

    public required bool IsSeamlessLoopModeEnabled { get; init; }

    public required string ManualDurationUnit { get; init; }

    public required double ManualDurationValue { get; init; }

    public required double LoopDurationSeconds { get; init; }

    /// <summary>
    /// "Frame de fin exclusive" toggle: when true (default), the seamless
    /// loop's end frame (identical to its start frame) is never rendered,
    /// avoiding a duplicated frame on playback loop. When false, that end
    /// frame is included. Not <c>required</c> so presets saved by an older
    /// version — before this option existed — still deserialize, defaulting
    /// to the safe/default behavior (<c>true</c>).
    /// </summary>
    public bool IsLoopEndFrameExclusive { get; init; } = true;

    public required bool IsLowSpecModeEnabled { get; init; }

    public DateTime SavedUtc { get; init; } = DateTime.UtcNow;
}
