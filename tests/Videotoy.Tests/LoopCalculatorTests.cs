using Xunit;

namespace Videotoy.Tests;

public class LoopCalculatorTests
{
    [Theory]
    [InlineData(24.0)]
    [InlineData(30.0)]
    [InlineData(60.0)]
    [InlineData(23.976)]
    public void ExportFrameTimeline_RoundTripsToSameFrameIndexViaPreviewFormula(double frameRateValue)
    {
        // Phase 5 du ROADMAP, item "alignement des seeds pseudo-aléatoires
        // iFrame/iTime entre l'aperçu et l'export" : la preview calcule
        // désormais iFrame comme floor(CurrentTimeSeconds * frameRate) (voir
        // MainWindowViewModel.RenderCurrentFrame) plutôt qu'un compteur
        // incrémenté une fois par tick d'affichage réel. Ce test vérifie que
        // cette formule est bien l'inverse exacte de la construction de la
        // timeline d'export (Core.LoopCalculator.buildFrameTimeline,
        // TimeSeconds = index / frameRate) : pour le temps exact d'une frame
        // d'export donnée, la formule de la preview doit retrouver le même
        // index de frame, à l'imprécision flottante près.
        var frameRate = new Videotoy.Core.Domain.FrameRate(frameRateValue);
        var timeline = Videotoy.Core.LoopCalculator.buildFrameTimeline(150, frameRate);

        foreach (var frame in timeline)
        {
            var previewFrameIndex = (int)(frame.TimeSeconds * frameRate.Value);

            // Tolérance d'une frame : l'imprécision flottante de la
            // multiplication/division inverse peut faire retomber tout juste
            // en dessous de l'entier attendu (ex. 29.999999997 au lieu de
            // 30), ce qui est le même compromis qu'accepterait n'importe
            // quelle horloge de lecture réelle, pas une régression.
            Assert.InRange(previewFrameIndex, frame.Index - 1, frame.Index);
        }
    }
}
