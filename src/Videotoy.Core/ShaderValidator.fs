module Videotoy.Core.ShaderValidator

open System.Text.RegularExpressions
open Videotoy.Core.ShaderModel

let private mainImageRegex =
    Regex(@"void\s+mainImage\s*\(", RegexOptions.Compiled)

let private versionDirectiveRegex =
    Regex(@"^\s*#version\b", RegexOptions.Compiled)

let private reservedHlslCBufferRegex =
    Regex(@"\bcbuffer\s+ShadertoyUniforms\b", RegexOptions.Compiled)

let private reservedHlslChannelRegex =
    Regex(@"\bTexture2D\s+iChannel[0-3]\b", RegexOptions.Compiled)

let private fragmentEntryPointRegex =
    Regex(@"@fragment\b", RegexOptions.Compiled)

let private glslMarkerInWgslRegex =
    Regex(@"#version\b|\bcbuffer\b", RegexOptions.Compiled)

/// Recherche structurelle, indépendante du langage : toute source
/// GLSL/HLSL/WGSL utilise des accolades/parenthèses équilibrées de façon
/// identique, donc cette vérification est partagée par les trois chemins
/// de validation par langage ci-dessous.
let private findUnbalancedDelimiterLine (sourceLines: string[]) (openChar: char) (closeChar: char) : int option =
    let mutable depth = 0
    let mutable firstNegativeLine = None

    for lineIndex in 0 .. sourceLines.Length - 1 do
        for ch in sourceLines.[lineIndex] do
            if ch = openChar then
                depth <- depth + 1
            elif ch = closeChar then
                depth <- depth - 1
                if depth < 0 && firstNegativeLine.IsNone then
                    firstNegativeLine <- Some(lineIndex + 1)

    if depth > 0 then Some(sourceLines.Length)
    else firstNegativeLine

let private addSharedStructuralIssues (pass: ShaderPass) (sourceLines: string[]) (issues: ResizeArray<ShaderIssue>) : unit =
    match findUnbalancedDelimiterLine sourceLines '{' '}' with
    | Some line -> issues.Add(errorIssue pass.Name line "Unbalanced braces '{' / '}'.")
    | None -> ()

    match findUnbalancedDelimiterLine sourceLines '(' ')' with
    | Some line -> issues.Add(errorIssue pass.Name line "Unbalanced parentheses '(' / ')'.")
    | None -> ()

/// GLSL/Shadertoy : la convention `void mainImage(...)` est requise, et un
/// `#version` égaré (inutile pour une passe Shadertoy) déclenche un simple
/// avertissement.
let validatePassGlsl (pass: ShaderPass) : ShaderIssue list =
    let sourceLines = pass.SourceCode.Replace("\r\n", "\n").Split('\n')
    let issues = ResizeArray<ShaderIssue>()

    if not (mainImageRegex.IsMatch(pass.SourceCode)) then
        issues.Add(errorIssue pass.Name 1 "Missing 'mainImage' entry point.")

    addSharedStructuralIssues pass sourceLines issues

    sourceLines
    |> Array.iteri (fun index line ->
        if versionDirectiveRegex.IsMatch(line) then
            issues.Add(warningIssue pass.Name (index + 1) "Stray '#version' directive: not required for Shadertoy passes."))

    issues |> List.ofSeq

/// HLSL natif : même convention d'entrée `mainImage` (voir
/// `HlslNativeTranspiler`, qui exige et renomme cette même signature, pour
/// que tous les langages partagent le système de binding de canaux à
/// disposition de registres fixe). Signale en plus toute redéclaration par
/// l'utilisateur de `cbuffer ShadertoyUniforms`/`Texture2D iChannelN` — ces
/// déclarations sont injectées automatiquement par le transpileur et
/// entreraient en collision si le shader source les redéfinit lui-même.
let validatePassHlsl (pass: ShaderPass) : ShaderIssue list =
    let sourceLines = pass.SourceCode.Replace("\r\n", "\n").Split('\n')
    let issues = ResizeArray<ShaderIssue>()

    if not (mainImageRegex.IsMatch(pass.SourceCode)) then
        issues.Add(errorIssue pass.Name 1 "Missing 'mainImage' entry point.")

    addSharedStructuralIssues pass sourceLines issues

    if reservedHlslCBufferRegex.IsMatch(pass.SourceCode) then
        issues.Add(errorIssue pass.Name 1 "Reserved declaration 'cbuffer ShadertoyUniforms' is injected automatically; remove it from the source.")

    if reservedHlslChannelRegex.IsMatch(pass.SourceCode) then
        issues.Add(errorIssue pass.Name 1 "Reserved declaration 'Texture2D iChannelN' is injected automatically; remove it from the source.")

    issues |> List.ofSeq

/// WGSL : l'entrée native `@fragment fn <nom>(...) -> @location(0) vec4<f32>`
/// remplace la convention `mainImage` (le source WGSL est traduit tel quel
/// par Tint, pas écrit dans un style "Shadertoy"). Un marqueur GLSL/HLSL
/// trouvé dans un fichier étiqueté WGSL est un signal de sécurité —
/// probablement une détection de langage incorrecte — qui pointe vers le
/// sélecteur manuel de langage plutôt que de faire silencieusement échouer
/// la transpilation.
let validatePassWgsl (pass: ShaderPass) : ShaderIssue list =
    let sourceLines = pass.SourceCode.Replace("\r\n", "\n").Split('\n')
    let issues = ResizeArray<ShaderIssue>()

    if not (fragmentEntryPointRegex.IsMatch(pass.SourceCode)) then
        issues.Add(errorIssue pass.Name 1 "Missing '@fragment' entry point.")

    addSharedStructuralIssues pass sourceLines issues

    if glslMarkerInWgslRegex.IsMatch(pass.SourceCode) then
        issues.Add(warningIssue pass.Name 1 "This may not be WGSL source — check the detected language.")

    issues |> List.ofSeq

let validatePassForLanguage (language: ShaderSourceLanguage) (pass: ShaderPass) : ShaderIssue list =
    match language with
    | Glsl -> validatePassGlsl pass
    | Hlsl -> validatePassHlsl pass
    | Wgsl -> validatePassWgsl pass

let validateProject (project: ShaderProject) : ShaderIssue list =
    allPasses project
    |> List.collect (validatePassForLanguage project.SourceLanguage)
