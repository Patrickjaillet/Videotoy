module Videotoy.Core.LoopPeriodDetector

open System.Text.RegularExpressions
open Videotoy.Core.ShaderModel

/// Une période de boucle native candidate, détectée par heuristique dans le
/// code source d'une passe, avec de quoi expliquer d'où elle vient dans
/// l'UI. Purement indicative : plusieurs candidates peuvent coexister pour
/// un même shader (plusieurs appels périodiques indépendants), auquel cas
/// c'est à l'appelant de choisir laquelle proposer (voir `detectLoopPeriod`,
/// qui retient la plus grande — la boucle englobante la plus probable).
type LoopPeriodCandidate =
    { PeriodSeconds: float
      PassName: string
      SourceExpression: string }

/// Confiance de la détection, purement informative pour l'UI : jamais
/// utilisée pour imposer automatiquement une valeur, seulement pour nuancer
/// le message affiché à l'utilisateur.
type DetectionConfidence =
    | NoPeriodFound
    | SinglePeriodFound
    | MultiplePeriodsFound

type LoopPeriodDetectionResult =
    { Confidence: DetectionConfidence
      /// Candidate retenue comme suggestion par défaut (la période la plus
      /// grande parmi celles détectées, jamais appliquée automatiquement) —
      /// `None` si aucun motif périodique reconnu n'a été trouvé.
      SuggestedCandidate: LoopPeriodCandidate option
      /// Toutes les candidates détectées, y compris celle suggérée, pour un
      /// éventuel affichage détaillé ; conservées dans l'ordre de première
      /// apparition dans le code source.
      AllCandidates: LoopPeriodCandidate list }

/// Reconnaît `sin(iTime * K)`, `cos(K * iTime)`, `mod(iTime, K)`,
/// `fmod(iTime, K)` (espacement libre, ordre des opérandes libre pour
/// `sin`/`cos`), où `K` est un littéral flottant/entier constant. C'est
/// délibérément restreint aux formes les plus simples et les plus fiables :
/// toute expression plus complexe (iTime combiné avec d'autres variables,
/// fonctions imbriquées, uniforms custom, etc.) n'est jamais reconnue plutôt
/// que de risquer une fausse suggestion. Groupes nommés :
///   - `fn`      : nom de la fonction (`sin`/`cos`/`mod`/`fmod`)
///   - `factorA` : constante lorsqu'elle précède `iTime` (`mod`/`fmod` uniquement)
///   - `factorB` : constante lorsqu'elle suit `iTime`
let private periodicCallRegex =
    Regex(
        @"\b(?<fn>sin|cos)\s*\(\s*iTime\s*\*\s*(?<factorB>[0-9]*\.?[0-9]+)\s*\)"
        + @"|\b(?<fn>sin|cos)\s*\(\s*(?<factorA>[0-9]*\.?[0-9]+)\s*\*\s*iTime\s*\)"
        + @"|\b(?<fn>mod|fmod)\s*\(\s*iTime\s*,\s*(?<factorB>[0-9]*\.?[0-9]+)\s*\)",
        RegexOptions.Compiled)

let private tryParseConstant (token: string) : float option =
    match System.Double.TryParse(
        token,
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture) with
    | true, value when value > 0.0 -> Some value
    | _ -> None

/// Période induite par un seul appel reconnu : `sin(iTime * K)` /
/// `cos(iTime * K)` ont pour période `2*pi / K` (K est une pulsation
/// angulaire, pas une période) ; `mod(iTime, K)` / `fmod(iTime, K)` ont pour
/// période `K` directement (le motif redémarre littéralement tous les `K`
/// secondes).
let private impliedPeriodSeconds (fn: string) (factor: float) : float option =
    match fn with
    | "sin"
    | "cos" -> if factor > 0.0 then Some(2.0 * System.Math.PI / factor) else None
    | "mod"
    | "fmod" -> Some factor
    | _ -> None

/// Détecte les candidates de période de boucle native d'une seule passe, en
/// ignorant silencieusement toute correspondance dont la constante ne se
/// parse pas ou dont la période induite serait nulle/négative (motif
/// dégénéré, jamais une base de suggestion valable).
let private detectPassCandidates (pass: ShaderPass) : LoopPeriodCandidate list =
    periodicCallRegex.Matches(pass.SourceCode)
    |> Seq.cast<Match>
    |> Seq.choose (fun m ->
        let fn = m.Groups.["fn"].Value
        let rawFactor =
            if m.Groups.["factorA"].Success then m.Groups.["factorA"].Value
            elif m.Groups.["factorB"].Success then m.Groups.["factorB"].Value
            else ""

        match tryParseConstant rawFactor with
        | None -> None
        | Some factor ->
            match impliedPeriodSeconds fn factor with
            | Some period when period > 0.0 ->
                Some
                    { PeriodSeconds = period
                      PassName = pass.Name
                      SourceExpression = m.Value.Trim() }
            | _ -> None)
    |> List.ofSeq

/// Détection assistée (optionnelle) de la période de boucle native d'un
/// shader : scanne le code `Common` et chaque passe à la recherche des
/// motifs périodiques simples reconnus par `periodicCallRegex`, et propose
/// — sans jamais l'imposer — la plus grande période détectée comme valeur
/// par défaut éditable pour le mode "Boucle parfaite". C'est une pure
/// heuristique textuelle sur des formes syntaxiques précises, non garantie
/// pour les shaders complexes (expressions composées, iTime transformé par
/// une fonction intermédiaire, période réelle dépendant d'un uniform custom,
/// etc.) : dans ces cas, `AllCandidates` reste vide ou incomplète et
/// l'utilisateur reste entièrement libre de saisir sa propre valeur — cette
/// fonction ne fait jamais autre chose que suggérer.
///
/// Retient la plus grande période parmi les candidates comme suggestion par
/// défaut plutôt que la première trouvée ou la plus petite : dans un shader
/// composant plusieurs effets périodiques indépendants (ex. une rotation
/// lente en `mod(iTime, 8.0)` et un scintillement rapide en
/// `sin(iTime * 40.0)`), c'est la période la plus longue qui correspond le
/// plus souvent à la boucle visuelle globale perçue par l'utilisateur — les
/// motifs plus courts se répètent alors un nombre entier de fois à
/// l'intérieur de cette fenêtre plus longue s'ils sont réellement liés
/// (rapport de fréquence rationnel), sans que cela soit vérifié ici.
let detectLoopPeriod (project: ShaderProject) : LoopPeriodDetectionResult =
    let fromCommon =
        match project.CommonCode with
        | Some common -> detectPassCandidates (emptyPass "Common" common)
        | None -> []

    let fromPasses =
        allPasses project
        |> List.collect detectPassCandidates

    let allCandidates = fromCommon @ fromPasses

    let suggested =
        match allCandidates with
        | [] -> None
        | candidates -> candidates |> List.maxBy (fun candidate -> candidate.PeriodSeconds) |> Some

    let confidence =
        match allCandidates with
        | [] -> NoPeriodFound
        | [ _ ] -> SinglePeriodFound
        | _ -> MultiplePeriodsFound

    { Confidence = confidence
      SuggestedCandidate = suggested
      AllCandidates = allCandidates }
