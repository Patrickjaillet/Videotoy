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

/// Identifiants Shadertoy standards connus pour être vectoriels (jamais
/// déclarés explicitement dans le corps d'une passe, donc invisibles à un
/// scan de déclarations locales) — utilisés en pratique comme argument
/// unique d'un constructeur de conversion (`ivec2(fragCoord)`,
/// `vec3(iResolution)`), jamais comme scalaire à diffuser.
let private knownVectorBuiltinIdentifiers =
    set [ "fragCoord"; "iResolution"; "iMouse"; "iDate"; "iChannelResolution" ]

/// Identifiants déclarés comme scalaire (`float`/`int`/`uint`/`bool`, jamais
/// suivis d'un chiffre de composante) dans la source d'une passe — sert à
/// `isBareIdentifierAlreadyVectorSized` pour distinguer un argument unique
/// scalaire (`vec3(monFloat)`, à diffuser) d'un argument déjà vectoriel de
/// la bonne taille (`ivec2(fragCoord)`, à convertir sans dupliquer).
let private scalarTypedIdentifierRegex =
    Regex(@"\b(float|int|uint|bool)\s+([A-Za-z_]\w*)\s*[=;,)]", RegexOptions.Compiled)

let private findScalarTypedIdentifiers (source: string) : Set<string> =
    scalarTypedIdentifierRegex.Matches(source)
    |> Seq.map (fun m -> m.Groups.[2].Value)
    |> Set.ofSeq

/// Repère un identifiant nu (ex. `fragCoord`, `glow`) comme seul contenu
/// d'un argument de constructeur, et décide s'il doit être traité comme
/// déjà vectoriel (donc jamais dupliqué — GLSL `ivec2(fragCoord)`, syntaxe
/// de conversion identique et valide en HLSL) plutôt que comme un scalaire
/// à diffuser (GLSL `vec3(monFloat)`, DOIT être dupliqué N fois pour HLSL) :
/// sans système de types complet, un identifiant est considéré "déjà
/// vectoriel" seulement s'il est syntaxiquement reconnu comme tel — présent
/// dans `knownVectorBuiltinIdentifiers`, ou absent de
/// `scalarIdentifiers` (déclarations scalaires repérées dans la même passe
/// par `findScalarTypedIdentifiers`). Un identifiant scalaire connu (le cas
/// dominant en pratique, ex. `glow`/`t`/`sum` déclarés `float`) est donc
/// bien diffusé comme avant, tandis qu'un identifiant dont le type ne peut
/// pas être déterminé (paramètre d'une fonction non visible dans cette
/// passe, etc.) reste traité comme potentiellement déjà vectoriel — un choix
/// conservateur : convertir sans dupliquer produit alors une erreur de
/// compilation HLSL explicite si c'était en fait un scalaire, plutôt qu'un
/// résultat numériquement faux si la duplication avait été appliquée à tort
/// à un vecteur.
let private isBareIdentifierAlreadyVectorSized (scalarIdentifiers: Set<string>) (argument: string) : bool =
    Regex.IsMatch(argument, @"^[A-Za-z_]\w*$")
    && (Set.contains argument knownVectorBuiltinIdentifiers || not (Set.contains argument scalarIdentifiers))

