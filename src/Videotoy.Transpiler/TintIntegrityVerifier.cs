using System.IO;

namespace Videotoy.Transpiler;

/// <summary>
/// Mirroir de <c>Videotoy.Ffmpeg.FfmpegIntegrityVerifier</c>, avec une
/// différence de posture volontaire : le support WGSL est optionnel, donc
/// cette vérification n'est jamais appelée au démarrage de l'application
/// (contrairement à FFmpeg) — seulement de façon paresseuse, au premier
/// chargement d'un fichier <c>.wgsl</c> (voir le routeur de transpileurs
/// dans <c>Videotoy.App</c>). Un binaire absent lève
/// <see cref="TintNotAvailableException"/> plutôt que
/// <see cref="FileNotFoundException"/> directement, pour que l'appelant
/// puisse distinguer "jamais vendu" (dégradation normale) de
/// "présent mais corrompu" (<see cref="TintIntegrityException"/>).
/// </summary>
public sealed class TintIntegrityVerifier
{
    private readonly TintLocator _locator;

    public TintIntegrityVerifier(TintLocator locator)
    {
        _locator = locator;
    }

    public void VerifyOrThrow()
    {
        var executablePath = _locator.ResolveExecutablePath();
        var hashFilePath = _locator.ResolveHashFilePath();

        if (!File.Exists(executablePath))
        {
            throw new TintNotAvailableException(
                $"Tint executable not found at '{executablePath}'. WGSL shader support requires it — see COMPILATION.md.");
        }

        if (!File.Exists(hashFilePath))
        {
            throw new TintIntegrityException(
                $"Tint integrity hash file not found at '{hashFilePath}'.");
        }

        var expectedHash = ReadExpectedHash(hashFilePath);
        var actualHash = TintLocator.ComputeSha256(executablePath);

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new TintIntegrityException(
                "Embedded Tint executable failed the SHA-256 integrity check. " +
                "The binary may be corrupted or has been tampered with.");
        }
    }

    private static string ReadExpectedHash(string hashFilePath)
    {
        var content = File.ReadAllText(hashFilePath).Trim();
        var separatorIndex = content.IndexOfAny(new[] { ' ', '\t' });
        return separatorIndex > 0 ? content[..separatorIndex] : content;
    }
}
