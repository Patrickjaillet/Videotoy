module Videotoy.Core.LoopCalculator

open Videotoy.Core.Domain

type FrameCountResult =
    { FrameCount: int
      ExactFrameCount: float
      HasRoundingMismatch: bool }

let private isWholeNumber (value: float) (epsilon: float) =
    let rounded = System.Math.Round(value)
    System.Math.Abs(value - rounded) < epsilon

let computeFrameCount (durationMode: DurationMode) (frameRate: FrameRate) : FrameCountResult =
    match durationMode with
    | Manual seconds ->
        let exact = seconds * frameRate.Value
        let rounded = System.Math.Round(exact)

        { FrameCount = int rounded
          ExactFrameCount = exact
          HasRoundingMismatch = not (isWholeNumber exact 0.0005) }
    | SeamlessLoop (seconds, excludeEndFrame) ->
        let exact = seconds * frameRate.Value
        let rounded = System.Math.Round(exact)
        // Frame de fin exclusive (comportement par défaut) : la frame à
        // `t = loopSeconds`, identique à celle à `t = 0` du cycle suivant,
        // n'est jamais rendue — `frameCount` couvre exactement `[0, loopSeconds[`.
        // Frame de fin incluse (`excludeEndFrame = false`) : cette frame
        // supplémentaire est ajoutée volontairement, produisant une image
        // dupliquée au raccord si la vidéo est lue en boucle.
        let frameCount = if excludeEndFrame then int rounded else int rounded + 1

        { FrameCount = frameCount
          ExactFrameCount = exact
          HasRoundingMismatch = not (isWholeNumber exact 0.0005) }

/// Durée réellement couverte par les frames qui seront effectivement rendues
/// et écrites (`frameCount / frameRate`), par opposition à la durée
/// initialement demandée (`DurationMode`'s seconds value). Les deux ne
/// coïncident pas forcément à l'identique en mode `SeamlessLoop` lorsque
/// `loopSeconds * fps` n'est pas un nombre entier (`HasRoundingMismatch`) :
/// le nombre de frames est alors arrondi, ce qui décale très légèrement la
/// durée effective de la vidéo par rapport à la durée demandée. C'est cette
/// durée effective — et non la durée demandée — qui doit servir de référence
/// pour tout ce qui doit rester strictement aligné sur la timeline de rendu
/// (en particulier le muxage audio : la piste doit se terminer exactement
/// avec la dernière frame vidéo, jamais avant ni après, pour un raccord de
/// boucle sans coupure).
let effectiveDurationSeconds (frameCount: FrameCountResult) (frameRate: FrameRate) : float =
    float frameCount.FrameCount / frameRate.Value

let buildFrameTimeline (frameCount: int) (frameRate: FrameRate) : RenderFrame list =
    let delta = 1.0 / frameRate.Value

    [ 0 .. frameCount - 1 ]
    |> List.map (fun index ->
        { Index = index
          TimeSeconds = float index * delta
          DeltaSeconds = delta })

let buildSeamlessLoopTimeline (loopSeconds: float) (excludeEndFrame: bool) (frameRate: FrameRate) : RenderFrame list * FrameCountResult =
    let result = computeFrameCount (SeamlessLoop(loopSeconds, excludeEndFrame)) frameRate
    let timeline = buildFrameTimeline result.FrameCount frameRate
    timeline, result

/// Calcule la durée de boucle "assistée" la plus proche de `loopSeconds` qui
/// correspond à un nombre entier de frames exact à `frameRate` (arrondi au
/// nombre de frames le plus proche de `loopSeconds * frameRate`, jamais à
/// zéro frame). Sert de valeur proposée — jamais imposée automatiquement —
/// lorsque `computeFrameCount` signale un `HasRoundingMismatch` : appliquer
/// cette durée fait disparaître le micro-saut au raccord de boucle, car
/// `suggestedSeconds * frameRate` est alors, par construction, un entier.
let suggestAssistedLoopSeconds (loopSeconds: float) (frameRate: FrameRate) : float =
    if frameRate.Value <= 0.0 then
        loopSeconds
    else
        let exactFrameCount = loopSeconds * frameRate.Value
        let nearestFrameCount = System.Math.Max(1.0, System.Math.Round(exactFrameCount))
        nearestFrameCount / frameRate.Value
