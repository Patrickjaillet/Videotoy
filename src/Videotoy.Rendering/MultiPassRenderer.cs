using System.Linq;
using System.Numerics;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace Videotoy.Rendering;

/// <summary>
/// Rend un ShaderProject Shadertoy complet (Buffer A/B/C/D + Image) en respectant
/// l'ordre de dépendance des buffers, avec ping-pong de render targets pour les
/// passes qui se lisent elles-mêmes (feedback loops) d'une frame à l'autre.
/// </summary>
public class MultiPassRenderer : IDisposable
{
    private const string FullscreenTriangleVertexShaderSource =
        """
        struct VertexOutput
        {
            float4 Position : SV_Position;
        };

        VertexOutput VSMain(uint vertexId : SV_VertexID)
        {
            VertexOutput output;
            float2 texCoord = float2((vertexId << 1) & 2, vertexId & 2);
            output.Position = float4(texCoord * float2(2, -2) + float2(-1, 1), 0, 1);
            return output;
        }
        """;

    private static readonly ShaderFlags CompileFlags =
#if DEBUG
        ShaderFlags.EnableStrictness | ShaderFlags.Debug | ShaderFlags.SkipValidation;
#else
        ShaderFlags.EnableStrictness | ShaderFlags.OptimizationLevel3;
#endif

    /// <summary>
    /// Nature d'un asset externe lié à un channel (jamais un buffer, géré
    /// séparément via <see cref="PassSlot.BufferBindings"/>) : détermine
    /// comment son contenu GPU est rafraîchi — <see cref="Image"/>,
    /// <see cref="Cubemap"/> et <see cref="Volume"/> sont uploadées une seule
    /// fois à <see cref="Initialize"/> (contenu statique), <see cref="AudioSpectrum"/>
    /// et <see cref="Video"/> sont ré-uploadées à chaque <see cref="RenderFrame"/>.
    /// </summary>
    private enum AssetKind
    {
        Image,
        AudioSpectrum,
        Video,
        Cubemap,
        Volume
    }

    /// <summary>
    /// <see cref="Resource"/> est le type concret de la ressource D3D11 sous-
    /// jacente : <see cref="ID3D11Texture2D"/> pour <see cref="AssetKind.Image"/>/
    /// <see cref="AssetKind.AudioSpectrum"/>/<see cref="AssetKind.Video"/>/
    /// <see cref="AssetKind.Cubemap"/> (une cubemap est un <c>Texture2D</c>
    /// D3D11 avec <c>ArraySize=6</c>/<c>MiscFlags.TextureCube</c>, pas un
    /// type de ressource séparé), <see cref="ID3D11Texture3D"/> pour
    /// <see cref="AssetKind.Volume"/> — <see cref="ID3D11Resource"/> est la
    /// classe de base commune aux deux, seule nécessaire pour
    /// <see cref="ID3D11DeviceContext.Dispose"/>/libération.
    /// </summary>
    private sealed record BoundAsset(ID3D11Resource Resource, ID3D11ShaderResourceView View, AssetKind Kind, int Width, int Height);

    private sealed class PassSlot : IDisposable
    {
        public required string Name { get; init; }
        public required bool IsPingPong { get; init; }
        public required (int ChannelIndex, string BufferPassName)[] BufferBindings { get; init; }
        public required (int ChannelIndex, string AssetPath, AssetKind Kind, Core.ShaderModel.ChannelSamplerSettings Sampler)[] AssetBindings { get; init; }

        // Passe simple (Image, jamais lue par une autre passe) : un seul contexte.
        // Passe ping-pong (Buffer A/B/C/D auto-référencé) : deux contextes, Front = résultat
        // de la dernière frame rendue (lisible), Back = cible d'écriture de la frame en cours.
        public OffscreenRenderContext Front { get; private set; } = null!;
        public OffscreenRenderContext? Back { get; private set; }

        public ID3D11PixelShader PixelShader { get; set; } = null!;

        public void Initialize(RenderTargetSize size, ID3D11Device device, ID3D11DeviceContext immediateContext)
        {
            Front = new OffscreenRenderContext(device, immediateContext);
            Front.Resize(size);

            if (IsPingPong)
            {
                Back = new OffscreenRenderContext(device, immediateContext);
                Back.Resize(size);
            }
        }

        public void Resize(RenderTargetSize size)
        {
            Front.Resize(size);
            Back?.Resize(size);
        }

        public OffscreenRenderContext WriteTarget => IsPingPong ? Back! : Front;

        public void SwapPingPong()
        {
            if (!IsPingPong)
            {
                return;
            }

            (Front, Back) = (Back!, Front);
        }

        public void Dispose()
        {
            PixelShader?.Dispose();
            Front?.Dispose();
            Back?.Dispose();
        }
    }

    private readonly OffscreenRenderContext _sharedContext = new();
    private readonly List<PassSlot> _orderedSlots = new();
    private readonly Dictionary<string, PassSlot> _slotsByName = new();

    private ID3D11VertexShader? _vertexShader;
    private ID3D11Buffer? _uniformsBuffer;
    private ID3D11SamplerState? _defaultSampler;

