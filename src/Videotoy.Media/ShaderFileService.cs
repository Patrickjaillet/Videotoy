using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Videotoy.Media;

public sealed class LoadedShader
{
    public required Videotoy.Core.ShaderModel.ShaderProject Project { get; init; }

    public required IReadOnlyList<Videotoy.Core.ShaderModel.ShaderIssue> Issues { get; init; }

    public required IReadOnlyDictionary<string, TextureAsset> Textures { get; init; }

    public required IReadOnlyDictionary<string, AudioTrack> AudioTracks { get; init; }

    public required IReadOnlyDictionary<string, Videotoy.Ffmpeg.VideoTextureSource> VideoSources { get; init; }

    public required IReadOnlyDictionary<string, Videotoy.Core.ShaderTranspiler.TranspileResult> HlslPasses { get; init; }

    public bool HasErrors => Issues.Any(issue => issue.IsErrorIssue);
}

public sealed class ShaderFileService
{
    private static readonly string[] JsonExtensions = { ".json", ".shadertoy" };
    private static readonly string[] RawExtensions = { ".glsl", ".frag", ".wgsl", ".hlsl", ".hlsli" };

    private readonly TextureLoader _textureLoader;
    private readonly AudioTrackLoader _audioTrackLoader;
    private readonly Videotoy.Ffmpeg.VideoTextureLoader _videoTextureLoader;
    private readonly IShaderTranspilerRouter _transpilerRouter;

    public ShaderFileService(
        TextureLoader textureLoader,
        AudioTrackLoader audioTrackLoader,
        Videotoy.Ffmpeg.VideoTextureLoader videoTextureLoader,
        IShaderTranspilerRouter transpilerRouter)
    {
        _textureLoader = textureLoader;
        _audioTrackLoader = audioTrackLoader;
        _videoTextureLoader = videoTextureLoader;
        _transpilerRouter = transpilerRouter;
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
                project = Videotoy.Core.ShaderModel.fromRawSource(string.Empty, filePath, Videotoy.Core.ShaderModel.ShaderSourceLanguage.Glsl);
            }
        }
        else if (RawExtensions.Contains(extension))
        {
            var sourceCode = File.ReadAllText(filePath);
            var detectedLanguage = Videotoy.Core.ShaderLanguageDetector.detect(filePath, sourceCode);
            project = Videotoy.Core.ShaderModel.fromRawSource(sourceCode, filePath, detectedLanguage);
        }
        else
        {
            throw new NotSupportedException($"Unsupported shader file extension: '{extension}'.");
        }

        return BuildLoadedShader(project, issues);
    }

    /// <summary>
    /// Reconstruit un <see cref="LoadedShader"/> avec un langage source forcé
    /// manuellement par l'utilisateur (voir <c>MainWindowViewModel.ForceShaderLanguageAsync</c>),
    /// sans relire le fichier depuis le disque ni recharger les assets
    /// (textures/audio/vidéo) — seules la validation et la transpilation
    /// dépendent du langage, donc seules elles sont ré-exécutées.
    /// </summary>
    public LoadedShader ReloadWithLanguageOverride(LoadedShader previousLoad, Videotoy.Core.ShaderModel.ShaderSourceLanguage overrideLanguage)
    {
        var project = Videotoy.Core.ShaderModel.withSourceLanguage(overrideLanguage, previousLoad.Project);
        var issues = new List<Videotoy.Core.ShaderModel.ShaderIssue>();

        var reloaded = BuildLoadedShader(project, issues);

        return new LoadedShader
        {
            Project = reloaded.Project,
            Issues = reloaded.Issues,
            Textures = previousLoad.Textures,
            AudioTracks = previousLoad.AudioTracks,
            VideoSources = previousLoad.VideoSources,
            HlslPasses = reloaded.HlslPasses
        };
    }

    private LoadedShader BuildLoadedShader(Videotoy.Core.ShaderModel.ShaderProject project, List<Videotoy.Core.ShaderModel.ShaderIssue> issues)
    {
        issues.AddRange(Videotoy.Core.ShaderValidator.validateProject(project));

        var hlslPasses = _transpilerRouter.TranspileProjectAsync(project, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        foreach (var pair in hlslPasses)
        {
            issues.AddRange(pair.Value.Diagnostics);
        }

        var baseDirectory = Path.GetDirectoryName(project.SourceFilePath) ?? string.Empty;
        var textures = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        var audioTracks = new Dictionary<string, AudioTrack>(StringComparer.OrdinalIgnoreCase);
        var videoSources = new Dictionary<string, Videotoy.Ffmpeg.VideoTextureSource>(StringComparer.OrdinalIgnoreCase);

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

                var videoPath = Videotoy.Core.ShaderModel.channelVideoPath(channel);
                if (videoPath is not null && !videoSources.ContainsKey(videoPath.Value))
                {
                    LoadVideo(pass.Name, baseDirectory, videoPath.Value, videoSources, issues);
                }
            }
        }

        return new LoadedShader
        {
            Project = project,
            Issues = issues,
            Textures = textures,
            AudioTracks = audioTracks,
            VideoSources = videoSources,
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

    private void LoadVideo(
        string passName,
        string baseDirectory,
        string relativeOrAbsolutePath,
        IDictionary<string, Videotoy.Ffmpeg.VideoTextureSource> videoSources,
        ICollection<Videotoy.Core.ShaderModel.ShaderIssue> issues)
    {
        var resolvedPath = ResolveAssetPath(baseDirectory, relativeOrAbsolutePath);

        if (!File.Exists(resolvedPath))
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Video file not found: '{relativeOrAbsolutePath}'."));
            return;
        }

        try
        {
            var probe = _videoTextureLoader.ProbeAsync(resolvedPath).GetAwaiter().GetResult();
            videoSources[relativeOrAbsolutePath] = new Videotoy.Ffmpeg.VideoTextureSource
            {
                FilePath = resolvedPath,
                Probe = probe
            };
        }
        catch (Exception ex)
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Failed to load video source '{relativeOrAbsolutePath}': {ex.Message}"));
        }
    }

    private static string ResolveAssetPath(string baseDirectory, string assetPath)
    {
        return Path.IsPathRooted(assetPath) ? assetPath : Path.Combine(baseDirectory, assetPath);
    }
}
