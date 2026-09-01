/// Transpileur pour les shaders déjà écrits en HLSL natif (`.hlsl`/`.hlsli`/
/// `.fx`). Ne réécrit aucune syntaxe (le source utilise déjà `float2`/
/// `float4`/`.Sample(...)` directement) — se contente d'exiger une fonction
/// d'entrée au format Shadertoy `void mainImage(out float4 X, in float2 Y)`
/// (même convention que GLSL/WGSL, pour que tous les langages participent au
/// même système de binding de canaux/buffers à disposition de registres
/// fixe — voir Phase v1.7.0 du roadmap) et de préfixer la plomberie GPU
/// partagée (`HlslBoilerplate`). Un fichier HLSL natif ne doit PAS
/// redéclarer `ShadertoyUniforms`/`iChannel*` lui-même : voir
/// `ShaderValidator.validatePassHlsl` pour la règle qui signale une telle
/// redéclaration comme une erreur avant que la compilation FXC n'échoue de
/// façon opaque.
module Videotoy.Core.HlslNativeTranspiler

open System.Text.RegularExpressions
open Videotoy.Core.ShaderModel
open Videotoy.Core.ShaderTranspiler

let private mainImagePresenceRegex =
    Regex(@"void\s+mainImage\s*\(", RegexOptions.Compiled)

let transpilePass (commonCode: string option) (pass: ShaderPass) : TranspileResult =
    let diagnostics = ResizeArray<ShaderIssue>()

    let rawSource =
        match commonCode with
        | Some common -> common + "\n" + pass.SourceCode
        | None -> pass.SourceCode

    if not (mainImagePresenceRegex.IsMatch(rawSource)) then
        diagnostics.Add(errorIssue pass.Name 1 "Missing 'mainImage' entry point; cannot compile HLSL.")

    let renamed, outputVar, _coordVar = Videotoy.Core.HlslBoilerplate.renameMainImage rawSource

    let hlslBody = Videotoy.Core.HlslBoilerplate.appendReturnStatement renamed outputVar

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
