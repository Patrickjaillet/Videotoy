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
    | Volume
    | Music
    | MusicStream

/// Mode de filtrage d'échantillonnage d'un `iChannel`, tel que déclaré par
/// l'attribut `"filter"` d'un input d'export Shadertoy JSON
/// (`"nearest"`/`"linear"`/`"mipmap"`). `Linear` est la valeur par défaut
/// Shadertoy si l'attribut est absent.
type ChannelFilterMode =
    | NearestFilter
    | LinearFilter
    | MipmapFilter

/// Mode d'adressage hors-[0,1] d'un `iChannel`, tel que déclaré par
/// l'attribut `"wrap"` d'un input d'export Shadertoy JSON
/// (`"clamp"`/`"repeat"`). `Repeat` est la valeur par défaut Shadertoy si
/// l'attribut est absent.
type ChannelWrapMode =
    | ClampWrap
    | RepeatWrap

/// Réglages d'échantillonnage par `iChannel`, tels que portés par chaque
/// entrée `inputs[]` d'un export Shadertoy JSON (`"filter"`, `"wrap"`,
/// `"vflip"`, `"srgb"`) — appliqués indépendamment par canal plutôt qu'avec
/// un réglage global fixe pour tous les `iChannel0-3`, sous peine de rendu
/// visuellement différent de l'original shadertoy.com (texture inversée,
/// bandes de répétition inattendues, etc.). `Internal` (profondeur "byte" vs
/// "float") n'est volontairement pas modélisé ici : le pipeline de chargement
/// de texture (`Videotoy.Media.TextureLoader`) ne décode qu'en 8 bits par
/// canal aujourd'hui, changer cela est un chantier à part entière.
type ChannelSamplerSettings =
    { Filter: ChannelFilterMode
      Wrap: ChannelWrapMode
      VerticalFlip: bool
      Srgb: bool }

/// Réglages d'échantillonnage Shadertoy par défaut lorsqu'un input ne
/// déclare aucun attribut `"filter"`/`"wrap"`/`"vflip"`/`"srgb"` explicite —
/// reproduit le comportement observé par défaut sur shadertoy.com.
let defaultSamplerSettings: ChannelSamplerSettings =
    { Filter = LinearFilter
      Wrap = RepeatWrap
      VerticalFlip = false
      Srgb = false }

type ChannelSource =
    { InputType: ChannelInputType
      AssetPath: string option
      BufferName: string option
      Sampler: ChannelSamplerSettings }

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

/// Nom de passe réservé pour le code partagé `Common`, préfixé à chaque
/// autre passe avant transpilation (voir `GlslToHlslTranspiler`). N'est pas
/// un vrai `ShaderPass` (il n'a pas de channels propres) — utilisé
/// uniquement comme identifiant d'onglet côté éditeur intégré (Phase 1 du
/// ROADMAP) pour distinguer "éditer le Common" d'"éditer une passe".
let commonPassName = "Common"

/// Reconstruit `project` avec le code source d'une seule passe (identifiée
/// par son nom, tel que retourné par `allPasses`/`commonPassName`) remplacé
/// par `newSourceCode` — les channels et tout le reste du projet restent
/// inchangés. Utilisé par l'éditeur de code intégré (Phase 1 du ROADMAP)
/// pour appliquer les modifications de l'utilisateur avant recompilation,
/// sans jamais relire le fichier depuis le disque. Ignore silencieusement
/// un nom de passe qui ne correspond à aucune passe existante du projet
/// (l'éditeur ne doit jamais pouvoir en désigner un).
let withPassSourceCode (passName: string) (newSourceCode: string) (project: ShaderProject) : ShaderProject =
    if passName = commonPassName then
        { project with CommonCode = Some newSourceCode }
    else
        let replaceIfMatch (pass: ShaderPass option) =
            match pass with
            | Some p when p.Name = passName -> Some { p with SourceCode = newSourceCode }
            | other -> other

        if project.ImagePass.Name = passName then
            { project with ImagePass = { project.ImagePass with SourceCode = newSourceCode } }
        else
            { project with
                BufferA = replaceIfMatch project.BufferA
                BufferB = replaceIfMatch project.BufferB
                BufferC = replaceIfMatch project.BufferC
                BufferD = replaceIfMatch project.BufferD }

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