    /// <summary>
    /// Un <see cref="ID3D11SamplerState"/> par combinaison distincte de
    /// réglages <see cref="Core.ShaderModel.ChannelSamplerSettings"/>
    /// effectivement utilisée par au moins un <c>iChannel</c> du projet
    /// chargé (Phase 3 du ROADMAP, attributs <c>filter</c>/<c>wrap</c> par
    /// input Shadertoy JSON) — <see cref="_defaultSampler"/> reste utilisé
    /// pour les buffers inter-passes (jamais de réglages d'échantillonnage
    /// propres côté Shadertoy) afin de ne pas recréer inutilement un état
    /// identique pour chacun. Vidé et recréé à chaque <see cref="BuildPassGraph"/>
    /// comme le reste de l'état dépendant du projet chargé.
    /// </summary>
    private readonly Dictionary<Core.ShaderModel.ChannelSamplerSettings, ID3D11SamplerState> _channelSamplers = new();

    private ID3D11Buffer? _customUniformsBuffer;
    private IReadOnlyList<Core.CustomUniformParser.CustomUniformDeclaration> _customUniformDeclarations =
        Array.Empty<Core.CustomUniformParser.CustomUniformDeclaration>();
    private readonly Dictionary<string, float[]> _customUniformValues = new();

    private readonly Dictionary<string, BoundAsset> _boundAssets = new();
    private IReadOnlyDictionary<string, BoundImageAsset> _images = new Dictionary<string, BoundImageAsset>();
    private IReadOnlyDictionary<string, BoundAudioAsset> _audioTracks = new Dictionary<string, BoundAudioAsset>();
    private IReadOnlyDictionary<string, BoundVideoAsset> _videoSources = new Dictionary<string, BoundVideoAsset>();
    private IReadOnlyDictionary<string, BoundCubemapAsset> _cubemaps = new Dictionary<string, BoundCubemapAsset>();
    private IReadOnlyDictionary<string, BoundVolumeAsset> _volumes = new Dictionary<string, BoundVolumeAsset>();

    private RenderTargetSize _size;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Uniforms custom exposés par le shader actuellement chargé (déclarations
    /// `// uniform: ...` détectées par <see cref="Core.CustomUniformParser"/>
    /// dans le Common ou n'importe quelle passe), dans un ordre stable. Vide
    /// si le shader n'en expose aucun. Utilisé par le panneau de paramètres de
    /// rendu pour générer dynamiquement un slider par composant.
    /// </summary>
    public IReadOnlyList<Core.CustomUniformParser.CustomUniformDeclaration> CustomUniformDeclarations =>
        _customUniformDeclarations;

    /// <summary>
    /// Valeur courante (par composant) de l'uniform custom nommé
    /// <paramref name="name"/>, ou ses valeurs par défaut si elle n'a jamais
    /// été modifiée. Lève si <paramref name="name"/> ne correspond à aucune
    /// déclaration du shader chargé.
    /// </summary>
    public IReadOnlyList<float> GetCustomUniformValue(string name)
    {
        if (_customUniformValues.TryGetValue(name, out var values))
        {
            return values;
        }

        throw new ArgumentException($"Unknown custom uniform '{name}'.", nameof(name));
    }

    /// <summary>
    /// Met à jour, en direct, la valeur d'un composant (0 = x, 1 = y, ...) de
    /// l'uniform custom nommé <paramref name="name"/>. Sans effet sur le
    /// pipeline tant que <see cref="RenderFrame"/> n'a pas été appelé à
    /// nouveau : la prochaine frame de prévisualisation reflète immédiatement
    /// la nouvelle valeur, sans recompilation de shader. No-op silencieux si
    /// le nom ou l'index de composant est invalide, pour rester tolérant à un
    /// slider qui référencerait encore un ancien shader pendant un changement.
    /// </summary>
    public void SetCustomUniformComponent(string name, int componentIndex, float value)
    {
        if (!_customUniformValues.TryGetValue(name, out var values) || componentIndex < 0 || componentIndex >= values.Length)
        {
            return;
        }

        values[componentIndex] = value;
    }

    /// <summary>
    /// Nom de la passe finale (toujours "Image") dont les pixels sont retournés par RenderFrame.
    /// </summary>
    public const string FinalPassName = Core.PassGraph.ImagePassName;

    public void Initialize(
        RenderTargetSize size,
        Core.ShaderModel.ShaderProject project,
        IReadOnlyDictionary<string, Core.ShaderTranspiler.TranspileResult> hlslPasses,
        IReadOnlyDictionary<string, BoundImageAsset>? images = null,
        IReadOnlyDictionary<string, BoundAudioAsset>? audioTracks = null,
        IReadOnlyDictionary<string, BoundVideoAsset>? videoSources = null,
        IReadOnlyDictionary<string, BoundCubemapAsset>? cubemaps = null,
        IReadOnlyDictionary<string, BoundVolumeAsset>? volumes = null)
    {
        _size = size;
        _images = images ?? new Dictionary<string, BoundImageAsset>();
        _audioTracks = audioTracks ?? new Dictionary<string, BoundAudioAsset>();
        _videoSources = videoSources ?? new Dictionary<string, BoundVideoAsset>();
        _cubemaps = cubemaps ?? new Dictionary<string, BoundCubemapAsset>();
        _volumes = volumes ?? new Dictionary<string, BoundVolumeAsset>();

        var vertexShaderBytecode = Compiler.Compile(
            FullscreenTriangleVertexShaderSource,
            "VSMain",
            "videotoy-vs",
            "vs_5_0",
            CompileFlags);

        _vertexShader = _sharedContext.Device.CreateVertexShader(vertexShaderBytecode.Span);

        var uniformsBufferDescription = new BufferDescription
        {
            ByteWidth = ShadertoyUniformsBuffer.SizeInBytes,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        };

        _uniformsBuffer = _sharedContext.Device.CreateBuffer(uniformsBufferDescription);

        var samplerDescription = new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            ComparisonFunc = ComparisonFunction.Never,
            MaxLOD = float.MaxValue
        };

        _defaultSampler = _sharedContext.Device.CreateSamplerState(samplerDescription);

        foreach (var sampler in _channelSamplers.Values)
        {
            sampler.Dispose();
        }
        _channelSamplers.Clear();

        InitializeCustomUniforms(hlslPasses);
        BuildPassGraph(project, hlslPasses);
        InitializeBoundAssets();

        foreach (var slot in _orderedSlots)
        {
            slot.Initialize(size, _sharedContext.Device, _sharedContext.ImmediateContext);
        }

        _initialized = true;
    }

