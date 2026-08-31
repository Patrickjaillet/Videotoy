module Videotoy.Core.ExportSettingsValidator

open System.IO
open Videotoy.Core.Domain

type ExportSettingsIssue =
    | InvalidResolution
    | InvalidFrameRate
    | InvalidDuration
    | MissingOutputDirectory
    | MissingOutputFileName
    | OutOfRangeConstantRateFactor of value: int
    | OutOfRangeTargetBitrate of value: int
    | InvalidThrottleDuration of value: int

let minConstantRateFactor = 0
let maxConstantRateFactor = 51

let minTargetBitrateKbps = 100

/// Convertit le premier problème de validation en une phrase courte,
/// prête pour l'UI. Exposée comme fonction "friendly" pour que le côté
/// C# n'ait jamais besoin de faire un pattern match direct sur les cas
/// de `ExportSettingsIssue` (dont la représentation .NET générée par le
/// compilateur F# n'est pas stable d'une configuration à l'autre).
let describeIssue (issue: ExportSettingsIssue) : string =
    match issue with
    | InvalidResolution -> "The export resolution must be greater than zero."
    | InvalidFrameRate -> "The export frame rate must be greater than zero."
    | InvalidDuration -> "The export duration must be greater than zero."
    | MissingOutputDirectory -> "Choose an output folder before exporting."
    | MissingOutputFileName -> "Enter an output file name before exporting."
    | OutOfRangeConstantRateFactor crf -> sprintf "The quality setting (CRF %d) is out of range." crf
    | OutOfRangeTargetBitrate kbps -> sprintf "The target bitrate (%d kbps) is too low." kbps
    | InvalidThrottleDuration _ -> "The low-spec mode throttle duration is invalid."

/// Même chose que `describeIssue`, mais pour le premier élément d'une
/// liste de problèmes de validation — évite au côté C# de manipuler
/// `FSharpList.Head` et le pattern matching associé.
let describeFirstIssue (issues: ExportSettingsIssue list) : string =
    match issues with
    | issue :: _ -> describeIssue issue
    | [] -> "The export settings are invalid."

let validate (settings: ExportSettings) : ExportSettingsIssue list =
    let issues = ResizeArray<ExportSettingsIssue>()

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

    if System.String.IsNullOrWhiteSpace(settings.OutputFileName) then
        issues.Add(MissingOutputFileName)

    match settings.RateControl with
    | ConstantRateFactor crf when crf < minConstantRateFactor || crf > maxConstantRateFactor ->
        issues.Add(OutOfRangeConstantRateFactor crf)
    | TargetBitrate kbps when kbps < minTargetBitrateKbps ->
        issues.Add(OutOfRangeTargetBitrate kbps)
    | _ -> ()

    match settings.Performance with
    | LowSpec ms when ms < 0 -> issues.Add(InvalidThrottleDuration ms)
    | _ -> ()

    issues |> List.ofSeq

let isValid (settings: ExportSettings) : bool =
    validate settings |> List.isEmpty

/// Durée totale de l'export en secondes telle que **demandée**, quel que
/// soit le mode de durée (`Manual` ou `SeamlessLoop`). Exposée comme
/// fonction "friendly" plutôt que de laisser le côté C# faire un pattern
/// match direct sur le DU `DurationMode`, conformément au style de frontière
/// déjà utilisé ailleurs dans ce module. Ne pas utiliser cette valeur pour
/// aligner une piste audio muxée : la durée **effective** (dérivée du nombre
/// de frames réellement rendu) peut en différer légèrement en mode
/// `SeamlessLoop` — voir `Videotoy.Core.LoopCalculator.effectiveDurationSeconds`.
let resolveDurationSeconds (settings: ExportSettings) : float =
    match settings.Duration with
    | Manual s -> s
    | SeamlessLoop (s, _) -> s

let containerExtension (container: ContainerFormat) : string =
    match container with
    | Mp4 -> ".mp4"

let resolveOutputFilePath (settings: ExportSettings) : string =
    let extension = containerExtension settings.Container
    let fileNameWithExtension =
        if settings.OutputFileName.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase) then
            settings.OutputFileName
        else
            settings.OutputFileName + extension

    Path.Combine(settings.OutputDirectory, fileNameWithExtension)

let resolveCodecName (codec: VideoCodec) : string =
    match codec with
    | H264 -> "libx264"
    | H265 -> "libx265"

let tryResolveConstantRateFactor (rateControl: RateControlMode) : System.Nullable<int> =
    match rateControl with
    | ConstantRateFactor crf -> System.Nullable(crf)
    | TargetBitrate _ -> System.Nullable()

let tryResolveTargetBitrateKbps (rateControl: RateControlMode) : System.Nullable<int> =
    match rateControl with
    | TargetBitrate kbps -> System.Nullable(kbps)
    | ConstantRateFactor _ -> System.Nullable()

/// Délai de throttling (en millisecondes) à observer entre chaque frame
/// rendue en mode "petite config" ; 0 en mode `Normal` (aucun throttling).
let resolveThrottleMilliseconds (performance: PerformanceMode) : int =
    match performance with
    | Normal -> 0
    | LowSpec ms -> max 0 ms
