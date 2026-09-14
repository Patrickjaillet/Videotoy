namespace Videotoy.Core.Domain

type Resolution =
    { Width: int
      Height: int }

type FrameRate =
    { Value: float }

/// `SeamlessLoop`'s `excludeEndFrame` : lorsque vrai (comportement par
/// défaut), la frame à `t = loopSeconds` — identique à la frame à `t = 0`
/// du prochain cycle — n'est jamais rendue, pour un raccord de boucle sans
/// image dupliquée en lecture. Lorsque faux, cette frame de fin est incluse
/// (utile pour une inspection visuelle explicite du raccord, ou tout usage
/// volontaire d'une frame de "fermeture" dupliquée).
type DurationMode =
    | Manual of seconds: float
    | SeamlessLoop of loopSeconds: float * excludeEndFrame: bool

type VideoCodec =
    | H264
    | H265
    | Vp9
    | ProRes

type RateControlMode =
    | ConstantRateFactor of crf: int
    | TargetBitrate of kilobitsPerSecond: int

/// Conteneur de sortie : chaque conteneur n'accepte qu'un sous-ensemble de
/// `VideoCodec` (voir `ExportSettingsValidator.isCodecAllowedForContainer`) —
/// `Mp4` accepte H.264/H.265, `Mov` accepte H.264/H.265/ProRes, `WebM`
/// n'accepte que VP9.
type ContainerFormat =
    | Mp4
    | WebM
    | Mov

/// Mode de performance de l'export : `Normal` rend aussi vite que le GPU/CPU
/// le permettent ; `LowSpec` ("petite config") introduit un throttling
/// volontaire entre chaque frame pour ne jamais saturer une machine modeste,
/// au prix d'un export plus lent mais garanti sans frame perdue.
type PerformanceMode =
    | Normal
    | LowSpec of throttleMillisecondsPerFrame: int

/// Vitesse d'encodage x264/x265 (`-preset`) : de `UltraFast` (le plus rapide,
/// le moins compressé) à `VerySlow` (le plus lent, le plus compressé). Sans
/// effet sur les encodeurs matériels (NVENC/QuickSync/AMF), qui ne partagent
/// pas ce vocabulaire de presets.
type EncodingSpeedPreset =
    | UltraFast
    | SuperFast
    | VeryFast
    | Faster
    | Fast
    | Medium
    | Slow
    | Slower
    | VerySlow

type H264Profile =
    | BaselineProfile
    | MainProfile
    | HighProfile

type H265Profile =
    | MainProfile265
    | Main10Profile265

/// Sous-format ProRes : détermine à la fois la qualité/débit (ProRes n'a
/// aucune notion de CRF/bitrate, voir `ExportSettingsValidator.isRateControlLessCodec`)
/// et le format de pixel émis (voir `resolvePixelFormatName`).
type ProResProfile =
    | ProResProfile422
    | ProResProfile422Hq
    | ProResProfile4444

/// Profil d'encodage, dépendant du codec vidéo choisi : un profil H.264 ne
/// peut pas être appliqué à un export H.265 et inversement. `NoProfilePreference`
/// laisse l'encodeur choisir son profil par défaut (aucun flag `-profile:v` émis).
type VideoProfile =
    | H264ProfileSelection of H264Profile
    | H265ProfileSelection of H265Profile
    | ProResProfileSelection of ProResProfile
    | NoProfilePreference

/// Mode d'encodage bitrate cible : `SinglePass` (une seule passe FFmpeg) ou
/// `TwoPass` (deux passes complètes, la première n'écrivant qu'un fichier de
/// statistiques, pour un contrôle de débit plus précis). Sans effet en mode
/// `ConstantRateFactor`, où une seule passe suffit.
type EncodingPassMode =
    | SinglePass
    | TwoPass

/// Préférence d'encodeur matériel : une préférence autre que `SoftwareOnly`
/// est une tentative, jamais une garantie — l'encodeur matériel demandé n'est
/// utilisé que s'il est réellement disponible sur la machine (détecté par
/// `HardwareEncoderProbe`), avec repli transparent et automatique sur
/// l'encodage logiciel x264/x265 dans le cas contraire.
type HardwareEncoderPreference =
    | SoftwareOnly
    | PreferNvenc
    | PreferQuickSync
    | PreferAmf

type AudioCodec =
    | Aac
    | Opus
    | Copy

/// Mode alpha de l'export vidéo : `Opaque` (par défaut, comportement
/// historique) ignore le canal alpha rendu par le shader et émet un flux
/// opaque standard. `Straight` préserve le canal alpha du shader
/// jusqu'au flux encodé — n'a d'effet réel qu'avec un couple codec/conteneur
/// qui le supporte (ProRes 4444 en MOV, VP9 en WebM ; voir
/// `ExportSettingsValidator.isAlphaSupportedByCodec`), sinon rejeté à la
/// validation plutôt que silencieusement ignoré.
type AlphaMode =
    | Opaque
    | Straight