/// GLSL autorise `vec3(0.0)` pour diffuser un scalaire sur toutes les
/// composantes ; HLSL n'accepte ce raccourci pour aucun constructeur
/// vectoriel et lève `X3014: incorrect number of arguments`. Cette passe
/// repère, après conversion des types (`vec3` -> `float3`, etc.), tout appel
/// `floatN(...)` / `intN(...)` / `uintN(...)` / `boolN(...)` ne contenant
/// qu'un seul argument top-level (les virgules à l'intérieur d'appels ou de
/// constructeurs imbriqués ne comptent pas) et n'étant pas un identifiant nu
/// déjà vectoriel (voir `isBareIdentifierAlreadyVectorSized` — GLSL utilise
/// aussi la syntaxe à un seul argument pour convertir un vecteur déjà de la
/// bonne taille vers un autre type de composante, ex. `ivec2(fragCoord)`,
/// qui ne doit jamais être dupliqué), et duplique cet argument N fois pour
/// produire un appel HLSL valide. Utilise un scan à parenthèses équilibrées
/// plutôt qu'une regex pure car les arguments peuvent eux-mêmes contenir des
/// appels de fonction avec des virgules (ex. `float3(dot(a, b))` ne doit pas
/// être scindé sur la virgule interne).
/// Traite récursivement le contenu d'un appel `floatN(...)`/`intN(...)`/...
/// déjà repéré : quand l'appel a plusieurs arguments top-level (donc pas de
/// diffusion scalaire possible pour lui-même), chaque argument est quand
/// même réexaminé récursivement — un appel imbriqué comme `float3(glow)`
/// dans `float4(float3(glow) + x, 1.0)` doit être développé même si l'appel
/// englobant, lui, ne l'est pas. Sans cette récursion, la première passe qui
/// repère `float4(...)` comme ayant plusieurs arguments top-level recopiait
/// tout son contenu tel quel — y compris l'appel `float3(glow)` imbriqué —
/// sans jamais lui laisser sa chance d'être développé séparément (régression
/// constatée manuellement : `float3(glow)` avec `glow` scalaire restait à 1
/// argument, erreur de compilation FXC `X3014`).
let rec private expandScalarVectorConstructorsWithScalars (scalarIdentifiers: Set<string>) (source: string) : string =
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
                let rawArgs = source.Substring(argsStart, argsEnd - argsStart)
                let singleArg = rawArgs.Trim()

                sb.Append(source.Substring(searchStart, m.Index - searchStart)) |> ignore

                if topLevelCommaCount = 0 && singleArg.Length > 0 && not (isBareIdentifierAlreadyVectorSized scalarIdentifiers singleArg) then
                    let broadcastArgs = List.replicate componentCount singleArg |> String.concat ", "
                    sb.Append(m.Value).Append(broadcastArgs).Append(")") |> ignore
                else
                    // Aucune diffusion pour cet appel lui-même (plusieurs
                    // arguments top-level, ou argument déjà vectoriel) —
                    // mais ses arguments peuvent contenir un appel imbriqué
                    // à développer, donc on les retraite récursivement au
                    // lieu de les recopier tels quels.
                    let expandedArgs = expandScalarVectorConstructorsWithScalars scalarIdentifiers rawArgs
                    sb.Append(m.Value).Append(expandedArgs).Append(")") |> ignore

                searchStart <- argsEnd + 1

    sb.ToString()

let private expandScalarVectorConstructors (source: string) : string =
    let scalarIdentifiers = findScalarTypedIdentifiers source
    expandScalarVectorConstructorsWithScalars scalarIdentifiers source

let private compiledTypeReplacements =
    typeReplacements
    |> List.map (fun (pattern, replacement) -> Regex(pattern, RegexOptions.Compiled), replacement)

let private functionReplacements : (string * string) list =
    [ @"\bmix\s*\(", "lerp("
      @"\bfract\s*\(", "frac("
      @"\bmod\s*\(", "fmod("
      @"\batan\s*\(", "atan2("
      @"\binversesqrt\s*\(", "rsqrt(" ]

let private compiledFunctionReplacements =
    functionReplacements
    |> List.map (fun (pattern, replacement) -> Regex(pattern, RegexOptions.Compiled), replacement)

let private textureCallRegex =
    Regex(@"\btexture\s*\(\s*(iChannel[0-3])\s*,", RegexOptions.Compiled)

/// `texelFetch(iChannelN, ...)`/`textureLod(iChannelN, ...)` sont réécrits
/// vers les fonctions wrapper par canal `__texelFetchN`/`__textureLodN`
/// (voir `HlslBoilerplate.channelHelperFunctionDeclarations`) plutôt que
/// vers un unique nom générique : HLSL n'a pas d'équivalent sous forme de
/// fonction libre prenant la texture en premier argument comme le fait GLSL
/// (`.Load`/`.SampleLevel` sont des méthodes de l'objet `TextureXxx`), donc
/// la texture ciblée doit être encodée dans le nom de la fonction wrapper
/// elle-même. Contrairement à `textureCallRegex`, la texture n'est donc plus
/// un argument après réécriture — seul le reste des arguments (coordonnées,
/// lod) est conservé tel quel.
let private texelFetchCallRegex =
    Regex(@"\btexelFetch\s*\(\s*iChannel([0-3])\s*,", RegexOptions.Compiled)

let private textureLodCallRegex =
    Regex(@"\btextureLod\s*\(\s*iChannel([0-3])\s*,", RegexOptions.Compiled)

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

let private applyTexelFetchAndTextureLodCalls (source: string) : string =
    let withTexelFetch = texelFetchCallRegex.Replace(source, "__texelFetch$1(")
    textureLodCallRegex.Replace(withTexelFetch, "__textureLod$1(")

let private applyDiscard (source: string) : string =
    discardRegex.Replace(source, "clip(-1);")

