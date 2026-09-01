using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        var shaderIssuesView = ((CollectionViewSource)Resources["ShaderIssuesViewSource"]).View;
        shaderIssuesView.Filter = OnShaderIssuesFilter;
        _viewModel.ShaderIssuesFilterChanged += (_, _) => shaderIssuesView.Refresh();
    }

    /// <summary>
    /// Predicate backing the Shader Issues panel's severity/pass filter
    /// chips (Phase v1.8.0) — an <see cref="System.Windows.Data.ICollectionView"/>
    /// Filter is a plain delegate, not declaratively bindable, so it reads
    /// current filter state directly off <see cref="_viewModel"/> each time
    /// it runs.
    /// </summary>
    private bool OnShaderIssuesFilter(object item)
    {
        if (item is not ShaderIssueViewModel issue)
        {
            return true;
        }

        var severityMatches = issue.IsError ? _viewModel.ShowErrorIssues : _viewModel.ShowWarningIssues;
        if (!severityMatches)
        {
            return false;
        }

        var passFilter = _viewModel.AvailablePassNames.FirstOrDefault(p => p.PassName == issue.PassName);
        return passFilter is null || passFilter.IsSelected;
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

    /// <summary>
    /// Début d'un glisser sur un slider d'uniform custom : capture l'état
    /// "avant" côté ViewModel (voir <see cref="MainWindowViewModel.BeginCustomUniformEdit"/>)
    /// afin que tout le geste de glisser soit regroupé en une seule entrée
    /// d'historique (Phase v1.6.0), plutôt qu'une entrée par tick.
    /// </summary>
    private void OnCustomUniformSliderDragStarted(object sender, MouseButtonEventArgs e)
    {
        _viewModel.BeginCustomUniformEditCommand.Execute(null);
    }

    private void OnCustomUniformSliderDragCompleted(object sender, MouseButtonEventArgs e)
    {
        _viewModel.EndCustomUniformEditCommand.Execute(null);
    }

    /// <summary>
    /// Regroupe toute une session de frappe dans un champ numérique
    /// undoable (résolution custom, FPS custom, durée manuelle, CRF,
    /// bitrate cible, GOP, bitrate audio, GifColorCount, WebPQuality) en une
    /// seule entrée d'historique (Phase v1.6.0), plutôt qu'une entrée par
    /// caractère tapé — la transaction s'ouvre au focus et se referme au
    /// <see cref="OnUndoableTextBoxLostFocus"/>, les hooks
    /// <c>On&lt;Prop&gt;Changing</c>/<c>Changed</c> déclenchés par chaque
    /// frappe s'imbriquant dans cette transaction déjà ouverte.
    /// </summary>
    private void OnUndoableTextBoxGotFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.BeginHistoryTransaction();
    }

    private void OnUndoableTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.EndHistoryTransaction();
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

    private static readonly string[] SupportedVideoExtensions = { ".mp4", ".webm", ".mov" };

    private static bool IsDroppableVideoFile(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        return data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files
               && SupportedVideoExtensions.Contains(System.IO.Path.GetExtension(files[0]).ToLowerInvariant());
    }

    /// <summary>
    /// Empêche la propagation vers <see cref="OnDragEnter"/> (drop de shader
    /// sur toute la fenêtre) : un glisser-déposer sur une zone de channel
    /// vidéo n'a de sens que pour un fichier vidéo, jamais pour un shader.
    /// </summary>
    private void OnVideoChannelDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = IsDroppableVideoFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnVideoChannelDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (sender is not FrameworkElement { DataContext: VideoChannelViewModel viewModel })
        {
            return;
        }

        if (!IsDroppableVideoFile(e.Data))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
        {
            return;
        }

        await viewModel.HandleFileDroppedAsync(files[0]);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowChromeHelper.ApplyMicaBackdrop(this);
        CompositionTarget.Rendering += OnPreviewCompositionRendering;

        _viewModel.PropertyChanged += OnViewModelPropertyChangedForOnboarding;
        _viewModel.StartOnboardingIfFirstLaunch();
        if (_viewModel.IsOnboardingActive)
        {
            UpdateOnboardingStep();
        }
    }

    /// <summary>
    /// Repositions the onboarding overlay's highlight/callout whenever the
    /// active step changes or the overlay is (re)opened (Phase v1.8.0) —
    /// computing a target Rect via TranslatePoint is view geometry, so this
    /// logic lives in code-behind rather than the ViewModel.
    /// </summary>
    private void OnViewModelPropertyChangedForOnboarding(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsOnboardingActive) or nameof(MainWindowViewModel.CurrentOnboardingStepIndex))
        {
            if (_viewModel.IsOnboardingActive)
            {
                UpdateOnboardingStep();
            }
        }
    }

    private void UpdateOnboardingStep()
    {
        var (target, titleKey, bodyKey) = _viewModel.CurrentOnboardingStepIndex switch
        {
            0 => ((FrameworkElement)ViewportRegion, "onboarding.step1.title", "onboarding.step1.body"),
            1 => (SettingsPanelRegion, "onboarding.step2.title", "onboarding.step2.body"),
            _ => ((FrameworkElement)RenderQueueToolbarButton, "onboarding.step3.title", "onboarding.step3.body")
        };

        OnboardingStepTitle.Text = Localization.LocalizationRuntime.Service.GetString(titleKey);
        OnboardingStepBody.Text = Localization.LocalizationRuntime.Service.GetString(bodyKey);
        OnboardingStepCounter.Text = $"{_viewModel.CurrentOnboardingStepIndex + 1} / {MainWindowViewModel.OnboardingStepCount}";

        var isLastStep = _viewModel.CurrentOnboardingStepIndex == MainWindowViewModel.OnboardingStepCount - 1;
        OnboardingNextButton.Content = Localization.LocalizationRuntime.Service.GetString(
            isLastStep ? "onboarding.next.done" : "onboarding.next");

        if (!target.IsLoaded || target.ActualWidth <= 0 || target.ActualHeight <= 0)
        {
            return;
        }

        var topLeft = target.TranslatePoint(new Point(0, 0), OnboardingOverlay);
        var bounds = new Rect(topLeft, new Size(target.ActualWidth, target.ActualHeight));

        OnboardingHighlight.Margin = new Thickness(bounds.Left - 4, bounds.Top - 4, 0, 0);
        OnboardingHighlight.Width = bounds.Width + 8;
        OnboardingHighlight.Height = bounds.Height + 8;

        var calloutLeft = Math.Min(Math.Max(bounds.Left, 16), Math.Max(16, OnboardingOverlay.ActualWidth - OnboardingCallout.Width - 16));
        var calloutTop = bounds.Bottom + 12;
        if (calloutTop + 160 > OnboardingOverlay.ActualHeight)
        {
            calloutTop = Math.Max(16, bounds.Top - 172);
        }

        OnboardingCallout.Margin = new Thickness(calloutLeft, calloutTop, 0, 0);
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

    private Point _renderQueueDragStartPoint;

    /// <summary>
    /// Réordonnancement par glisser-déposer de la file de rendu — même
    /// idiome standard <c>PreviewMouseLeftButtonDown</c>/<c>MouseMove</c>/
    /// <c>DragDrop.DoDragDrop</c>/<c>Drop</c> que <see cref="OnVideoChannelDragEnter"/>/
    /// <see cref="OnVideoChannelDrop"/>, mais réordonnant les éléments d'une
    /// <see cref="ListBox"/> plutôt que d'accepter un fichier externe.
    /// </summary>
    private void OnRenderQueueItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _renderQueueDragStartPoint = e.GetPosition(null);
    }

    private void OnRenderQueueItemPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBox listBox)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        var delta = _renderQueueDragStartPoint - currentPosition;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject originalSource)
        {
            return;
        }

        var container = FindAncestor<ListBoxItem>(originalSource);
        if (container?.DataContext is not RenderQueueItemViewModel item)
        {
            return;
        }

        DragDrop.DoDragDrop(listBox, item, DragDropEffects.Move);
    }

    private void OnRenderQueueItemDrop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox || e.Data.GetData(typeof(RenderQueueItemViewModel)) is not RenderQueueItemViewModel draggedItem)
        {
            return;
        }

        var targetItem = (e.OriginalSource as DependencyObject) is { } originalSource
            ? FindAncestor<ListBoxItem>(originalSource)?.DataContext as RenderQueueItemViewModel
            : null;

        if (targetItem is null || ReferenceEquals(targetItem, draggedItem))
        {
            return;
        }

        var orderedIds = _viewModel.RenderQueue.Select(item => item.Id).ToList();
        var draggedIndex = orderedIds.IndexOf(draggedItem.Id);
        var targetIndex = orderedIds.IndexOf(targetItem.Id);
        if (draggedIndex < 0 || targetIndex < 0)
        {
            return;
        }

        orderedIds.RemoveAt(draggedIndex);
        orderedIds.Insert(targetIndex, draggedItem.Id);

        _viewModel.ReorderRenderQueueItemsCommand.Execute(orderedIds);
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
