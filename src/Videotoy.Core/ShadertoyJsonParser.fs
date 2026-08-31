module Videotoy.Core.ShadertoyJsonParser

open System.Text.Json
open Videotoy.Core.ShaderModel

let private parseInputType (typeName: string) : ChannelInputType option =
    match typeName.ToLowerInvariant() with
    | "texture" -> Some Texture
    | "buffer" -> Some Buffer
    | "video" -> Some Video
    | "cubemap" -> Some Cubemap
    | "music" -> Some Music
    | "musicstream" -> Some MusicStream
    | _ -> None

let private tryGetProperty (element: JsonElement) (name: string) : JsonElement option =
    match element.TryGetProperty(name) with
    | true, value -> Some value
    | false, _ -> None

/// Wrapper nul-safe autour de `JsonElement.GetString()`, qui est annoté
/// nullable en .NET 8 même si la valeur JSON est presque toujours une
/// vraie chaîne. Convertit le résultat en `string option` idiomatique F#.
let private tryGetStringValue (element: JsonElement) : string option =
    match element.GetString() with
    | null -> None
    | value -> Some value

let private parseChannelInput (inputElement: JsonElement) : ChannelSource option =
    let typeName =
        tryGetProperty inputElement "type"
        |> Option.bind tryGetStringValue
        |> Option.defaultValue ""

    match parseInputType typeName with
    | None -> None
    | Some inputType ->
        let source =
            tryGetProperty inputElement "src"
            |> Option.bind tryGetStringValue

        match inputType with
        | Buffer ->
            Some
                { InputType = Buffer
                  AssetPath = None
                  BufferName = source }
        | _ ->
            Some
                { InputType = inputType
                  AssetPath = source
                  BufferName = None }

let private parseChannel (passElement: JsonElement) (channelIndex: int) : ChannelSource option =
    match tryGetProperty passElement "inputs" with
    | None -> None
    | Some inputsElement when inputsElement.ValueKind <> JsonValueKind.Array -> None
    | Some inputsElement ->
        inputsElement.EnumerateArray()
        |> Seq.tryFind (fun inputElement ->
            match tryGetProperty inputElement "channel" with
            | Some channelElement -> channelElement.GetInt32() = channelIndex
            | None -> false)
        |> Option.bind parseChannelInput

let private parsePassCode (passElement: JsonElement) : string =
    tryGetProperty passElement "code"
    |> Option.bind tryGetStringValue
    |> Option.defaultValue ""

let private parsePassType (passElement: JsonElement) : string =
    tryGetProperty passElement "type"
    |> Option.bind tryGetStringValue
    |> Option.defaultValue ""

let private toShaderPass (name: string) (passElement: JsonElement) : ShaderPass =
    { Name = name
      SourceCode = parsePassCode passElement
      Channel0 = parseChannel passElement 0
      Channel1 = parseChannel passElement 1
      Channel2 = parseChannel passElement 2
      Channel3 = parseChannel passElement 3 }

let parse (jsonText: string) (filePath: string) : Result<ShaderProject, ShaderIssue list> =
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
                let imagePass = toShaderPass "Image" imagePassElement

                let bufferPass (bufferLetter: string) =
                    findPass (sprintf "buffer%s" (bufferLetter.ToLowerInvariant()))
                    |> Option.map (toShaderPass (sprintf "Buffer %s" bufferLetter))

                let info = tryGetProperty shaderElement "info"

                let title =
                    info
                    |> Option.bind (fun infoElement -> tryGetProperty infoElement "name")
                    |> Option.bind tryGetStringValue
                    |> Option.defaultValue (
                        match System.IO.Path.GetFileNameWithoutExtension(filePath) with
                        | null -> "untitled"
                        | name -> name)

                Ok
                    { Title = title
                      CommonCode = commonCode
                      ImagePass = imagePass
                      BufferA = bufferPass "A"
                      BufferB = bufferPass "B"
                      BufferC = bufferPass "C"
                      BufferD = bufferPass "D"
                      SourceFilePath = filePath }
    with
    | :? JsonException as ex ->
        Result.Error [ errorIssue "Image" 1 (sprintf "Malformed Shadertoy JSON export: %s" ex.Message) ]
