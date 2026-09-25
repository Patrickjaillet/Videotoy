module Videotoy.Core.ShadertoyJsonParser

open System.Text.Json
open Videotoy.Core.ShaderModel

let private parseInputType (typeName: string) : ChannelInputType option =
    match typeName.ToLowerInvariant() with
    | "texture" -> Some Texture
    | "buffer" -> Some Buffer
    | "video" -> Some Video
    | "cubemap" -> Some Cubemap
    | "volume" -> Some Volume
    | "music" -> Some Music
    | "musicstream" -> Some MusicStream
    | _ -> None

/// Types de canal `iChannel` reconnus par Shadertoy mais non supportés par
/// ce pipeline de rendu déterministe — pas par oubli, mais par choix :
/// - `"keyboard"` (texture 256×3 encodant l'état des touches) suppose une
///   interaction utilisateur en temps réel, qui n'a pas de sens pour un
///   export vidéo reproductible.
/// - `"mic"` (entrée microphone en direct) est dans la même situation que
///   `iMouse`/`iDate` : une source non déterministe par nature.
/// `parseInputType` retourne `None` pour ces deux valeurs comme pour tout
/// type vraiment inconnu — cette fonction sert uniquement à distinguer "type
/// non supporté par design, à signaler clairement" de "type véritablement
/// non reconnu, ignoré silencieusement" pour `parseChannel`.
let private unsupportedByDesignInputTypeMessage (typeName: string) : string option =
    match typeName.ToLowerInvariant() with
    | "keyboard" ->
        Some "The 'Keyboard' iChannel input is not supported: this app's rendering pipeline is fully deterministic and cannot depend on real-time key state. Exporting this shader will render as if no key was ever pressed."
    | "mic" ->
        Some "The 'Mic' (microphone) iChannel input is not supported: this app's rendering pipeline is fully deterministic and cannot depend on a live audio input. Use a music file input instead."
    | _ -> None

let private tryGetProperty (element: JsonElement) (name: string) : JsonElement option =
    match element.TryGetProperty(name) with
    | true, value -> Some value
    | false, _ -> None

/// Wrapper nul-safe et type-safe autour de `JsonElement.GetString()` :
/// `GetString()` lève `InvalidOperationException` si la valeur JSON à cet
/// emplacement n'est pas une chaîne (ex. un fichier `.shadertoy` mal formé
/// ou modifié à la main avec `"src": 123`) — un fichier partagé/téléchargé
/// ne doit jamais planter tout le flux "Ouvrir un shader" pour un champ mal
/// typé, `None` est un résultat valide ici. Convertit sinon en `string
/// option` idiomatique F#.
let private tryGetStringValue (element: JsonElement) : string option =
    if element.ValueKind <> JsonValueKind.String then
        None
    else
        match element.GetString() with
        | null -> None
        | value -> Some value

let private tryGetStringOrNumberAsString (element: JsonElement) : string option =
    match element.ValueKind with
    | JsonValueKind.String -> tryGetStringValue element
    | JsonValueKind.Number -> Some(element.GetRawText())
    | _ -> None

/// Résout le nom de passe réel (`"Buffer A"`, etc.) référencé par un
/// `iChannel` de type `Buffer`, en préférant l'`id` opaque
/// (`renderpass[].outputs[].id` ↔ `renderpass[].inputs[].id`, tel qu'un
/// export JSON réel obtenu depuis shadertoy.com les relie — le nom lisible
/// `"Buffer A"` n'apparaît alors que dans `renderpass[].name`, jamais dans le
/// lien `inputs`/`outputs` lui-même) et ne retombant sur le champ `"src"` de
/// l'input que si aucun `id` n'a pu être résolu — cas d'un export plus
/// ancien ou construit/édité à la main où `src` contient déjà directement un
/// nom de buffer exploitable (`"Buffer A"`/`"BufferA"`, normalisé plus loin
/// par `PassGraph.normalizeBufferName`). Le `src` d'un export réel pour un
/// input de type `"buffer"` (ex. `/media/previz/buffer00.png`) n'est qu'un
/// aperçu miniature côté shadertoy.com, jamais la source réelle à charger —
/// le préférer seulement en dernier recours évite de tenter de le résoudre
/// comme un nom de buffer alors qu'un `id` était disponible.
let private resolveBufferReference (outputIdToPassName: Map<string, string>) (inputElement: JsonElement) : string option =
    let byId =
        tryGetProperty inputElement "id"
        |> Option.bind tryGetStringOrNumberAsString
        |> Option.bind (fun id -> Map.tryFind id outputIdToPassName)

    match byId with
    | Some _ -> byId
    | None ->
        tryGetProperty inputElement "src"
        |> Option.bind tryGetStringValue

