using Videotoy.Core.Domain;

namespace Videotoy.App.ViewModels;

public sealed record ContainerFormatOption(string Key, string DisplayName, ContainerFormat Value)
{
    public static readonly ContainerFormatOption Mp4 = new("Mp4", "MP4", ContainerFormat.Mp4);
    public static readonly ContainerFormatOption WebM = new("WebM", "WebM", ContainerFormat.WebM);
    public static readonly ContainerFormatOption Mov = new("Mov", "MOV", ContainerFormat.Mov);

    public static readonly IReadOnlyList<ContainerFormatOption> All = [Mp4, WebM, Mov];

    public static ContainerFormatOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Mp4;
}

public sealed record VideoCodecOption(string Key, string DisplayName, VideoCodec Value)
{
    public static readonly VideoCodecOption H264 = new("H264", "H.264", VideoCodec.H264);
    public static readonly VideoCodecOption H265 = new("H265", "H.265", VideoCodec.H265);
    public static readonly VideoCodecOption Vp9 = new("Vp9", "VP9", VideoCodec.Vp9);
    public static readonly VideoCodecOption ProRes = new("ProRes", "Apple ProRes", VideoCodec.ProRes);

    public static readonly IReadOnlyList<VideoCodecOption> All = [H264, H265, Vp9, ProRes];

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
/// <see cref="All"/> by <see cref="CodecKey"/> (matched against
/// <see cref="VideoCodecOption.Key"/>, or <c>"Any"</c> for
/// <see cref="None"/>, always included regardless of codec).
/// </summary>
public sealed record VideoProfileOption(string Key, string DisplayName, VideoProfile Value, string CodecKey)
{
    public static readonly VideoProfileOption None = new("None", "Default", VideoProfile.NoProfilePreference, "Any");
    public static readonly VideoProfileOption Baseline = new(
        "Baseline", "Baseline", VideoProfile.NewH264ProfileSelection(H264Profile.BaselineProfile), "H264");
    public static readonly VideoProfileOption Main = new(
        "Main", "Main", VideoProfile.NewH264ProfileSelection(H264Profile.MainProfile), "H264");
    public static readonly VideoProfileOption High = new(
        "High", "High", VideoProfile.NewH264ProfileSelection(H264Profile.HighProfile), "H264");
    public static readonly VideoProfileOption Main265 = new(
        "Main265", "Main", VideoProfile.NewH265ProfileSelection(H265Profile.MainProfile265), "H265");
    public static readonly VideoProfileOption Main10 = new(
        "Main10", "Main 10", VideoProfile.NewH265ProfileSelection(H265Profile.Main10Profile265), "H265");
    public static readonly VideoProfileOption ProRes422 = new(
        "ProRes422", "ProRes 422", VideoProfile.NewProResProfileSelection(ProResProfile.ProResProfile422), "ProRes");
    public static readonly VideoProfileOption ProRes422Hq = new(
        "ProRes422Hq", "ProRes 422 HQ", VideoProfile.NewProResProfileSelection(ProResProfile.ProResProfile422Hq), "ProRes");
    public static readonly VideoProfileOption ProRes4444 = new(
        "ProRes4444", "ProRes 4444", VideoProfile.NewProResProfileSelection(ProResProfile.ProResProfile4444), "ProRes");

    public static readonly IReadOnlyList<VideoProfileOption> All =
        [None, Baseline, Main, High, Main265, Main10, ProRes422, ProRes422Hq, ProRes4444];

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

/// <summary>
/// Preset entry for the alpha mode toggle. <see cref="Straight"/> is only
/// offered in the UI when the current codec/profile combination actually
/// supports it (ProRes 4444 or VP9 — see
/// <see cref="Videotoy.Core.ExportSettingsValidator.isAlphaSupportedByCodec"/>);
/// selecting it otherwise is caught by <c>ExportSettingsValidator</c> before
/// export rather than silently ignored.
/// </summary>
public sealed record AlphaModeOption(string Key, string DisplayName, AlphaMode Value)
{
    public static readonly AlphaModeOption Opaque = new("Opaque", "Opaque", AlphaMode.Opaque);
    public static readonly AlphaModeOption Straight = new("Straight", "Straight alpha", AlphaMode.Straight);

    public static readonly IReadOnlyList<AlphaModeOption> All = [Opaque, Straight];

    public static AlphaModeOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Opaque;
}

public sealed record AudioCodecOption(string Key, string DisplayName, AudioCodec Value)
{
    public static readonly AudioCodecOption Aac = new("Aac", "AAC", AudioCodec.Aac);
    public static readonly AudioCodecOption Opus = new("Opus", "Opus", AudioCodec.Opus);
    public static readonly AudioCodecOption Copy = new("Copy", "Copy (no re-encode)", AudioCodec.Copy);

    public static readonly IReadOnlyList<AudioCodecOption> All = [Aac, Opus, Copy];

    public static AudioCodecOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Aac;

    /// <summary>
    /// Audio codecs meaningful for <paramref name="container"/>: WebM pairs
    /// with Opus (its native audio codec), MP4/MOV pair with AAC. Both
    /// always allow "Copy" (re-use the source track as-is).
    /// </summary>
    public static IReadOnlyList<AudioCodecOption> AllowedFor(ContainerFormatOption container) =>
        container == ContainerFormatOption.WebM ? [Opus, Copy] : [Aac, Copy];
}

/// <summary>
/// Top-level toggle between exporting a classic video file (MP4/WebM/MOV)
/// and an animated image (GIF/WebP) — the two are structurally distinct
/// pipelines (<see cref="VideoExportPipeline"/> vs.
/// <see cref="AnimatedImageExportPipeline"/>) with no shared settings.
/// </summary>
public sealed record ExportKindOption(string Key, string DisplayName)
{
    public static readonly ExportKindOption Video = new("Video", "Video (MP4/WebM/MOV)");
    public static readonly ExportKindOption AnimatedImage = new("AnimatedImage", "Animated image (GIF/WebP)");

    public static readonly IReadOnlyList<ExportKindOption> All = [Video, AnimatedImage];

    public static ExportKindOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Video;
}

public sealed record AnimatedImageFormatOption(string Key, string DisplayName, AnimatedImageFormat Value)
{
    public static readonly AnimatedImageFormatOption Gif = new("Gif", "GIF", AnimatedImageFormat.Gif);
    public static readonly AnimatedImageFormatOption WebP = new("AnimatedWebP", "Animated WebP", AnimatedImageFormat.AnimatedWebP);

    public static readonly IReadOnlyList<AnimatedImageFormatOption> All = [Gif, WebP];

    public static AnimatedImageFormatOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Gif;
}

public sealed record GifDitherOption(string Key, string DisplayName, GifDitherMode Value)
{
    public static readonly GifDitherOption NoDither = new("NoDither", "None", GifDitherMode.NoDither);
    public static readonly GifDitherOption Bayer = new("Bayer", "Bayer", GifDitherMode.Bayer);
    public static readonly GifDitherOption FloydSteinberg = new("FloydSteinberg", "Floyd-Steinberg", GifDitherMode.FloydSteinberg);
    public static readonly GifDitherOption Sierra2 = new("Sierra2", "Sierra2", GifDitherMode.Sierra2);
    public static readonly GifDitherOption Sierra2_4a = new("Sierra2_4a", "Sierra2 4A", GifDitherMode.Sierra2_4a);

    public static readonly IReadOnlyList<GifDitherOption> All = [NoDither, Bayer, FloydSteinberg, Sierra2, Sierra2_4a];

    public static GifDitherOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? FloydSteinberg;
}
