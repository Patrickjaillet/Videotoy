using System.IO;

namespace Videotoy.Ffmpeg;

public sealed class FfmpegIntegrityVerifier
{
    private readonly FfmpegLocator _locator;

    public FfmpegIntegrityVerifier(FfmpegLocator locator)
    {
        _locator = locator;
    }

    public void VerifyOrThrow()
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
    }

    private static string ReadExpectedHash(string hashFilePath)
    {
        var content = File.ReadAllText(hashFilePath).Trim();
        var separatorIndex = content.IndexOfAny(new[] { ' ', '\t' });
        return separatorIndex > 0 ? content[..separatorIndex] : content;
    }
}
