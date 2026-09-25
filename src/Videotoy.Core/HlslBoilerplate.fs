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
/// `(0, 0, 0, 1)` — alpha opaque par défaut, puisque l'immense majorité des
/// shaders Shadertoy n'écrivent jamais leur composante `.a` et supposent un
/// rendu opaque ; seul un shader qui assigne explicitement `.a` produit une
/// vraie transparence en export alpha (voir `Domain.AlphaMode`). Utilisé
/// identiquement par le transpileur GLSL et le transpileur HLSL natif —
/// seule la source à laquelle la regex est appliquée diffère (GLSL après
/// réécriture de types, HLSL natif directement). Retourne la source
/// inchangée avec des noms de variables par défaut ("fragColor"/"fragCoord")
/// si la signature n'est pas trouvée — l'appelant est responsable de
/// diagnostiquer cette absence en amont.
let renameMainImage (source: string) : string * string * string =
    let currentMatch = mainImageSignatureRegex.Match(source)

    if currentMatch.Success then
        let outputVar = currentMatch.Groups.[1].Value
        let coordVar = currentMatch.Groups.[2].Value
        let rewritten =
            mainImageSignatureRegex.Replace(
                source,
                sprintf "float4 PSMain(float4 __svPosition : SV_Position) : SV_Target\n{\n    float4 %s = float4(0, 0, 0, 1);\n    float2 %s = float2(__svPosition.x, iResolution.y - __svPosition.y);" outputVar coordVar,
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
    float iFrameRate;
    float4 iMouse;
    float4 iDate;
    float4 iChannelResolution[4];
    float4 __iChannelTimePacked[4];
};
static const float iChannelTime[4] = { __iChannelTimePacked[0].x, __iChannelTimePacked[1].x, __iChannelTimePacked[2].x, __iChannelTimePacked[3].x };

"""

/// Type de déclaration HLSL d'un `iChannel`, déterminé par le type d'entrée
/// Shadertoy du canal (`Cubemap`/`Volume` échantillonnent respectivement en
/// `TextureCube`/`Texture3D`, tout le reste — y compris l'absence de canal —
/// en `Texture2D`, valeur par défaut historique de ce module). L'appel de
/// texture (`texture(iChannelN, coord)` → `iChannelN.Sample(...)`) reste
/// syntaxiquement identique quel que soit le type déclaré ici : seule la
/// déclaration change, jamais le site d'appel (voir `applyTextureCalls`).
let private channelHlslTextureType (channel: Videotoy.Core.ShaderModel.ChannelSource option) : string =
    match channel with
    | Some { InputType = Videotoy.Core.ShaderModel.Cubemap } -> "TextureCube"
    | Some { InputType = Videotoy.Core.ShaderModel.Volume } -> "Texture3D"
    | _ -> "Texture2D"

let channelDeclarations (channels: Videotoy.Core.ShaderModel.ChannelSource option[]) : string =
    [ 0 .. 3 ]
    |> List.map (fun index ->
        let textureType = channelHlslTextureType (if index < channels.Length then channels.[index] else None)
        sprintf
            "%s iChannel%d : register(t%d);\nSamplerState iChannel%dSampler : register(s%d);\n"
            textureType index index index index)
    |> String.concat ""

/// Génère, pour un `iChannel` donné, les fonctions `__texelFetchN`/
/// `__textureLodN` que `GlslToHlslTranspiler.applyFunctionReplacements`
/// réécrit `texelFetch(iChannelN, ...)`/`textureLod(iChannelN, ...)` vers
/// (ex. `texelFetch(iChannel0, p, 0)` → `__texelFetch0(p, 0)`) : HLSL n'a pas
/// d'équivalent direct pour ces deux built-ins GLSL sous forme d'appel libre
/// sur l'objet `iChannelN` (`.Load`/`.SampleLevel` sont des méthodes de
/// l'objet texture, pas des fonctions globales prenant la texture en premier
/// argument), donc un wrapper par canal est nécessaire pour préserver la
/// syntaxe d'appel GLSL après réécriture textuelle. `TextureCube` n'a pas de
/// méthode `.Load` (accès par texel entier, sans filtrage — non défini pour
/// une cubemap, adressée uniquement par direction) : `__texelFetchN` y est
/// donc volontairement absent, un shader appelant `texelFetch` sur un canal
/// cubemap obtient une erreur de compilation HLSL claire (identifiant non
/// déclaré) plutôt qu'un comportement silencieusement incorrect.
let private channelHelperFunctions (index: int) (channel: Videotoy.Core.ShaderModel.ChannelSource option) : string =
    let textureType = channelHlslTextureType channel
    let sb = StringBuilder()

    let texelFetchSignature =
        match textureType with
        | "Texture2D" -> Some(sprintf "float4 __texelFetch%d(int2 p, int lod) { return iChannel%d.Load(int3(p, lod)); }\n" index index)
        | "Texture3D" -> Some(sprintf "float4 __texelFetch%d(int3 p, int lod) { return iChannel%d.Load(int4(p, lod)); }\n" index index)
        | _ -> None

    texelFetchSignature |> Option.iter (sb.Append >> ignore)

    let textureLodCoordType = if textureType = "Texture2D" then "float2" else "float3"
    sb.Append(
        sprintf
            "float4 __textureLod%d(%s uv, float lod) { return iChannel%d.SampleLevel(iChannel%dSampler, uv, lod); }\n"
            index textureLodCoordType index index)
    |> ignore

    sb.ToString()

let channelHelperFunctionDeclarations (channels: Videotoy.Core.ShaderModel.ChannelSource option[]) : string =
    [ 0 .. 3 ]
    |> List.map (fun index -> channelHelperFunctions index (if index < channels.Length then channels.[index] else None))
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
    (channels: Videotoy.Core.ShaderModel.ChannelSource option[])
    (hlslBody: string)
    : string =
    StringBuilder()
        .Append(shadertoyUniformCBuffer)
        .Append(customUniformsCBuffer customUniforms)
        .Append(channelDeclarations channels)
        .Append(channelHelperFunctionDeclarations channels)
        .Append("\n")
        .Append(hlslBody)
        .ToString()
