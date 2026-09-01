/// Type de résultat partagé par toutes les implémentations de transpileur
/// (GLSL, HLSL natif, WGSL) ainsi que le contrat qu'elles respectent toutes :
/// une simple convention de signature de fonction F#
/// `(commonCode: string option) -> (pass: ShaderPass) -> TranspileResult`,
/// consommée aussi bien depuis F# que depuis C# (comme
/// `GlslToHlslTranspiler`/`ShaderValidator` le sont déjà) sans qu'une
/// interface .NET explicite soit nécessaire : chaque module de langage
/// expose une fonction `transpilePass` de cette forme. Le dispatch par
/// langage (`ShaderModel.ShaderSourceLanguage`) est effectué par
/// l'appelant (voir `ShaderFileService.Load`), pas par ce module.
module Videotoy.Core.ShaderTranspiler

open Videotoy.Core.ShaderModel

type TranspileResult =
    { HlslSource: string
      EntryPoint: string
      Diagnostics: ShaderIssue list
      CustomUniforms: Videotoy.Core.CustomUniformParser.CustomUniformDeclaration list }

/// Union dédupliquée (par nom) des uniforms custom exposés par l'ensemble
/// des passes déjà transpilées d'un projet, dans un ordre stable de
/// première apparition. Accepte directement les `TranspileResult` (sans
/// transiter par une `Map` F#) pour rester trivialement appelable depuis
/// C#/.NET, où seules les valeurs d'un `IReadOnlyDictionary` sont
/// disponibles. Utilisée pour construire dynamiquement les sliders du
/// panneau de paramètres de rendu sans reparser le shader source.
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
