module Videotoy.Core.GlslToHlslTranspiler

open System.Text
open System.Text.RegularExpressions
open Videotoy.Core.ShaderModel

type TranspileResult =
    { HlslSource: string
      Diagnostics: ShaderIssue list
      CustomUniforms: Videotoy.Core.CustomUniformParser.CustomUniformDeclaration list }

let private constructorRegex =
    Regex(@"\bvec2\s*\(", RegexOptions.Compiled)

let private typeReplacements : (string * string) list =
    [ @"\bvec2\b", "float2"
      @"\bvec3\b", "float3"
      @"\bvec4\b", "float4"
      @"\bmat2\b", "float2x2"
      @"\bmat3\b", "float3x3"
      @"\bmat4\b", "float4x4"
      @"\bivec2\b", "int2"
      @"\bivec3\b", "int3"
      @"\bivec4\b", "int4"
      @"\buvec2\b", "uint2"
      @"\buvec3\b", "uint3"
      @"\buvec4\b", "uint4"
      @"\bbvec2\b", "bool2"
      @"\bbvec3\b", "bool3"
      @"\bbvec4\b", "bool4" ]

let private vectorConstructorHeadRegex =
    Regex(@"\b(float|int|uint|bool)([234])\s*\(", RegexOptions.Compiled)

/// GLSL autorise `vec3(0.0)` pour diffuser un scalaire sur toutes les
/// composantes ; HLSL n'accepte ce raccourci pour aucun constructeur
/// vectoriel et lève `X3014: incorrect number of arguments`. Cette passe
/// repère, après conversion des types (`vec3` -> `float3`, etc.), tout appel
/// `floatN(...)` / `intN(...)` / `uintN(...)` / `boolN(...)` ne contenant
/// qu'un seul argument top-level (les virgules à l'intérieur d'appels ou de
/// constructeurs imbriqués ne comptent pas), et duplique cet argument N fois
/// pour produire un appel HLSL valide. Utilise un scan à parenthèses
/// équilibrées plutôt qu'une regex pure car les arguments peuvent eux-mêmes
/// contenir des appels de fonction avec des virgules (ex. `float3(dot(a, b))`
/// ne doit pas être scindé sur la virgule interne).
let private expandScalarVectorConstructors (source: string) : string =
    let sb = StringBuilder()
    let mutable searchStart = 0
    let mutable keepGoing = true

    while keepGoing do
        let m = vectorConstructorHeadRegex.Match(source, searchStart)
        if not m.Success then
            sb.Append(source.Substring(searchStart)) |> ignore
            keepGoing <- false
        else
            let componentCount = int m.Groups.[2].Value
            let argsStart = m.Index + m.Length
            let mutable depth = 1
            let mutable i = argsStart
            let mutable topLevelCommaCount = 0
            while depth > 0 && i < source.Length do
                match source.[i] with
                | '(' -> depth <- depth + 1
                | ')' -> depth <- depth - 1
                | ',' when depth = 1 -> topLevelCommaCount <- topLevelCommaCount + 1
                | _ -> ()
                if depth > 0 then i <- i + 1

            if i >= source.Length then
                // Parenthèse non fermée : source malformée, on abandonne
                // l'expansion et on recopie le reste tel quel.
                sb.Append(source.Substring(searchStart)) |> ignore
                keepGoing <- false
            else
                let argsEnd = i
                let singleArg = source.Substring(argsStart, argsEnd - argsStart).Trim()

                sb.Append(source.Substring(searchStart, m.Index - searchStart)) |> ignore

                if topLevelCommaCount = 0 && singleArg.Length > 0 then
                    let broadcastArgs = List.replicate componentCount singleArg |> String.concat ", "
                    sb.Append(m.Value).Append(broadcastArgs).Append(")") |> ignore
                else
                    sb.Append(source.Substring(m.Index, argsEnd - m.Index + 1)) |> ignore

                searchStart <- argsEnd + 1

    sb.ToString()

let private compiledTypeReplacements =
    typeReplacements
    |> List.map (fun (pattern, replacement) -> Regex(pattern, RegexOptions.Compiled), replacement)

