module Videotoy.Core.AnimatedImageExportSettingsValidator

open System.IO
open Videotoy.Core.Domain

type AnimatedImageExportSettingsIssue =
    | InvalidResolution
    | InvalidFrameRate
    | InvalidLoopDuration
    | MissingOutputDirectory
    | MissingOutputFileName
    | OutOfRangeGifColorCount of value: int
    | OutOfRangeWebPQuality of value: int

let minGifColorCount = 2
let maxGifColorCount = 256

let minWebPQuality = 0
let maxWebPQuality = 100

let formatExtension (format: AnimatedImageFormat) : string =
    match format with
    | Gif -> ".gif"
    | AnimatedWebP -> ".webp"

/// Convertit le premier problème de validation en une phrase courte, prête
/// pour l'UI. Même convention de frontière que `ExportSettingsValidator` :
/// le côté C# ne fait jamais de pattern match direct sur
/// `AnimatedImageExportSettingsIssue`.
let describeIssue (issue: AnimatedImageExportSettingsIssue) : string =
    match issue with
    | InvalidResolution -> "The export resolution must be greater than zero."
    | InvalidFrameRate -> "The export frame rate must be greater than zero."
    | InvalidLoopDuration -> "The loop duration must be greater than zero."
    | MissingOutputDirectory -> "Choose an output folder before exporting."
    | MissingOutputFileName -> "Enter an output file name before exporting."
    | OutOfRangeGifColorCount count -> sprintf "The GIF color count (%d) must be between %d and %d." count minGifColorCount maxGifColorCount
    | OutOfRangeWebPQuality quality -> sprintf "The WebP quality (%d) must be between %d and %d." quality minWebPQuality maxWebPQuality

let describeFirstIssue (issues: AnimatedImageExportSettingsIssue list) : string =
    match issues with
    | issue :: _ -> describeIssue issue
    | [] -> "The animated image export settings are invalid."

let validate (settings: AnimatedImageExportSettings) : AnimatedImageExportSettingsIssue list =
    let issues = ResizeArray<AnimatedImageExportSettingsIssue>()

    if settings.Resolution.Width <= 0 || settings.Resolution.Height <= 0 then
        issues.Add(InvalidResolution)

    if settings.FrameRate.Value <= 0.0 then
        issues.Add(InvalidFrameRate)

    if settings.LoopSeconds <= 0.0 then
        issues.Add(InvalidLoopDuration)

    if System.String.IsNullOrWhiteSpace(settings.OutputDirectory) then
        issues.Add(MissingOutputDirectory)

    if System.String.IsNullOrWhiteSpace(settings.OutputFileName) then
        issues.Add(MissingOutputFileName)

    match settings.Format with
    | Gif ->
        let colorCount = settings.Encoding.GifColorCount
        if colorCount < minGifColorCount || colorCount > maxGifColorCount then
            issues.Add(OutOfRangeGifColorCount colorCount)
    | AnimatedWebP ->
        if not settings.Encoding.WebPLossless then
            let quality = settings.Encoding.WebPQuality
            if quality < minWebPQuality || quality > maxWebPQuality then
                issues.Add(OutOfRangeWebPQuality quality)

    issues |> List.ofSeq

let isValid (settings: AnimatedImageExportSettings) : bool =
    validate settings |> List.isEmpty

let resolveOutputFilePath (settings: AnimatedImageExportSettings) : string =
    let extension = formatExtension settings.Format
    let fileNameWithExtension =
        if settings.OutputFileName.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase) then
            settings.OutputFileName
        else
            settings.OutputFileName + extension

    Path.Combine(settings.OutputDirectory, fileNameWithExtension)

/// Reconstruit le `DurationMode` attendu par `LoopCalculator` — toujours
/// `SeamlessLoop`, puisque `AnimatedImageExportSettings` n'a aucune notion
/// de durée manuelle : l'export image animée boucle par construction.
let resolveDurationMode (settings: AnimatedImageExportSettings) : DurationMode =
    SeamlessLoop(settings.LoopSeconds, settings.ExcludeEndFrame)

let resolveGifDitherName (dither: GifDitherMode) : string =
    match dither with
    | NoDither -> "none"
    | Bayer -> "bayer"
    | FloydSteinberg -> "floyd_steinberg"
    | Sierra2 -> "sierra2"
    | Sierra2_4a -> "sierra2_4a"

let resolveFormatKey (format: AnimatedImageFormat) : string =
    match format with
    | Gif -> "Gif"
    | AnimatedWebP -> "AnimatedWebP"
