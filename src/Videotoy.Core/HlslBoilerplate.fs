/// Émission du "plomberie" HLSL partagée par toutes les implémentations de
/// transpileur (GLSL, HLSL natif, WGSL) : le cbuffer `ShadertoyUniforms`
/// (register b0), le cbuffer optionnel `CustomUniforms` (register b1) et les
/// déclarations `Texture2D`/`SamplerState` des `iChannel0-3` (registres
/// t0-3/s0-3). Ce module ne contient aucune logique spécifique à un langage
/// source — il définit uniquement la disposition GPU attendue par
/// `MultiPassRenderer`, identique quel que soit le langage d'origine du
/// shader.
module Videotoy.Core.HlslBoilerplate

open System.Text
open System.Text.RegularExpressions

let private mainImageSignatureRegex =
    Regex(@"void\s+mainImage\s*\(\s*out\s+float4\s+(\w+)\s*,\s*(?:in\s+)?float2\s+(\w+)\s*\)\s*\{", RegexOptions.Compiled)

/// Reconnaît la signature canonique Shadertoy `void mainImage(out float4 X,
/// in float2 Y)` (après conversion des types vers leurs équivalents HLSL,
/// donc appelé une fois `vec4`/`vec2` déjà réécrits en `float4`/`float2`
/// côté appelant) et la remplace par le point d'entrée HLSL `PSMain`
/// attendu par `MultiPassRenderer`, en dérivant `fragCoord` de la position
/// d'écran D3D (avec inversion Y GL/D3D) et en initialisant `fragColor` à
/// zéro. Utilisé identiquement par le transpileur GLSL et le transpileur
/// HLSL natif — seule la source à laquelle la regex est appliquée diffère
/// (GLSL après réécriture de types, HLSL natif directement). Retourne la
/// source inchangée avec des noms de variables par défaut
/// ("fragColor"/"fragCoord") si la signature n'est pas trouvée — l'appelant
/// est responsable de diagnostiquer cette absence en amont.
let renameMainImage (source: string) : string * string * string =
    let currentMatch = mainImageSignatureRegex.Match(source)

    if currentMatch.Success then
        let outputVar = currentMatch.Groups.[1].Value
        let coordVar = currentMatch.Groups.[2].Value
        let rewritten =
            mainImageSignatureRegex.Replace(
                source,
                sprintf "float4 PSMain(float4 __svPosition : SV_Position) : SV_Target\n{\n    float4 %s = float4(0, 0, 0, 0);\n    float2 %s = float2(__svPosition.x, iResolution.y - __svPosition.y);" outputVar coordVar,
                1)
        rewritten, outputVar, coordVar
    else
        source, "fragColor", "fragCoord"

/// Ajoute `return <outputVar>;` juste avant l'accolade fermante finale du
/// corps de la fonction d'entrée renommée par `renameMainImage`. Partagé
/// pour la même raison que `renameMainImage`.
let appendReturnStatement (source: string) (outputVar: string) : string =
    let trimmedEnd = source.TrimEnd()
    if trimmedEnd.EndsWith("}") then
        let lastBraceIndex = trimmedEnd.LastIndexOf('}')
        let body = trimmedEnd.Substring(0, lastBraceIndex)
        sprintf "%s    return %s;\n}\n" body outputVar
    else
        source

let shadertoyUniformCBuffer =
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

let channelDeclarations () : string =
    [ 0 .. 3 ]
    |> List.map (fun index ->
        sprintf
            "Texture2D iChannel%d : register(t%d);\nSamplerState iChannel%dSampler : register(s%d);\n"
            index index index index)
    |> String.concat ""

let hlslTypeName (uniformType: Videotoy.Core.CustomUniformParser.CustomUniformType) : string =
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
let customUniformsCBuffer (declarations: Videotoy.Core.CustomUniformParser.CustomUniformDeclaration list) : string =
    if List.isEmpty declarations then
        ""
    else
        let fields =
            declarations
            |> List.map (fun declaration -> sprintf "    %s %s;" (hlslTypeName declaration.UniformType) declaration.Name)
            |> String.concat "\n"

        sprintf "cbuffer CustomUniforms : register(b1)\n{\n%s\n}\n\n" fields

/// Préfixe un corps HLSL déjà normalisé (fonction d'entrée renommée
/// `PSMain`, etc.) avec l'ensemble de la plomberie GPU partagée : cbuffer
/// Shadertoy, cbuffer des uniforms custom (si non vide) puis déclarations
/// des `iChannel0-3`. Utilisé identiquement par chaque implémentation de
/// transpileur pour ne jamais dupliquer cette disposition.
let prependBoilerplate
    (customUniforms: Videotoy.Core.CustomUniformParser.CustomUniformDeclaration list)
    (hlslBody: string)
    : string =
    StringBuilder()
        .Append(shadertoyUniformCBuffer)
        .Append(customUniformsCBuffer customUniforms)
        .Append(channelDeclarations ())
        .Append("\n")
        .Append(hlslBody)
        .ToString()
