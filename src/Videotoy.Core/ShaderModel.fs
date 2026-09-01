module Videotoy.Core.ShaderModel

/// Langage source d'un projet shader — détecté à l'ouverture du fichier
/// (voir `ShaderLanguageDetector`) ou forcé manuellement par l'utilisateur.
/// Un seul langage par projet : les projets JSON/Shadertoy multi-passes
/// restent toujours Glsl (aucun mélange de langage entre passes).
type ShaderSourceLanguage =
    | Glsl
    | Wgsl
    | Hlsl

/// Fonction-frontière : convertit `ShaderSourceLanguage` en une simple
/// clé `string` ("Glsl"/"Wgsl"/"Hlsl") consommable en toute sécurité par du
/// C# (le filtrage direct d'une union discriminée F# depuis C# est fragile
/// — sa représentation compilée n'est pas un contrat stable — voir
/// CLAUDE.md). Utilisé par `ShaderTranspilerRouter` (Videotoy.App) pour
/// dispatcher vers l'implémentation de transpileur adéquate.
let languageKey (language: ShaderSourceLanguage) : string =
    match language with
    | Glsl -> "Glsl"
    | Wgsl -> "Wgsl"
    | Hlsl -> "Hlsl"

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
      SourceFilePath: string
      SourceLanguage: ShaderSourceLanguage }

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

let fromRawSource (sourceCode: string) (filePath: string) (sourceLanguage: ShaderSourceLanguage) : ShaderProject =
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
      SourceFilePath = filePath
      SourceLanguage = sourceLanguage }

/// Copie `project` avec un langage source différent — utilisé par la
/// substitution manuelle de langage (voir
/// `MainWindowViewModel.ForceShaderLanguageAsync`/`ShaderFileService.ReloadWithLanguageOverride`)
/// pour re-router la validation/transpilation sans recharger le fichier ni
/// les assets, qui ne dépendent pas du langage.
let withSourceLanguage (language: ShaderSourceLanguage) (project: ShaderProject) : ShaderProject =
    { project with SourceLanguage = language }

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
