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
            var channels = new[] { pass.Channel0, pass.Channel1, pass.Channel2, pass.Channel3 };
            hlslSource = HlslBoilerplate.prependBoilerplate(customUniformDeclarations, channels, normalizedBody);
        }

        return new ShaderTranspiler.TranspileResult(
            hlslSource,
            "PSMain",
            ListModule.OfSeq(diagnostics),
            customUniformDeclarations);
    }

    /// <summary>
    /// Tint traduit l'attribut d'entrée <c>@fragment fn &lt;nom&gt;(...)</c>
    /// WGSL vers un point d'entrée HLSL nommé d'après ce même <c>&lt;nom&gt;</c>
    /// (jamais <c>PSMain</c>), avec des structs d'E/S séparés générés par
    /// Tint sous les noms <c>&lt;nom&gt;_inputs</c>/<c>&lt;nom&gt;_outputs</c>
    /// — vérifié contre une compilation réelle du binaire vendu (une
    /// hypothèse antérieure de ce fichier supposait à tort une signature
    /// directement annotée <c>: SV_Target</c> sur la fonction, jamais générée
    /// par les versions actuelles de Tint qui passent systématiquement par
    /// ces structs). Exemple observé pour <c>@fragment fn mainImage(...)</c> :
    /// <code>
    /// struct mainImage_outputs { float4 tint_symbol : SV_Target0; };
    /// struct mainImage_inputs { float4 fragCoord : SV_Position; };
    /// mainImage_outputs mainImage(mainImage_inputs inputs) { ... }
    /// </code>
    /// Cette passe renomme le point d'entrée et ses deux structs associés en
    /// <c>PSMain</c>/<c>PSMain_inputs</c>/<c>PSMain_outputs</c> pour rester
    /// cohérente avec les autres chemins de langage, sans quoi
    /// <c>MultiPassRenderer</c> ne retrouverait pas le point d'entrée attendu
    /// (il compile toujours littéralement <c>"PSMain"</c>, voir
    /// <see cref="TranspilePassAsync"/>).
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
    /// Repère le point d'entrée fragment généré par Tint via son struct de
    /// sortie <c>&lt;nom&gt;_outputs</c> (contenant un champ annoté
    /// <c>SV_Target0</c>, <c>SV_Target1</c>, etc.) plutôt que via une
    /// annotation <c>: SV_Target</c> directement sur une fonction — cette
    /// dernière forme n'a jamais été observée en sortie réelle de Tint.
    /// </summary>
    private static readonly Regex FragmentEntryPointOutputRegex =
        new(@"struct\s+(\w+)_outputs\s*\{[^}]*SV_Target\d*", RegexOptions.Compiled);
}
