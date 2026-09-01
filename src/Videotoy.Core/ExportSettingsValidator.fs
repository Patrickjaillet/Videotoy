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
    | InvalidGopSize of value: int
    | OutOfRangeAudioBitrate of value: int

let minConstantRateFactor = 0
let maxConstantRateFactor = 51

let minTargetBitrateKbps = 100

let minAudioBitrateKbps = 32
let maxAudioBitrateKbps = 512

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
    | InvalidGopSize gop -> sprintf "The GOP size (%d) must be greater than zero." gop
    | OutOfRangeAudioBitrate kbps -> sprintf "The audio bitrate (%d kbps) is out of range." kbps

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

    match settings.Encoding.GopSize with
    | Some gop when gop <= 0 -> issues.Add(InvalidGopSize gop)
    | _ -> ()

    let audioBitrateKbps = settings.Encoding.AudioBitrateKbps
    if audioBitrateKbps < minAudioBitrateKbps || audioBitrateKbps > maxAudioBitrateKbps then
        issues.Add(OutOfRangeAudioBitrate audioBitrateKbps)

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

/// Nom du preset de vitesse FFmpeg (`-preset`) attendu par les encodeurs
/// logiciels x264/x265 ; sans effet côté encodeur matériel.
let resolveSpeedPresetName (speed: EncodingSpeedPreset) : string =
    match speed with
    | UltraFast -> "ultrafast"
    | SuperFast -> "superfast"
    | VeryFast -> "veryfast"
    | Faster -> "faster"
    | Fast -> "fast"
    | Medium -> "medium"
    | Slow -> "slow"
    | Slower -> "slower"
    | VerySlow -> "veryslow"

let resolveAudioCodecName (codec: AudioCodec) : string =
    match codec with
    | Aac -> "aac"
    | Copy -> "copy"

/// Nom de profil FFmpeg (`-profile:v`) pour le profil demandé, ou chaîne vide
/// lorsque `NoProfilePreference` est sélectionné : une chaîne vide signale à
/// l'appelant (côté C#) de ne pas émettre le flag `-profile:v` du tout,
/// plutôt que de lui faire manipuler l'union F# directement.
let tryResolveVideoProfileName (profile: VideoProfile) : string =
    match profile with
    | H264ProfileSelection BaselineProfile -> "baseline"
    | H264ProfileSelection MainProfile -> "main"
    | H264ProfileSelection HighProfile -> "high"
    | H265ProfileSelection MainProfile265 -> "main"
    | H265ProfileSelection Main10Profile265 -> "main10"
    | NoProfilePreference -> ""

let resolveGopSize (gopSize: int option) : System.Nullable<int> =
    match gopSize with
    | Some gop -> System.Nullable(gop)
    | None -> System.Nullable()

let resolvePassModeIsTwoPass (passMode: EncodingPassMode) : bool =
    match passMode with
    | SinglePass -> false
    | TwoPass -> true

/// Clé stable (indépendante de la représentation .NET compilée de l'union
/// F#) identifiant la préférence d'encodeur matériel, utilisée aussi bien
/// pour construire les arguments FFmpeg que pour la persistance (journal
/// d'export, presets).
let resolveHardwareEncoderPreferenceKey (preference: HardwareEncoderPreference) : string =
    match preference with
    | SoftwareOnly -> "software"
    | PreferNvenc -> "nvenc"
    | PreferQuickSync -> "qsv"
    | PreferAmf -> "amf"
