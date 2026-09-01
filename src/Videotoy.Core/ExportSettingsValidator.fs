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
    | CodecNotSupportedByContainer of codec: VideoCodec * container: ContainerFormat
    | OddDimensionForCodec of codec: VideoCodec * width: int * height: int
    | InvalidVideoProfileForCodec of codec: VideoCodec

let minConstantRateFactor = 0
let maxConstantRateFactor = 51

let minTargetBitrateKbps = 100

let minAudioBitrateKbps = 32
let maxAudioBitrateKbps = 512

let containerExtension (container: ContainerFormat) : string =
    match container with
    | Mp4 -> ".mp4"
    | WebM -> ".webm"
    | Mov -> ".mov"

let resolveCodecName (codec: VideoCodec) : string =
    match codec with
    | H264 -> "libx264"
    | H265 -> "libx265"
    | Vp9 -> "libvpx-vp9"
    | ProRes -> "prores_ks"

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
    | CodecNotSupportedByContainer (codec, container) ->
        sprintf "%s is not supported inside a %s container." (resolveCodecName codec) (containerExtension container)
    | OddDimensionForCodec (_, w, h) ->
        sprintf "VP9 requires even width and height (got %dx%d)." w h
    | InvalidVideoProfileForCodec _ ->
        "The selected video profile does not match the selected codec."

/// Même chose que `describeIssue`, mais pour le premier élément d'une
/// liste de problèmes de validation — évite au côté C# de manipuler
/// `FSharpList.Head` et le pattern matching associé.
let describeFirstIssue (issues: ExportSettingsIssue list) : string =
    match issues with
    | issue :: _ -> describeIssue issue
    | [] -> "The export settings are invalid."

/// ProRes n'a aucune notion de CRF/bitrate : sa taille de sortie est
/// entièrement déterminée par le profil choisi, pas par un flag de
/// qualité/débit.
let isRateControlLessCodec (codec: VideoCodec) : bool =
    match codec with
    | ProRes -> true
    | H264 | H265 | Vp9 -> false

/// ProRes est intra-only : aucune notion de taille de GOP ni de passe
/// multiple.
let isIntraOnlyCodec (codec: VideoCodec) : bool =
    match codec with
    | ProRes -> true
    | H264 | H265 | Vp9 -> false

let isCodecAllowedForContainer (container: ContainerFormat) (codec: VideoCodec) : bool =
    match container, codec with
    | Mp4, (H264 | H265) -> true
    | Mov, (H264 | H265 | ProRes) -> true
    | WebM, Vp9 -> true
    | _ -> false

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

    let isRateControlLess = isRateControlLessCodec settings.Codec

    if not isRateControlLess then
        let audioBitrateKbps = settings.Encoding.AudioBitrateKbps
        if audioBitrateKbps < minAudioBitrateKbps || audioBitrateKbps > maxAudioBitrateKbps then
            issues.Add(OutOfRangeAudioBitrate audioBitrateKbps)

    if not (isCodecAllowedForContainer settings.Container settings.Codec) then
        issues.Add(CodecNotSupportedByContainer (settings.Codec, settings.Container))

    if settings.Codec = Vp9 && (settings.Resolution.Width % 2 <> 0 || settings.Resolution.Height % 2 <> 0) then
        issues.Add(OddDimensionForCodec (settings.Codec, settings.Resolution.Width, settings.Resolution.Height))

    let profileMatchesCodec =
        match settings.Codec, settings.Encoding.Profile with
        | _, NoProfilePreference -> true
        | H264, H264ProfileSelection _ -> true
        | H265, H265ProfileSelection _ -> true
        | ProRes, ProResProfileSelection _ -> true
        | _ -> false

    if not profileMatchesCodec then
        issues.Add(InvalidVideoProfileForCodec settings.Codec)

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

let resolveOutputFilePath (settings: ExportSettings) : string =
    let extension = containerExtension settings.Container
    let fileNameWithExtension =
        if settings.OutputFileName.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase) then
            settings.OutputFileName
        else
            settings.OutputFileName + extension

    Path.Combine(settings.OutputDirectory, fileNameWithExtension)

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
    | Opus -> "libopus"
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
    | ProResProfileSelection ProResProfile422 -> "2"
    | ProResProfileSelection ProResProfile422Hq -> "3"
    | ProResProfileSelection ProResProfile4444 -> "4"
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

/// Nom du muxer FFmpeg (`-f`) à forcer explicitement, ou chaîne vide lorsque
/// FFmpeg peut déjà déduire le muxer correct à partir de l'extension du
/// fichier de sortie (cas de Mp4/Mov). Seul WebM a besoin d'un `-f` explicite
/// (son nom de muxer, "webm", diffère de la détection par extension).
let tryResolveMuxerName (container: ContainerFormat) : string =
    match container with
    | WebM -> "webm"
    | Mp4 | Mov -> ""

/// Format de pixel (`-pix_fmt`) : yuv420p pour H.264/H.265/VP9 (format par
/// défaut déjà utilisé par ce pipeline), yuv422p10le pour ProRes 422/422 HQ,
/// yuv444p10le pour ProRes 4444 (opaque — le canal alpha de la lecture BGRA
/// est ignoré dans cette phase).
let resolvePixelFormatName (codec: VideoCodec) (profile: VideoProfile) : string =
    match codec with
    | ProRes ->
        match profile with
        | ProResProfileSelection ProResProfile4444 -> "yuv444p10le"
        | _ -> "yuv422p10le"
    | H264 | H265 | Vp9 -> "yuv420p"

/// True lorsque `container` prend en charge le flag `-movflags +faststart`
/// (muxer MP4/MOV) ; sans objet pour le muxer WebM, différent.
let supportsFaststart (container: ContainerFormat) : bool =
    match container with
    | Mp4 | Mov -> true
    | WebM -> false

/// Clé stable identifiant le codec vidéo, utilisée côté C# pour le filtrage
/// des options d'UI (profil disponible selon le codec sélectionné), sans
/// jamais manipuler l'union F# directement.
let resolveCodecKey (codec: VideoCodec) : string =
    match codec with
    | H264 -> "H264"
    | H265 -> "H265"
    | Vp9 -> "Vp9"
    | ProRes -> "ProRes"
