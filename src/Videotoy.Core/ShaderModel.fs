module Videotoy.Core.ShaderModel

type ChannelInputType =
    | Texture
    | Buffer
    | Video
    | Cubemap
    | Music
    | MusicStream

type ChannelSource =
    { InputType: ChannelInputType
      AssetPath: string option
      BufferName: string option }

type ShaderPass =
    { Name: string
      SourceCode: string
      Channel0: ChannelSource option
      Channel1: ChannelSource option
      Channel2: ChannelSource option
      Channel3: ChannelSource option }

type ShaderProject =
    { Title: string
      CommonCode: string option
      ImagePass: ShaderPass
      BufferA: ShaderPass option
      BufferB: ShaderPass option
      BufferC: ShaderPass option
      BufferD: ShaderPass option
      SourceFilePath: string }

type IssueSeverity =
    | Warning
    | Error

type ShaderIssue =
    { PassName: string
      Line: int
      Message: string
      Severity: IssueSeverity }

    member this.IsErrorIssue = this.Severity = Error

let errorIssue (passName: string) (line: int) (message: string) : ShaderIssue =
    { PassName = passName
      Line = line
      Message = message
      Severity = Error }

let warningIssue (passName: string) (line: int) (message: string) : ShaderIssue =
    { PassName = passName
      Line = line
      Message = message
      Severity = Warning }

let emptyPass (name: string) (sourceCode: string) : ShaderPass =
    { Name = name
      SourceCode = sourceCode
      Channel0 = None
      Channel1 = None
      Channel2 = None
      Channel3 = None }

let fromRawSource (sourceCode: string) (filePath: string) : ShaderProject =
    let title =
        match System.IO.Path.GetFileNameWithoutExtension(filePath) with
        | null -> "untitled"
        | name -> name
    { Title = title
      CommonCode = None
      ImagePass = emptyPass "Image" sourceCode
      BufferA = None
      BufferB = None
      BufferC = None
      BufferD = None
      SourceFilePath = filePath }

let allPasses (project: ShaderProject) : ShaderPass list =
    [ Some project.ImagePass; project.BufferA; project.BufferB; project.BufferC; project.BufferD ]
    |> List.choose id

let passChannels (pass: ShaderPass) : ChannelSource list =
    [ pass.Channel0; pass.Channel1; pass.Channel2; pass.Channel3 ]
    |> List.choose id

let channelTexturePath (channel: ChannelSource) : string option =
    match channel.InputType with
    | Texture -> channel.AssetPath
    | _ -> None

let channelAudioPath (channel: ChannelSource) : string option =
    match channel.InputType with
    | Music
    | MusicStream -> channel.AssetPath
    | _ -> None

let channelVideoPath (channel: ChannelSource) : string option =
    match channel.InputType with
    | Video -> channel.AssetPath
    | _ -> None

/// Chemin (tel que déclaré dans le shader, relatif ou absolu) de la première
/// source audio trouvée sur n'importe quel `iChannel` de n'importe quelle
/// passe du projet, ou `None` si le shader n'utilise aucune entrée audio.
/// Utilisé à l'export pour déterminer si la vidéo générée doit inclure une
/// piste audio muxée.
let firstAudioChannelPath (project: ShaderProject) : string option =
    allPasses project
    |> List.collect passChannels
    |> List.tryPick channelAudioPath
