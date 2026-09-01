using System.IO;
using System.Security.Cryptography;

namespace Videotoy.Transpiler;

public sealed class TintLocator
{
    private const string RelativeExecutablePath = "tools\\tint\\tint.exe";
    private const string RelativeHashFilePath = "tools\\tint\\tint.exe.sha256";

    public string ResolveExecutablePath()
    {
        return Path.Combine(AppContext.BaseDirectory, RelativeExecutablePath);
    }

    public string ResolveHashFilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, RelativeHashFilePath);
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