    /// <summary>
    /// Chaque uniform custom occupe un multiple de 16 octets dans le buffer
    /// (un `float4` complet même pour un simple `float`), quel que soit son
    /// nombre réel de composants : c'est l'alignement le plus restrictif
    /// qu'HLSL puisse exiger pour un champ de `cbuffer`, et le garantir
    /// uniformément évite tout calcul de padding par variante de type.
    /// </summary>
    private const int CustomUniformSlotSizeInBytes = 16;

    /// <summary>
    /// Recense les uniforms custom exposés par le shader (union dédupliquée
    /// des déclarations `// uniform: ...` de toutes les passes déjà
    /// transpilées), initialise leur valeur courante à la valeur par défaut
    /// déclarée, et alloue le buffer constant HLSL `register(b1)` qui leur
    /// correspond. Ré-appelée à chaque chargement de shader : les valeurs
    /// d'un shader précédent ne survivent jamais au chargement d'un autre.
    /// </summary>
    private void InitializeCustomUniforms(
        IReadOnlyDictionary<string, Core.ShaderTranspiler.TranspileResult> hlslPasses)
    {
        _customUniformsBuffer?.Dispose();
        _customUniformsBuffer = null;
        _customUniformValues.Clear();

        var declarations = Core.ShaderTranspiler.projectCustomUniformsOf(hlslPasses.Values)
            .ToArray();

        _customUniformDeclarations = declarations;

        foreach (var declaration in declarations)
        {
            _customUniformValues[declaration.Name] = declaration.DefaultValues.ToArray();
        }

        if (declarations.Length == 0)
        {
            return;
        }

        var bufferDescription = new BufferDescription
        {
            ByteWidth = (uint)(declarations.Length * CustomUniformSlotSizeInBytes),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        };

        _customUniformsBuffer = _sharedContext.Device.CreateBuffer(bufferDescription);
    }

    private void BuildPassGraph(
        Core.ShaderModel.ShaderProject project,
        IReadOnlyDictionary<string, Core.ShaderTranspiler.TranspileResult> hlslPasses)
    {
        DisposeSlots();

        try
        {
            var executionOrder = Core.PassGraph.executionOrder(project);
            var selfReferencing = Core.PassGraph.selfReferencingPassNames(project);
            var passesByName = Core.ShaderModel.allPasses(project)
                .ToDictionary(pass => pass.Name);

            foreach (var passName in executionOrder)
            {
                if (!passesByName.TryGetValue(passName, out var pass))
                {
                    continue;
                }

                if (!hlslPasses.TryGetValue(passName, out var transpileResult))
                {
                    continue;
                }

                var bindings = Core.PassGraph.bufferChannelBindings(project, pass)
                    .Select(binding => (binding.Item1, binding.Item2))
                    .ToArray();

                var assetBindings = Core.PassGraph.assetChannelBindings(pass)
                    .Select(binding => ResolveAssetBinding(binding.Item1, binding.Item2))
                    .Where(binding => binding is not null)
                    .Select(binding => binding!.Value)
                    .ToArray();

                foreach (var binding in assetBindings)
                {
                    GetOrCreateSamplerState(binding.Sampler);
                }

                var pixelShaderBytecode = Compiler.Compile(
                    transpileResult.HlslSource,
                    transpileResult.EntryPoint,
                    $"videotoy-ps-{passName}",
                    "ps_5_0",
                    CompileFlags);

                var slot = new PassSlot
                {
                    Name = passName,
                    IsPingPong = selfReferencing.Contains(passName),
                    BufferBindings = bindings,
                    AssetBindings = assetBindings,
                    PixelShader = _sharedContext.Device.CreatePixelShader(pixelShaderBytecode.Span)
                };

                _orderedSlots.Add(slot);
                _slotsByName[passName] = slot;
            }
        }
        catch
        {
            // Une passe suivante qui échoue à compiler (HLSL malformé issu
            // du transpileur) ne doit pas laisser les ID3D11PixelShader déjà
            // créés pour les passes précédentes fuir jusqu'au prochain
            // chargement réussi : les libérer immédiatement ici plutôt que
            // d'attendre le prochain DisposeSlots() (ou Dispose() final).
            DisposeSlots();
            throw;
        }
    }

