namespace Videotoy.App.ViewModels;

/// <summary>
/// Preset entry for the export resolution combo box. <see cref="Width"/> and
/// <see cref="Height"/> are meaningless (0) for <see cref="IsCustom"/> entries,
/// where the effective resolution instead comes from
/// <see cref="MainWindowViewModel.CustomResolutionWidth"/> /
/// <see cref="MainWindowViewModel.CustomResolutionHeight"/>.
/// </summary>
public sealed record ResolutionPresetOption(string Key, string DisplayName, int Width, int Height, bool IsCustom = false)
{
    public static readonly ResolutionPresetOption Preview = new("Preview", "Preview (800 x 450)", 800, 450);
    public static readonly ResolutionPresetOption Sd480 = new("Sd480", "SD (854 x 480)", 854, 480);
    public static readonly ResolutionPresetOption Hd720 = new("Hd720", "HD (1280 x 720)", 1280, 720);
    public static readonly ResolutionPresetOption FullHd1080 = new("FullHd1080", "Full HD (1920 x 1080)", 1920, 1080);
    public static readonly ResolutionPresetOption Uhd4K = new("Uhd4K", "4K UHD (3840 x 2160)", 3840, 2160);
    public static readonly ResolutionPresetOption Screen4By3 = new("Screen4By3", "Screen 4:3 (1440 x 1080)", 1440, 1080);
    public static readonly ResolutionPresetOption Screen16By9 = new("Screen16By9", "Screen 16:9 (1920 x 1080)", 1920, 1080);
    public static readonly ResolutionPresetOption Smartphone9By16 = new("Smartphone9By16", "Smartphone 9:16 (1080 x 1920)", 1080, 1920);
    public static readonly ResolutionPresetOption Custom = new("Custom", "Custom...", 0, 0, IsCustom: true);

    public static readonly IReadOnlyList<ResolutionPresetOption> All =
        [Preview, Sd480, Hd720, FullHd1080, Uhd4K, Screen4By3, Screen16By9, Smartphone9By16, Custom];

    /// <summary>
    /// Resolves a persisted <see cref="Key"/> (from
    /// <see cref="Videotoy.Media.ExportPreset.ResolutionPresetName"/>) back to
    /// its <see cref="ResolutionPresetOption"/>, falling back to
    /// <see cref="Custom"/> for an unrecognized or missing key (e.g. a preset
    /// saved by a future version that added a new resolution preset).
    /// </summary>
    public static ResolutionPresetOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Custom;
}

/// <summary>
/// Preset entry for the export frame rate combo box. <see cref="Value"/> is
/// meaningless (0) for <see cref="IsCustom"/>, where the effective frame rate
/// instead comes from <see cref="MainWindowViewModel.CustomFrameRateValue"/>.
/// </summary>
public sealed record FrameRatePresetOption(string Key, string DisplayName, double Value, bool IsCustom = false)
{
    public static readonly FrameRatePresetOption Fps24 = new("Fps24", "24 fps", 24.0);
    public static readonly FrameRatePresetOption Fps25 = new("Fps25", "25 fps", 25.0);
    public static readonly FrameRatePresetOption Fps30 = new("Fps30", "30 fps", 30.0);
    public static readonly FrameRatePresetOption Fps60 = new("Fps60", "60 fps", 60.0);
    public static readonly FrameRatePresetOption Custom = new("Custom", "Custom...", 0.0, IsCustom: true);

    public static readonly IReadOnlyList<FrameRatePresetOption> All =
        [Fps24, Fps25, Fps30, Fps60, Custom];

    /// <summary>
    /// Resolves a persisted <see cref="Key"/> (from
    /// <see cref="Videotoy.Media.ExportPreset.FrameRatePresetName"/>) back to
    /// its <see cref="FrameRatePresetOption"/>, falling back to
    /// <see cref="Custom"/> for an unrecognized or missing key.
    /// </summary>
    public static FrameRatePresetOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Custom;
}

/// <summary>
/// Unit in which the user expresses the manual export duration
/// (<see cref="MainWindowViewModel.ManualDurationValue"/>): either directly in
/// seconds, or in frames (converted to seconds against the selected frame rate
/// before being handed to <see cref="Videotoy.Core.Domain.DurationMode"/>).
/// </summary>
public enum DurationUnit
{
    Seconds,
    Frames
}
