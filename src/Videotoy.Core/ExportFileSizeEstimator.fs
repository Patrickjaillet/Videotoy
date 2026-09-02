module Videotoy.Core.ExportFileSizeEstimator

open Videotoy.Core.Domain

/// Very rough bits-per-pixel factor used to turn a constant-rate-factor (CRF)
/// H.264/H.265 encode into an estimated output bitrate, in the absence of an
/// actual test encode. Higher CRF values mean stronger compression (lower
/// bitrate); this table intentionally only covers the CRF range accepted by
/// `ExportSettingsValidator` (0-51) and is deliberately coarse — it exists to
/// give the user an order-of-magnitude "estimated file size" figure in the
/// render settings panel, never an exact prediction.
let private bitsPerPixelForCrf (crf: int) : float =
    let clamped = System.Math.Clamp(crf, 0, 51)
    // Linear falloff from a generous 0.12 bit/pixel at CRF 0 down to a very
    // light 0.01 bit/pixel at CRF 51, matching the qualitative CRF scale
    // (0 = visually lossless, 51 = worst quality) without pretending to
    // reproduce libx264/libx265's actual rate-control curve.
    0.12 - (0.11 * float clamped / 51.0)

/// Coarse, qualitative efficiency multiplier for the video codec itself,
/// applied to the CRF-based bitrate estimate: H.265 is materially more
/// efficient than H.264 at the same CRF value for the same perceived
/// quality. Like `bitsPerPixelForCrf`, this is not a calibrated measurement,
/// only an order-of-magnitude correction.
let private codecEfficiencyFactor (codec: VideoCodec) : float =
    match codec with
    | H264 -> 1.0
    | H265 -> 0.5
    | Vp9 -> 0.55
    | ProRes -> 1.0 // Unused by the ProRes estimate path; present for match exhaustiveness only.

/// Coarse, qualitative efficiency multiplier for the encoding speed preset:
/// faster presets (`UltraFast`) trade compression efficiency for speed, so
/// the same CRF value yields a measurably larger output than at `VerySlow`.
let private speedPresetEfficiencyFactor (speed: EncodingSpeedPreset) : float =
    match speed with
    | UltraFast -> 1.35
    | SuperFast -> 1.25
    | VeryFast -> 1.15
    | Faster -> 1.08
    | Fast -> 1.03
    | Medium -> 1.0
    | Slow -> 0.93
    | Slower -> 0.88
    | VerySlow -> 0.82

/// Coarse, qualitative efficiency multiplier for the selected video profile:
/// a Baseline H.264 profile forgoes B-frames and is measurably less
/// efficient than High; profile choice is otherwise a secondary factor
/// compared to `codecEfficiencyFactor`/`speedPresetEfficiencyFactor`.
let private profileEfficiencyFactor (profile: VideoProfile) : float =
    match profile with
    | H264ProfileSelection BaselineProfile -> 1.10
    | H264ProfileSelection MainProfile -> 1.0
    | H264ProfileSelection HighProfile -> 0.95
    | H265ProfileSelection MainProfile265 -> 1.0
    | H265ProfileSelection Main10Profile265 -> 1.05
    | ProResProfileSelection _ -> 1.0 // Unused by the ProRes estimate path; present for match exhaustiveness only.
    | NoProfilePreference -> 1.0

/// ProRes bits-per-pixel-per-frame constants, derived from Apple's published
/// reference bitrates at 1920x1080/29.97fps (147/220/330 Mbps for
/// 422/422 HQ/4444 respectively), normalized to pixels so the estimate scales
/// with any resolution/frame rate. ProRes has no CRF/bitrate concept: its
/// output size is fully determined by resolution, frame rate and profile —
/// this is a fundamentally different, non-quality-driven estimate from
/// `bitsPerPixelForCrf`.
let private proResBitsPerPixel (profile: VideoProfile) : float =
    let referencePixelsPerSecond = 1920.0 * 1080.0 * 29.97
    match profile with
    | ProResProfileSelection ProResProfile422 -> 147_000_000.0 / referencePixelsPerSecond
    | ProResProfileSelection ProResProfile422Hq -> 220_000_000.0 / referencePixelsPerSecond
    | ProResProfileSelection ProResProfile4444 -> 330_000_000.0 / referencePixelsPerSecond
    | _ -> 147_000_000.0 / referencePixelsPerSecond