/// Interprète la valeur (insensible à la casse) de l'attribut
/// `"sampler.filter"` d'un input Shadertoy JSON — `"mipmap"`/`"linear"`/
/// `"nearest"`, ou toute autre valeur/absence retombe sur `LinearFilter`
/// (défaut Shadertoy documenté).
let private parseFilterMode (value: string option) : ChannelFilterMode =
    match value |> Option.map (fun v -> v.ToLowerInvariant()) with
    | Some "nearest" -> NearestFilter
    | Some "mipmap" -> MipmapFilter
    | _ -> LinearFilter

/// Interprète la valeur (insensible à la casse) de l'attribut
/// `"sampler.wrap"` — `"clamp"`/`"repeat"`, ou toute autre valeur/absence
/// retombe sur `RepeatWrap` (défaut Shadertoy documenté).
let private parseWrapMode (value: string option) : ChannelWrapMode =
    match value |> Option.map (fun v -> v.ToLowerInvariant()) with
    | Some "clamp" -> ClampWrap
    | _ -> RepeatWrap

/// `"sampler.vflip"`/`"sampler.srgb"` sont des chaînes (`"true"`/"false"`),
/// pas des booléens JSON, dans un export Shadertoy réel — comparaison
/// textuelle insensible à la casse plutôt que `JsonElement.GetBoolean()`
/// (qui lèverait `InvalidOperationException` sur une valeur `string`).
let private parseFlagString (value: string option) : bool =
    value |> Option.map (fun v -> v.ToLowerInvariant() = "true") |> Option.defaultValue false

/// Lit les réglages d'échantillonnage par canal (`inputs[].sampler.*`) d'un
/// export Shadertoy JSON — absents ou partiellement renseignés, chaque
/// attribut manquant retombe individuellement sur son défaut Shadertoy
/// documenté (voir `defaultSamplerSettings`) plutôt que d'invalider tout le
/// bloc `sampler`.
let private parseSamplerSettings (inputElement: JsonElement) : ChannelSamplerSettings =
    match tryGetProperty inputElement "sampler" with
    | None -> defaultSamplerSettings
    | Some samplerElement ->
        { Filter = tryGetProperty samplerElement "filter" |> Option.bind tryGetStringValue |> parseFilterMode
          Wrap = tryGetProperty samplerElement "wrap" |> Option.bind tryGetStringValue |> parseWrapMode
          VerticalFlip = tryGetProperty samplerElement "vflip" |> Option.bind tryGetStringValue |> parseFlagString
          Srgb = tryGetProperty samplerElement "srgb" |> Option.bind tryGetStringValue |> parseFlagString }

let private parseChannelInput (outputIdToPassName: Map<string, string>) (inputElement: JsonElement) : ChannelSource option =
    let typeName =
        tryGetProperty inputElement "type"
        |> Option.bind tryGetStringValue
        |> Option.defaultValue ""

    match parseInputType typeName with
    | None -> None
    | Some inputType ->
        let sampler = parseSamplerSettings inputElement

        match inputType with
        | Buffer ->
            Some
                { InputType = Buffer
                  AssetPath = None
                  BufferName = resolveBufferReference outputIdToPassName inputElement
                  Sampler = sampler }
        | _ ->
            let source =
                tryGetProperty inputElement "src"
                |> Option.bind tryGetStringValue

            Some
                { InputType = inputType
                  AssetPath = source
                  BufferName = None
                  Sampler = sampler }

let private findChannelInput (passElement: JsonElement) (channelIndex: int) : JsonElement option =
    match tryGetProperty passElement "inputs" with
    | None -> None
    | Some inputsElement when inputsElement.ValueKind <> JsonValueKind.Array -> None
    | Some inputsElement ->
        inputsElement.EnumerateArray()
        |> Seq.tryFind (fun inputElement ->
            match tryGetProperty inputElement "channel" with
            | Some channelElement when channelElement.ValueKind = JsonValueKind.Number ->
                // TryGetInt32 lève encore InvalidOperationException si
                // ValueKind n'est pas Number (ex. "channel": "zero") : le
                // garde-fou porte donc sur ValueKind d'abord, TryGetInt32 ne
                // protégeant que contre un nombre mal formé (décimal,
                // dépassement de capacité), pas contre un type différent.
                match channelElement.TryGetInt32() with
                | true, value -> value = channelIndex
                | false, _ -> false
            | _ -> false)

