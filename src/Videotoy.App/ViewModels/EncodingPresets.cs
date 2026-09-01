using Videotoy.Core.Domain;

namespace Videotoy.App.ViewModels;

public sealed record VideoCodecOption(string Key, string DisplayName, VideoCodec Value)
{
    public static readonly VideoCodecOption H264 = new("H264", "H.264", VideoCodec.H264);
    public static readonly VideoCodecOption H265 = new("H265", "H.265", VideoCodec.H265);

    public static readonly IReadOnlyList<VideoCodecOption> All = [H264, H265];

    public static VideoCodecOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? H264;
}

public sealed record SpeedPresetOption(string Key, string DisplayName, EncodingSpeedPreset Value)
{
    public static readonly SpeedPresetOption UltraFast = new("UltraFast", "Ultra fast", EncodingSpeedPreset.UltraFast);
    public static readonly SpeedPresetOption SuperFast = new("SuperFast", "Super fast", EncodingSpeedPreset.SuperFast);
    public static readonly SpeedPresetOption VeryFast = new("VeryFast", "Very fast", EncodingSpeedPreset.VeryFast);
    public static readonly SpeedPresetOption Faster = new("Faster", "Faster", EncodingSpeedPreset.Faster);
    public static readonly SpeedPresetOption Fast = new("Fast", "Fast", EncodingSpeedPreset.Fast);
    public static readonly SpeedPresetOption Medium = new("Medium", "Medium", EncodingSpeedPreset.Medium);
    public static readonly SpeedPresetOption Slow = new("Slow", "Slow", EncodingSpeedPreset.Slow);
    public static readonly SpeedPresetOption Slower = new("Slower", "Slower", EncodingSpeedPreset.Slower);
    public static readonly SpeedPresetOption VerySlow = new("VerySlow", "Very slow", EncodingSpeedPreset.VerySlow);

    public static readonly IReadOnlyList<SpeedPresetOption> All =
        [UltraFast, SuperFast, VeryFast, Faster, Fast, Medium, Slow, Slower, VerySlow];

    public static SpeedPresetOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Medium;
}

/// <summary>
/// Preset entry for the video profile combo box, whose available options
/// depend on the currently selected <see cref="VideoCodecOption"/> — see
/// <see cref="MainWindowViewModel.VideoProfileOptions"/>, which filters
/// <see cref="All"/> by <see cref="IsForH265"/>.
/// </summary>
public sealed record VideoProfileOption(string Key, string DisplayName, VideoProfile Value, bool IsForH265)
{
    public static readonly VideoProfileOption None = new("None", "Default", VideoProfile.NoProfilePreference, false);
    public static readonly VideoProfileOption Baseline = new(
        "Baseline", "Baseline", VideoProfile.NewH264ProfileSelection(H264Profile.BaselineProfile), false);
    public static readonly VideoProfileOption Main = new(
        "Main", "Main", VideoProfile.NewH264ProfileSelection(H264Profile.MainProfile), false);
    public static readonly VideoProfileOption High = new(
        "High", "High", VideoProfile.NewH264ProfileSelection(H264Profile.HighProfile), false);
    public static readonly VideoProfileOption Main265 = new(
        "Main265", "Main", VideoProfile.NewH265ProfileSelection(H265Profile.MainProfile265), true);
    public static readonly VideoProfileOption Main10 = new(
        "Main10", "Main 10", VideoProfile.NewH265ProfileSelection(H265Profile.Main10Profile265), true);

    public static readonly IReadOnlyList<VideoProfileOption> All =
        [None, Baseline, Main, High, Main265, Main10];

    public static VideoProfileOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? None;
}

public sealed record HardwareEncoderOption(string Key, string DisplayName, HardwareEncoderPreference Value)
{
    public static readonly HardwareEncoderOption Software = new("Software", "Software (CPU)", HardwareEncoderPreference.SoftwareOnly);
    public static readonly HardwareEncoderOption Nvenc = new("Nvenc", "NVIDIA NVENC", HardwareEncoderPreference.PreferNvenc);
    public static readonly HardwareEncoderOption QuickSync = new("QuickSync", "Intel Quick Sync", HardwareEncoderPreference.PreferQuickSync);
    public static readonly HardwareEncoderOption Amf = new("Amf", "AMD AMF", HardwareEncoderPreference.PreferAmf);

    public static readonly IReadOnlyList<HardwareEncoderOption> All = [Software, Nvenc, QuickSync, Amf];

    public static HardwareEncoderOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Software;
}

public sealed record AudioCodecOption(string Key, string DisplayName, AudioCodec Value)
{
    public static readonly AudioCodecOption Aac = new("Aac", "AAC", AudioCodec.Aac);
    public static readonly AudioCodecOption Copy = new("Copy", "Copy (no re-encode)", AudioCodec.Copy);

    public static readonly IReadOnlyList<AudioCodecOption> All = [Aac, Copy];

    public static AudioCodecOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Aac;
}
