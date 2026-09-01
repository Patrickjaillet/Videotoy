namespace Videotoy.Ffmpeg;

/// <summary>
/// Clé de cache pour une frame vidéo décodée, basée sur un index de frame
/// résolu (<c>timestampSeconds * probedFrameRate</c>, arrondi) plutôt que
/// sur le timestamp brut : deux requêtes qui retombent sur la même frame
/// source (relecture de la même position pendant le scrubbing de
/// l'aperçu, ou ré-rendu identique de la même timeline lors de la seconde
/// passe d'un export GIF) doivent systématiquement partager la même entrée
/// de cache, pour que le décodage reste une fonction pure de
/// <c>(fichier, index de frame résolu)</c> — jamais de l'ordre de rendu.
/// </summary>
public readonly record struct VideoFrameKey(string VideoFilePath, int FrameIndex);
