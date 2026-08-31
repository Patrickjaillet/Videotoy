module Videotoy.Core.CustomUniformParser

open System.Text.RegularExpressions
open Videotoy.Core.ShaderModel

/// Types scalaires/vectoriels HLSL supportés pour un uniform custom.
/// Chaque composant (x/y/z/w) est piloté par son propre slider dans le
/// panneau de paramètres de rendu.
type CustomUniformType =
    | Float
    | Vec2
    | Vec3
    | Vec4

/// Nombre de composants float portés par un `CustomUniformType`
/// (1 pour Float, jusqu'à 4 pour Vec4).
let componentCount (uniformType: CustomUniformType) : int =
    match uniformType with
    | Float -> 1
    | Vec2 -> 2
    | Vec3 -> 3
    | Vec4 -> 4

/// Déclaration d'un uniform custom telle qu'exposée par le shader, indépendante
/// de toute valeur courante : nom, type, bornes et valeur par défaut par
/// composant, et étiquette d'affichage optionnelle.
type CustomUniformDeclaration =
    { Name: string
      UniformType: CustomUniformType
      DefaultValues: float32[]
      MinValues: float32[]
      MaxValues: float32[]
      Label: string
      PassName: string }

/// Convention de déclaration reconnue dans le code GLSL (Common ou n'importe
/// quelle passe), sous forme de commentaire ligne dédié :
///
///   // uniform: float MySpeed = 1.0 [0.0, 5.0] "Speed"
///   // uniform: vec3 TintColor = (1.0, 0.5, 0.2) [0.0, 1.0]
///
/// - le type (`float`/`vec2`/`vec3`/`vec4`) détermine le nombre de composants ;
/// - la valeur par défaut est un scalaire nu pour `float`, ou un tuple
///   parenthésé `(x, y, ...)` pour les types vectoriels ;
/// - les bornes `[min, max]` s'appliquent à chaque composant et sont
///   optionnelles (par défaut `[-10.0, 10.0]` si omises) ;
/// - l'étiquette entre guillemets est optionnelle et vaut le nom sinon.
///
/// Cette convention n'affecte jamais la compatibilité Shadertoy d'origine :
/// un simple commentaire GLSL est ignoré par tout autre outil.
let private declarationRegex =
    Regex(
        @"^[ \t]*//[ \t]*uniform:[ \t]*(float|vec2|vec3|vec4)[ \t]+([A-Za-z_][A-Za-z0-9_]*)[ \t]*=[ \t]*(\([^)]*\)|[^\[""\r\n]+?)[ \t]*(?:\[[ \t]*([^,\]]+)[ \t]*,[ \t]*([^\]]+)\])?[ \t]*(?:""([^""]*)"")?[ \t]*$",
        RegexOptions.Compiled ||| RegexOptions.Multiline)

let private parseUniformType (token: string) : CustomUniformType option =
    match token with
    | "float" -> Some Float
    | "vec2" -> Some Vec2
    | "vec3" -> Some Vec3
    | "vec4" -> Some Vec4
    | _ -> None

let private tryParseFloat (token: string) : float32 option =
    match System.Single.TryParse(
        token.Trim(),
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture) with
    | true, value -> Some value
    | false, _ -> None

/// Découpe une valeur par défaut brute en composants scalaires : un scalaire
/// nu pour un `float`, ou le contenu d'un tuple parenthésé `(x, y, z, w)`
/// pour les types vectoriels. Renvoie `None` si le nombre de composants
/// obtenus ne correspond pas à `expectedCount`.
let private parseComponents (raw: string) (expectedCount: int) : float32[] option =
    let trimmed = raw.Trim()
    let inner =
        if trimmed.StartsWith("(") && trimmed.EndsWith(")") then
            trimmed.Substring(1, trimmed.Length - 2)
        else
            trimmed

    let tokens =
        inner.Split(',')
        |> Array.map (fun token -> token.Trim())
        |> Array.filter (fun token -> token.Length > 0)

    if tokens.Length <> expectedCount then
        None
    else
        let parsed = tokens |> Array.choose tryParseFloat
        if parsed.Length = expectedCount then Some parsed else None

let private defaultBounds (expectedCount: int) : float32[] * float32[] =
    Array.create expectedCount -10.0f, Array.create expectedCount 10.0f

/// Extrait les déclarations d'uniforms custom d'un seul bloc de code source
/// (Common ou une passe), en associant chaque déclaration au nom de passe
/// fourni pour l'affichage des diagnostics.
let parseDeclarations (passName: string) (sourceCode: string) : CustomUniformDeclaration list =
    declarationRegex.Matches(sourceCode)
    |> Seq.cast<Match>
    |> Seq.choose (fun m ->
        let typeToken = m.Groups.[1].Value
        let name = m.Groups.[2].Value
        let rawDefault = m.Groups.[3].Value
        let rawMin = m.Groups.[4]
        let rawMax = m.Groups.[5]
        let rawLabel = m.Groups.[6]

        match parseUniformType typeToken with
        | None -> None
        | Some uniformType ->
            let expectedCount = componentCount uniformType

            match parseComponents rawDefault expectedCount with
            | None -> None
            | Some defaults ->
                let fallbackMin, fallbackMax = defaultBounds expectedCount

                let mins =
                    if rawMin.Success then
                        match tryParseFloat rawMin.Value with
                        | Some value -> Array.create expectedCount value
                        | None -> fallbackMin
                    else
                        fallbackMin

                let maxs =
                    if rawMax.Success then
                        match tryParseFloat rawMax.Value with
                        | Some value -> Array.create expectedCount value
                        | None -> fallbackMax
                    else
                        fallbackMax

                Some
                    { Name = name
                      UniformType = uniformType
                      DefaultValues = defaults
                      MinValues = mins
                      MaxValues = maxs
                      Label = if rawLabel.Success && rawLabel.Value.Trim().Length > 0 then rawLabel.Value else name
                      PassName = passName })
    |> List.ofSeq

/// Extrait toutes les déclarations d'uniforms custom exposées par un
/// `ShaderProject` complet : le code `Common` (partagé par toutes les
/// passes) et chaque passe individuelle (Image, Buffer A/B/C/D). Les
/// doublons de nom (même uniform déclaré dans le Common et repris tel quel
/// dans une passe) sont dédupliqués en conservant la première occurrence.
let parseProjectDeclarations (project: ShaderProject) : CustomUniformDeclaration list =
    let fromCommon =
        match project.CommonCode with
        | Some common -> parseDeclarations "Common" common
        | None -> []

    let fromPasses =
        allPasses project
        |> List.collect (fun pass -> parseDeclarations pass.Name pass.SourceCode)

    fromCommon @ fromPasses
    |> List.distinctBy (fun declaration -> declaration.Name)