let private parseChannel (outputIdToPassName: Map<string, string>) (passElement: JsonElement) (channelIndex: int) : ChannelSource option =
    findChannelInput passElement channelIndex
    |> Option.bind (parseChannelInput outputIdToPassName)

/// Détecte, pour un `iChannel` donné, soit un type d'entrée reconnu mais non
/// supporté par design (`"keyboard"`/`"mic"`, voir
/// `unsupportedByDesignInputTypeMessage`), soit un type qui n'est ni l'un des
/// types gérés par `parseInputType` ni l'un des types non supportés par
/// design connus — un futur type Shadertoy jamais rencontré au moment
/// d'écrire ce parseur, ou une valeur malformée/mal orthographiée dans un
/// export tiers — et produit l'avertissement correspondant dans les deux
/// cas. Sans cette détection, `parseChannel` (qui retourne `None` pour
/// n'importe quel type non géré, keyboard/mic compris) ferait disparaître le
/// canal silencieusement plutôt que de signaler clairement au panneau
/// **Shader Issues** qu'un élément du shader importé n'a pas été pris en
/// compte (Phase 6 du ROADMAP).
let private detectUnsupportedChannel (passName: string) (passElement: JsonElement) (channelIndex: int) : ShaderIssue option =
    findChannelInput passElement channelIndex
    |> Option.bind (fun inputElement -> tryGetProperty inputElement "type")
    |> Option.bind tryGetStringValue
    |> Option.bind (fun typeName ->
        match unsupportedByDesignInputTypeMessage typeName with
        | Some message -> Some message
        | None ->
            match parseInputType typeName with
            | Some _ -> None
            | None ->
                Some (
                    sprintf
                        "This iChannel's type ('%s') is not recognized by this app and will be ignored — the channel will read as empty. If this is a valid Shadertoy input type, please report it."
                        typeName))
    |> Option.map (warningIssue passName 1)

let private parsePassCode (passElement: JsonElement) : string =
    tryGetProperty passElement "code"
    |> Option.bind tryGetStringValue
    |> Option.defaultValue ""

let private parsePassType (passElement: JsonElement) : string =
    tryGetProperty passElement "type"
    |> Option.bind tryGetStringValue
    |> Option.defaultValue ""

let private toShaderPass (outputIdToPassName: Map<string, string>) (name: string) (passElement: JsonElement) : ShaderPass =
    { Name = name
      SourceCode = parsePassCode passElement
      Channel0 = parseChannel outputIdToPassName passElement 0
      Channel1 = parseChannel outputIdToPassName passElement 1
      Channel2 = parseChannel outputIdToPassName passElement 2
      Channel3 = parseChannel outputIdToPassName passElement 3 }

/// Construit la table `id → nom de passe` à partir de tous les
/// `renderpass[].outputs[].id` du fichier, avant toute résolution
/// d'`inputs[]` — un export JSON réel relie ses passes exclusivement par cet
/// `id` opaque (voir `resolveBufferReference`), jamais par nom de buffer.
/// `passNameForType` associe le `"type"` de la passe propriétaire de chaque
/// sortie (`"image"`, `"buffer a"`, ...) au nom de passe canonique
/// (`"Image"`, `"Buffer A"`, ...) déjà utilisé ailleurs dans ce module.
let private buildOutputIdToPassNameMap (passes: JsonElement list) (passNameForType: string -> string option) : Map<string, string> =
    passes
    |> List.collect (fun passElement ->
        let passTypeName = (parsePassType passElement).ToLowerInvariant()

        match passNameForType passTypeName with
        | None -> []
        | Some passName ->
            match tryGetProperty passElement "outputs" with
            | Some outputsElement when outputsElement.ValueKind = JsonValueKind.Array ->
                outputsElement.EnumerateArray()
                |> Seq.choose (fun outputElement ->
                    tryGetProperty outputElement "id"
                    |> Option.bind tryGetStringOrNumberAsString
                    |> Option.map (fun id -> id, passName))
                |> List.ofSeq
            | _ -> [])
    |> Map.ofList

let private unsupportedChannelWarnings (passName: string) (passElement: JsonElement) : ShaderIssue list =
    [ 0 .. 3 ]
    |> List.choose (detectUnsupportedChannel passName passElement)

