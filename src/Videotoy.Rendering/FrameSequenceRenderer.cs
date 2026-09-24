using Videotoy.Core.Domain;

namespace Videotoy.Rendering;

public sealed class FrameSequenceRenderer
{
    private readonly MultiPassRenderer _renderer;

    public FrameSequenceRenderer(MultiPassRenderer renderer)
    {
        _renderer = renderer;
    }

    /// <summary>
    /// Rend chaque frame de la timeline l'une après l'autre : chaque frame
    /// (rendu D3D11 + lecture des pixels, synchrone et coûteux en CPU/GPU)
    /// est calculée via <see cref="Task.Run(Action, CancellationToken)"/>,
    /// donc jamais exécutée en ligne sur le thread appelant. Garantit que la
    /// boucle de rendu ne bloque jamais le thread UI, même quand un appel
    /// <c>await</c> voisin (écriture FFmpeg, résolution d'encodeur déjà en
    /// cache...) se termine de façon synchrone et ne cède donc pas la main
    /// de lui-même.
    /// </summary>
    public async IAsyncEnumerable<RenderedFrame> RenderSequence(
        DurationMode durationMode,
        FrameRate frameRate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var frameCount = Core.LoopCalculator.computeFrameCount(durationMode, frameRate);
        var timeline = Core.LoopCalculator.buildFrameTimeline(frameCount.FrameCount, frameRate);

        foreach (var frame in timeline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pixels = await Task.Run(
                () => _renderer.RenderFrame(frame.TimeSeconds, frame.DeltaSeconds, frame.Index),
                cancellationToken).ConfigureAwait(false);

            yield return new RenderedFrame(frame.Index, frame.TimeSeconds, pixels);
        }
    }
}