let private functionReplacements : (string * string) list =
    [ @"\bmix\s*\(", "lerp("
      @"\bfract\s*\(", "frac("
      @"\bmod\s*\(", "fmod("
      @"\batan\s*\(", "atan2("
      @"\btexelFetch\s*\(", "__texelFetch("
      @"\btextureLod\s*\(", "__textureLod("
      @"\binversesqrt\s*\(", "rsqrt(" ]

let private compiledFunctionReplacements =
    functionReplacements
    |> List.map (fun (pattern, replacement) -> Regex(pattern, RegexOptions.Compiled), replacement)

let private textureCallRegex =
    Regex(@"\btexture\s*\(\s*(iChannel[0-3])\s*,", RegexOptions.Compiled)

let private discardRegex =
    Regex(@"\bdiscard\s*;", RegexOptions.Compiled)

let private mainImageSignatureRegex =
    Regex(@"void\s+mainImage\s*\(\s*out\s+float4\s+(\w+)\s*,\s*(?:in\s+)?float2\s+(\w+)\s*\)\s*\{", RegexOptions.Compiled)

let private stripCStyleComments (source: string) : string =
    let noBlockComments = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline)
    Regex.Replace(noBlockComments, @"//[^\n]*", "")

let private stripVersionDirectives (source: string) : string =
    Regex.Replace(source, @"^\s*#version[^\n]*\n?", "", RegexOptions.Multiline)

let private applyTypeReplacements (source: string) : string =
    compiledTypeReplacements
    |> List.fold (fun (acc: string) (regex: Regex, replacement: string) -> regex.Replace(acc, replacement)) source

let private applyFunctionReplacements (source: string) : string =
    compiledFunctionReplacements
    |> List.fold (fun (acc: string) (regex: Regex, replacement: string) -> regex.Replace(acc, replacement)) source

let private applyTextureCalls (source: string) : string =
    textureCallRegex.Replace(source, "$1.Sample($1Sampler,")

let private applyDiscard (source: string) : string =
    discardRegex.Replace(source, "clip(-1);")

let private renameMainImage (source: string) : string * string * string =
    let currentMatch = mainImageSignatureRegex.Match(source)

    if currentMatch.Success then
        let outputVar = currentMatch.Groups.[1].Value
        let coordVar = currentMatch.Groups.[2].Value
        let rewritten =
            mainImageSignatureRegex.Replace(
                source,
                sprintf "float4 PSMain(float4 __svPosition : SV_Position) : SV_Target\n{\n    float4 %s = float4(0, 0, 0, 0);\n    float2 %s = __svPosition.xy;" outputVar coordVar,
                1)
        rewritten, outputVar, coordVar
    else
        source, "fragColor", "fragCoord"

let private appendReturnStatement (source: string) (outputVar: string) : string =
    let trimmedEnd = source.TrimEnd()
    if trimmedEnd.EndsWith("}") then
        let lastBraceIndex = trimmedEnd.LastIndexOf('}')
        let body = trimmedEnd.Substring(0, lastBraceIndex)
        sprintf "%s    return %s;\n}\n" body outputVar
    else
        source

let private shadertoyUniformCBuffer =
    """cbuffer ShadertoyUniforms : register(b0)
{
    float3 iResolution;
    float iTime;
    float iTimeDelta;
    int iFrame;
    float iSampleRate;
    float __padding0;
    float4 iMouse;
    float4 iDate;
    float4 iChannelResolution[4];
};

"""

let private channelDeclarations () : string =
    [ 0 .. 3 ]
    |> List.map (fun index ->
        sprintf
            "Texture2D iChannel%d : register(t%d);\nSamplerState iChannel%dSampler : register(s%d);\n"
            index index index index)
    |> String.concat ""

let private hlslTypeName (uniformType: Videotoy.Core.CustomUniformParser.CustomUniformType) : string =
    match uniformType with
    | Videotoy.Core.CustomUniformParser.Float -> "float"
    | Videotoy.Core.CustomUniformParser.Vec2 -> "float2"
    | Videotoy.Core.CustomUniformParser.Vec3 -> "float3"
    | Videotoy.Core.CustomUniformParser.Vec4 -> "float4"

