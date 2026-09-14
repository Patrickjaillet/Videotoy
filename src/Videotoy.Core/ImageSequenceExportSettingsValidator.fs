module Videotoy.Core.ImageSequenceExportSettingsValidator

open System.IO
open System.Text.RegularExpressions
open Videotoy.Core.Domain

type ImageSequenceExportSettingsIssue =
    | InvalidResolution
    | InvalidFrameRate
    | InvalidDuration
    | MissingOutputDirectory
    | MissingNamingToken
    | InvalidNamingPattern

/// A printf-style specifier that distinguishes one frame's file name from
/// another — anything from `%d` to `%05d` (width/zero-padding), the only
/// flavor this app ever emits or accepts.
let private printfFrameSpecifier = Regex(@"%0?\d*d", RegexOptions.Compiled)

let private tokenFrameToken = "{index}"

/// True when `pattern` contains a specifier/token that varies per frame —
/// without one, every frame would resolve to the exact same file name and
/// silently overwrite the previous frame.
let patternHasFrameDistinguishingToken (pattern: string) : bool =
    if System.String.IsNullOrEmpty(pattern) then
        false
    else
        printfFrameSpecifier.IsMatch(pattern) || pattern.Contains(tokenFrameToken)

/// Convertit le premier problème de validation en une phrase courte, prête
/// pour l'UI — même convention de frontière que les autres validateurs
/// d'export.
let describeIssue (issue: ImageSequenceExportSettingsIssue) : string =
    match issue with
    | InvalidResolution -> "The export resolution must be greater than zero."
    | InvalidFrameRate -> "The export frame rate must be greater than zero."
    | InvalidDuration -> "The export duration must be greater than zero."
    | MissingOutputDirectory -> "Choose an output folder before exporting."
    | MissingNamingToken -> "The naming pattern must contain a frame index specifier (%05d-style) or the {index} token, otherwise every frame would overwrite the same file."
    | InvalidNamingPattern -> "The naming pattern is empty or invalid."

let describeFirstIssue (issues: ImageSequenceExportSettingsIssue list) : string =
    match issues with
    | issue :: _ -> describeIssue issue
    | [] -> "The image sequence export settings are invalid."

let private namingPatternText (namingMode: ImageSequenceNamingMode) : string =
    match namingMode with
    | Printf pattern -> pattern
    | TokenPattern pattern -> pattern

let validate (settings: ImageSequenceExportSettings) : ImageSequenceExportSettingsIssue list =
    let issues = ResizeArray<ImageSequenceExportSettingsIssue>()

    if settings.Resolution.Width <= 0 || settings.Resolution.Height <= 0 then
        issues.Add(InvalidResolution)

    if settings.FrameRate.Value <= 0.0 then
        issues.Add(InvalidFrameRate)

    let durationSeconds =
        match settings.Duration with
        | Manual s -> s
        | SeamlessLoop (s, _) -> s

    if durationSeconds <= 0.0 then
        issues.Add(InvalidDuration)

    if System.String.IsNullOrWhiteSpace(settings.OutputDirectory) then
        issues.Add(MissingOutputDirectory)

    let pattern = namingPatternText settings.NamingPattern

    if System.String.IsNullOrWhiteSpace(pattern) then
        issues.Add(InvalidNamingPattern)
    elif not (patternHasFrameDistinguishingToken pattern) then
        issues.Add(MissingNamingToken)

    issues |> List.ofSeq

let isValid (settings: ImageSequenceExportSettings) : bool =
    validate settings |> List.isEmpty

/// Durée totale de l'export en secondes telle que **demandée** — même
/// convention que `ExportSettingsValidator.resolveDurationSeconds`.
let resolveDurationSeconds (settings: ImageSequenceExportSettings) : float =
    match settings.Duration with
    | Manual s -> s
    | SeamlessLoop (s, _) -> s

/// Extension de fichier (avec le point) pour le format de séquence donné.
let resolveFileExtension (format: ImageSequenceFormat) : string =
    match format with
    | Png8 -> ".png"
    | Png16 -> ".png"
    | Tiff16 -> ".tiff"
    | Exr16 -> ".exr"

/// Clé stable identifiant le format de séquence, pour la persistance
/// (file de rendu) sans jamais manipuler l'union F# directement côté C#.
let resolveFormatKey (format: ImageSequenceFormat) : string =
    match format with
    | Png8 -> "Png8"
    | Png16 -> "Png16"
    | Tiff16 -> "Tiff16"
    | Exr16 -> "Exr16"

let tryResolveFormatFromKey (key: string) : ImageSequenceFormat =
    match key with
    | "Png16" -> Png16
    | "Tiff16" -> Tiff16
    | "Exr16" -> Exr16
    | _ -> Png8

/// Clé stable identifiant le mode de nommage ("Printf" ou "Token"), pour la
/// persistance — le motif texte lui-même est stocké séparément.
let resolveNamingPatternKey (namingMode: ImageSequenceNamingMode) : string =
    match namingMode with
    | Printf _ -> "Printf"
    | TokenPattern _ -> "Token"

let tryResolveNamingPatternFromKey (key: string) (pattern: string) : ImageSequenceNamingMode =
    match key with
    | "Token" -> TokenPattern pattern
    | _ -> Printf pattern

/// Texte brut du motif de nommage, indépendamment du mode (`Printf` ou
/// `TokenPattern`) — évite au côté C# de déconstruire l'union F# directement,
/// même convention de frontière que le reste de ce module.
let resolveNamingPatternText (namingMode: ImageSequenceNamingMode) : string =
    namingPatternText namingMode

/// Résout le nom de fichier d'une frame donnée à partir du motif de nommage,
/// en substituant soit le spécificateur printf-style (`%05d` → l'index
/// zéro-paddé), soit les jetons `{index}`/`{time}` — jamais les deux à la
/// fois (un seul mode est actif par réglage, voir `ImageSequenceNamingMode`).
/// `extension` est ajoutée seulement si le motif ne se termine pas déjà par
/// elle (même convention que les autres `resolveOutputFilePath`).
let resolveFrameFileName (namingMode: ImageSequenceNamingMode) (index: int) (timeSeconds: float) (extension: string) : string =
    let rawName =
        match namingMode with
        | Printf pattern ->
            let m = printfFrameSpecifier.Match(pattern)
            if not m.Success then
                pattern
            else
                let specifier = m.Value
                // Largeur = chiffres entre "%0" et "d" (ex. "%05d" -> "05").
                let widthDigits = specifier.TrimStart('%').TrimEnd('d').TrimStart('0')
                let width =
                    match System.Int32.TryParse(widthDigits) with
                    | true, w when w > 0 -> w
                    | _ -> 0
                let indexText =
                    if width > 0 then index.ToString().PadLeft(width, '0') else string index
                printfFrameSpecifier.Replace(pattern, indexText, 1)
        | TokenPattern pattern ->
            let timeText = timeSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture).Replace(".", "_")
            pattern
                .Replace("{index}", string index)
                .Replace("{time}", timeText)

    if rawName.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase) then
        rawName
    else
        rawName + extension
