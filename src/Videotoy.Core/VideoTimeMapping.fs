module Videotoy.Core.VideoTimeMapping

/// Correspondance entre le temps de rendu (`iTime`, dérivé uniquement de
/// l'index de frame — jamais du temps réel, invariant central du projet) et
/// la position de lecture dans une vidéo source utilisée comme `iChannel` :
/// `Linear` fige la lecture au-delà de la durée de la vidéo en restant sur
/// sa dernière seconde décodable (voir `FrozenOnLastFrame` pour figer sur la
/// toute dernière frame précisément), `Looped` boucle indéfiniment sur la
/// durée de la vidéo, `FrozenOnLastFrame` fige explicitement sur la
/// dernière frame dès que le temps de rendu atteint ou dépasse la durée.
type VideoTimeMappingMode =
    | Linear
    | Looped
    | FrozenOnLastFrame

/// Marge sous la durée totale utilisée par `FrozenOnLastFrame` pour que la
/// position de lecture recherchée reste strictement dans la plage
/// décodable de la vidéo (une recherche exactement à la durée totale
/// échouerait ou retomberait sur une frame noire/EOF selon le décodeur).
let private epsilonSeconds = 0.0005

/// Résout la position de lecture (en secondes, dans la vidéo source) à
/// utiliser pour un temps de rendu donné, selon le mode de correspondance
/// choisi. Fonction pure — même entrée, même sortie, à chaque appel — pour
/// rester compatible avec le pipeline de rendu déterministe : le contenu
/// d'un channel vidéo à `iTime` donné ne dépend jamais de l'ordre de rendu
/// ni d'un état de lecture, seulement de `renderTimeSeconds` lui-même.
let resolveVideoPlaybackTimeSeconds
    (mode: VideoTimeMappingMode)
    (videoDurationSeconds: float)
    (renderTimeSeconds: float)
    : float =
    if videoDurationSeconds <= 0.0 then
        0.0
    else
        match mode with
        | Linear -> min renderTimeSeconds videoDurationSeconds
        | Looped -> renderTimeSeconds % videoDurationSeconds
        | FrozenOnLastFrame ->
            if renderTimeSeconds >= videoDurationSeconds then
                videoDurationSeconds - epsilonSeconds
            else
                renderTimeSeconds
