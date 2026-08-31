namespace Videotoy.Media;

/// <summary>
/// Last-used "Duration Mode" state for a single shader file, persisted by
/// <see cref="LoopSettingsService"/> and keyed on the shader's absolute
/// file path. Deliberately separate from <see cref="ExportPreset"/>: presets
/// are named, user-curated, and reused across shaders, while this entry is
/// an implicit, per-file memory of what was last selected for that specific
/// shader — restored automatically on load, never explicitly named or
/// managed by the user.
/// </summary>
public sealed class LoopSettingsEntry
{
    /// <summary>
    /// Absolute path of the shader file this entry applies to. Matched
    /// case-insensitively against <see cref="ShaderFileService.Load"/>'s
    /// input path, mirroring <see cref="RecentShaderFile"/>'s convention.
    /// </summary>
    public required string ShaderFilePath { get; init; }

    /// <summary>
    /// True when "Seamless loop" duration mode was last selected for this
    /// shader, false for "Manual duration". Mirrors
    /// <c>MainWindowViewModel.IsSeamlessLoopModeEnabled</c>.
    /// </summary>
    public required bool IsSeamlessLoopModeEnabled { get; init; }

    /// <summary>
    /// Last loop duration (in seconds) used for this shader in "Seamless
    /// loop" mode, restored into
    /// <c>MainWindowViewModel.LoopDurationSeconds</c> regardless of which
    /// duration mode was last active — so switching to "Seamless loop"
    /// later still recalls the last value used for this shader specifically,
    /// rather than whatever was left over from the previously loaded shader.
    /// </summary>
    public required double LoopDurationSeconds { get; init; }

    public System.DateTime LastUpdatedUtc { get; init; } = System.DateTime.UtcNow;
}
