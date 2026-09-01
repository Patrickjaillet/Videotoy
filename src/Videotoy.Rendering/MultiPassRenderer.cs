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
    /// comment son contenu GPU est rafraîchi — <see cref="Image"/> est
    /// uploadée une seule fois à <see cref="Initialize"/>, <see cref="AudioSpectrum"/>
    /// et <see cref="Video"/> sont ré-uploadées à chaque <see cref="RenderFrame"/>.
    /// </summary>
    private enum AssetKind
    {
        Image,
        AudioSpectrum,
        Video
    }

    private sealed record BoundAsset(ID3D11Texture2D Texture, ID3D11ShaderResourceView View, AssetKind Kind);

    private sealed class PassSlot : IDisposable
    {
        public required string Name { get; init; }
        public required bool IsPingPong { get; init; }
        public required (int ChannelIndex, string BufferPassName)[] BufferBindings { get; init; }
        public required (int ChannelIndex, string AssetPath, AssetKind Kind)[] AssetBindings { get; init; }

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

    private ID3D11Buffer? _customUniformsBuffer;
    private IReadOnlyList<Core.CustomUniformParser.CustomUniformDeclaration> _customUniformDeclarations =
        Array.Empty<Core.CustomUniformParser.CustomUniformDeclaration>();
    private readonly Dictionary<string, float[]> _customUniformValues = new();

    private readonly Dictionary<string, BoundAsset> _boundAssets = new();
    private IReadOnlyDictionary<string, BoundImageAsset> _images = new Dictionary<string, BoundImageAsset>();
    private IReadOnlyDictionary<string, BoundAudioAsset> _audioTracks = new Dictionary<string, BoundAudioAsset>();
    private IReadOnlyDictionary<string, BoundVideoAsset> _videoSources = new Dictionary<string, BoundVideoAsset>();

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
        IReadOnlyDictionary<string, Core.GlslToHlslTranspiler.TranspileResult> hlslPasses,
        IReadOnlyDictionary<string, BoundImageAsset>? images = null,
        IReadOnlyDictionary<string, BoundAudioAsset>? audioTracks = null,
        IReadOnlyDictionary<string, BoundVideoAsset>? videoSources = null)
    {
        _size = size;
        _images = images ?? new Dictionary<string, BoundImageAsset>();
        _audioTracks = audioTracks ?? new Dictionary<string, BoundAudioAsset>();
        _videoSources = videoSources ?? new Dictionary<string, BoundVideoAsset>();

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
        IReadOnlyDictionary<string, Core.GlslToHlslTranspiler.TranspileResult> hlslPasses)
    {
        _customUniformsBuffer?.Dispose();
        _customUniformsBuffer = null;
        _customUniformValues.Clear();

        var declarations = Core.GlslToHlslTranspiler.projectCustomUniformsOf(hlslPasses.Values)
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
        IReadOnlyDictionary<string, Core.GlslToHlslTranspiler.TranspileResult> hlslPasses)
    {
        DisposeSlots();

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

            var pixelShaderBytecode = Compiler.Compile(
                transpileResult.HlslSource,
                "PSMain",
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

    /// <summary>
    /// Résout une ChannelSource en (index, chemin d'asset, nature) si elle
    /// référence bien une texture image/audio/vidéo effectivement chargée
    /// (présente dans <see cref="_images"/>/<see cref="_audioTracks"/>/
    /// <see cref="_videoSources"/>) ; <c>null</c> sinon (asset manquant,
    /// ex. fichier introuvable au chargement — voir ShaderFileService).
    /// </summary>
    private (int ChannelIndex, string AssetPath, AssetKind Kind)? ResolveAssetBinding(
        int channelIndex,
        Core.ShaderModel.ChannelSource channel)
    {
        var texturePath = Core.ShaderModel.channelTexturePath(channel);
        if (texturePath is not null && texturePath.Value is { } imagePath && _images.ContainsKey(imagePath))
        {
            return (channelIndex, imagePath, AssetKind.Image);
        }

        var audioPath = Core.ShaderModel.channelAudioPath(channel);
        if (audioPath is not null && audioPath.Value is { } spectrumPath && _audioTracks.ContainsKey(spectrumPath))
        {
            return (channelIndex, spectrumPath, AssetKind.AudioSpectrum);
        }

        var videoPath = Core.ShaderModel.channelVideoPath(channel);
        if (videoPath is not null && videoPath.Value is { } videoAssetPath && _videoSources.ContainsKey(videoAssetPath))
        {
            return (channelIndex, videoAssetPath, AssetKind.Video);
        }

        return null;
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
            asset.Texture.Dispose();
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
        return new BoundAsset(texture, view, AssetKind.Image);
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
        return new BoundAsset(texture, view, kind);
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

        RefreshDynamicAssets(timeSeconds);

        foreach (var slot in _orderedSlots)
        {
            RenderSlot(slot, timeSeconds, deltaSeconds, frameIndex);
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

        UpdateUniforms(timeSeconds, deltaSeconds, frameIndex);
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

        foreach (var (channelIndex, assetPath, _) in slot.AssetBindings)
        {
            if (!_boundAssets.TryGetValue(assetPath, out var boundAsset))
            {
                continue;
            }

            context.PSSetShaderResource((uint)channelIndex, boundAsset.View);
            context.PSSetSampler((uint)channelIndex, _defaultSampler);
        }

        context.Draw(3, 0);

        // Libère les slots de lecture pour éviter un conflit lecture/écriture
        // au tour suivant si ce même buffer redevient une cible de rendu.
        foreach (var (channelIndex, _) in slot.BufferBindings)
        {
            context.PSSetShaderResource((uint)channelIndex, null!);
        }

        foreach (var (channelIndex, _, _) in slot.AssetBindings)
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

            var mapped = context.Map(boundAsset.Texture, 0, MapMode.WriteDiscard, MapFlags.None);

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
                context.Unmap(boundAsset.Texture, 0);
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

    private void UpdateUniforms(double timeSeconds, double deltaSeconds, int frameIndex)
    {
        var uniforms = new ShadertoyUniformsBuffer
        {
            Resolution = new Vector3(_size.Width, _size.Height, 1f),
            Time = (float)timeSeconds,
            TimeDelta = (float)deltaSeconds,
            Frame = frameIndex,
            SampleRate = 44100f,
            Padding0 = 0f,
            Mouse = Vector4.Zero,
            Date = Vector4.Zero,
            ChannelResolution0 = Vector4.Zero,
            ChannelResolution1 = Vector4.Zero,
            ChannelResolution2 = Vector4.Zero,
            ChannelResolution3 = Vector4.Zero
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
            asset.Texture.Dispose();
        }

        _boundAssets.Clear();

        _defaultSampler?.Dispose();
        _uniformsBuffer?.Dispose();
        _customUniformsBuffer?.Dispose();
        _vertexShader?.Dispose();
        _sharedContext.Dispose();

        _disposed = true;
    }
}
