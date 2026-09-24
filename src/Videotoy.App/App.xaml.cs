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

        // Sans ces trois gestionnaires, toute exception échappant à l'UI
        // (DispatcherUnhandledException), à une tâche jamais attendue
        // (UnobservedTaskException) ou à tout autre thread
        // (AppDomain.UnhandledException, non annulable — .NET termine le
        // process dans tous les cas juste après) plante le process entier
        // sans le moindre message ni log exploitable pour un rapport de bug.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
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

        Views.MainWindow mainWindow;
        try
        {
            mainWindow = Services.GetRequiredService<Views.MainWindow>();
        }
        catch (Exception ex)
        {
            // Construire la fenêtre principale résout tout le graphe de
            // dépendances, y compris la création du device Direct3D 11
            // (OffscreenRenderContext) : sur une machine sans GPU/pilote
            // utilisable (même en repli WARP) ou une installation
            // corrompue (DLL manquante), cela lève ici plutôt qu'à un point
            // dédié — un message clair vaut mieux que la boîte de dialogue
            // de crash générique de Windows.
            splash.Close();
            MessageBox.Show(
                $"Videotoy failed to start: {ex.Message}",
                "Videotoy — Startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var remainingSplashTime = MinimumSplashDuration - splashStopwatch.Elapsed;
        if (remainingSplashTime > TimeSpan.Zero)
        {
            await Task.Delay(remainingSplashTime);
        }

        mainWindow.Show();
        splash.Close();
    }

    private static void OnDispatcherUnhandledException(
        object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Trace.TraceError($"Unhandled UI exception: {e.Exception}");
        MessageBox.Show(
            $"Videotoy encountered an unexpected error and must close.\n\n{e.Exception.Message}",
            "Videotoy — Unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current?.Shutdown(1);
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Trace.TraceError($"Unhandled exception (terminating: {e.IsTerminating}): {e.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Trace.TraceError($"Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // ServiceProvider.Dispose() dispose à son tour tout service singleton
        // enregistré qui implémente IDisposable (dont MainWindowViewModel,
        // voir son Dispose()) : point d'accroche unique pour la libération
        // de fin de vie de l'application, sans avoir à traquer chaque
        // service individuellement ici.
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
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
        services.AddSingleton<ImageSequenceExportPipeline>();

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
        services.AddSingleton<OnboardingStateService>();
        services.AddSingleton<ViewportBackgroundSettingsService>();
        services.AddSingleton<LocalizationService>();

        services.AddSingleton<BoundAssetsBuilder>();
        services.AddSingleton<RenderQueueProcessor>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<Views.MainWindow>();

        return services.BuildServiceProvider();
    }
}