type EncodingOptions =
    { Speed: EncodingSpeedPreset
      Profile: VideoProfile
      GopSize: int option
      PassMode: EncodingPassMode
      HardwareEncoder: HardwareEncoderPreference
      AudioCodec: AudioCodec
      AudioBitrateKbps: int }

type ExportSettings =
    { Resolution: Resolution
      FrameRate: FrameRate
      Duration: DurationMode
      OutputDirectory: string
      OutputFileName: string
      Codec: VideoCodec
      RateControl: RateControlMode
      Container: ContainerFormat
      Performance: PerformanceMode
      Encoding: EncodingOptions
      AlphaMode: AlphaMode }

type AnimatedImageFormat =
    | Gif
    | AnimatedWebP

/// Mode de tramage (`paletteuse`'s `dither`) appliqué lors de la conversion
/// vers la palette indexée d'un export GIF ; sans effet pour WebP animé.
type GifDitherMode =
    | NoDither
    | Bayer
    | FloydSteinberg
    | Sierra2
    | Sierra2_4a

type AnimatedImageEncodingOptions =
    { GifColorCount: int
      GifDither: GifDitherMode
      WebPQuality: int
      WebPLossless: bool }

/// Réglages d'un export image animée (GIF/WebP), volontairement séparés
/// d'<see cref="ExportSettings"/> plutôt que d'y être fondus : ni audio, ni
/// conteneur/codec/profil/encodeur matériel n'ont de sens ici, et la durée
/// est toujours une boucle parfaite (`LoopSeconds`/`ExcludeEndFrame`
/// stockés directement plutôt que via `DurationMode`, pour que l'absence de
/// mode "durée manuelle" soit une garantie de type plutôt qu'une simple
/// contrainte de validation).
type AnimatedImageExportSettings =
    { Resolution: Resolution
      FrameRate: FrameRate
      LoopSeconds: float
      ExcludeEndFrame: bool
      OutputDirectory: string
      OutputFileName: string
      Format: AnimatedImageFormat
      Encoding: AnimatedImageEncodingOptions }

/// Format d'une frame de séquence d'images. `Png8` est un export 8-bit
/// fidèle à la render target D3D11 sous-jacente ; `Png16`/`Tiff16`/`Exr16`
/// sont produits en **étendant** ce même source 8-bit RGBA sur 16 bits
/// (multiplication/réplication de chaque canal, pas de nouvelle information),
/// car le pipeline de rendu est actuellement 8-bit uniquement — une véritable
/// plage dynamique étendue (HDR linéaire réel) arrivera avec le pipeline de
/// rendu flottant de la v2.3.0 (voir ROADMAP.md). Ne pas présenter ces
/// formats 16-bit comme de la vraie HDR tant que ce pipeline n'existe pas.
type ImageSequenceFormat =
    | Png8
    | Png16
    | Tiff16
    | Exr16

/// Convention de nommage des fichiers de frame d'une séquence d'images :
/// `Printf` attend un motif de style `frame_%05d.png` (un seul spécificateur
/// `%d`-like, validé par `ImageSequenceExportSettingsValidator`) ; `TokenPattern`
/// attend un motif avec les jetons `{index}`/`{time}`, ex.
/// `shot_{index}_{time}.png`. Les deux exigent un jeton distinguant chaque
/// frame (sinon toutes les frames écraseraient le même fichier), rejeté sinon
/// à la validation.
type ImageSequenceNamingMode =
    | Printf of pattern: string
    | TokenPattern of pattern: string

/// Réglages d'un export séquence d'images, volontairement séparés
/// d'<see cref="ExportSettings"/> (même logique que <see cref="AnimatedImageExportSettings"/>) :
/// ni conteneur, ni codec vidéo, ni profil, ni encodeur matériel, ni piste
/// audio n'ont de sens ici (chaque frame est un fichier image indépendant).
/// Contrairement à l'image animée, une séquence d'images accepte aussi bien
/// une durée manuelle qu'une boucle parfaite (`Duration: DurationMode`) : il
/// n'y a pas de contrainte de bouclage propre au format de sortie.
type ImageSequenceExportSettings =
    { Resolution: Resolution
      FrameRate: FrameRate
      Duration: DurationMode
      OutputDirectory: string
      NamingPattern: ImageSequenceNamingMode
      Format: ImageSequenceFormat
      AlphaMode: AlphaMode
      TiffUseLzwCompression: bool }

type RenderFrame =
    { Index: int
      TimeSeconds: float
      DeltaSeconds: float }

type UniformValues =
    { Resolution: Resolution
      Time: float
      TimeDelta: float
      FrameIndex: int
      MousePosition: float * float
      SampleRate: float
      Date: float * float * float * float
      ChannelResolutions: Resolution[] }
