namespace Videotoy.Media;

/// <summary>
/// Route la transpilation d'un projet shader vers l'implémentation
/// correspondant à <c>ShaderProject.SourceLanguage</c> (GLSL, HLSL natif ou
/// WGSL). Défini dans <c>Videotoy.Media</c> (pas <c>Videotoy.Core</c>, ni
/// implémenté ici) car les implémentations GLSL/HLSL natif vivent en F#
/// pur dans Core, tandis que l'implémentation WGSL a besoin du nouveau
/// projet <c>Videotoy.Transpiler</c> (E/S de processus) — <c>Videotoy.Media</c>
/// ne peut référencer ce dernier sans inverser le sens de dépendance
/// (App → {Rendering, Ffmpeg, Media, Transpiler} → Core). L'implémentation
/// concrète est donc câblée depuis la racine de composition
/// (<c>Videotoy.App/App.xaml.cs</c>), où tous les projets frères sont
/// visibles.
/// </summary>
public interface IShaderTranspilerRouter
{
    Task<IReadOnlyDictionary<string, Videotoy.Core.ShaderTranspiler.TranspileResult>> TranspileProjectAsync(
        Videotoy.Core.ShaderModel.ShaderProject project,
        CancellationToken cancellationToken);
}