/// Parse un export Shadertoy JSON. Le cas succès renvoie, en plus du
/// `ShaderProject`, la liste des avertissements non bloquants détectés
/// pendant le parsing (aujourd'hui : canaux `"keyboard"`/`"mic"` non
/// supportés par design, voir `unsupportedByDesignInputTypeMessage`) — un
/// canal reconnu-mais-non-supporté n'empêche jamais de charger le reste du
/// shader, contrairement aux erreurs du cas `Result.Error` (JSON malformé,
/// passe `Image` absente, etc.) qui empêchent toute exploitation du fichier.
let parse (jsonText: string) (filePath: string) : Result<ShaderProject * ShaderIssue list, ShaderIssue list> =
    try
        use document = JsonDocument.Parse(jsonText)
        let root = document.RootElement

        let shaderElement =
            match tryGetProperty root "Shader" with
            | Some element -> element
            | None -> root

        let renderpassElement = tryGetProperty shaderElement "renderpass"

        match renderpassElement with
        | None ->
            Result.Error [ errorIssue "Image" 1 "Missing 'renderpass' array in Shadertoy export." ]
        | Some renderpassArray when renderpassArray.ValueKind <> JsonValueKind.Array ->
            Result.Error [ errorIssue "Image" 1 "'renderpass' must be an array." ]
        | Some renderpassArray ->
            let passes = renderpassArray.EnumerateArray() |> List.ofSeq

            let findPass (passTypeName: string) =
                passes
                |> List.tryFind (fun passElement ->
                    (parsePassType passElement).ToLowerInvariant() = passTypeName)

            let commonCode =
                findPass "common"
                |> Option.map parsePassCode

            match findPass "image" with
            | None ->
                Result.Error [ errorIssue "Image" 1 "Missing 'image' render pass in Shadertoy export." ]
            | Some imagePassElement ->
                let bufferPassNames = [ "A", "buffera"; "B", "bufferb"; "C", "bufferc"; "D", "bufferd" ]

                let passNameForType (passTypeName: string) : string option =
                    if passTypeName = "image" then
                        Some "Image"
                    else
                        bufferPassNames
                        |> List.tryFind (fun (_, typeName) -> typeName = passTypeName)
                        |> Option.map (fun (letter, _) -> sprintf "Buffer %s" letter)

                let outputIdToPassName = buildOutputIdToPassNameMap passes passNameForType

                let imagePass = toShaderPass outputIdToPassName "Image" imagePassElement

                let bufferPassElements =
                    bufferPassNames
                    |> List.map (fun (letter, passTypeName) -> letter, findPass passTypeName)

                let bufferPass (bufferLetter: string) =
                    bufferPassElements
                    |> List.tryFind (fun (letter, _) -> letter = bufferLetter)
                    |> Option.bind snd
                    |> Option.map (toShaderPass outputIdToPassName (sprintf "Buffer %s" bufferLetter))

                let info = tryGetProperty shaderElement "info"

                let title =
                    info
                    |> Option.bind (fun infoElement -> tryGetProperty infoElement "name")
                    |> Option.bind tryGetStringValue
                    |> Option.defaultValue (
                        match System.IO.Path.GetFileNameWithoutExtension(filePath) with
                        | null -> "untitled"
                        | name -> name)

                let project =
                    { Title = title
                      CommonCode = commonCode
                      ImagePass = imagePass
                      BufferA = bufferPass "A"
                      BufferB = bufferPass "B"
                      BufferC = bufferPass "C"
                      BufferD = bufferPass "D"
                      SourceFilePath = filePath
                      SourceLanguage = Videotoy.Core.ShaderModel.Glsl }

                let warnings =
                    unsupportedChannelWarnings "Image" imagePassElement
                    @ (bufferPassElements
                       |> List.collect (fun (letter, passElementOption) ->
                           match passElementOption with
                           | Some passElement -> unsupportedChannelWarnings (sprintf "Buffer %s" letter) passElement
                           | None -> []))

                Ok (project, warnings)
    with
    | :? JsonException as ex ->
        Result.Error [ errorIssue "Image" 1 (sprintf "Malformed Shadertoy JSON export: %s" ex.Message) ]
    | :? System.InvalidOperationException as ex ->
        // Filet de sécurité : les accesseurs `JsonElement` ci-dessus sont
        // désormais tous gardés par leur `ValueKind`, mais un champ
        // inattendu dans un export Shadertoy tiers ne doit jamais faire
        // planter tout le flux "Ouvrir un shader" pour autant.
        Result.Error [ errorIssue "Image" 1 (sprintf "Malformed Shadertoy JSON export: %s" ex.Message) ]
