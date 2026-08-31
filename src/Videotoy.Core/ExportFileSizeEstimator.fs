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

let private audioBitsPerSecond = 192_000.0

/// Estimates the exported file's size in bytes for the given settings and
/// total frame count, assuming H.264/H.265 constant-rate-factor encoding.
/// Purely informational (see `bitsPerPixelForCrf`) — meant to populate a
/// "~X MB" hint next to the frame-count preview, not to guarantee an exact
/// output size.
let estimateFileSizeBytes
    (resolution: Resolution)
    (frameRate: FrameRate)
    (rateControl: RateControlMode)
    (frameCount: int)
    (includeAudio: bool)
    : float =
    if resolution.Width <= 0 || resolution.Height <= 0 || frameRate.Value <= 0.0 || frameCount <= 0 then
        0.0
    else
        let durationSeconds = float frameCount / frameRate.Value
        let pixelsPerFrame = float (resolution.Width * resolution.Height)

        let videoBitsPerSecond =
            match rateControl with
            | TargetBitrate kbps -> float kbps * 1000.0
            | ConstantRateFactor crf ->
                let bitsPerPixel = bitsPerPixelForCrf crf
                bitsPerPixel * pixelsPerFrame * frameRate.Value

        let audioBps = if includeAudio then audioBitsPerSecond else 0.0
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
