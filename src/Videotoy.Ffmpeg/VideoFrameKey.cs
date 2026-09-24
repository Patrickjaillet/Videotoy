namespace Videotoy.Ffmpeg;

/// <summary>
/// Clé de cache pour une frame vidéo décodée, basée sur un index de frame
/// résolu (<c>timestampSeconds * probedFrameRate</c>, arrondi) plutôt que
/// sur le timestamp brut : deux requêtes qui retombent sur la même frame
/// source (relecture de la même position pendant le scrubbing de
/// l'aperçu, ou ré-rendu identique de la même timeline lors de la seconde
/// passe d'un export GIF) doivent systématiquement partager la même entrée
/// de cache, pour que le décodage reste une fonction pure de
/// <c>(fichier, index de frame résolu, résolution cible)</c> — jamais de
/// l'ordre de rendu. La résolution cible fait partie de la clé car
/// <see cref="VideoTextureLoader"/> est un singleton partagé entre le
/// renderer d'aperçu (résolution fixe, basse) et le renderer d'export
/// (résolution choisie par l'utilisateur, potentiellement très différente) :
/// sans elle, une frame décodée pour l'aperçu serait réutilisée telle quelle
/// pour l'export, avec un tampon de taille incorrecte copié dans une texture
/// D3D11 dimensionnée pour l'export.
/// </summary>
public readonly record struct VideoFrameKey(string VideoFilePath, int FrameIndex, int TargetWidth, int TargetHeight);
