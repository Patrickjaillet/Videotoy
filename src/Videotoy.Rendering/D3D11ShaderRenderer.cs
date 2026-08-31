using System.Numerics;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace Videotoy.Rendering;

public sealed class D3D11ShaderRenderer : IShaderRenderer
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

    private readonly OffscreenRenderContext _context = new();

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11Buffer? _uniformsBuffer;
    private ID3D11SamplerState? _defaultSampler;

    private RenderTargetSize _size;
    private bool _disposed;

    public void Initialize(RenderTargetSize size)
    {
        _size = size;
        _context.Resize(size);

        var vertexShaderBytecode = Compiler.Compile(
            FullscreenTriangleVertexShaderSource,
            "VSMain",
            "videotoy-vs",
            "vs_5_0",
            CompileFlags);

        _vertexShader = _context.Device.CreateVertexShader(vertexShaderBytecode.Span);

        var uniformsBufferDescription = new BufferDescription
        {
            ByteWidth = ShadertoyUniformsBuffer.SizeInBytes,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        };

        _uniformsBuffer = _context.Device.CreateBuffer(uniformsBufferDescription);

        var samplerDescription = new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            ComparisonFunc = ComparisonFunction.Never,
            MaxLOD = float.MaxValue
        };

        _defaultSampler = _context.Device.CreateSamplerState(samplerDescription);
    }

    public void Resize(RenderTargetSize size)
    {
        _size = size;
        _context.Resize(size);
    }

    public void LoadShader(string hlslSource)
    {
        var pixelShaderBytecode = Compiler.Compile(
            hlslSource,
            "PSMain",
            "videotoy-ps",
            "ps_5_0",
            CompileFlags);

        _pixelShader?.Dispose();
        _pixelShader = _context.Device.CreatePixelShader(pixelShaderBytecode.Span);
    }

    public byte[] RenderFrame(double timeSeconds, double deltaSeconds, int frameIndex)
    {
        if (_pixelShader is null || _vertexShader is null || _uniformsBuffer is null)
        {
            throw new InvalidOperationException("No shader has been loaded; call LoadShader before RenderFrame.");
        }

        UpdateUniforms(timeSeconds, deltaSeconds, frameIndex);

        _context.Clear(0f, 0f, 0f, 1f);
        _context.BindRenderTarget();

        var context = _context.ImmediateContext;
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _uniformsBuffer);
        context.PSSetSampler(0, _defaultSampler);
        context.Draw(3, 0);

        return _context.ReadPixelsRgba();
    }

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

        var context = _context.ImmediateContext;
        var mapped = context.Map(_uniformsBuffer!, 0, MapMode.WriteDiscard, MapFlags.None);

        unsafe
        {
            *(ShadertoyUniformsBuffer*)mapped.DataPointer = uniforms;
        }

        context.Unmap(_uniformsBuffer!, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _defaultSampler?.Dispose();
        _uniformsBuffer?.Dispose();
        _pixelShader?.Dispose();
        _vertexShader?.Dispose();
        _context.Dispose();

        _disposed = true;
    }
}
