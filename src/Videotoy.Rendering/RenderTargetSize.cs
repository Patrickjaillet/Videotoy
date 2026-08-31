namespace Videotoy.Rendering;

public readonly record struct RenderTargetSize(int Width, int Height)
{
    public static readonly RenderTargetSize PreviewDefault = new(800, 450);

    public bool IsValid => Width > 0 && Height > 0;
}
