using Videotoy.Core.Domain;

namespace Videotoy.Rendering;

public sealed class FrameSequenceRenderer
{
    private readonly IShaderRenderer _renderer;

    public FrameSequenceRenderer(IShaderRenderer renderer)
    {
        _renderer = renderer;
    }

    public IEnumerable<RenderedFrame> RenderSequence(
        DurationMode durationMode,
        FrameRate frameRate,
        CancellationToken cancellationToken = default)
    {
        var frameCount = Core.LoopCalculator.computeFrameCount(durationMode, frameRate);
        var timeline = Core.LoopCalculator.buildFrameTimeline(frameCount.FrameCount, frameRate);

        foreach (var frame in timeline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pixels = _renderer.RenderFrame(frame.TimeSeconds, frame.DeltaSeconds, frame.Index);

            yield return new RenderedFrame(frame.Index, frame.TimeSeconds, pixels);
        }
    }
}