    /// <summary>
    /// Résout une ChannelSource en (index, chemin d'asset, nature, réglages
    /// d'échantillonnage) si elle référence bien une texture image/audio/
    /// vidéo effectivement chargée (présente dans <see cref="_images"/>/
    /// <see cref="_audioTracks"/>/<see cref="_videoSources"/>) ; <c>null</c>
    /// sinon (asset manquant, ex. fichier introuvable au chargement — voir
    /// ShaderFileService).
    /// </summary>
    private (int ChannelIndex, string AssetPath, AssetKind Kind, Core.ShaderModel.ChannelSamplerSettings Sampler)? ResolveAssetBinding(
        int channelIndex,
        Core.ShaderModel.ChannelSource channel)
    {
        var texturePath = Core.ShaderModel.channelTexturePath(channel);
        if (texturePath is not null && texturePath.Value is { } imagePath && _images.ContainsKey(imagePath))
        {
            return (channelIndex, imagePath, AssetKind.Image, channel.Sampler);
        }

        var audioPath = Core.ShaderModel.channelAudioPath(channel);
        if (audioPath is not null && audioPath.Value is { } spectrumPath && _audioTracks.ContainsKey(spectrumPath))
        {
            return (channelIndex, spectrumPath, AssetKind.AudioSpectrum, channel.Sampler);
        }

        var videoPath = Core.ShaderModel.channelVideoPath(channel);
        if (videoPath is not null && videoPath.Value is { } videoAssetPath && _videoSources.ContainsKey(videoAssetPath))
        {
            return (channelIndex, videoAssetPath, AssetKind.Video, channel.Sampler);
        }

        var cubemapPath = Core.ShaderModel.channelCubemapPath(channel);
        if (cubemapPath is not null && cubemapPath.Value is { } cubemapAssetPath && _cubemaps.ContainsKey(cubemapAssetPath))
        {
            return (channelIndex, cubemapAssetPath, AssetKind.Cubemap, channel.Sampler);
        }

        var volumePath = Core.ShaderModel.channelVolumePath(channel);
        if (volumePath is not null && volumePath.Value is { } volumeAssetPath && _volumes.ContainsKey(volumeAssetPath))
        {
            return (channelIndex, volumeAssetPath, AssetKind.Volume, channel.Sampler);
        }

        return null;
    }

    private static Filter ToD3DFilter(Core.ShaderModel.ChannelFilterMode filter)
    {
        if (filter.IsNearestFilter)
        {
            return Filter.MinMagMipPoint;
        }

        if (filter.IsMipmapFilter)
        {
            return Filter.MinMagMipLinear;
        }

        return Filter.MinMagLinearMipPoint;
    }

    private static TextureAddressMode ToD3DAddressMode(Core.ShaderModel.ChannelWrapMode wrap) =>
        wrap.IsClampWrap ? TextureAddressMode.Clamp : TextureAddressMode.Wrap;

    /// <summary>
    /// Renvoie (en le créant au besoin) le <see cref="ID3D11SamplerState"/>
    /// correspondant à <paramref name="settings"/> — un état distinct par
    /// combinaison filtre/mode d'adressage effectivement utilisée par un
    /// <c>iChannel</c> (Phase 3 du ROADMAP), mis en cache dans
    /// <see cref="_channelSamplers"/> pour toute la durée de vie du projet
    /// chargé. <c>Srgb</c> n'influence pas cet état (l'espace colorimétrique
    /// se règle au niveau du format de la texture/vue, pas de
    /// l'échantillonneur) — voir <see cref="CreateImageAsset"/>.
    /// </summary>
    private ID3D11SamplerState GetOrCreateSamplerState(Core.ShaderModel.ChannelSamplerSettings settings)
    {
        if (_channelSamplers.TryGetValue(settings, out var existing))
        {
            return existing;
        }

        var addressMode = ToD3DAddressMode(settings.Wrap);
        var description = new SamplerDescription
        {
            Filter = ToD3DFilter(settings.Filter),
            AddressU = addressMode,
            AddressV = addressMode,
            AddressW = addressMode,
            ComparisonFunc = ComparisonFunction.Never,
            MaxLOD = float.MaxValue
        };

        var sampler = _sharedContext.Device.CreateSamplerState(description);
        _channelSamplers[settings] = sampler;
        return sampler;
    }

