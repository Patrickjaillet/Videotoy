using System.Collections.Concurrent;
using System.Diagnostics;
using Videotoy.Core.Domain;

namespace Videotoy.Ffmpeg;

public enum HardwareEncoderKind
{
    Nvenc,
    QuickSync,
    Amf
}

public sealed record HardwareEncoderAvailability(bool IsAvailable, string? UnavailableReason);

/// <summary>
/// Détecte, à la demande et avec mise en cache pour la durée de la session
/// applicative, si un encodeur matériel (NVENC, Quick Sync, AMF) est
/// réellement utilisable sur la machine courante — pas seulement compilé
/// dans le binaire FFmpeg embarqué, mais capable d'encoder effectivement
/// (pilote présent, GPU compatible). Classe séparée de <see cref="FfmpegService"/>,
/// qui possède le cycle de vie du process d'export : celle-ci ne fait que de
/// la découverte de capacités, jamais de rendu ni de muxage.
/// </summary>
public sealed class HardwareEncoderProbe
{
    private static readonly TimeSpan TestEncodeTimeout = TimeSpan.FromSeconds(5);

    private static readonly IReadOnlyDictionary<(HardwareEncoderKind Kind, VideoCodec Codec), string> EncoderNames =
        new Dictionary<(HardwareEncoderKind, VideoCodec), string>
        {
            [(HardwareEncoderKind.Nvenc, VideoCodec.H264)] = "h264_nvenc",
            [(HardwareEncoderKind.Nvenc, VideoCodec.H265)] = "hevc_nvenc",
            [(HardwareEncoderKind.QuickSync, VideoCodec.H264)] = "h264_qsv",
            [(HardwareEncoderKind.QuickSync, VideoCodec.H265)] = "hevc_qsv",
            [(HardwareEncoderKind.Amf, VideoCodec.H264)] = "h264_amf",
            [(HardwareEncoderKind.Amf, VideoCodec.H265)] = "hevc_amf",
        };

    private readonly FfmpegLocator _locator;
    private readonly ConcurrentDictionary<string, HardwareEncoderAvailability> _availabilityCache = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HashSet<string>? _compiledEncoderNames;

    public HardwareEncoderProbe(FfmpegLocator locator)
    {
        _locator = locator;
    }

    /// <summary>
    /// Résout le nom d'encodeur FFmpeg effectif à utiliser pour la clé de
    /// préférence <paramref name="hardwareEncoderKey"/> (produite côté F#
    /// par <c>ExportSettingsValidator.resolveHardwareEncoderPreferenceKey</c>
    /// — jamais l'union F# elle-même, conformément à la convention de
    /// frontière du projet) et <paramref name="codec"/> : le nom de
    /// l'encodeur matériel demandé s'il est réellement disponible (compilé
    /// et fonctionnel), sinon <paramref name="softwareFallbackName"/> de
    /// façon transparente et silencieuse. La clé <c>"software"</c> ne
    /// déclenche jamais de probe.
    /// </summary>
    public async Task<string> ResolveEncoderNameAsync(
        string hardwareEncoderKey,
        VideoCodec codec,
        string softwareFallbackName,
        CancellationToken cancellationToken = default)
    {
        var kind = ToKind(hardwareEncoderKey);
        if (kind is null)
        {
            return softwareFallbackName;
        }

        var availability = await ProbeAsync(kind.Value, codec, cancellationToken).ConfigureAwait(false);
        return availability.IsAvailable
            ? EncoderNames[(kind.Value, codec)]
            : softwareFallbackName;
    }

    public async Task<HardwareEncoderAvailability> ProbeAsync(
        HardwareEncoderKind kind,
        VideoCodec codec,
        CancellationToken cancellationToken = default)
    {
        if (!EncoderNames.TryGetValue((kind, codec), out var encoderName))
        {
            return new HardwareEncoderAvailability(false, "No hardware encoder mapping for this codec.");
        }

        if (_availabilityCache.TryGetValue(encoderName, out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_availabilityCache.TryGetValue(encoderName, out cached))
            {
                return cached;
            }

            var availability = await ProbeUncachedAsync(encoderName, cancellationToken).ConfigureAwait(false);
            _availabilityCache[encoderName] = availability;
            return availability;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HardwareEncoderAvailability> ProbeUncachedAsync(string encoderName, CancellationToken cancellationToken)
    {
        var compiledEncoders = await EnsureCompiledEncoderNamesAsync(cancellationToken).ConfigureAwait(false);

        if (!compiledEncoders.Contains(encoderName))
        {
            return new HardwareEncoderAvailability(false, "Not compiled into this FFmpeg build.");
        }

        return await RunTestEncodeAsync(encoderName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Interroge une seule fois par session la liste des encodeurs compilés
    /// dans le binaire FFmpeg embarqué (`ffmpeg -encoders`), pour éliminer
    /// immédiatement tout encodeur matériel absent du build sans avoir à
    /// tenter un encodage de test.
    /// </summary>
    private async Task<HashSet<string>> EnsureCompiledEncoderNamesAsync(CancellationToken cancellationToken)
    {
        if (_compiledEncoderNames is not null)
        {
            return _compiledEncoderNames;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _locator.ResolveExecutablePath(),
                ArgumentList = { "-hide_banner", "-encoders" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is not null)
            {
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                foreach (var line in output.Split('\n'))
                {
                    // Format d'une ligne d'encodeur : " V..... h264_nvenc  NVIDIA NVENC H.264 encoder (codec h264)"
                    var trimmed = line.TrimStart();
                    var firstSpace = trimmed.IndexOf(' ');
                    if (firstSpace <= 0)
                    {
                        continue;
                    }

                    var flags = trimmed[..firstSpace];
                    var rest = trimmed[(firstSpace + 1)..].TrimStart();
                    var nameEnd = rest.IndexOf(' ');
                    if (flags.Length < 1 || nameEnd <= 0)
                    {
                        continue;
                    }

                    names.Add(rest[..nameEnd]);
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // FFmpeg introuvable/injoignable : aucun encodeur matériel ne sera
            // jamais considéré disponible, le repli logiciel s'applique donc
            // systématiquement — comportement sûr par défaut.
        }

        _compiledEncoderNames = names;
        return names;
    }

    /// <summary>
    /// Tente un encodage d'une unique frame de test (générée en interne par
    /// FFmpeg via `lavfi`, sans dépendre d'un fichier source) avec l'encodeur
    /// demandé : un encodeur listé par `-encoders` mais dont le pilote GPU
    /// est absent ou incompatible échoue précisément à cette étape.
    /// </summary>
    private async Task<HardwareEncoderAvailability> RunTestEncodeAsync(string encoderName, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _locator.ResolveExecutablePath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("lavfi");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add("color=black:s=64x64:d=0.04");
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add(encoderName);
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("null");
            startInfo.ArgumentList.Add("-");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new HardwareEncoderAvailability(false, "Could not start FFmpeg for the test encode.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TestEncodeTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new HardwareEncoderAvailability(false, "The test encode timed out.");
            }

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                var reason = string.IsNullOrWhiteSpace(stderr) ? "The test encode failed." : stderr.Trim();
                return new HardwareEncoderAvailability(false, reason);
            }

            return new HardwareEncoderAvailability(true, null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return new HardwareEncoderAvailability(false, ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static HardwareEncoderKind? ToKind(string hardwareEncoderKey) =>
        hardwareEncoderKey switch
        {
            "nvenc" => HardwareEncoderKind.Nvenc,
            "qsv" => HardwareEncoderKind.QuickSync,
            "amf" => HardwareEncoderKind.Amf,
            _ => null
        };
}
