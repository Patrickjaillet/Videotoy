module Videotoy.Core.ImageSequenceFileSizeEstimator

open Videotoy.Core.Domain

/// Coarse bytes-per-pixel estimate for a single frame, before per-format
/// compression: raw bytes-per-pixel for the format's bit depth (always
/// 4 channels — RGBA — since every image-sequence format in this app
/// carries alpha), times a compression fudge factor. PNG/TIFF/EXR have no
/// bitrate/CRF concept the way video codecs do, so this is deliberately
/// coarser than `ExportFileSizeEstimator` — an order-of-magnitude figure
/// only, same spirit as the other estimators in this module family.
let private bytesPerPixelForFormat (format: ImageSequenceFormat) (tiffUseLzwCompression: bool) : float =
    match format with
    | Png8 ->
        // 4 bytes/pixel raw, PNG's deflate compresses typical shader output
        // (smooth gradients, moderate noise) to roughly half that.
        4.0 * 0.5
    | Png16 ->
        // 8 bytes/pixel raw (16-bit x 4 channels); PNG deflate still helps,
        // but 16-bit channels compress somewhat less than 8-bit ones.
        8.0 * 0.6
    | Tiff16 ->
        if tiffUseLzwCompression then
            8.0 * 0.65
        else
            8.0 // Uncompressed TIFF: no compression factor at all.
    | Exr16 ->
        // Half-float EXR with its own (usually zip/piz) internal
        // compression; 8 bytes/pixel raw, EXR's compression is typically
        // efficient on smooth gradients.
        8.0 * 0.55

/// Estimates an image-sequence export's total size in bytes: per-frame
/// bytes-per-pixel (see `bytesPerPixelForFormat`) times resolution times
/// frame count. Purely informational, meant to populate the same "~X MB"
/// hint as the video/animated-image estimators — not an exact prediction,
/// since real PNG/TIFF/EXR compression ratios vary widely with actual pixel
/// content.
let estimateImageSequenceOutputBytes
    (resolution: Resolution)
    (format: ImageSequenceFormat)
    (tiffUseLzwCompression: bool)
    (frameCount: int)
    : float =
    if resolution.Width <= 0 || resolution.Height <= 0 || frameCount <= 0 then
        0.0
    else
        let pixelsPerFrame = float (resolution.Width * resolution.Height)
        let bytesPerPixel = bytesPerPixelForFormat format tiffUseLzwCompression
        bytesPerPixel * pixelsPerFrame * float frameCount

/// Same "~X MB"/"~X GB" formatting as the other estimators, duplicated here
/// rather than cross-referenced so this module stays independent.
let formatEstimatedFileSize (bytes: float) : string =
    if bytes <= 0.0 then
        "-"
    else
        let megabytes = bytes / (1024.0 * 1024.0)

        if megabytes >= 1024.0 then
            sprintf "~%.1f GB" (megabytes / 1024.0)
        else
            sprintf "~%.1f MB" megabytes