    /// <summary>
    /// Crée les ressources GPU pour chaque asset externe effectivement
    /// référencé par au moins un channel du shader chargé, une seule fois
    /// par appel à <see cref="Initialize"/> : les textures image sont
    /// uploadées ici et jamais retouchées ensuite (contenu statique) ; les
    /// textures audio/vidéo sont créées vides ici (Dynamic) et remplies à
    /// chaque <see cref="RenderFrame"/> par <see cref="RefreshDynamicAssets"/>.
    /// </summary>
    private void InitializeBoundAssets()
    {
        foreach (var asset in _boundAssets.Values)
        {
            asset.View.Dispose();
            asset.Resource.Dispose();
        }

        _boundAssets.Clear();

        var referencedPaths = _orderedSlots
            .SelectMany(slot => slot.AssetBindings)
            .Select(binding => (binding.AssetPath, binding.Kind))
            .Distinct();

        foreach (var (assetPath, kind) in referencedPaths)
        {
            var boundAsset = kind switch
            {
                AssetKind.Image when _images.TryGetValue(assetPath, out var image) => CreateImageAsset(image),
                AssetKind.AudioSpectrum => CreateDynamicAsset(BoundAudioAsset.TextureWidth, BoundAudioAsset.TextureHeight, AssetKind.AudioSpectrum),
                AssetKind.Video => CreateDynamicAsset(_size.Width, _size.Height, AssetKind.Video),
                AssetKind.Cubemap when _cubemaps.TryGetValue(assetPath, out var cubemap) => CreateCubemapAsset(cubemap),
                AssetKind.Volume when _volumes.TryGetValue(assetPath, out var volume) => CreateVolumeAsset(volume),
                _ => null
            };

            if (boundAsset is not null)
            {
                _boundAssets[assetPath] = boundAsset;
            }
        }
    }

