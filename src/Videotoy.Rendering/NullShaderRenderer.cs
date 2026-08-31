namespace Videotoy.Rendering;

public sealed class NullShaderRenderer : IShaderRenderer
{
    private RenderTargetSize _size;

    public void Initialize(RenderTargetSize size)
    {
        _size = size;
    }

    public void Resize(RenderTargetSize size)
    {
        _size = size;
    }

    public void LoadShader(string hlslSource)
    {
    }

    public byte[] RenderFrame(double timeSeconds, double deltaSeconds, int frameIndex)
    {
        return new byte[_size.Width * _size.Height * 4];
    }

    public void Dispose()
    {
    }
}
