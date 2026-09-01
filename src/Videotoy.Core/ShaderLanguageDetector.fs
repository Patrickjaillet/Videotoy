module Videotoy.Core.ShaderLanguageDetector

open System.Text.RegularExpressions
open Videotoy.Core.ShaderModel

/// Chemin rapide : reconnaissance par extension de fichier. Retourne `None`
/// pour une extension inconnue/ambiguë (ex. `.txt` glissé sur le viewport),
/// auquel cas `detectFromContent` doit être utilisé en repli.
let detectFromExtension (filePath: string) : ShaderSourceLanguage option =
    let extension =
        match System.IO.Path.GetExtension(filePath) with
        | null -> ""
        | value -> value.ToLowerInvariant()

    match extension with
    | ".wgsl" -> Some Wgsl
    | ".hlsl"
    | ".hlsli"
    | ".fx" -> Some Hlsl
    | ".glsl"
    | ".frag" -> Some Glsl
    | ".json"
    | ".shadertoy" -> Some Glsl
    | _ -> None

/// Marqueurs syntaxiques par langage, pondérés par catégorie distincte
/// plutôt que par nombre brut d'occurrences (un jeton répété ne doit pas
/// dominer le score). Chaque langage est évalué indépendamment pour qu'un
/// fichier ne "gagne" pas accidentellement un langage à cause d'une
/// sous-chaîne coïncidente (ex. `vec2` à l'intérieur d'un nom de variable
/// HLSL comme `float2 vec2Scale`).
let private wgslMarkers =
    [ Regex(@"@group\s*\(", RegexOptions.Compiled)
      Regex(@"@binding\s*\(", RegexOptions.Compiled)
      Regex(@"@vertex\b", RegexOptions.Compiled)
      Regex(@"@fragment\b", RegexOptions.Compiled)
      Regex(@"@compute\b", RegexOptions.Compiled)
      Regex(@"\bfn\s+\w+\s*\([^)]*\)\s*->", RegexOptions.Compiled)
      Regex(@"\bvar\s*<\s*(uniform|storage)\s*>", RegexOptions.Compiled)
      Regex(@"\[\[\s*block\s*\]\]", RegexOptions.Compiled) ]

let private hlslMarkers =
    [ Regex(@"\bcbuffer\b", RegexOptions.Compiled)
      Regex(@"\bSV_Target\b", RegexOptions.IgnoreCase ||| RegexOptions.Compiled)
      Regex(@"\bSV_POSITION\b", RegexOptions.IgnoreCase ||| RegexOptions.Compiled)
      Regex(@"\bTexture2D\b", RegexOptions.Compiled)
      Regex(@"\bRWTexture\w*\b", RegexOptions.Compiled)
      Regex(@":\s*register\s*\(\s*[bts]\d", RegexOptions.Compiled)
      Regex(@"\bnumthreads\s*\(", RegexOptions.Compiled) ]

let private glslMarkers =
    [ Regex(@"#version\b", RegexOptions.Compiled)
      Regex(@"\bgl_FragColor\b", RegexOptions.Compiled)
      Regex(@"\bgl_FragCoord\b", RegexOptions.Compiled)
      Regex(@"\buniform\s+sampler2D\b", RegexOptions.Compiled)
      Regex(@"\bvarying\s+", RegexOptions.Compiled)
      Regex(@"\bprecision\s+(low|medium|high)p\b", RegexOptions.Compiled)
      Regex(@"\bvoid\s+mainImage\s*\(", RegexOptions.Compiled) ]

let private countDistinctMatches (markers: Regex list) (sourceCode: string) : int =
    markers |> List.filter (fun regex -> regex.IsMatch(sourceCode)) |> List.length

/// Analyse de syntaxe pour un fichier d'extension inconnue/ambiguë (ex.
/// `.txt` glissé sur le viewport). Retourne toujours un résultat — en cas
/// d'égalité totale (ou d'absence de marqueur), retombe sur `Glsl` pour
/// préserver le comportement historique (mono-langage) de l'application.
let detectFromContent (sourceCode: string) : ShaderSourceLanguage =
    let wgslScore = countDistinctMatches wgslMarkers sourceCode
    let hlslScore = countDistinctMatches hlslMarkers sourceCode
    let glslScore = countDistinctMatches glslMarkers sourceCode

    if wgslScore > hlslScore && wgslScore > glslScore then Wgsl
    elif hlslScore > wgslScore && hlslScore > glslScore then Hlsl
    else Glsl

/// Détection combinée : chemin rapide par extension, puis analyse de
/// syntaxe du contenu si l'extension est inconnue/ambiguë.
let detect (filePath: string) (sourceCode: string) : ShaderSourceLanguage =
    match detectFromExtension filePath with
    | Some language -> language
    | None -> detectFromContent sourceCode