/// Génère le `cbuffer` HLSL (register b1) déclarant chaque uniform custom
/// détecté par `CustomUniformParser`, dans l'ordre de détection, avec un
/// padding explicite pour respecter l'alignement 16 octets attendu par
/// `CustomUniformsBuffer` côté C#. Vide si le shader n'expose aucun uniform
/// custom : aucun `cbuffer` supplémentaire n'est alors émis.
let private customUniformsCBuffer (declarations: Videotoy.Core.CustomUniformParser.CustomUniformDeclaration list) : string =
    if List.isEmpty declarations then
        ""
    else
        let fields =
            declarations
            |> List.map (fun declaration -> sprintf "    %s %s;" (hlslTypeName declaration.UniformType) declaration.Name)
            |> String.concat "\n"

        sprintf "cbuffer CustomUniforms : register(b1)\n{\n%s\n}\n\n" fields

let transpilePass (commonCode: string option) (pass: ShaderPass) : TranspileResult =
    let diagnostics = ResizeArray<ShaderIssue>()

    let rawSource =
        match commonCode with
        | Some common -> common + "\n" + pass.SourceCode
        | None -> pass.SourceCode

    if not (Regex.IsMatch(rawSource, @"void\s+mainImage\s*\(")) then
        diagnostics.Add(errorIssue pass.Name 1 "Missing 'mainImage' entry point; cannot transpile to HLSL.")

    let preprocessed =
        rawSource
        |> stripCStyleComments
        |> stripVersionDirectives
        |> applyTypeReplacements
        |> expandScalarVectorConstructors
        |> applyFunctionReplacements

    let renamed, outputVar, _coordVar = renameMainImage preprocessed

    let hlslBody =
        renamed
        |> applyTextureCalls
        |> applyDiscard
        |> fun source -> appendReturnStatement source outputVar

    if constructorRegex.IsMatch(pass.SourceCode) |> not && not (rawSource.Contains("mainImage")) then
        diagnostics.Add(warningIssue pass.Name 1 "No 'vec2' constructors detected: pass may be empty or non-standard.")

    let customUniformDeclarations =
        Videotoy.Core.CustomUniformParser.parseDeclarations pass.Name rawSource

    let hlslSource =
        StringBuilder()
            .Append(shadertoyUniformCBuffer)
            .Append(customUniformsCBuffer customUniformDeclarations)
            .Append(channelDeclarations ())
            .Append("\n")
            .Append(hlslBody)
            .ToString()

    { HlslSource = hlslSource
      Diagnostics = diagnostics |> List.ofSeq
      CustomUniforms = customUniformDeclarations }

let transpileProject (project: ShaderProject) : Map<string, TranspileResult> =
    allPasses project
    |> List.map (fun pass -> pass.Name, transpilePass project.CommonCode pass)
    |> Map.ofList

/// Union dédupliquée (par nom) des uniforms custom exposés par l'ensemble des
/// passes déjà transpilées d'un projet, dans un ordre stable de première
/// apparition. Accepte directement les `TranspileResult` (sans transiter par
/// la `Map` F#) pour rester trivialement appelable depuis C#/.NET, où seules
/// les valeurs d'un `IReadOnlyDictionary` sont disponibles. Utilisée pour
/// construire dynamiquement les sliders du panneau de paramètres de rendu
/// sans reparser le GLSL.
let projectCustomUniformsOf (hlslPasses: TranspileResult seq) : Videotoy.Core.CustomUniformParser.CustomUniformDeclaration list =
    hlslPasses
    |> Seq.collect (fun result -> result.CustomUniforms)
    |> Seq.distinctBy (fun declaration -> declaration.Name)
    |> List.ofSeq

let projectCustomUniforms (hlslPasses: Map<string, TranspileResult>) : Videotoy.Core.CustomUniformParser.CustomUniformDeclaration list =
    hlslPasses
    |> Map.toList
    |> List.map snd
    |> projectCustomUniformsOf
