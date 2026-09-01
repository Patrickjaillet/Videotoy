using Videotoy.Core;
using Videotoy.Media;
using Videotoy.Transpiler;

namespace Videotoy.App;

/// <summary>
/// Implémentation de <see cref="IShaderTranspilerRouter"/> câblée depuis la
/// racine de composition (<c>App.xaml.cs</c>), seul endroit où
/// <c>Videotoy.Transpiler</c> (le projet portant l'implémentation WGSL,
/// impure) est visible aux côtés des implémentations GLSL/HLSL natif
/// (pures, F#, dans <c>Videotoy.Core</c>) — voir <see cref="IShaderTranspilerRouter"/>
/// pour la justification de cette séparation. Dispatch simplement sur
/// <c>ShaderProject.SourceLanguage</c> (un seul langage par projet — voir
/// Phase v1.7.0 du roadmap).
/// </summary>
public sealed class ShaderTranspilerRouter : IShaderTranspilerRouter
{
    private readonly WgslToHlslTranspiler _wgslTranspiler;

    public ShaderTranspilerRouter(WgslToHlslTranspiler wgslTranspiler)
    {
        _wgslTranspiler = wgslTranspiler;
    }

    public async Task<IReadOnlyDictionary<string, ShaderTranspiler.TranspileResult>> TranspileProjectAsync(
        ShaderModel.ShaderProject project,
        CancellationToken cancellationToken)
    {
        return ShaderModel.languageKey(project.SourceLanguage) switch
        {
            "Glsl" => ToReadOnlyDictionary(GlslToHlslTranspiler.transpileProject(project)),
            "Hlsl" => ToReadOnlyDictionary(HlslNativeTranspiler.transpileProject(project)),
            "Wgsl" => await TranspileWgslProjectAsync(project, cancellationToken).ConfigureAwait(false),
            var key => throw new NotSupportedException($"Unsupported shader source language: {key}.")
        };
    }

    /// <summary>
    /// L'absence/corruption de <c>tint.exe</c> ne doit jamais faire planter
    /// l'application (le support WGSL est optionnel, contrairement à
    /// FFmpeg) : convertit <see cref="TintNotAvailableException"/>/
    /// <see cref="TintIntegrityException"/> en une <c>ShaderIssue</c>
    /// d'erreur par passe plutôt que de laisser l'exception se propager.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ShaderTranspiler.TranspileResult>> TranspileWgslProjectAsync(
        ShaderModel.ShaderProject project,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, ShaderTranspiler.TranspileResult>();

        foreach (var pass in ShaderModel.allPasses(project))
        {
            try
            {
                var commonCode = project.CommonCode is not null && Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(project.CommonCode)
                    ? project.CommonCode.Value
                    : null;

                results[pass.Name] = await _wgslTranspiler
                    .TranspilePassAsync(commonCode, pass, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TintNotAvailableException or TintIntegrityException)
            {
                results[pass.Name] = new ShaderTranspiler.TranspileResult(
                    string.Empty,
                    "PSMain",
                    Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { ShaderModel.errorIssue(pass.Name, 1, ex.Message) }),
                    Microsoft.FSharp.Collections.ListModule.Empty<CustomUniformParser.CustomUniformDeclaration>());
            }
        }

        return results;
    }

    private static IReadOnlyDictionary<string, ShaderTranspiler.TranspileResult> ToReadOnlyDictionary(
        Microsoft.FSharp.Collections.FSharpMap<string, ShaderTranspiler.TranspileResult> map)
    {
        return Microsoft.FSharp.Collections.MapModule.ToSeq(map)
            .ToDictionary(pair => pair.Item1, pair => pair.Item2);
    }
}