/// Coarse multiplier accounting for the extra bandwidth a straight alpha
/// channel adds on top of the color planes. ProRes 4444's bits-per-pixel
/// constant (see `proResBitsPerPixel`) is Apple's own published reference
/// rate for that profile and already includes its alpha plane — no
/// additional factor needed there. VP9's `yuva420p` alpha is encoded as a
/// second, smaller alt-ref grayscale stream (not a full extra YUV frame),
/// roughly a third of the base estimate.
let private alphaMultiplier (codec: VideoCodec) (alphaMode: AlphaMode) : float =
    match codec, alphaMode with
    | Vp9, Straight -> 1.35
    | _ -> 1.0

/// Estimates the exported file's size in bytes for the given settings and
/// total frame count. In `ConstantRateFactor` mode, the baseline
/// CRF-derived bitrate (see `bitsPerPixelForCrf`) is further adjusted by
/// coarse, qualitative multipliers accounting for the chosen codec, speed
/// preset and profile (see `codecEfficiencyFactor`, `speedPresetEfficiencyFactor`,
/// `profileEfficiencyFactor`) — none of these are calibrated measurements,
/// only order-of-magnitude corrections. `TargetBitrate` mode is already
/// exact and unaffected by any of these factors. Purely informational —
/// meant to populate a "~X MB" hint next to the frame-count preview, not to
/// guarantee an exact output size.
let estimateFileSizeBytes
    (resolution: Resolution)
    (frameRate: FrameRate)
    (rateControl: RateControlMode)
    (codec: VideoCodec)
    (encoding: EncodingOptions)
    (frameCount: int)
    (includeAudio: bool)
    (alphaMode: AlphaMode)
    : float =
    if resolution.Width <= 0 || resolution.Height <= 0 || frameRate.Value <= 0.0 || frameCount <= 0 then
        0.0
    else
        let durationSeconds = float frameCount / frameRate.Value
        let pixelsPerFrame = float (resolution.Width * resolution.Height)

        let videoBitsPerSecond =
            match codec with
            | ProRes ->
                proResBitsPerPixel encoding.Profile * pixelsPerFrame * frameRate.Value
            | H264 | H265 | Vp9 ->
                match rateControl with
                | TargetBitrate kbps -> float kbps * 1000.0 * alphaMultiplier codec alphaMode
                | ConstantRateFactor crf ->
                    let bitsPerPixel = bitsPerPixelForCrf crf
                    let baseline = bitsPerPixel * pixelsPerFrame * frameRate.Value
                    baseline
                    * codecEfficiencyFactor codec
                    * speedPresetEfficiencyFactor encoding.Speed
                    * profileEfficiencyFactor encoding.Profile
                    * alphaMultiplier codec alphaMode

        let audioBps = if includeAudio then float encoding.AudioBitrateKbps * 1000.0 else 0.0
        let totalBitsPerSecond = videoBitsPerSecond + audioBps

        totalBitsPerSecond * durationSeconds / 8.0

/// Formats an estimated byte count as a short human-readable string
/// (`"~12.4 MB"`, `"~1.8 GB"`), matching the `"~X unit"` convention expected
/// by the render settings panel's file-size preview.
let formatEstimatedFileSize (bytes: float) : string =
    if bytes <= 0.0 then
        "-"
    else
        let megabytes = bytes / (1024.0 * 1024.0)

        if megabytes >= 1024.0 then
            sprintf "~%.1f GB" (megabytes / 1024.0)
        else
            sprintf "~%.1f MB" megabytes
