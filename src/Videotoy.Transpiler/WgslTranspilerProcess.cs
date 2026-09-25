using System.Diagnostics;
using System.IO;

namespace Videotoy.Transpiler;

public sealed record WgslProcessResult(bool Succeeded, string HlslSource, string ErrorMessage);

/// <summary>
/// Seule classe construisant la ligne de commande <c>tint.exe</c> : isolée
/// délibérément (voir Phase v1.7.0 du roadmap et le plan d'implémentation)
/// car l'invocation exacte de Tint (flags, format d'E/S) n'a pas pu être
/// vérifiée en ligne au moment d'écrire ce code — une hypothèse incorrecte
/// ne nécessite alors de corriger qu'un seul fichier. Utilise des fichiers
/// temporaires pour l'entrée et la sortie plutôt qu'un pipe stdin/stdout :
/// contrairement à FFmpeg (streaming continu de frames), il s'agit d'une
/// transformation ponctuelle d'un document entier, et les outils CLI de
/// transpilation shader exposent typiquement un chemin de fichier en
/// argument plutôt qu'un flux — les fichiers temporaires évitent de
/// présumer un support stdin/stdout par l'outil vendu.
/// </summary>
public sealed class WgslTranspilerProcess
{
    private readonly TintLocator _locator;

    public WgslTranspilerProcess(TintLocator locator)
    {
        _locator = locator;
    }

    public async Task<WgslProcessResult> InvokeAsync(string wgslSource, CancellationToken cancellationToken)
    {
        var inputPath = Path.Combine(Path.GetTempPath(), $"videotoy-wgsl-{Guid.NewGuid():N}.wgsl");
        var outputPath = Path.Combine(Path.GetTempPath(), $"videotoy-wgsl-{Guid.NewGuid():N}.hlsl");

        try
        {
            await File.WriteAllTextAsync(inputPath, wgslSource, cancellationToken).ConfigureAwait(false);

            var startInfo = new ProcessStartInfo
            {
                FileName = _locator.ResolveExecutablePath(),
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Invocation `<fichier d'entrée> --format hlsl -o <fichier de
            // sortie>` : vérifiée directement contre le code source du CLI
            // Tint (dawn/src/tint/cmd/tint/main.cc, tel que vendored dans ce
            // dépôt) plutôt que déduite de la documentation seule — usage
            // exact `tint [options] <input-file>` (fichier d'entrée
            // positionnel, jamais de sous-commande), option `--format`
            // (raccourci `-f`) et option `--output-name` (raccourci `-o`,
            // qui accepte un chemin de sortie pris tel quel, sans extension
            // ajoutée). Si `--format` est omis, Tint déduit le format de
            // l'extension du fichier de sortie et retombe sinon sur
            // `spvasm` — on le passe donc toujours explicitement pour ne
            // jamais dépendre de cette déduction. Reste néanmoins à
            // confirmer que le binaire réellement embarqué dans
            // `tools/tint/tint.exe` a été compilé avec `TINT_BUILD_HLSL_WRITER`
            // activé (sinon `--format hlsl` échoue avec « HLSL writer not
            // enabled in tint build », message identifiable dans stderr) :
            // c'est une option de compilation, pas de la CLI, donc non
            // vérifiable depuis le seul code source.
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("hlsl");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputPath);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return new WgslProcessResult(false, string.Empty, $"tint.exe exited with code {process.ExitCode}: {stderr}");
            }

            if (!File.Exists(outputPath))
            {
                return new WgslProcessResult(false, string.Empty, $"tint.exe reported success but produced no output file. stderr: {stderr}");
            }

            var hlslSource = await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false);
            return new WgslProcessResult(true, hlslSource, string.Empty);
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(outputPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
