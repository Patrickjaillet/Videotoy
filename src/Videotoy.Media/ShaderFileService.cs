using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Videotoy.Media;

public sealed class LoadedShader
{
    public required Videotoy.Core.ShaderModel.ShaderProject Project { get; init; }

    public required IReadOnlyList<Videotoy.Core.ShaderModel.ShaderIssue> Issues { get; init; }

    public required IReadOnlyDictionary<string, TextureAsset> Textures { get; init; }

    public required IReadOnlyDictionary<string, AudioTrack> AudioTracks { get; init; }

    public required IReadOnlyDictionary<string, Videotoy.Core.GlslToHlslTranspiler.TranspileResult> HlslPasses { get; init; }

    public bool HasErrors => Issues.Any(issue => issue.IsErrorIssue);
}

public sealed class ShaderFileService
{
    private static readonly string[] JsonExtensions = { ".json", ".shadertoy" };
    private static readonly string[] RawExtensions = { ".glsl", ".frag" };

    private readonly TextureLoader _textureLoader;
    private readonly AudioTrackLoader _audioTrackLoader;

    public ShaderFileService(TextureLoader textureLoader, AudioTrackLoader audioTrackLoader)
    {
        _textureLoader = textureLoader;
        _audioTrackLoader = audioTrackLoader;
    }

    public static bool IsSupportedShaderFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return JsonExtensions.Contains(extension) || RawExtensions.Contains(extension);
    }

    public LoadedShader Load(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var issues = new List<Videotoy.Core.ShaderModel.ShaderIssue>();
        Videotoy.Core.ShaderModel.ShaderProject project;

        if (JsonExtensions.Contains(extension))
        {
            var jsonText = File.ReadAllText(filePath);
            var result = Videotoy.Core.ShadertoyJsonParser.parse(jsonText, filePath);

            if (result.IsOk)
            {
                project = result.ResultValue;
            }
            else
            {
                issues.AddRange(result.ErrorValue);
                project = Videotoy.Core.ShaderModel.fromRawSource(string.Empty, filePath);
            }
        }
        else if (RawExtensions.Contains(extension))
        {
            var sourceCode = File.ReadAllText(filePath);
            project = Videotoy.Core.ShaderModel.fromRawSource(sourceCode, filePath);
        }
        else
        {
            throw new NotSupportedException($"Unsupported shader file extension: '{extension}'.");
        }

        issues.AddRange(Videotoy.Core.ShaderValidator.validateProject(project));

        var hlslPasses = Videotoy.Core.GlslToHlslTranspiler.transpileProject(project);
        foreach (var pair in hlslPasses)
        {
            issues.AddRange(pair.Value.Diagnostics);
        }

        var baseDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var textures = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        var audioTracks = new Dictionary<string, AudioTrack>(StringComparer.OrdinalIgnoreCase);

        foreach (var pass in Videotoy.Core.ShaderModel.allPasses(project))
        {
            foreach (var channel in Videotoy.Core.ShaderModel.passChannels(pass))
            {
                var texturePath = Videotoy.Core.ShaderModel.channelTexturePath(channel);
                if (texturePath is not null && !textures.ContainsKey(texturePath.Value))
                {
                    LoadTexture(pass.Name, baseDirectory, texturePath.Value, textures, issues);
                }

                var audioPath = Videotoy.Core.ShaderModel.channelAudioPath(channel);
                if (audioPath is not null && !audioTracks.ContainsKey(audioPath.Value))
                {
                    LoadAudio(pass.Name, baseDirectory, audioPath.Value, audioTracks, issues);
                }
            }
        }

        return new LoadedShader
        {
            Project = project,
            Issues = issues,
            Textures = textures,
            AudioTracks = audioTracks,
            HlslPasses = hlslPasses
        };
    }

    private void LoadTexture(
        string passName,
        string baseDirectory,
        string relativeOrAbsolutePath,
        IDictionary<string, TextureAsset> textures,
        ICollection<Videotoy.Core.ShaderModel.ShaderIssue> issues)
    {
        var resolvedPath = ResolveAssetPath(baseDirectory, relativeOrAbsolutePath);

        if (!File.Exists(resolvedPath))
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Texture file not found: '{relativeOrAbsolutePath}'."));
            return;
        }

        try
        {
            textures[relativeOrAbsolutePath] = _textureLoader.Load(resolvedPath);
        }
        catch (Exception ex)
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Failed to load texture '{relativeOrAbsolutePath}': {ex.Message}"));
        }
    }

    private void LoadAudio(
        string passName,
        string baseDirectory,
        string relativeOrAbsolutePath,
        IDictionary<string, AudioTrack> audioTracks,
        ICollection<Videotoy.Core.ShaderModel.ShaderIssue> issues)
    {
        var resolvedPath = ResolveAssetPath(baseDirectory, relativeOrAbsolutePath);

        if (!File.Exists(resolvedPath))
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Audio file not found: '{relativeOrAbsolutePath}'."));
            return;
        }

        try
        {
            audioTracks[relativeOrAbsolutePath] = _audioTrackLoader.Load(resolvedPath);
        }
        catch (Exception ex)
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Failed to load audio source '{relativeOrAbsolutePath}': {ex.Message}"));
        }
    }

    private static string ResolveAssetPath(string baseDirectory, string assetPath)
    {
        return Path.IsPathRooted(assetPath) ? assetPath : Path.Combine(baseDirectory, assetPath);
    }
}
