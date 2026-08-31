module Videotoy.Core.ExportProgressEstimator

/// Estime le temps restant (en secondes) d'un export vidéo à partir du temps
/// écoulé et du nombre de frames déjà traitées, en extrapolant linéairement
/// le débit moyen observé (secondes par frame) sur les frames restantes.
/// Retourne un `Nullable` (plutôt qu'une `option` F#) afin de rester
/// directement consommable depuis `Videotoy.Ffmpeg` (C#), conformément au
/// style de frontière déjà utilisé par `ExportSettingsValidator`.
let estimateRemainingSeconds
    (elapsedSeconds: float)
    (framesCompleted: int)
    (totalFrameCount: int)
    : System.Nullable<float> =
    if framesCompleted <= 0 || totalFrameCount <= 0 || elapsedSeconds <= 0.0 then
        System.Nullable()
    else
        let remainingFrames = totalFrameCount - framesCompleted

        if remainingFrames <= 0 then
            System.Nullable(0.0)
        else
            let secondsPerFrame = elapsedSeconds / float framesCompleted
            System.Nullable(secondsPerFrame * float remainingFrames)

/// Fraction de progression (0.0 à 1.0) pour le nombre de frames déjà traitées.
let progressFraction (framesCompleted: int) (totalFrameCount: int) : float =
    if totalFrameCount <= 0 then
        0.0
    else
        System.Math.Clamp(float framesCompleted / float totalFrameCount, 0.0, 1.0)
