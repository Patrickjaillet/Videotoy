using System.Text.RegularExpressions;
using Microsoft.FSharp.Collections;
using Videotoy.Core;

namespace Videotoy.Transpiler;

/// <summary>
/// Seule implémentation impure du contrat <c>IShaderTranspiler</c> (voir
/// <c>Videotoy.Core.ShaderTranspiler</c>) : transpile WGSL vers HLSL en
/// invoquant le binaire vendu <c>tint.exe</c> via <see cref="WgslTranspilerProcess"/>,
/// puis normalise et enveloppe la sortie exactement comme les transpileurs
/// GLSL/HLSL natif (plomberie <c>HlslBoilerplate</c> partagée, point
/// d'entrée renommé <c>PSMain</c>). Les uniforms custom sont extraits de la
/// source WGSL BRUTE, avant toute transformation — même contrainte
/// d'ordre que <c>GlslToHlslTranspiler</c>, puisque <c>//</c> est aussi un
/// commentaire de ligne valide en WGSL.
/// </summary>
public sealed class WgslToHlslTranspiler
{
    private static readonly Regex FragmentEntryPointRegex = new(@"@fragment\s+fn\s+(\w+)\s*\(", RegexOptions.Compiled);

    private readonly WgslTranspilerProcess _process;
    private readonly TintIntegrityVerifier _integrityVerifier;
    private bool _integrityVerified;

    public WgslToHlslTranspiler(WgslTranspilerProcess process, TintIntegrityVerifier integrityVerifier)
    {
        _process = process;
        _integrityVerifier = integrityVerifier;
    }

    public async Task<ShaderTranspiler.TranspileResult> TranspilePassAsync(
        string? commonCode,
        ShaderModel.ShaderPass pass,
        CancellationToken cancellationToken)
    {
        if (!_integrityVerified)
        {
            _integrityVerifier.VerifyOrThrow();
            _integrityVerified = true;
        }

        var rawSource = commonCode is not null
            ? commonCode + "\n" + pass.SourceCode
            : pass.SourceCode;

        var customUniformDeclarations = CustomUniformParser.parseDeclarations(pass.Name, rawSource);

        var result = await _process.InvokeAsync(rawSource, cancellationToken).ConfigureAwait(false);

        var diagnostics = new List<ShaderModel.ShaderIssue>();
        string hlslSource;

        if (!result.Succeeded)
        {
            diagnostics.Add(ShaderModel.errorIssue(pass.Name, 1, $"WGSL transpilation failed: {result.ErrorMessage}"));
            hlslSource = string.Empty;
        }
        else
        {
            var normalizedBody = NormalizeEntryPoint(result.HlslSource);
            hlslSource = HlslBoilerplate.prependBoilerplate(customUniformDeclarations, normalizedBody);
        }

        return new ShaderTranspiler.TranspileResult(
            hlslSource,
            "PSMain",
            ListModule.OfSeq(diagnostics),
            customUniformDeclarations);
    }

    /// <summary>
    /// Tint traduit l'attribut d'entrée <c>@fragment fn &lt;nom&gt;(...)</c>
    /// WGSL vers un nom de fonction HLSL arbitraire (dépendant de Tint, pas
    /// nécessairement <c>PSMain</c>) — cette passe renomme la fonction
    /// repérée en <c>PSMain</c> pour rester cohérente avec les autres
    /// chemins de langage, sans quoi <c>MultiPassRenderer</c> ne
    /// retrouverait pas le point d'entrée attendu.
    /// </summary>
    private static string NormalizeEntryPoint(string hlslSource)
    {
        var match = FragmentEntryPointOutputRegex.Match(hlslSource);
        if (!match.Success)
        {
            return hlslSource;
        }

        var originalName = match.Groups[1].Value;
        if (originalName == "PSMain")
        {
            return hlslSource;
        }

        return Regex.Replace(hlslSource, $@"\b{Regex.Escape(originalName)}\b", "PSMain");
    }

    /// <summary>
    /// Signature de fonction attendue en sortie HLSL de Tint pour un point
    /// d'entrée fragment : <c>&lt;returnType&gt; &lt;name&gt;(...) : SV_Target</c>.
    /// Marquée à vérifier/ajuster une fois la sortie réelle de Tint
    /// observée (même posture que <see cref="WgslTranspilerProcess"/>).
    /// </summary>
    private static readonly Regex FragmentEntryPointOutputRegex =
        new(@"\w+\s+(\w+)\s*\([^)]*\)\s*:\s*SV_Target", RegexOptions.Compiled);
}
