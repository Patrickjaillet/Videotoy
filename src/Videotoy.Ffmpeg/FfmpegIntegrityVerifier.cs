using System.IO;

namespace Videotoy.Ffmpeg;

public sealed class FfmpegIntegrityVerifier
{
    private readonly FfmpegLocator _locator;
    private readonly object _lastVerifiedLock = new();
    private (DateTime LastWriteTimeUtc, long Length)? _lastVerifiedStamp;

    public FfmpegIntegrityVerifier(FfmpegLocator locator)
    {
        _locator = locator;
    }

    public void VerifyOrThrow() => VerifyAndRecordStamp();

    /// <summary>
    /// Revérifie l'intégrité du binaire juste avant de le lancer, appelée
    /// depuis chaque site qui démarre <c>ffmpeg.exe</c> (<see cref="FfmpegService"/>,
    /// <see cref="VideoFrameDecoder"/>, <see cref="HardwareEncoderProbe"/>,
    /// <see cref="VideoProber"/>) : la vérification au démarrage de l'appli
    /// seule laisse une fenêtre TOCTOU entre ce contrôle et une exécution
    /// ultérieure, potentiellement bien après (le binaire pourrait être
    /// remplacé entre-temps). Pour éviter de recalculer un SHA-256 complet
    /// sur un fichier de plusieurs dizaines de mégaoctets à chaque frame
    /// décodée pendant un export, l'empreinte complète n'est recalculée que
    /// si la date de dernière écriture ou la taille du fichier a changé
    /// depuis la dernière vérification réussie ; un remplacement du binaire
    /// modifie nécessairement l'un des deux.
    /// </summary>
    public void EnsureStillValid()
    {
        var executablePath = _locator.ResolveExecutablePath();
        var info = new FileInfo(executablePath);

        if (!info.Exists)
        {
            throw new FileNotFoundException(
                $"Embedded FFmpeg executable not found at '{executablePath}'.", executablePath);
        }

        var currentStamp = (info.LastWriteTimeUtc, info.Length);

        lock (_lastVerifiedLock)
        {
            if (_lastVerifiedStamp == currentStamp)
            {
                return;
            }
        }

        VerifyAndRecordStamp();
    }

    private void VerifyAndRecordStamp()
    {
        var executablePath = _locator.ResolveExecutablePath();
        var hashFilePath = _locator.ResolveHashFilePath();

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Embedded FFmpeg executable not found at '{executablePath}'.", executablePath);
        }

        if (!File.Exists(hashFilePath))
        {
            throw new FfmpegIntegrityException(
                $"FFmpeg integrity hash file not found at '{hashFilePath}'.");
        }

        var expectedHash = ReadExpectedHash(hashFilePath);
        var actualHash = FfmpegLocator.ComputeSha256(executablePath);

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new FfmpegIntegrityException(
                "Embedded FFmpeg executable failed the SHA-256 integrity check. " +
                "The binary may be corrupted or has been tampered with.");
        }

        var info = new FileInfo(executablePath);
        lock (_lastVerifiedLock)
        {
            _lastVerifiedStamp = (info.LastWriteTimeUtc, info.Length);
        }
    }

    private static string ReadExpectedHash(string hashFilePath)
    {
        var content = File.ReadAllText(hashFilePath).Trim();
        var separatorIndex = content.IndexOfAny(new[] { ' ', '\t' });
        return separatorIndex > 0 ? content[..separatorIndex] : content;
    }
}
