using System;
using System.IO;

namespace Videotoy.Media;

public sealed class RecentShaderFile
{
    public required string FilePath { get; init; }

    public required DateTime LastOpenedUtc { get; init; }

    public string DisplayName => Path.GetFileName(FilePath);
}