/// Chemin de base d'un `iChannel` de type `Cubemap`, tel que déclaré par
/// `"src"` dans l'export Shadertoy JSON — désigne la face +X (face 0) ; les
/// 5 autres faces sont dérivées de ce chemin par `cubemapFacePaths`. `None`
/// pour tout autre type de canal.
let channelCubemapPath (channel: ChannelSource) : string option =
    match channel.InputType with
    | Cubemap -> channel.AssetPath
    | _ -> None

/// Dérive les 6 chemins de face d'une cubemap Shadertoy à partir du chemin
/// de base de la face 0 (`"src"` du canal, ex. `/media/a/xxxxxx.png`) : la
/// convention Shadertoy insère `_1` à `_5` avant l'extension pour les faces
/// 1 à 5 (`xxxxxx.png`, `xxxxxx_1.png`, ..., `xxxxxx_5.png`), la face 0
/// gardant le chemin de base tel quel. Retourne les faces dans l'ordre
/// D3D11 attendu pour `ID3D11Texture2D` `ArraySize=6`/`MiscFlags.TextureCube`
/// (+X, -X, +Y, -Y, +Z, -Z), qui correspond à l'ordre Shadertoy (+X est la
/// face 0/de base).
/// <remarks>
/// Convention documentée mais non vérifiée contre un export shadertoy.com
/// réel utilisant une cubemap (aucun disponible au moment de l'implémentation,
/// Phase 3 du ROADMAP) — à confirmer/ajuster dès qu'un tel export est
/// disponible pour test.
/// </remarks>
let cubemapFacePaths (basePath: string) : string list =
    let extension = System.IO.Path.GetExtension(basePath) |> Option.ofObj |> Option.defaultValue ""
    let withoutExtension =
        if extension = "" then basePath
        else basePath.Substring(0, basePath.Length - extension.Length)

    basePath :: [ for faceIndex in 1 .. 5 -> sprintf "%s_%d%s" withoutExtension faceIndex extension ]

let channelAudioPath (channel: ChannelSource) : string option =
    match channel.InputType with
    | Music
    | MusicStream -> channel.AssetPath
    | _ -> None

let channelVideoPath (channel: ChannelSource) : string option =
    match channel.InputType with
    | Video -> channel.AssetPath
    | _ -> None

/// Chemin de l'image atlas d'un `iChannel` de type `Volume` ("Texture 3D"
/// sur shadertoy.com), tel que déclaré par `"src"` dans l'export Shadertoy
/// JSON. `None` pour tout autre type de canal.
/// <remarks>
/// Convention de décodage documentée mais non vérifiée contre un export
/// shadertoy.com réel utilisant une texture volume (aucun disponible au
/// moment de l'implémentation, Phase 3 du ROADMAP) — à confirmer/ajuster dès
/// qu'un tel export est disponible pour test. L'image atlas est supposée
/// être une bande horizontale de tranches carrées (largeur = hauteur ×
/// nombre de tranches), convention courante pour ce type d'export ; voir
/// <see cref="volumeSliceCountFromAtlasDimensions"/>.
/// </remarks>
let channelVolumePath (channel: ChannelSource) : string option =
    match channel.InputType with
    | Volume -> channel.AssetPath
    | _ -> None

/// Déduit le nombre de tranches (profondeur Z) d'un atlas de texture volume
/// à partir de ses dimensions : la convention suivie ici est une bande
/// horizontale de tranches carrées, donc `atlasWidth / atlasHeight` tranches
/// de côté `atlasHeight`. Retourne 1 si l'atlas est déjà carré (une seule
/// tranche, ou dimensions invalides) plutôt que d'échouer.
let volumeSliceCountFromAtlasDimensions (atlasWidth: int) (atlasHeight: int) : int =
    if atlasHeight <= 0 || atlasWidth < atlasHeight then
        1
    else
        max 1 (atlasWidth / atlasHeight)

/// Chemin (tel que déclaré dans le shader, relatif ou absolu) de la première
/// source audio trouvée sur n'importe quel `iChannel` de n'importe quelle
/// passe du projet, ou `None` si le shader n'utilise aucune entrée audio.
/// Utilisé à l'export pour déterminer si la vidéo générée doit inclure une
/// piste audio muxée.
let firstAudioChannelPath (project: ShaderProject) : string option =
    allPasses project
    |> List.collect passChannels
    |> List.tryPick channelAudioPath
