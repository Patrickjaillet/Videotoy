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

    public required IReadOnlyDictionary<string, CubemapAsset> Cubemaps { get; init; }

    public required IReadOnlyDictionary<string, VolumeAsset> Volumes { get; init; }

    public required IReadOnlyDictionary<string, AudioTrack> AudioTracks { get; init; }

    public required IReadOnlyDictionary<string, Videotoy.Ffmpeg.VideoTextureSource> VideoSources { get; init; }

    public required IReadOnlyDictionary<string, Videotoy.Core.ShaderTranspiler.TranspileResult> HlslPasses { get; init; }

    public bool HasErrors => Issues.Any(issue => issue.IsErrorIssue);
}

public sealed class ShaderFileService
{
    private static readonly string[] JsonExtensions = { ".json", ".shadertoy" };
    private static readonly string[] RawExtensions = { ".glsl", ".frag", ".wgsl", ".hlsl", ".hlsli", ".txt" };

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
                project = result.ResultValue.Item1;
                issues.AddRange(result.ResultValue.Item2);
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
            Cubemaps = previousLoad.Cubemaps,
            Volumes = previousLoad.Volumes,
            AudioTracks = previousLoad.AudioTracks,
            VideoSources = previousLoad.VideoSources,
            HlslPasses = reloaded.HlslPasses
        };
    }

    /// <summary>
    /// Reconstruit un <see cref="LoadedShader"/> à partir d'un
    /// <see cref="Videotoy.Core.ShaderModel.ShaderProject"/> déjà en mémoire
    /// (typiquement <paramref name="previousLoad"/>.Project modifié par
    /// <see cref="Videotoy.Core.ShaderModel.withPassSourceCode"/> avec le
    /// contenu actuel de l'éditeur intégré, Phase 1 du ROADMAP) plutôt que
    /// depuis un fichier sur disque. Réutilise les assets (textures/audio/
    /// vidéo) déjà chargés par <paramref name="previousLoad"/> — seuls le
    /// code source et sa validation/transpilation changent lors d'une
    /// compilation "à la volée" depuis l'éditeur, jamais les channels.
    /// </summary>
    public LoadedShader LoadFromProject(Videotoy.Core.ShaderModel.ShaderProject project, LoadedShader previousLoad)
    {
        var issues = new List<Videotoy.Core.ShaderModel.ShaderIssue>();
        var reloaded = BuildLoadedShader(project, issues);

        return new LoadedShader
        {
            Project = reloaded.Project,
            Issues = reloaded.Issues,
            Textures = previousLoad.Textures,
            Cubemaps = previousLoad.Cubemaps,
            Volumes = previousLoad.Volumes,
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
        var cubemaps = new Dictionary<string, CubemapAsset>(StringComparer.OrdinalIgnoreCase);
        var volumes = new Dictionary<string, VolumeAsset>(StringComparer.OrdinalIgnoreCase);
        var audioTracks = new Dictionary<string, AudioTrack>(StringComparer.OrdinalIgnoreCase);
        var videoSources = new Dictionary<string, Videotoy.Ffmpeg.VideoTextureSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var pass in Videotoy.Core.ShaderModel.allPasses(project))
        {
            foreach (var channel in Videotoy.Core.ShaderModel.passChannels(pass))
            {
                var texturePath = Videotoy.Core.ShaderModel.channelTexturePath(channel);
                if (texturePath is not null && !textures.ContainsKey(texturePath.Value))
                {
                    LoadTexture(pass.Name, baseDirectory, texturePath.Value, channel.Sampler.VerticalFlip, textures, issues);
                }

                var cubemapPath = Videotoy.Core.ShaderModel.channelCubemapPath(channel);
                if (cubemapPath is not null && !cubemaps.ContainsKey(cubemapPath.Value))
                {
                    LoadCubemap(pass.Name, baseDirectory, cubemapPath.Value, channel.Sampler.VerticalFlip, cubemaps, issues);
                }

                var volumePath = Videotoy.Core.ShaderModel.channelVolumePath(channel);
                if (volumePath is not null && !volumes.ContainsKey(volumePath.Value))
                {
                    LoadVolume(pass.Name, baseDirectory, volumePath.Value, channel.Sampler.VerticalFlip, volumes, issues);
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
            Cubemaps = cubemaps,
            Volumes = volumes,
            AudioTracks = audioTracks,
            VideoSources = videoSources,
            HlslPasses = hlslPasses
        };
    }

    private void LoadTexture(
        string passName,
        string baseDirectory,
        string relativeOrAbsolutePath,
        bool verticalFlip,
        IDictionary<string, TextureAsset> textures,
        ICollection<Videotoy.Core.ShaderModel.ShaderIssue> issues)
    {
        if (!TryResolveAssetPath(baseDirectory, relativeOrAbsolutePath, out var resolvedPath))
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Texture path escapes the shader's directory: '{relativeOrAbsolutePath}'."));
            return;
        }

        if (!File.Exists(resolvedPath))
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Texture file not found: '{relativeOrAbsolutePath}'."));
            return;
        }

        try
        {
            textures[relativeOrAbsolutePath] = _textureLoader.Load(resolvedPath, verticalFlip);
        }
        catch (Exception ex)
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Failed to load texture '{relativeOrAbsolutePath}': {ex.Message}"));
        }
    }

    /// <summary>
    /// Charge les 6 faces d'une cubemap (Phase 3 du ROADMAP) : dérive leurs
    /// 6 chemins depuis <paramref name="relativeOrAbsolutePath"/> (chemin de
    /// la face 0, tel que déclaré par <c>"src"</c>) via
    /// <see cref="Videotoy.Core.ShaderModel.cubemapFacePaths"/>, puis valide
    /// et résout chacun individuellement — même garde-fous que
    /// <see cref="LoadTexture"/> (traversée de répertoire, fichier
    /// manquant), appliqués face par face.
    /// </summary>
    private void LoadCubemap(
        string passName,
        string baseDirectory,
        string relativeOrAbsolutePath,
        bool verticalFlip,
        IDictionary<string, CubemapAsset> cubemaps,
        ICollection<Videotoy.Core.ShaderModel.ShaderIssue> issues)
    {
        var faceRelativePaths = Videotoy.Core.ShaderModel.cubemapFacePaths(relativeOrAbsolutePath);
        var resolvedFacePaths = new List<string>(faceRelativePaths.Length);

        foreach (var faceRelativePath in faceRelativePaths)
        {
            if (!TryResolveAssetPath(baseDirectory, faceRelativePath, out var resolvedFacePath))
            {
                issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Cubemap face path escapes the shader's directory: '{faceRelativePath}'."));
                return;
            }

            if (!File.Exists(resolvedFacePath))
            {
                issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Cubemap face file not found: '{faceRelativePath}'."));
                return;
            }

            resolvedFacePaths.Add(resolvedFacePath);
        }

        try
        {
            cubemaps[relativeOrAbsolutePath] = _textureLoader.LoadCubemap(resolvedFacePaths, verticalFlip);
        }
        catch (Exception ex)
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Failed to load cubemap '{relativeOrAbsolutePath}': {ex.Message}"));
        }
    }

    private void LoadVolume(
        string passName,
        string baseDirectory,
        string relativeOrAbsolutePath,
        bool verticalFlip,
        IDictionary<string, VolumeAsset> volumes,
        ICollection<Videotoy.Core.ShaderModel.ShaderIssue> issues)
    {
        if (!TryResolveAssetPath(baseDirectory, relativeOrAbsolutePath, out var resolvedPath))
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Volume texture path escapes the shader's directory: '{relativeOrAbsolutePath}'."));
            return;
        }

        if (!File.Exists(resolvedPath))
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Volume texture file not found: '{relativeOrAbsolutePath}'."));
            return;
        }

        try
        {
            volumes[relativeOrAbsolutePath] = _textureLoader.LoadVolume(resolvedPath, verticalFlip);
        }
        catch (Exception ex)
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Failed to load volume texture '{relativeOrAbsolutePath}': {ex.Message}"));
        }
    }

    private void LoadAudio(
        string passName,
        string baseDirectory,
        string relativeOrAbsolutePath,
        IDictionary<string, AudioTrack> audioTracks,
        ICollection<Videotoy.Core.ShaderModel.ShaderIssue> issues)
    {
        if (!TryResolveAssetPath(baseDirectory, relativeOrAbsolutePath, out var resolvedPath))
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Audio path escapes the shader's directory: '{relativeOrAbsolutePath}'."));
            return;
        }

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
        if (!TryResolveAssetPath(baseDirectory, relativeOrAbsolutePath, out var resolvedPath))
        {
            issues.Add(Videotoy.Core.ShaderModel.warningIssue(passName, 1, $"Video path escapes the shader's directory: '{relativeOrAbsolutePath}'."));
            return;
        }

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

    /// <summary>
    /// Résout <paramref name="assetPath"/> (le champ <c>src</c> d'un
    /// <c>iChannel</c>, tel que déclaré dans un export Shadertoy JSON — donc
    /// un contenu potentiellement partagé/téléchargé, jamais du texte de
    /// confiance) relativement à <paramref name="baseDirectory"/>, et refuse
    /// tout résultat en dehors de ce répertoire : un export JSON de shader
    /// ne référence jamais légitimement un média en dehors de son propre
    /// dossier, donc <c>"../../../../Windows/win.ini"</c> ou un chemin
    /// absolu pointant ailleurs sur le disque (<c>C:\Windows\win.ini</c>)
    /// sont rejetés plutôt que silencieusement chargés comme texture/piste
    /// audio/vidéo.
    /// </summary>
    private static bool TryResolveAssetPath(string baseDirectory, string assetPath, out string resolvedPath)
    {
        var normalizedBaseDirectory = Path.GetFullPath(baseDirectory);
        var candidatePath = Path.GetFullPath(Path.Combine(normalizedBaseDirectory, assetPath));

        var baseDirectoryWithSeparator = normalizedBaseDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedBaseDirectory
            : normalizedBaseDirectory + Path.DirectorySeparatorChar;

        if (!candidatePath.StartsWith(baseDirectoryWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            resolvedPath = string.Empty;
            return false;
        }

        resolvedPath = candidatePath;
        return true;
    }
}
