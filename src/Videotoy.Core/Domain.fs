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

type RateControlMode =
    | ConstantRateFactor of crf: int
    | TargetBitrate of kilobitsPerSecond: int

type ContainerFormat =
    | Mp4

/// Mode de performance de l'export : `Normal` rend aussi vite que le GPU/CPU
/// le permettent ; `LowSpec` ("petite config") introduit un throttling
/// volontaire entre chaque frame pour ne jamais saturer une machine modeste,
/// au prix d'un export plus lent mais garanti sans frame perdue.
type PerformanceMode =
    | Normal
    | LowSpec of throttleMillisecondsPerFrame: int

type ExportSettings =
    { Resolution: Resolution
      FrameRate: FrameRate
      Duration: DurationMode
      OutputDirectory: string
      OutputFileName: string
      Codec: VideoCodec
      RateControl: RateControlMode
      Container: ContainerFormat
      Performance: PerformanceMode }

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
