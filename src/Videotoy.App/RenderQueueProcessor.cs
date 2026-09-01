using System.Windows.Media.Imaging;
using Videotoy.App.ViewModels;
using Videotoy.Ffmpeg;
using Videotoy.Media;
using Videotoy.Rendering;

namespace Videotoy.App;

/// <summary>
/// Moteur d'exécution séquentielle de la file de rendu (export par lots).
/// Rejoue, pour chaque <see cref="RenderQueueItem"/>, exactement la même
/// séquence qu'un export manuel unique
/// (<c>ShaderFileService.Load</c> → <see cref="BoundAssetsBuilder.Build"/> →
/// <see cref="ExportMultiPassRenderer.Initialize"/> →
/// <c>VideoExportPipeline.RunAsync</c>/<c>AnimatedImageExportPipeline.RunAsync</c>),
/// sans jamais toucher <c>PreviewMultiPassRenderer</c> ni l'état du shader
/// actuellement ouvert dans l'UI. Isolation des erreurs : l'échec d'un
/// élément ne fait jamais remonter d'exception hors de la boucle
/// principale — il marque uniquement cet élément en échec et la boucle
/// continue avec le suivant.
/// </summary>
public sealed class RenderQueueProcessor
{
    private readonly ShaderFileService _shaderFileService;
    private readonly BoundAssetsBuilder _boundAssetsBuilder;
    private readonly ExportMultiPassRenderer _exportRenderer;
    private readonly VideoExportPipeline _exportPipeline;
    private readonly AnimatedImageExportPipeline _animatedImageExportPipeline;
    private readonly RenderQueueService _renderQueueService;

    private CancellationTokenSource? _queueCancellationTokenSource;
    private CancellationTokenSource? _currentItemCancellationTokenSource;
    private TaskCompletionSource? _pauseGate;

    public RenderQueueProcessor(
        ShaderFileService shaderFileService,
        BoundAssetsBuilder boundAssetsBuilder,
        ExportMultiPassRenderer exportRenderer,
        VideoExportPipeline exportPipeline,
        AnimatedImageExportPipeline animatedImageExportPipeline,
        RenderQueueService renderQueueService)
    {
        _shaderFileService = shaderFileService;
        _boundAssetsBuilder = boundAssetsBuilder;
        _exportRenderer = exportRenderer;
        _exportPipeline = exportPipeline;
        _animatedImageExportPipeline = animatedImageExportPipeline;
        _renderQueueService = renderQueueService;
    }

    public bool IsRunning { get; private set; }

    public bool IsPaused { get; private set; }

    public Guid? CurrentItemId { get; private set; }

    public event EventHandler<RenderQueueItemProgressEventArgs>? ItemProgressChanged;

    public event EventHandler<RenderQueueItemStatusEventArgs>? ItemStatusChanged;

    public event EventHandler<RenderQueueCompletedEventArgs>? QueueCompleted;

    public async Task StartAsync(IReadOnlyList<RenderQueueItem> items)
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        IsPaused = false;
        _queueCancellationTokenSource = new CancellationTokenSource();
        var queueToken = _queueCancellationTokenSource.Token;

        var succeeded = 0;
        var failed = 0;
        var cancelled = 0;

        try
        {
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];

                if (item.Status is not RenderQueueItemStatus.Pending)
                {
                    continue;
                }

                if (queueToken.IsCancellationRequested)
                {
                    MarkCancelled(item);
                    cancelled++;
                    continue;
                }

                await WaitWhilePausedAsync(queueToken);

                if (queueToken.IsCancellationRequested)
                {
                    MarkCancelled(item);
                    cancelled++;
                    continue;
                }

                CurrentItemId = item.Id;
                SetStatus(item, RenderQueueItemStatus.Running, index, items.Count, null);

                _currentItemCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(queueToken);

