module Videotoy.Core.GlslToHlslTranspiler

open System.Text
open System.Text.RegularExpressions
open Videotoy.Core.ShaderModel

open Videotoy.Core.ShaderTranspiler

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

/// GLSL tolère l'usage de variables locales scalaires/vectorielles avant
/// toute affectation dans une déclaration multiple (ex. `float a,c,h,j;`
/// suivi de `u *= a;`) : la plupart des drivers OpenGL/WebGL initialisent
/// implicitement ces slots à zéro, un comportement non garanti par la norme
/// mais massivement exploité par les shaders "minifiés"/code-golf de
/// Shadertoy pour économiser une init explicite. HLSL/FXC refuse ce
/// raccourci (`X4000: variable used without having been completely
/// initialized`) et interrompt la compilation. Cette passe repère chaque
/// déclaration multiple d'un type scalaire ou vectoriel HLSL (float/int/
/// uint/bool, float2..4/int2..4/...) et ajoute `= 0`/`= 0.` (ou
/// `= floatN(0, ...)` pour les types vectoriels) à tout identifiant de la
/// liste qui n'a pas déjà d'initialiseur explicite, reproduisant ainsi le
/// comportement observé sur shadertoy.com. Les déclarations déjà
/// entièrement initialisées, ou celles à un seul identifiant, sont
/// laissées à l'écart du dernier `fold` (aucune modification nécessaire).
let private uninitializedDeclarationRegex =
    Regex(
        @"(?<![.\w])(float|int|uint|bool|float2|float3|float4|int2|int3|int4|uint2|uint3|uint4|bool2|bool3|bool4)\s+([A-Za-z_]\w*(?:\s*(?:=[^,;]+)?\s*,\s*[A-Za-z_]\w*(?:\s*=[^,;]+)?)+)\s*;",
        RegexOptions.Compiled)

let private zeroLiteralFor (typeName: string) : string =
    match typeName with
    | "float" -> "0."
    | "int" -> "0"
    | "uint" -> "0u"
    | "bool" -> "false"
    | t when t.StartsWith("float") -> t + "(" + String.replicate (int (t.Substring(5)) - 1) "0, " + "0)"
    | t when t.StartsWith("int") -> t + "(" + String.replicate (int (t.Substring(3)) - 1) "0, " + "0)"
    | t when t.StartsWith("uint") -> t + "(" + String.replicate (int (t.Substring(4)) - 1) "0u, " + "0u)"
    | t when t.StartsWith("bool") -> t + "(" + String.replicate (int (t.Substring(4)) - 1) "false, " + "false)"
    | _ -> "0."

let private initializeUnassignedLocals (source: string) : string =
    uninitializedDeclarationRegex.Replace(
        source,
        fun m ->
            let typeName = m.Groups.[1].Value
            let identifiersPart = m.Groups.[2].Value
            let zero = zeroLiteralFor typeName

            let rewrittenIdentifiers =
                identifiersPart.Split(',')
                |> Array.map (fun rawIdentifier ->
                    let identifier = rawIdentifier.Trim()
                    if identifier.Contains("=") then
                        identifier
                    else
                        sprintf "%s = %s" identifier zero)
                |> String.concat ", "

            sprintf "%s %s;" typeName rewrittenIdentifiers)

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
        |> initializeUnassignedLocals

    let renamed, outputVar, _coordVar = Videotoy.Core.HlslBoilerplate.renameMainImage preprocessed

    let hlslBody =
        renamed
        |> applyTextureCalls
        |> applyDiscard
        |> fun source -> Videotoy.Core.HlslBoilerplate.appendReturnStatement source outputVar

    if constructorRegex.IsMatch(pass.SourceCode) |> not && not (rawSource.Contains("mainImage")) then
        diagnostics.Add(warningIssue pass.Name 1 "No 'vec2' constructors detected: pass may be empty or non-standard.")

    let customUniformDeclarations =
        Videotoy.Core.CustomUniformParser.parseDeclarations pass.Name rawSource

    let hlslSource = Videotoy.Core.HlslBoilerplate.prependBoilerplate customUniformDeclarations hlslBody

    { HlslSource = hlslSource
      EntryPoint = "PSMain"
      Diagnostics = diagnostics |> List.ofSeq
      CustomUniforms = customUniformDeclarations }

let transpileProject (project: ShaderProject) : Map<string, TranspileResult> =
    allPasses project
    |> List.map (fun pass -> pass.Name, transpilePass project.CommonCode pass)
    |> Map.ofList