/// Repère les identifiants déclarés comme `float2x2`/`float3x3`/`float4x4`
/// (variable locale ou paramètre de fonction, après conversion des types
/// GLSL -> HLSL par `applyTypeReplacements`) dans la source.
let private matrixTypedIdentifierRegex =
    Regex(@"\bfloat[234]x[234]\s+([A-Za-z_]\w*)", RegexOptions.Compiled)

let private findMatrixTypedIdentifiers (source: string) : Set<string> =
    matrixTypedIdentifierRegex.Matches(source)
    |> Seq.map (fun m -> m.Groups.[1].Value)
    |> Set.ofSeq

/// GLSL utilise l'opérateur `*` aussi bien pour une multiplication
/// composante-par-composante (vecteur * vecteur) que pour une vraie
/// multiplication matricielle (matrice * vecteur, matrice * matrice) ; HLSL
/// réserve `*` au premier cas uniquement — une matrice HLSL multipliée par
/// `*` produit un résultat composante-par-composante numériquement faux (ou
/// une erreur de type selon les dimensions), la vraie multiplication
/// matricielle nécessitant l'intrinsèque `mul(a, b)`. Cette passe réécrit
/// `<matrice> * <expr>` et `<expr> * <matrice>` en `mul(<matrice>, <expr>)`/
/// `mul(<expr>, <matrice>)` pour tout identifiant préalablement repéré comme
/// étant de type matriciel par `findMatrixTypedIdentifiers` — une heuristique
/// par nom de variable plutôt qu'une vraie analyse de types, qui rate donc
/// le cas plus rare d'une matrice retournée directement par un appel de
/// fonction et multipliée inline (ex. `rotationMatrix(a) * v`), mais couvre
/// le cas dominant en pratique (une matrice nommée, typiquement une rotation
/// 2D/3D construite une fois puis réutilisée). N'agit que sur le côté de
/// l'opérateur `*` directement adjacent à l'identifiant matriciel : l'autre
/// opérande est capturé jusqu'à la fin de l'expression englobante (point-
/// virgule, ou parenthèse/accolade fermante de profondeur inférieure).
let private rewriteMatrixVectorMultiplication (source: string) : string =
    let matrixIdentifiers = findMatrixTypedIdentifiers source
    if Set.isEmpty matrixIdentifiers then
        source
    else
        // <matrice> * <identifiant ou expression entre parenthèses simple>
        let matrixLeftRegex =
            Regex(
                @"\b(" + String.concat "|" matrixIdentifiers + @")\s*\*\s*([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?|\([^()]*\))",
                RegexOptions.Compiled)
        // <identifiant ou expression entre parenthèses simple> * <matrice>
        let matrixRightRegex =
            Regex(
                @"([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?|\([^()]*\))\s*\*\s*\b(" + String.concat "|" matrixIdentifiers + @")\b",
                RegexOptions.Compiled)

        let afterLeft = matrixLeftRegex.Replace(source, "mul($1, $2)")
        matrixRightRegex.Replace(afterLeft, "mul($1, $2)")

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
        |> rewriteMatrixVectorMultiplication
        |> expandScalarVectorConstructors
        |> applyFunctionReplacements
        |> initializeUnassignedLocals

    let renamed, outputVar, _coordVar = Videotoy.Core.HlslBoilerplate.renameMainImage preprocessed

    let hlslBody =
        renamed
        |> applyTextureCalls
        |> applyTexelFetchAndTextureLodCalls
        |> applyDiscard
        |> fun source -> Videotoy.Core.HlslBoilerplate.appendReturnStatement source outputVar

    if constructorRegex.IsMatch(pass.SourceCode) |> not && not (rawSource.Contains("mainImage")) then
        diagnostics.Add(warningIssue pass.Name 1 "No 'vec2' constructors detected: pass may be empty or non-standard.")

    let customUniformDeclarations =
        Videotoy.Core.CustomUniformParser.parseDeclarations pass.Name rawSource

    let channels = [| pass.Channel0; pass.Channel1; pass.Channel2; pass.Channel3 |]
    let hlslSource = Videotoy.Core.HlslBoilerplate.prependBoilerplate customUniformDeclarations channels hlslBody

    { HlslSource = hlslSource
      EntryPoint = "PSMain"
      Diagnostics = diagnostics |> List.ofSeq
      CustomUniforms = customUniformDeclarations }

let transpileProject (project: ShaderProject) : Map<string, TranspileResult> =
    allPasses project
    |> List.map (fun pass -> pass.Name, transpilePass project.CommonCode pass)
    |> Map.ofList
