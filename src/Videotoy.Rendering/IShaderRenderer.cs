namespace Videotoy.Rendering;

public interface IShaderRenderer : IDisposable
{
    void Initialize(RenderTargetSize size);

    void Resize(RenderTargetSize size);

    byte[] RenderFrame(double timeSeconds, double deltaSeconds, int frameIndex);

    void LoadShader(string hlslSource);
}
