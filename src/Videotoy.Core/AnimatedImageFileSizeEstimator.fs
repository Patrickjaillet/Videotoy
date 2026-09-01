module Videotoy.Core.AnimatedImageFileSizeEstimator

open Videotoy.Core.Domain

/// Coarse bits-per-pixel estimate for an indexed-palette GIF frame: roughly
/// log2(colorCount) bits are needed per pixel to address the palette, then
/// discounted for the temporal redundancy LZW compresses well in typical
/// looping content (largely-static backgrounds, repeating motion) — a
/// perfectly noisy loop would compress far worse than this, but this is
/// meant as an order-of-magnitude figure, not a calibrated measurement,
/// same spirit as `ExportFileSizeEstimator.bitsPerPixelForCrf`.
let private gifBitsPerPixel (colorCount: int) : float =
    let clamped = System.Math.Clamp(colorCount, 2, 256)
    let indexBits = System.Math.Log2(float clamped)
    let temporalRedundancyDiscount = 0.35
    (indexBits / 8.0) * temporalRedundancyDiscount

/// Coarse bits-per-pixel estimate for a lossy WebP frame, mirroring the
/// qualitative shape of `bitsPerPixelForCrf` (higher quality = more bits).
/// Lossless WebP uses a fixed, higher multiplier instead, since it forgoes
/// quality-driven compression entirely.
let private webpBitsPerPixel (quality: int) (lossless: bool) : float =
    if lossless then
        0.5
    else
        let clamped = System.Math.Clamp(quality, 0, 100)
        0.02 + (0.10 * float clamped / 100.0)

/// Estimates an animated image export's size in bytes. Fundamentally more
/// content-dependent than video CRF curves (palette-indexed/lossy image
/// compression varies wildly with actual pixel content) — this is
/// deliberately coarse, purely informational, meant to populate a "~X MB"
/// hint next to the frame-count preview, not to guarantee an exact output
/// size. No audio component: animated images carry no audio track.
let estimateFileSizeBytes
    (resolution: Resolution)
    (frameRate: FrameRate)
    (format: AnimatedImageFormat)
    (encoding: AnimatedImageEncodingOptions)
    (frameCount: int)
    : float =
    if resolution.Width <= 0 || resolution.Height <= 0 || frameRate.Value <= 0.0 || frameCount <= 0 then
        0.0
    else
        let pixelsPerFrame = float (resolution.Width * resolution.Height)

        let bitsPerPixel =
            match format with
            | Gif -> gifBitsPerPixel encoding.GifColorCount
            | AnimatedWebP -> webpBitsPerPixel encoding.WebPQuality encoding.WebPLossless

        bitsPerPixel * pixelsPerFrame * float frameCount / 8.0

/// Same "~X MB"/"~X GB" formatting as `ExportFileSizeEstimator.formatEstimatedFileSize`,
/// duplicated here rather than cross-referenced so this module stays
/// independent of the video estimator.
let formatEstimatedFileSize (bytes: float) : string =
    if bytes <= 0.0 then
        "-"
    else
        let megabytes = bytes / (1024.0 * 1024.0)

        if megabytes >= 1024.0 then
            sprintf "~%.1f GB" (megabytes / 1024.0)
        else
            sprintf "~%.1f MB" megabytes
