using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Videotoy.App.Interop;
using Videotoy.App.ViewModels;
using Videotoy.Media;

namespace Videotoy.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Stopwatch _previewStopwatch = new();
    private bool _isUserScrubbingTimeline;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DragEnter += OnDragEnter;
        Drop += OnFileDropped;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnPreviewCompositionRendering;
        _previewStopwatch.Stop();
    }

    /// <summary>
    /// Boucle de rafraîchissement du viewport de prévisualisation, alignée sur le
    /// vsync WPF : mesure le temps réel réellement écoulé depuis la dernière frame
    /// affichée et le transmet au ViewModel pour avancer la lecture (play/pause/scrub).
    /// </summary>
    private void OnPreviewCompositionRendering(object? sender, EventArgs e)
    {
        if (!_previewStopwatch.IsRunning)
        {
            _previewStopwatch.Start();
            return;
        }

        var deltaSeconds = _previewStopwatch.Elapsed.TotalSeconds;
        _previewStopwatch.Restart();

        _viewModel.AdvancePreview(deltaSeconds);
    }

    /// <summary>
    /// Début d'un scrub manuel de la timeline (clic ou drag du curseur) : met la
    /// lecture en pause côté ViewModel pour que le geste utilisateur ne soit pas
    /// écrasé par la mise à jour continue de PlaybackTimeSeconds pendant la lecture.
    /// </summary>
    private void OnTimelineDragStarted(object sender, MouseButtonEventArgs e)
    {
        _isUserScrubbingTimeline = true;
        _viewModel.BeginScrubCommand.Execute(null);
    }

    private void OnTimelineDragCompleted(object sender, MouseButtonEventArgs e)
    {
        if (!_isUserScrubbingTimeline)
        {
            return;
        }

        _isUserScrubbingTimeline = false;
        _viewModel.EndScrubCommand.Execute(null);
    }

    /// <summary>
    /// Ne répercute la nouvelle valeur du slider vers le ViewModel que lorsqu'elle
    /// résulte d'un geste utilisateur ; ignore les mises à jour programmatiques
    /// (rafraîchissement de PlaybackTimeSeconds pendant la lecture normale) pour
    /// éviter une boucle de rétroaction Seek -> TimeChanged -> ValueChanged -> Seek.
    /// </summary>
    private void OnTimelineValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isUserScrubbingTimeline)
        {
            return;
        }

        _viewModel.Seek(e.NewValue);
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = IsDroppableFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDropped(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
        {
            return;
        }

        _viewModel.LoadShaderFile(files[0]);
    }

    private static bool IsDroppableFile(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        return data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files
               && ShaderFileService.IsSupportedShaderFile(files[0]);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowChromeHelper.ApplyMicaBackdrop(this);
        CompositionTarget.Rendering += OnPreviewCompositionRendering;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClicked(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow
        {
            Owner = this
        };
        aboutWindow.ShowDialog();
    }

    private void OnCloseIssuesPanelClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseIssuesPanelCommand.Execute(null);
    }

    private void OnTogglePanelClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleSettingsPanelCommand.Execute(null);

        var storyboardKey = _viewModel.IsSettingsPanelOpen
            ? "ExpandPanelStoryboard"
            : "CollapsePanelStoryboard";

        var storyboard = (Storyboard)Resources[storyboardKey];
        storyboard.Begin(this);

        TogglePanelIcon.Data = (System.Windows.Media.Geometry)FindResource(
            _viewModel.IsSettingsPanelOpen ? "IconChevronRight" : "IconChevronLeft");
    }
}
