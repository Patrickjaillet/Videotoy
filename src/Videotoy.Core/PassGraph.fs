module Videotoy.Core.PassGraph

open Videotoy.Core.ShaderModel

/// Nom canonique de la passe Image, tel qu'assigné par ShadertoyJsonParser.
[<Literal>]
let ImagePassName = "Image"

/// Normalise un nom de buffer pour comparaison (insensible à la casse et aux
/// espaces), afin de faire correspondre BufferName ("BufferA", "Buffer A", "A", ...)
/// au ShaderPass.Name correspondant ("Buffer A") quelle que soit la variante
/// utilisée par l'export Shadertoy source.
let private normalizeBufferName (name: string) : string =
    name.Replace(" ", "").Replace("_", "").ToUpperInvariant()

let private namedPasses (project: ShaderProject) : (string * ShaderPass) list =
    [ project.BufferA; project.BufferB; project.BufferC; project.BufferD ]
    |> List.choose id
    |> List.map (fun pass -> pass.Name, pass)
    |> fun buffers -> buffers @ [ ImagePassName, project.ImagePass ]

let private resolveBufferPassName (passesByNormalizedName: Map<string, string>) (bufferName: string) : string option =
    Map.tryFind (normalizeBufferName bufferName) passesByNormalizedName

/// Les noms de passe (channel -> nom de buffer référencé) dont dépend une passe,
/// résolus vers le ShaderPass.Name réel du buffer cible.
let private bufferDependencies (passesByNormalizedName: Map<string, string>) (pass: ShaderPass) : string list =
    passChannels pass
    |> List.choose (fun channel ->
        match channel.InputType, channel.BufferName with
        | Buffer, Some name -> resolveBufferPassName passesByNormalizedName name
        | _ -> None)
    |> List.distinct

/// Une passe dépend d'elle-même (feedback loop) lorsqu'un de ses channels
/// pointe vers son propre buffer : cas normal en ping-pong, jamais une erreur de cycle.
let selfReferencingPassNames (project: ShaderProject) : Set<string> =
    let passes = namedPasses project
    let passesByNormalizedName =
        passes |> List.map (fun (name, _) -> normalizeBufferName name, name) |> Map.ofList

    passes
    |> List.filter (fun (name, pass) -> bufferDependencies passesByNormalizedName pass |> List.contains name)
    |> List.map fst
    |> Set.ofList

/// Ordre topologique d'exécution des passes : chaque buffer est rendu avant
/// tout buffer/Image qui le consomme (hors auto-référence, résolue par ping-pong
/// avec le contenu de la frame précédente et non par ré-ordonnancement).
/// Retourne les noms de passe (ShaderPass.Name) dans l'ordre d'exécution ; lève en
/// cas de cycle impliquant strictement plus d'un buffer (dépendance circulaire
/// non résoluble par ping-pong simple).
let executionOrder (project: ShaderProject) : string list =
    let namedPassesList = namedPasses project
    let passes = namedPassesList |> Map.ofList
    let passesByNormalizedName =
        namedPassesList |> List.map (fun (name, _) -> normalizeBufferName name, name) |> Map.ofList

    let dependenciesOf (name: string) : string list =
        match Map.tryFind name passes with
        | None -> []
        | Some pass ->
            bufferDependencies passesByNormalizedName pass
            |> List.filter (fun dep -> dep <> name)

    let visited = System.Collections.Generic.HashSet<string>()
    let visiting = System.Collections.Generic.HashSet<string>()
    let order = ResizeArray<string>()

    let rec visit (name: string) (path: string list) =
        if visited.Contains name then
            ()
        elif visiting.Contains name then
            failwithf
                "Circular buffer dependency detected: %s"
                (String.concat " -> " (List.rev (name :: path)))
        else
            visiting.Add name |> ignore
            for dependency in dependenciesOf name do
                visit dependency (name :: path)
            visiting.Remove name |> ignore
            visited.Add name |> ignore
            order.Add name

    for name, _ in namedPassesList do
        visit name []

    order |> List.ofSeq

/// Pour une passe donnée, associe chaque index de channel (0-3) qui référence
/// un buffer au ShaderPass.Name réel de ce buffer, pour résolution de la
/// texture d'entrée au rendu.
let bufferChannelBindings (project: ShaderProject) (pass: ShaderPass) : (int * string) list =
    let passesByNormalizedName =
        namedPasses project
        |> List.map (fun (name, _) -> normalizeBufferName name, name)
        |> Map.ofList

    [ 0, pass.Channel0
      1, pass.Channel1
      2, pass.Channel2
      3, pass.Channel3 ]
    |> List.choose (fun (index, channel) ->
        match channel with
        | Some { InputType = Buffer; BufferName = Some name } ->
            resolveBufferPassName passesByNormalizedName name
            |> Option.map (fun resolvedName -> index, resolvedName)
        | _ -> None)

/// Pour une passe donnée, associe chaque index de channel qui référence un
/// asset externe (texture image, audio, vidéo — jamais un buffer ni un
/// cubemap, non pris en charge) à sa ChannelSource complète, pour
/// résolution du fichier et du type côté C# via channelTexturePath/
/// channelAudioPath/channelVideoPath.
let assetChannelBindings (pass: ShaderPass) : (int * ChannelSource) list =
    [ 0, pass.Channel0
      1, pass.Channel1
      2, pass.Channel2
      3, pass.Channel3 ]
    |> List.choose (fun (index, channel) ->
        match channel with
        | Some ({ InputType = Texture | Music | MusicStream | Video } as source) -> Some(index, source)
        | _ -> None)
