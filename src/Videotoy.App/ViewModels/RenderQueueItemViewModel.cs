using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Videotoy.Media;

namespace Videotoy.App.ViewModels;

/// <summary>
/// Wrapper bindable autour d'un <see cref="RenderQueueItem"/> persistable
/// (POCO pur, sans type WPF). La miniature générée
/// (<see cref="RenderQueueProcessor.TryGenerateThumbnail"/>) et l'état
/// d'avancement en direct vivent uniquement ici, jamais sur le modèle
/// persisté — voir <see cref="RenderQueueItem.SortOrder"/> et voisins.
/// </summary>
public sealed partial class RenderQueueItemViewModel : ObservableObject
{
    public RenderQueueItemViewModel(RenderQueueItem model)
    {
        Model = model;
        _status = model.Status;
        _errorSummary = model.ErrorSummary;
    }

    public RenderQueueItem Model { get; }

    public Guid Id => Model.Id;

    public string ShaderDisplayName => Model.ShaderDisplayName;

    public RenderQueueItemKind Kind => Model.Kind;

    public string OutputFileName => Model.OutputFileName;

    public bool IsRunning => Status == RenderQueueItemStatus.Running;

    [ObservableProperty]
    private WriteableBitmap? _thumbnail;

    [ObservableProperty]
    private RenderQueueItemStatus _status;

    partial void OnStatusChanged(RenderQueueItemStatus value) => OnPropertyChanged(nameof(IsRunning));

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _errorSummary;
}