                try
                {
                    await ProcessItemAsync(item, index, items.Count, _currentItemCancellationTokenSource.Token);
                    SetStatus(item, RenderQueueItemStatus.Succeeded, index, items.Count, null);
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    if (queueToken.IsCancellationRequested)
                    {
                        MarkCancelled(item);
                        cancelled++;
                    }
                    else
                    {
                        SetStatus(item, RenderQueueItemStatus.Cancelled, index, items.Count, null);
                        cancelled++;
                    }
                }
                catch (FfmpegEncodingException ex)
                {
                    SetStatus(item, RenderQueueItemStatus.Failed, index, items.Count, ex.Diagnosis.Summary);
                    failed++;
                }
                catch (Exception ex)
                {
                    SetStatus(item, RenderQueueItemStatus.Failed, index, items.Count, ex.Message);
                    failed++;
                }
                finally
                {
                    _currentItemCancellationTokenSource?.Dispose();
                    _currentItemCancellationTokenSource = null;
                }
            }

            if (queueToken.IsCancellationRequested)
            {
                foreach (var remaining in items.Where(i => i.Status == RenderQueueItemStatus.Pending))
                {
                    MarkCancelled(remaining);
                    cancelled++;
                }
            }
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
            CurrentItemId = null;
            _queueCancellationTokenSource?.Dispose();
            _queueCancellationTokenSource = null;

            QueueCompleted?.Invoke(this, new RenderQueueCompletedEventArgs(succeeded, failed, cancelled));
        }
    }

    /// <summary>
    /// Marque la fin de la file après l'élément en cours : n'interrompt
    /// jamais un export déjà démarré (FFmpeg n'a pas de primitive de pause
    /// propre), seulement le passage à l'élément suivant.
    /// </summary>
    public void Pause()
    {
        if (!IsRunning || IsPaused)
        {
            return;
        }

        IsPaused = true;
        _pauseGate = new TaskCompletionSource();
    }

    public void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        _pauseGate?.TrySetResult();
        _pauseGate = null;
    }

    public void CancelCurrentItem() => _currentItemCancellationTokenSource?.Cancel();

    public void CancelAll() => _queueCancellationTokenSource?.Cancel();

    /// <summary>
    /// Génère une miniature best-effort pour <paramref name="shaderFilePath"/>
    /// via <see cref="ExportMultiPassRenderer"/> (jamais le renderer de
    /// prévisualisation, pour ne pas perturber la lecture live) à la frame 0.
    /// Retourne <c>null</c> sur tout échec de chargement/rendu — appelant
    /// responsable de gérer l'absence de miniature. Ne doit être appelée que
    /// lorsque <see cref="IsRunning"/> est faux : partage le même
    /// <see cref="ExportMultiPassRenderer"/> que le traitement de la file.
    /// </summary>
    public WriteableBitmap? TryGenerateThumbnail(string shaderFilePath)
    {
        if (IsRunning)
        {
            return null;
        }

        try
        {
            var loadedShader = _shaderFileService.Load(shaderFilePath);
            if (loadedShader.HasErrors)
            {
                return null;
            }

            var (images, audioTracks, videoSources) = _boundAssetsBuilder.Build(loadedShader);
            _exportRenderer.Initialize(
                RenderTargetSize.PreviewDefault,
                loadedShader.Project,
                loadedShader.HlslPasses,
                images,
                audioTracks,
                videoSources);

            var pixels = _exportRenderer.RenderFrame(0.0, 0.0, 0);
            return MainWindowViewModel.CreatePreviewBitmap(pixels);
        }
        catch
        {
            return null;
        }
    }

    private async Task ProcessItemAsync(RenderQueueItem item, int itemIndex, int totalItems, CancellationToken cancellationToken)
    {
        var loadedShader = await Task.Run(() => _shaderFileService.Load(item.ShaderFilePath), cancellationToken);
        if (loadedShader.HasErrors)
        {
            throw new InvalidOperationException($"Shader '{item.ShaderDisplayName}' has validation errors.");
        }

        var (images, audioTracks, videoSources) = _boundAssetsBuilder.Build(loadedShader);

        var progress = new Progress<VideoExportProgress>(p =>
            ItemProgressChanged?.Invoke(this, new RenderQueueItemProgressEventArgs(item.Id, itemIndex, totalItems, p)));

        if (item.Kind == RenderQueueItemKind.Video)
        {
            var exportSettings = RenderQueueSettingsBuilder.BuildExportSettings(item);

            _exportRenderer.Initialize(
                new RenderTargetSize(exportSettings.Resolution.Width, exportSettings.Resolution.Height),
                loadedShader.Project,
                loadedShader.HlslPasses,
                images,
                audioTracks,
                videoSources);

            var audioSourceFilePath = item.IncludeAudioInExport
                ? BoundAssetsBuilder.ResolveExportAudioSourceFilePath(loadedShader)
                : null;

            await _exportPipeline.RunAsync(exportSettings, progress, cancellationToken, audioSourceFilePath);
        }
        else
        {
            var exportSettings = RenderQueueSettingsBuilder.BuildAnimatedImageExportSettings(item);

            _exportRenderer.Initialize(
                new RenderTargetSize(exportSettings.Resolution.Width, exportSettings.Resolution.Height),
                loadedShader.Project,
                loadedShader.HlslPasses,
                images,
                audioTracks,
                videoSources);

            await _animatedImageExportPipeline.RunAsync(exportSettings, progress, cancellationToken);
        }
    }

    private async Task WaitWhilePausedAsync(CancellationToken queueToken)
    {
        while (IsPaused && _pauseGate is not null && !queueToken.IsCancellationRequested)
        {
            using var registration = queueToken.Register(() => _pauseGate?.TrySetCanceled());
            try
            {
                await _pauseGate.Task;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void MarkCancelled(RenderQueueItem item) =>
        SetStatus(item, RenderQueueItemStatus.Cancelled, -1, -1, null, raiseProgress: false);

    private void SetStatus(
        RenderQueueItem item,
        RenderQueueItemStatus status,
        int itemIndex,
        int totalItems,
        string? errorSummary,
        bool raiseProgress = true)
    {
        item.Status = status;
        item.ErrorSummary = errorSummary;
        _renderQueueService.UpdateStatus(item.Id, status, errorSummary);

        if (raiseProgress)
        {
            ItemStatusChanged?.Invoke(this, new RenderQueueItemStatusEventArgs(item.Id, itemIndex, totalItems, status, errorSummary));
        }
    }
}

public sealed record RenderQueueItemProgressEventArgs(Guid ItemId, int ItemIndex, int TotalItems, VideoExportProgress Progress);

public sealed record RenderQueueItemStatusEventArgs(Guid ItemId, int ItemIndex, int TotalItems, RenderQueueItemStatus Status, string? ErrorSummary);

public sealed record RenderQueueCompletedEventArgs(int Succeeded, int Failed, int Cancelled);
