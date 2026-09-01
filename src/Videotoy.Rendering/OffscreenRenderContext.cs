using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Videotoy.Rendering;

public sealed class OffscreenRenderContext : IDisposable
{
    private static readonly FeatureLevel[] RequestedFeatureLevels =
    {
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0
    };

    private ID3D11Texture2D? _colorTexture;
    private ID3D11Texture2D? _stagingTexture;
    private ID3D11RenderTargetView? _renderTargetView;
    private ID3D11ShaderResourceView? _shaderResourceView;

    private bool _disposed;

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext ImmediateContext { get; }

    public RenderTargetSize Size { get; private set; }

    public ID3D11ShaderResourceView ShaderResourceView =>
        _shaderResourceView ?? throw new InvalidOperationException("The offscreen render context has not been sized yet; call Resize first.");

    private readonly bool _ownsDevice;

    /// <summary>
    /// Crée un contexte de rendu hors-écran avec son propre device D3D11
    /// (fallback WARP automatique en cas d'échec matériel).
    /// </summary>
    public OffscreenRenderContext()
    {
        var creationFlags = DeviceCreationFlags.None;
#if DEBUG
        creationFlags |= DeviceCreationFlags.Debug;
#endif

        var hardwareResult = D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            creationFlags,
            RequestedFeatureLevels,
            out ID3D11Device hardwareDevice,
            out ID3D11DeviceContext hardwareContext);

        if (hardwareResult.Failure)
        {
            hardwareDevice?.Dispose();
            hardwareContext?.Dispose();

            D3D11.D3D11CreateDevice(
                null,
                DriverType.Warp,
                creationFlags,
                RequestedFeatureLevels,
                out hardwareDevice,
                out hardwareContext).CheckError();
        }

        Device = hardwareDevice;
        ImmediateContext = hardwareContext;
        Size = new RenderTargetSize(0, 0);
        _ownsDevice = true;
    }

    /// <summary>
    /// Crée un contexte de rendu hors-écran réutilisant un device D3D11 existant.
    /// Nécessaire pour le multi-passes : toutes les render targets consommées
    /// entre elles via ShaderResourceView doivent partager le même device.
    /// </summary>
    public OffscreenRenderContext(ID3D11Device device, ID3D11DeviceContext immediateContext)
    {
        Device = device;
        ImmediateContext = immediateContext;
        Size = new RenderTargetSize(0, 0);
        _ownsDevice = false;
    }

    public void Resize(RenderTargetSize size)
    {
        if (!size.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Render target width and height must both be positive.");
        }

        if (size == Size && _renderTargetView is not null)
        {
            return;
        }

        ReleaseTargets();

        var colorDescription = new Texture2DDescription
        {
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        _colorTexture = Device.CreateTexture2D(colorDescription);
        _renderTargetView = Device.CreateRenderTargetView(_colorTexture);
        _shaderResourceView = Device.CreateShaderResourceView(_colorTexture);

        var stagingDescription = colorDescription with
        {
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read
        };

        _stagingTexture = Device.CreateTexture2D(stagingDescription);

        Size = size;
    }

    public void Clear(float red, float green, float blue, float alpha)
    {
        EnsureInitialized();
        ImmediateContext.ClearRenderTargetView(_renderTargetView!, new Color4(red, green, blue, alpha));
    }

    public void BindRenderTarget()
    {
        EnsureInitialized();

        ImmediateContext.OMSetRenderTargets(_renderTargetView!);
        ImmediateContext.RSSetViewport(new Viewport(0, 0, Size.Width, Size.Height, 0.0f, 1.0f));
    }

    /// <summary>
    /// Lit le contenu de la render target courante en pixels BGRA 8 bits par canal
    /// (format natif de la texture couleur, <c>B8G8R8A8_UNorm</c>), directement
    /// compatible avec <c>PixelFormats.Bgra32</c> côté WPF sans conversion — il n'y a
    /// donc aucune conversion de format de pixel à proprement parler dans ce
    /// pipeline, seulement une recopie mémoire depuis la texture de staging
    /// mappée. Copie en un seul bloc (<see cref="Buffer.MemoryCopy"/> sur
    /// toute la surface, déjà vectorisée en interne par le CLR) lorsque le
    /// pitch de ligne D3D11 ne comporte aucun padding (cas courant), ce qui
    /// est nettement plus rapide qu'une copie ligne par ligne sur les
    /// résolutions élevées ; repli sur la copie ligne par ligne uniquement
    /// lorsqu'un padding existe (largeurs non multiples de l'alignement
    /// attendu par le driver). Une implémentation SIMD manuelle
    /// (<c>System.Runtime.Intrinsics</c>) a été jugée inutile : il s'agit
    /// d'un <c>memcpy</c> pur, déjà optimal via l'intrinsèque du CLR.
    /// </summary>
    public byte[] ReadPixelsRgba()
    {
        EnsureInitialized();

        ImmediateContext.CopyResource(_stagingTexture!, _colorTexture!);

        var mapped = ImmediateContext.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        try
        {
            var rowSizeInBytes = Size.Width * 4;
            var totalSizeInBytes = rowSizeInBytes * Size.Height;
            var pixels = new byte[totalSizeInBytes];

            unsafe
            {
                var sourceBase = (byte*)mapped.DataPointer;
                fixed (byte* destinationBase = pixels)
                {
                    if (mapped.RowPitch == rowSizeInBytes)
                    {
                        Buffer.MemoryCopy(sourceBase, destinationBase, totalSizeInBytes, totalSizeInBytes);
                    }
                    else
                    {
                        for (var row = 0; row < Size.Height; row++)
                        {
                            var sourceRow = sourceBase + (row * mapped.RowPitch);
                            var destinationRow = destinationBase + (row * rowSizeInBytes);
                            Buffer.MemoryCopy(sourceRow, destinationRow, rowSizeInBytes, rowSizeInBytes);
                        }
                    }
                }
            }

            return pixels;
        }
        finally
        {
            ImmediateContext.Unmap(_stagingTexture!, 0);
        }
    }

    private void EnsureInitialized()
    {
        if (_renderTargetView is null)
        {
            throw new InvalidOperationException("The offscreen render context has not been sized yet; call Resize first.");
        }
    }

    private void ReleaseTargets()
    {
        _shaderResourceView?.Dispose();
        _shaderResourceView = null;

        _renderTargetView?.Dispose();
        _renderTargetView = null;

        _colorTexture?.Dispose();
        _colorTexture = null;

        _stagingTexture?.Dispose();
        _stagingTexture = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseTargets();

        if (_ownsDevice)
        {
            ImmediateContext.Dispose();
            Device.Dispose();
        }

        _disposed = true;
    }
}
