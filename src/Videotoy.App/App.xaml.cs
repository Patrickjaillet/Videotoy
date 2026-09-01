using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Videotoy.App.Localization;
using Videotoy.App.ViewModels;
using Videotoy.Ffmpeg;
using Videotoy.Media;
using Videotoy.Rendering;
using Videotoy.Transpiler;

namespace Videotoy.App;

public partial class App : Application
{
    /// <summary>
    /// Minimum time the splash screen stays visible, so its loading
    /// animation is actually perceptible even when startup (FFmpeg
    /// integrity check, service resolution) completes almost instantly on
    /// a fast machine.
    /// </summary>
    private static readonly TimeSpan MinimumSplashDuration = TimeSpan.FromMilliseconds(900);

    public IServiceProvider Services { get; }

    public App()
    {
        Services = ConfigureServices();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LocalizationRuntime.Attach(Services.GetRequiredService<LocalizationService>());

        var splash = new Views.SplashWindow();
        splash.Show();

        var splashStopwatch = System.Diagnostics.Stopwatch.StartNew();

        var integrityVerifier = Services.GetRequiredService<FfmpegIntegrityVerifier>();

        try
        {
            integrityVerifier.VerifyOrThrow();
        }
        catch (Exception ex) when (ex is FfmpegIntegrityException or FileNotFoundException)
        {
            splash.Close();
            MessageBox.Show(
                ex.Message,
                "Videotoy — FFmpeg integrity check failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var mainWindow = Services.GetRequiredService<Views.MainWindow>();

        var remainingSplashTime = MinimumSplashDuration - splashStopwatch.Elapsed;
        if (remainingSplashTime > TimeSpan.Zero)
        {
            await Task.Delay(remainingSplashTime);
        }

        mainWindow.Show();
        splash.Close();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<FfmpegLocator>();
        services.AddSingleton<FfmpegIntegrityVerifier>();
        services.AddSingleton<HardwareEncoderProbe>();
        services.AddSingleton<FfmpegService>();
        services.AddSingleton<VideoProber>();
        services.AddSingleton<VideoFrameDecoder>();
        services.AddSingleton<VideoTextureLoader>();
        services.AddSingleton<PreviewMultiPassRenderer>();
        services.AddSingleton<ExportMultiPassRenderer>();

        // FrameSequenceRenderer (consommé par les pipelines d'export) doit
        // toujours utiliser le renderer dédié à l'export, jamais celui de
        // la prévisualisation : résolution explicite plutôt que de laisser
        // le conteneur choisir arbitrairement entre les deux MultiPassRenderer
        // enregistrés.
        services.AddSingleton(sp => new FrameSequenceRenderer(sp.GetRequiredService<ExportMultiPassRenderer>()));
        services.AddSingleton<VideoExportPipeline>();
        services.AddSingleton<AnimatedImageExportPipeline>();

        services.AddSingleton<TintLocator>();
        // Contrairement à FfmpegIntegrityVerifier, VerifyOrThrow() n'est
        // jamais appelé au démarrage : le support WGSL est optionnel, la
        // vérification est paresseuse (voir WgslToHlslTranspiler, premier
        // chargement d'un fichier .wgsl) et son échec ne doit jamais faire
        // planter l'application.
        services.AddSingleton<TintIntegrityVerifier>();
        services.AddSingleton<WgslTranspilerProcess>();
        services.AddSingleton<WgslToHlslTranspiler>();
        services.AddSingleton<IShaderTranspilerRouter, ShaderTranspilerRouter>();

        services.AddSingleton<TextureLoader>();
        services.AddSingleton<AudioTrackLoader>();
        services.AddSingleton<AudioSpectrumTextureGenerator>();
        services.AddSingleton<ShaderFileService>();
        services.AddSingleton<RecentFilesService>();
        services.AddSingleton<ExportPresetService>();
        services.AddSingleton<ExportHistoryService>();
        services.AddSingleton<RenderQueueService>();
        services.AddSingleton<LoopSettingsService>();
        services.AddSingleton<LocalizationService>();

        services.AddSingleton<BoundAssetsBuilder>();
        services.AddSingleton<RenderQueueProcessor>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<Views.MainWindow>();

        return services.BuildServiceProvider();
    }
}