    private BoundAsset CreateImageAsset(BoundImageAsset image)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)image.Width,
            Height = (uint)image.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        var texture = _sharedContext.Device.CreateTexture2D(description);
        var rowPitch = (uint)(image.Width * 4);
        _sharedContext.ImmediateContext.UpdateSubresource(image.PixelsBgra, texture, 0, rowPitch);

        var view = _sharedContext.Device.CreateShaderResourceView(texture);
        return new BoundAsset(texture, view, AssetKind.Image, image.Width, image.Height);
    }

    private BoundAsset CreateDynamicAsset(int width, int height, AssetKind kind)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)Math.Max(1, width),
            Height = (uint)Math.Max(1, height),
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.None
        };

        var texture = _sharedContext.Device.CreateTexture2D(description);
        var view = _sharedContext.Device.CreateShaderResourceView(texture);
        return new BoundAsset(texture, view, kind, width, height);
    }

    /// <summary>
    /// Crée une <c>ID3D11Texture2D</c> à 6 sous-ressources (<c>ArraySize=6</c>,
    /// <c>MiscFlags.TextureCube</c>) — une cubemap D3D11 n'est pas un type de
    /// ressource séparé, seulement un <c>Texture2D</c> avec ce flag et une
    /// vue interprétée comme telle par <c>CreateShaderResourceView</c> (qui
    /// détecte automatiquement <c>TextureCube</c> depuis la description de la
    /// ressource, sans description de vue explicite nécessaire ici). Chaque
    /// face est uploadée comme sa propre sous-ressource (index = index de
    /// face, mip 0 pour chacune puisque <c>MipLevels=1</c>), dans l'ordre où
    /// <c>Videotoy.Core.ShaderModel.cubemapFacePaths</c>/<c>TextureLoader.LoadCubemap</c>
    /// les ont chargées (+X, -X, +Y, -Y, +Z, -Z).
    /// </summary>
    private BoundAsset CreateCubemapAsset(BoundCubemapAsset cubemap)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)cubemap.FaceWidth,
            Height = (uint)cubemap.FaceHeight,
            MipLevels = 1,
            ArraySize = 6,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.TextureCube
        };

        var texture = _sharedContext.Device.CreateTexture2D(description);
        var rowPitch = (uint)(cubemap.FaceWidth * 4);

        for (var faceIndex = 0; faceIndex < cubemap.FacesBgra.Length; faceIndex++)
        {
            _sharedContext.ImmediateContext.UpdateSubresource(cubemap.FacesBgra[faceIndex], texture, (uint)faceIndex, rowPitch);
        }

        var view = _sharedContext.Device.CreateShaderResourceView(texture);
        return new BoundAsset(texture, view, AssetKind.Cubemap, cubemap.FaceWidth, cubemap.FaceHeight);
    }

    /// <summary>
    /// Crée une <c>ID3D11Texture3D</c> à partir des tranches déjà ré-extraites
    /// par <c>TextureLoader.LoadVolume</c> — une seule sous-ressource
    /// (mip 0), <c>UpdateSubresource</c> attend alors le volume complet en un
    /// seul appel avec un pas de ligne (<paramref name="volume"/>.SliceWidth)
    /// et un pas de tranche (une tranche complète) explicites.
    /// </summary>
    private BoundAsset CreateVolumeAsset(BoundVolumeAsset volume)
    {
        var description = new Texture3DDescription
        {
            Width = (uint)volume.SliceWidth,
            Height = (uint)volume.SliceHeight,
            Depth = (uint)volume.SliceCount,
            MipLevels = 1,
            Format = Format.B8G8R8A8_UNorm,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        var texture = _sharedContext.Device.CreateTexture3D(description);
        var rowPitch = (uint)(volume.SliceWidth * 4);
        var slicePitch = rowPitch * (uint)volume.SliceHeight;
        _sharedContext.ImmediateContext.UpdateSubresource(volume.SlicesBgra, texture, 0, rowPitch, slicePitch);

        var view = _sharedContext.Device.CreateShaderResourceView(texture);
        return new BoundAsset(texture, view, AssetKind.Volume, volume.SliceWidth, volume.SliceHeight);
    }

    public void Resize(RenderTargetSize size)
    {
        _size = size;
        foreach (var slot in _orderedSlots)
        {
            slot.Resize(size);
        }
    }

    /// <summary>
    /// Rend une frame complète : exécute chaque buffer dans l'ordre de dépendance,
    /// puis la passe Image, et retourne les pixels RGBA de la passe Image uniquement.
    /// Chaque passe ping-pong échantillonne le résultat de la frame précédente
    /// (jamais celui en cours d'écriture) pour son propre feedback.
    /// </summary>
    public byte[] RenderFrame(double timeSeconds, double deltaSeconds, int frameIndex)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("MultiPassRenderer has not been initialized; call Initialize first.");
        }

        try
        {
            RefreshDynamicAssets(timeSeconds);

            foreach (var slot in _orderedSlots)
            {
                RenderSlot(slot, timeSeconds, deltaSeconds, frameIndex);
            }
        }
        catch (SharpGen.Runtime.SharpGenException ex)
        {
            // Couvre notamment un pilote qui plante, un timeout TDR, ou un
            // GPU débranché/changé en cours d'export : sans cette traduction,
            // l'appelant ne verrait qu'un HRESULT COM opaque plutôt qu'un
            // message actionnable (voir GpuDeviceLostException).
            throw new GpuDeviceLostException(
                $"The GPU rendering device failed while rendering frame {frameIndex} (driver crash, GPU removed/reset, or a timeout). Please retry the render.",
                ex);
        }

        foreach (var slot in _orderedSlots)
        {
            slot.SwapPingPong();
        }

        if (!_slotsByName.TryGetValue(FinalPassName, out var finalSlot))
        {
            return Array.Empty<byte>();
        }

        return finalSlot.Front.ReadPixelsRgba();
    }

    private void RenderSlot(PassSlot slot, double timeSeconds, double deltaSeconds, int frameIndex)
    {
        var target = slot.WriteTarget;

        UpdateUniforms(slot, timeSeconds, deltaSeconds, frameIndex);
        UpdateCustomUniforms();

        target.Clear(0f, 0f, 0f, 1f);
        target.BindRenderTarget();

        var context = target.ImmediateContext;
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetShader(slot.PixelShader);
        context.PSSetConstantBuffer(0, _uniformsBuffer);
        if (_customUniformsBuffer is not null)
        {
            context.PSSetConstantBuffer(1, _customUniformsBuffer);
        }
        context.PSSetSampler(0, _defaultSampler);

        foreach (var (channelIndex, bufferPassName) in slot.BufferBindings)
        {
            if (!_slotsByName.TryGetValue(bufferPassName, out var sourceSlot))
            {
                continue;
            }

            // La passe source n'a pas encore swap sa frame courante : Front porte
            // toujours le dernier résultat complet et stable de la frame précédente.
            context.PSSetShaderResource((uint)channelIndex, sourceSlot.Front.ShaderResourceView);
            context.PSSetSampler((uint)channelIndex, _defaultSampler);
        }

        foreach (var (channelIndex, assetPath, _, sampler) in slot.AssetBindings)
        {
            if (!_boundAssets.TryGetValue(assetPath, out var boundAsset))
            {
                continue;
            }

            context.PSSetShaderResource((uint)channelIndex, boundAsset.View);
            context.PSSetSampler((uint)channelIndex, GetOrCreateSamplerState(sampler));
        }

        context.Draw(3, 0);

        // Libère les slots de lecture pour éviter un conflit lecture/écriture
        // au tour suivant si ce même buffer redevient une cible de rendu.
        foreach (var (channelIndex, _) in slot.BufferBindings)
        {
            context.PSSetShaderResource((uint)channelIndex, null!);
        }

        foreach (var (channelIndex, _, _, _) in slot.AssetBindings)
        {
            context.PSSetShaderResource((uint)channelIndex, null!);
        }
    }

    /// <summary>
    /// Ré-échantillonne le contenu de chaque asset dynamique (spectre audio,
    /// frame vidéo) lié à au moins un channel, une seule fois par frame
    /// rendue (pas une fois par passe : un même asset peut être lié à
    /// plusieurs channels/passes, son contenu reste identique pour toute la
    /// frame). Les textures image statiques ne sont jamais retouchées ici.
    /// </summary>
    private void RefreshDynamicAssets(double timeSeconds)
    {
        var context = _sharedContext.ImmediateContext;

        foreach (var (assetPath, boundAsset) in _boundAssets)
        {
            byte[]? pixels = boundAsset.Kind switch
            {
                AssetKind.AudioSpectrum when _audioTracks.TryGetValue(assetPath, out var audio) =>
                    audio.GenerateSpectrumTextureBgra(timeSeconds),
                AssetKind.Video when _videoSources.TryGetValue(assetPath, out var video) =>
                    video.GetFramePixelsBgra(timeSeconds, _size.Width, _size.Height),
                _ => null
            };

            if (pixels is null)
            {
                continue;
            }

            var mapped = context.Map(boundAsset.Resource, 0, MapMode.WriteDiscard, MapFlags.None);

            try
            {
                unsafe
                {
                    fixed (byte* sourceBase = pixels)
                    {
                        var destinationBase = (byte*)mapped.DataPointer;
                        var rowSizeInBytes = pixels.Length / Math.Max(1, GetTextureHeight(boundAsset.Kind));

                        if (mapped.RowPitch == rowSizeInBytes)
                        {
                            Buffer.MemoryCopy(sourceBase, destinationBase, pixels.Length, pixels.Length);
                        }
                        else
                        {
                            var height = GetTextureHeight(boundAsset.Kind);
                            for (var row = 0; row < height; row++)
                            {
                                var sourceRow = sourceBase + (row * rowSizeInBytes);
                                var destinationRow = destinationBase + (row * mapped.RowPitch);
                                Buffer.MemoryCopy(sourceRow, destinationRow, rowSizeInBytes, rowSizeInBytes);
                            }
                        }
                    }
                }
            }
            finally
            {
                context.Unmap(boundAsset.Resource, 0);
            }
        }
    }

    private int GetTextureHeight(AssetKind kind) =>
        kind switch
        {
            AssetKind.AudioSpectrum => BoundAudioAsset.TextureHeight,
            AssetKind.Video => _size.Height,
            _ => _size.Height
        };

    /// <summary>
    /// Résout <c>iChannelResolutionN</c> pour chaque channel 0-3 de
    /// <paramref name="slot"/> : la résolution (largeur, hauteur, 1) de la
    /// texture effectivement liée (buffer d'une autre passe — toujours
    /// dimensionné à <see cref="_size"/> — ou asset image/vidéo/spectre
    /// audio, voir <see cref="BoundAsset"/>), ou zéro si aucune texture
    /// n'est liée à ce channel, conformément à la convention Shadertoy.
    /// </summary>
    private Vector4[] ResolveChannelResolutions(PassSlot slot)
    {
        var resolutions = new[] { Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero };

        foreach (var (channelIndex, _) in slot.BufferBindings)
        {
            if (channelIndex is >= 0 and < 4)
            {
                resolutions[channelIndex] = new Vector4(_size.Width, _size.Height, 1f, 0f);
            }
        }

        foreach (var (channelIndex, assetPath, _, _) in slot.AssetBindings)
        {
            if (channelIndex is >= 0 and < 4 && _boundAssets.TryGetValue(assetPath, out var boundAsset))
            {
                resolutions[channelIndex] = new Vector4(boundAsset.Width, boundAsset.Height, 1f, 0f);
            }
        }

        return resolutions;
    }

    /// <summary>
    /// Résout <c>iChannelTime[n]</c> pour chaque channel 0-3 de
    /// <paramref name="slot"/> : la position de lecture mappée
    /// (linéaire/bouclée/figée, voir <see cref="Core.VideoTimeMapping"/>)
    /// pour un channel vidéo, ou <paramref name="timeSeconds"/> (= <c>iTime</c>)
    /// pour tout autre type de channel (buffer, image, audio, ou aucun) —
    /// comportement par défaut documenté de Shadertoy pour les canaux non
    /// vidéo.
    /// </summary>
    private Vector4[] ResolveChannelTimes(PassSlot slot, double timeSeconds)
    {
        var times = new[]
        {
            new Vector4((float)timeSeconds, 0f, 0f, 0f),
            new Vector4((float)timeSeconds, 0f, 0f, 0f),
            new Vector4((float)timeSeconds, 0f, 0f, 0f),
            new Vector4((float)timeSeconds, 0f, 0f, 0f)
        };

        foreach (var (channelIndex, assetPath, kind, _) in slot.AssetBindings)
        {
            if (channelIndex is >= 0 and < 4 && kind == AssetKind.Video && _videoSources.TryGetValue(assetPath, out var video))
            {
                times[channelIndex] = new Vector4((float)video.ResolvePlaybackTimeSeconds(timeSeconds), 0f, 0f, 0f);
            }
        }

        return times;
    }

    /// <summary>
    /// Résout <c>iSampleRate</c> : le taux d'échantillonnage réel (NAudio) du
    /// premier <c>iChannel</c> audio effectivement lié dans <paramref name="slot"/>,
    /// ou 44100 Hz si le shader n'utilise aucune entrée audio (valeur par
    /// défaut Shadertoy documentée, jamais utilisée par un shader qui ne lit
    /// aucun `iChannel` audio). Shadertoy n'expose qu'un seul `iSampleRate`
    /// global, jamais par canal — contrairement à `iChannelResolution`/
    /// `iChannelTime` — donc le premier canal audio trouvé suffit.
    /// </summary>
    private float ResolveSampleRate(PassSlot slot)
    {
        foreach (var (_, assetPath, kind, _) in slot.AssetBindings)
        {
            if (kind == AssetKind.AudioSpectrum && _audioTracks.TryGetValue(assetPath, out var audio))
            {
                return audio.SampleRate;
            }
        }

        return 44100f;
    }

    private void UpdateUniforms(PassSlot slot, double timeSeconds, double deltaSeconds, int frameIndex)
    {
        var channelResolutions = ResolveChannelResolutions(slot);
        var channelTimes = ResolveChannelTimes(slot, timeSeconds);

        var uniforms = new ShadertoyUniformsBuffer
        {
            Resolution = new Vector3(_size.Width, _size.Height, 1f),
            Time = (float)timeSeconds,
            TimeDelta = (float)deltaSeconds,
            Frame = frameIndex,
            SampleRate = ResolveSampleRate(slot),
            // iFrameRate = 1/deltaSeconds : le timeline déterministe construit par
            // Core.LoopCalculator.buildFrameTimeline fixe justement deltaSeconds à
            // 1.0/frameRate.Value pour chaque frame, donc cette relation est exacte
            // ici (pas une approximation) — évite de propager un paramètre de FPS
            // séparé jusqu'à Initialize() pour un renderer qui ne connaît sinon que
            // des temps/deltas par frame, jamais le FPS nominal lui-même.
            FrameRate = deltaSeconds > 0.0 ? (float)(1.0 / deltaSeconds) : 0f,
            // iMouse et iDate restent volontairement figés à zéro : le pipeline
            // de rendu est déterministe (voir la doc du projet) et ne dépend
            // jamais de l'horloge murale ni d'une interaction souris en temps
            // réel — une valeur non nulle changerait le résultat d'un export
            // selon l'instant ou la machine, ce que ce renderer garantit
            // justement de ne jamais faire. Un shader Shadertoy qui teste
            // `iMouse.z > 0.0` se comporte donc comme si le bouton n'avait
            // jamais été pressé, ce qui est le seul état reproductible.
            Mouse = Vector4.Zero,
            Date = Vector4.Zero,
            ChannelResolution0 = channelResolutions[0],
            ChannelResolution1 = channelResolutions[1],
            ChannelResolution2 = channelResolutions[2],
            ChannelResolution3 = channelResolutions[3],
            ChannelTime0 = channelTimes[0],
            ChannelTime1 = channelTimes[1],
            ChannelTime2 = channelTimes[2],
            ChannelTime3 = channelTimes[3]
        };

        var context = _sharedContext.ImmediateContext;
        var mapped = context.Map(_uniformsBuffer!, 0, MapMode.WriteDiscard, MapFlags.None);

        unsafe
        {
            *(ShadertoyUniformsBuffer*)mapped.DataPointer = uniforms;
        }

        context.Unmap(_uniformsBuffer!, 0);
    }

    /// <summary>
    /// Recopie la valeur courante de chaque uniform custom (telle que pilotée
    /// en direct par <see cref="SetCustomUniformComponent"/>) dans le buffer
    /// constant `register(b1)`, un slot de 16 octets par uniform déclaré,
    /// dans le même ordre que <see cref="CustomUniformDeclarations"/> — donc
    /// le même ordre que la déclaration `cbuffer CustomUniforms` émise par le
    /// transpileur. No-op si le shader chargé n'expose aucun uniform custom.
    /// </summary>
    private void UpdateCustomUniforms()
    {
        if (_customUniformsBuffer is null || _customUniformDeclarations.Count == 0)
        {
            return;
        }

        var context = _sharedContext.ImmediateContext;
        var mapped = context.Map(_customUniformsBuffer, 0, MapMode.WriteDiscard, MapFlags.None);

        unsafe
        {
            var basePointer = (float*)mapped.DataPointer;
            var floatsPerSlot = CustomUniformSlotSizeInBytes / sizeof(float);

            for (var slotIndex = 0; slotIndex < _customUniformDeclarations.Count; slotIndex++)
            {
                var declaration = _customUniformDeclarations[slotIndex];
                var values = _customUniformValues[declaration.Name];
                var slotBase = basePointer + (slotIndex * floatsPerSlot);

                for (var component = 0; component < floatsPerSlot; component++)
                {
                    slotBase[component] = component < values.Length ? values[component] : 0f;
                }
            }
        }

        context.Unmap(_customUniformsBuffer, 0);
    }

    private void DisposeSlots()
    {
        foreach (var slot in _orderedSlots)
        {
            slot.Dispose();
        }

        _orderedSlots.Clear();
        _slotsByName.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeSlots();

        foreach (var asset in _boundAssets.Values)
        {
            asset.View.Dispose();
            asset.Resource.Dispose();
        }

        _boundAssets.Clear();

        _defaultSampler?.Dispose();
        foreach (var sampler in _channelSamplers.Values)
        {
            sampler.Dispose();
        }
        _channelSamplers.Clear();
        _uniformsBuffer?.Dispose();
        _customUniformsBuffer?.Dispose();
        _vertexShader?.Dispose();
        _sharedContext.Dispose();

        _disposed = true;
    }
}
