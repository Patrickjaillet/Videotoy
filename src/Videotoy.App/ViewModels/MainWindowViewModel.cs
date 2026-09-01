using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Videotoy.Core.Domain;
using Videotoy.Ffmpeg;
using Videotoy.Media;
using Videotoy.Rendering;
using CoreFileSizeEstimator = Videotoy.Core.ExportFileSizeEstimator;
using CoreAnimatedImageFileSizeEstimator = Videotoy.Core.AnimatedImageFileSizeEstimator;
using CoreLoopCalculator = Videotoy.Core.LoopCalculator;

namespace Videotoy.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private static readonly string[] OpenFileDialogExtensions =
        { "*.glsl", "*.frag", "*.json", "*.shadertoy" };

    private readonly ShaderFileService _shaderFileService;
    private readonly RecentFilesService _recentFilesService;
    private readonly ExportPresetService _exportPresetService;
    private readonly ExportHistoryService _exportHistoryService;
    private readonly LoopSettingsService _loopSettingsService;
    private readonly LocalizationService _localizationService;
    private readonly MultiPassRenderer _previewRenderer;
    private readonly PreviewClock _previewClock = new();
    private readonly ExportMultiPassRenderer _exportRenderer;
    private readonly VideoExportPipeline _exportPipeline;
    private readonly AnimatedImageExportPipeline _animatedImageExportPipeline;
    private readonly AudioSpectrumTextureGenerator _audioSpectrumTextureGenerator;
    private readonly VideoTextureLoader _videoTextureLoader;
    private readonly BoundAssetsBuilder _boundAssetsBuilder;
    private readonly RenderQueueService _renderQueueService;
    private readonly RenderQueueProcessor _renderQueueProcessor;

    /// <summary>
    /// Pile d'annulation/rétablissement partagée par les paramètres d'export
    /// et les valeurs des sliders d'uniforms custom (jamais le contenu du
    /// shader lui-même) — voir Phase v1.6.0 du roadmap. Vidée à chaque
    /// chargement de shader (<see cref="LoadShaderFile"/>).
    /// </summary>
    private readonly Videotoy.App.History.SettingsUndoStack _historyStack = new();

    /// <summary>
    /// Profondeur de la transaction d'historique en cours : incrémentée par
    /// <c>On&lt;Prop&gt;Changing</c> lorsqu'une modification synchrone démarre,
    /// décrémentée par <c>On&lt;Prop&gt;Changed</c> une fois la mutation
    /// terminée. Une seule entrée d'historique est poussée lorsque la
    /// profondeur retombe à zéro, ce qui regroupe automatiquement les
    /// cascades (ex. changer le codec vidéo réinitialise aussi le profil
    /// vidéo) en une seule action annulable, sans avoir à énumérer
    /// explicitement quelles propriétés cascadent vers lesquelles.
    /// </summary>
    private int _historyTransactionDepth;

    private Videotoy.App.History.ExportSettingsSnapshot? _historyTransactionBefore;

    /// <summary>
    /// Empêche toute capture d'historique pendant l'application d'un
    /// undo/redo (<see cref="Undo"/>/<see cref="Redo"/>) ou pendant le
    /// chargement d'un nouveau shader (<see cref="LoadShaderFile"/>), pour
    /// éviter qu'une réaffectation programmatique ne soit elle-même
    /// capturée comme une action utilisateur annulable.
    /// </summary>
    private bool _suppressHistoryCapture;

    /// <summary>
    /// Snapshot des valeurs de sliders d'uniforms custom capturé par
    /// <see cref="BeginCustomUniformEdit"/>, ou <c>null</c> si aucun geste
    /// d'édition n'est en cours (voir <see cref="EndCustomUniformEdit"/>).
    /// </summary>
    private Dictionary<(string GroupName, int ComponentIndex), float>? _customUniformEditBefore;

    private WriteableBitmap? _previewBitmap;
    private LoadedShader? _loadedShader;
    private string? _loadedShaderFilePath;
    private bool _isScrubbing;
    private CancellationTokenSource? _exportCancellationTokenSource;

    [ObservableProperty]
    private string _statusMessage = "Idle";

    [ObservableProperty]
    private int _currentFrame;

    [ObservableProperty]
    private int _totalFrames;

    [ObservableProperty]
    private double _currentFps;

    [ObservableProperty]
    private bool _isSettingsPanelOpen = true;

    [ObservableProperty]
    private bool _isExportHistoryPanelOpen;

    [ObservableProperty]
    private bool _isRenderQueuePanelOpen;

    [ObservableProperty]
    private string _loadedShaderName = string.Empty;

    [ObservableProperty]
    private bool _isShaderLoaded;

    [ObservableProperty]
    private bool _isIssuesPanelOpen;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _playbackTimeSeconds;

    [ObservableProperty]
    private double _loopDurationSeconds = 10.0;

    [ObservableProperty]
    private ImageSource? _previewImageSource;

    /// <summary>
    /// First frame (t = 0) of the current "Seamless loop" configuration, as
    /// last generated by <see cref="GenerateLoopSeamPreviewCommand"/>. Null
    /// until the command has run at least once for the currently loaded
    /// shader/settings combination.
    /// </summary>
    [ObservableProperty]
    private ImageSource? _loopSeamStartFrameImageSource;

    /// <summary>
    /// Last frame actually rendered for export under the current "Seamless
    /// loop" configuration (index <c>EstimatedTotalFrames - 1</c>), as last
    /// generated by <see cref="GenerateLoopSeamPreviewCommand"/> — i.e. the
    /// frame immediately preceding the loop restart, which should look like
    /// a natural predecessor of <see cref="LoopSeamStartFrameImageSource"/>
    /// for the loop to read as seamless.
    /// </summary>
    [ObservableProperty]
    private ImageSource? _loopSeamEndFrameImageSource;

    /// <summary>
    /// True while <see cref="GenerateLoopSeamPreviewCommand"/> is rendering
    /// the loop sequence; disables the button and shows a busy indicator
    /// rather than freezing the panel silently, since a long loop at a high
    /// frame rate can take a noticeable moment to walk through.
    /// </summary>
    [ObservableProperty]
    private bool _isGeneratingLoopSeamPreview;

    /// <summary>
    /// True once <see cref="LoopSeamStartFrameImageSource"/> and
    /// <see cref="LoopSeamEndFrameImageSource"/> hold a result, so the panel
    /// can show the side-by-side comparison. Reset to false whenever a
    /// setting that would invalidate the preview changes (shader reload,
    /// resolution/frame-rate/loop-duration/exclusive-end-frame change) so a
    /// stale comparison is never shown as if it were current.
    /// </summary>
    [ObservableProperty]
    private bool _hasLoopSeamPreview;

    /// <summary>
    /// Currently selected export resolution preset. Switching to
    /// <see cref="ResolutionPresetOption.Custom"/> reveals the custom
    /// width/height inputs; any other preset directly supplies the export
    /// resolution and disables those inputs.
    /// </summary>
    [ObservableProperty]
    private ResolutionPresetOption _selectedResolutionPreset = ResolutionPresetOption.FullHd1080;

    [ObservableProperty]
    private int _customResolutionWidth = 1920;

    [ObservableProperty]
    private int _customResolutionHeight = 1080;

    /// <summary>
    /// Currently selected export frame rate preset. Switching to
    /// <see cref="FrameRatePresetOption.Custom"/> reveals the custom frame
    /// rate input; any other preset directly supplies the export frame rate
    /// and disables that input.
    /// </summary>
    [ObservableProperty]
    private FrameRatePresetOption _selectedFrameRatePreset = FrameRatePresetOption.Fps30;

    [ObservableProperty]
    private double _customFrameRateValue = 30.0;

    /// <summary>
    /// Unit in which <see cref="ManualDurationValue"/> is expressed when the
    /// export duration mode is "Manual duration". Ignored in "Seamless loop"
    /// mode, which always uses <see cref="LoopDurationSeconds"/> directly.
    /// </summary>
    [ObservableProperty]
    private DurationUnit _manualDurationUnit = DurationUnit.Seconds;

    /// <summary>
    /// Zero-based index proxy for <see cref="ManualDurationUnit"/>
    /// (0 = Seconds, 1 = Frames), so the "Seconds/Frames" combo box in the
    /// panel can bind <c>SelectedIndex</c> directly without a value
    /// converter.
    /// </summary>
    public int ManualDurationUnitIndex
    {
        get => (int)ManualDurationUnit;
        set => ManualDurationUnit = (DurationUnit)value;
    }

    /// <summary>
    /// Manual export duration, expressed in <see cref="ManualDurationUnit"/>
    /// (seconds or frames). Converted to seconds against the effective export
    /// frame rate before being handed to <see cref="DurationMode.NewManual"/>.
    /// </summary>
    [ObservableProperty]
    private double _manualDurationValue = 10.0;

    /// <summary>
    /// Absolute path of the folder the exported video will be written to.
    /// Defaults to the user's Videos folder and is editable directly or via
    /// <see cref="BrowseOutputDirectoryCommand"/>.
    /// </summary>
    [ObservableProperty]
    private string _outputDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    /// <summary>
    /// Output file name (without the container extension, which is appended
    /// automatically from <see cref="Videotoy.Core.Domain.ContainerFormat"/>
    /// at export time). Defaults to the loaded shader's title once a shader
    /// is loaded.
    /// </summary>
    [ObservableProperty]
    private string _outputFileName = "export";

    /// <summary>
    /// Total number of frames the current export configuration will produce,
    /// recomputed from <see cref="LoopCalculator.computeFrameCount"/> every
    /// time a setting that affects it changes. Drives the frame-count and
    /// estimated file-size preview in the render settings panel.
    /// </summary>
    [ObservableProperty]
    private int _estimatedTotalFrames;

    /// <summary>
    /// Human-readable estimated output file size ("~12.4 MB"), recomputed
    /// alongside <see cref="EstimatedTotalFrames"/>. Purely informational —
    /// see <see cref="Videotoy.Core.ExportFileSizeEstimator"/>.
    /// </summary>
    [ObservableProperty]
    private string _estimatedFileSizeText = "-";

    /// <summary>
    /// True when <see cref="LoopCalculator.computeFrameCount"/> reports that
    /// the requested duration doesn't divide evenly into whole frames at the
    /// selected frame rate ("Seamless loop" mode only) — surfaced in the
    /// panel as a rounding-mismatch warning.
    /// </summary>
    [ObservableProperty]
    private bool _hasLoopRoundingMismatch;

    /// <summary>
    /// Nearest loop duration (in seconds) that divides evenly into whole
    /// frames at the currently selected export frame rate, as computed by
    /// <see cref="LoopCalculator.suggestAssistedLoopSeconds"/>. Only
    /// meaningful — and only surfaced in the panel — while
    /// <see cref="HasLoopRoundingMismatch"/> is true; applying it via
    /// <see cref="ApplyAssistedLoopRoundingCommand"/> removes the mismatch
    /// entirely rather than merely reducing it.
    /// </summary>
    [ObservableProperty]
    private double _suggestedLoopDurationSeconds;

    /// <summary>
    /// True once <see cref="Videotoy.Core.LoopPeriodDetector.detectLoopPeriod"/>
    /// has found at least one candidate native loop period in the currently
    /// loaded shader's source (simple periodic patterns on <c>iTime</c>,
    /// e.g. <c>sin(iTime * K)</c> or <c>mod(iTime, K)</c>) — purely a
    /// heuristic, never guaranteed for complex shaders. Drives the
    /// visibility of the "Detected loop period" hint in the panel; recomputed
    /// once per shader load, never re-run automatically afterwards.
    /// </summary>
    [ObservableProperty]
    private bool _hasDetectedLoopPeriod;

    /// <summary>
    /// The suggested native loop period (in seconds), as returned by
    /// <see cref="Videotoy.Core.LoopPeriodDetector.detectLoopPeriod"/>'s
    /// <c>SuggestedCandidate</c> — the longest period among every simple
    /// periodic pattern detected in the shader's source. Only meaningful
    /// while <see cref="HasDetectedLoopPeriod"/> is true. Never applied
    /// automatically: <see cref="ApplyDetectedLoopPeriodCommand"/> is the
    /// only way it reaches <see cref="LoopDurationSeconds"/>, and only when
    /// the user explicitly triggers it.
    /// </summary>
    [ObservableProperty]
    private double _detectedLoopPeriodSeconds;

    /// <summary>
    /// True when more than one independent periodic pattern was found in
    /// the shader's source (e.g. a slow rotation and a fast flicker), so the
    /// panel can note that the suggested period is the longest detected one
    /// among several, not the only candidate found.
    /// </summary>
    [ObservableProperty]
    private bool _hasMultipleDetectedLoopPeriods;

    /// <summary>
    /// Short human-readable description of where
    /// <see cref="DetectedLoopPeriodSeconds"/> came from (source expression
    /// and pass name), shown alongside the suggestion so the user can judge
    /// for themselves whether it's trustworthy before applying it.
    /// </summary>
    [ObservableProperty]
    private string _detectedLoopPeriodSourceText = string.Empty;

    /// <summary>
    /// True when the export duration mode is "Seamless loop"
    /// (<see cref="LoopDurationSeconds"/>), false for "Manual duration"
    /// (<see cref="ManualDurationValue"/> / <see cref="ManualDurationUnit"/>).
    /// Bound to the two radio buttons in the "Duration Mode" card.
    /// </summary>
    [ObservableProperty]
    private bool _isSeamlessLoopModeEnabled;

    /// <summary>
    /// "Frame de fin exclusive" toggle, "Seamless loop" mode only. When true
    /// (default), the frame at <c>t = LoopDurationSeconds</c> — identical to
    /// the frame at <c>t = 0</c> of the next cycle — is never rendered, so
    /// looped playback never shows a duplicated frame at the seam. When
    /// false, that end frame is rendered and included in the export, one
    /// frame longer than the exclusive case. Fed into
    /// <see cref="DurationMode.NewSeamlessLoop"/>'s <c>excludeEndFrame</c>
    /// argument via <see cref="ResolveDurationMode"/>.
    /// </summary>
    [ObservableProperty]
    private bool _isLoopEndFrameExclusive = true;

    public IReadOnlyList<ResolutionPresetOption> ResolutionPresets => ResolutionPresetOption.All;

    public IReadOnlyList<FrameRatePresetOption> FrameRatePresets => FrameRatePresetOption.All;

    public IReadOnlyList<ExportKindOption> ExportKindOptions => ExportKindOption.All;

    public IReadOnlyList<AnimatedImageFormatOption> AnimatedImageFormatOptions => AnimatedImageFormatOption.All;

    public IReadOnlyList<GifDitherOption> GifDitherOptions => GifDitherOption.All;

    public bool IsVideoExportModeSelected => SelectedExportKind == ExportKindOption.Video;

    public bool IsAnimatedImageExportModeSelected => SelectedExportKind == ExportKindOption.AnimatedImage;

    public bool IsGifFormatSelected => SelectedAnimatedImageFormat == AnimatedImageFormatOption.Gif;

    public bool IsWebPFormatSelected => SelectedAnimatedImageFormat == AnimatedImageFormatOption.WebP;

    /// <summary>
    /// False when WebP lossless mode is enabled: <c>-lossless 1</c> takes no
    /// <c>-quality</c> flag, mirroring <see cref="IsAudioBitrateFieldVisible"/>'s
    /// show/hide precedent for a mode-dependent numeric field.
    /// </summary>
    public bool IsWebPQualitySectionVisible => IsWebPFormatSelected && !IsWebPLosslessEnabled;

    /// <summary>
    /// False while Animated Image export mode is selected: animated-image
    /// export has no "Manual duration" concept, so "Seamless loop" is forced
    /// on and its checkbox locked (disabled, not hidden — the user still
    /// sees it's active and why) rather than exposing a mode that would
    /// always fail validation.
    /// </summary>
    public bool IsSeamlessLoopModeToggleEnabled => !IsAnimatedImageExportModeSelected;

    /// <summary>
    /// Animated-image export carries no audio track: the Audio card is
    /// hidden in that mode even when the loaded shader declares an audio
    /// <c>iChannel</c> (<see cref="HasAudioChannel"/>).
    /// </summary>
    public bool IsAudioSectionVisible => HasAudioChannel && IsVideoExportModeSelected;

    public IReadOnlyList<ContainerFormatOption> ContainerFormatOptions => ContainerFormatOption.All;

    /// <summary>
    /// Video codec options applicable to <see cref="SelectedContainerFormat"/>,
    /// per <see cref="Videotoy.Core.ExportSettingsValidator.isCodecAllowedForContainer"/>
    /// (the single source of truth for the container↔codec matrix — never
    /// duplicated here).
    /// </summary>
    public IReadOnlyList<VideoCodecOption> VideoCodecOptions =>
        VideoCodecOption.All
            .Where(option => Videotoy.Core.ExportSettingsValidator.isCodecAllowedForContainer(SelectedContainerFormat.Value, option.Value))
            .ToList();

    public IReadOnlyList<SpeedPresetOption> SpeedPresetOptions => SpeedPresetOption.All;

    /// <summary>
    /// Video profile options applicable to <see cref="SelectedVideoCodec"/>:
    /// <see cref="VideoProfileOption.None"/> plus whichever codec-specific
    /// profile entries match <see cref="SelectedVideoCodec"/>'s key.
    /// </summary>
    public IReadOnlyList<VideoProfileOption> VideoProfileOptions =>
        VideoProfileOption.All
            .Where(option => option == VideoProfileOption.None || option.CodecKey == SelectedVideoCodec.Key)
            .ToList();

    public IReadOnlyList<HardwareEncoderOption> HardwareEncoderOptions => HardwareEncoderOption.All;

    public IReadOnlyList<AudioCodecOption> AudioCodecOptions => AudioCodecOption.AllowedFor(SelectedContainerFormat);

    /// <summary>
    /// False for <see cref="AudioCodecOption.Copy"/>, which re-uses the
    /// source audio track's bitrate as-is and therefore ignores
    /// <see cref="AudioBitrateKbps"/> entirely (FFmpeg's <c>-c:a copy</c>
    /// accepts no <c>-b:a</c> flag).
    /// </summary>
    public bool IsAudioBitrateFieldVisible => SelectedAudioCodec != AudioCodecOption.Copy;

    /// <summary>
    /// ProRes (<see cref="VideoCodecOption.ProRes"/>) has no rate-control
    /// concept: its output size is fully determined by profile, not by a
    /// quality/bitrate flag — the CRF/target-bitrate section is hidden
    /// entirely for it rather than shown disabled.
    /// </summary>
    public bool IsRateControlSectionVisible => SelectedVideoCodec != VideoCodecOption.ProRes;

    /// <summary>ProRes is intra-only: no GOP-size concept.</summary>
    public bool IsGopSectionVisible => SelectedVideoCodec != VideoCodecOption.ProRes;

    /// <summary>ProRes is intra-only: no multi-pass concept.</summary>
    public bool IsTwoPassCheckboxVisible => SelectedVideoCodec != VideoCodecOption.ProRes;

    /// <summary>ProRes has no encoding-speed-preset concept.</summary>
    public bool IsSpeedPresetVisible => SelectedVideoCodec != VideoCodecOption.ProRes;

    /// <summary>
    /// No hardware encoder support exists for VP9/ProRes in this pipeline —
    /// the whole "Hardware encoder" section is hidden for them rather than
    /// left visible with a dead software-only choice.
    /// </summary>
    public bool IsHardwareEncoderSectionVisible =>
        SelectedVideoCodec == VideoCodecOption.H264 || SelectedVideoCodec == VideoCodecOption.H265;

    private RateControlMode ResolveRateControlMode() =>
        IsTargetBitrateModeEnabled
            ? RateControlMode.NewTargetBitrate(TargetBitrateKbps)
            : RateControlMode.NewConstantRateFactor(ConstantRateFactorValue);

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private int _exportCurrentFrame;

    [ObservableProperty]
    private int _exportTotalFrames;

    [ObservableProperty]
    private double _exportProgressPercent;

    [ObservableProperty]
    private string _exportRemainingTimeText = string.Empty;

    [ObservableProperty]
    private bool _hasExportError;

    [ObservableProperty]
    private string _exportErrorSummary = string.Empty;

    /// <summary>
    /// Mode "petite config" : introduit un throttling volontaire entre chaque
    /// frame rendue pendant l'export, pour ne pas saturer le CPU/GPU sur une
    /// machine modeste. L'export reste séquentiel et sans perte de frame dans
    /// les deux cas ; seul le rythme change.
    /// </summary>
    [ObservableProperty]
    private bool _isLowSpecModeEnabled;

    /// <summary>
    /// Top-level toggle between the classic video pipeline
    /// (<see cref="VideoExportPipeline"/>) and the animated-image pipeline
    /// (<see cref="AnimatedImageExportPipeline"/>) — the two share no
    /// settings and are mutually exclusive: exactly one of
    /// <see cref="ExportVideoCommand"/>/<see cref="ExportAnimatedImageCommand"/>
    /// is available at a time.
    /// </summary>
    [ObservableProperty]
    private ExportKindOption _selectedExportKind = ExportKindOption.Video;

    [ObservableProperty]
    private AnimatedImageFormatOption _selectedAnimatedImageFormat = AnimatedImageFormatOption.Gif;

    [ObservableProperty]
    private int _gifColorCount = 256;

    [ObservableProperty]
    private GifDitherOption _selectedGifDither = GifDitherOption.FloydSteinberg;

    [ObservableProperty]
    private int _webPQuality = 80;

    [ObservableProperty]
    private bool _isWebPLosslessEnabled;

    [ObservableProperty]
    private ContainerFormatOption _selectedContainerFormat = ContainerFormatOption.Mp4;

    [ObservableProperty]
    private VideoCodecOption _selectedVideoCodec = VideoCodecOption.H264;

    [ObservableProperty]
    private bool _isTargetBitrateModeEnabled;

    [ObservableProperty]
    private int _targetBitrateKbps = 8000;

    [ObservableProperty]
    private int _constantRateFactorValue = 18;

    [ObservableProperty]
    private SpeedPresetOption _selectedSpeedPreset = SpeedPresetOption.Medium;

    [ObservableProperty]
    private VideoProfileOption _selectedVideoProfile = VideoProfileOption.None;

    [ObservableProperty]
    private bool _isGopSizeEnabled;

    [ObservableProperty]
    private int _gopSizeValue = 250;

    /// <summary>
    /// Two-pass FFmpeg encoding: only meaningful in target-bitrate mode
    /// (<see cref="IsTargetBitrateModeEnabled"/>) — a CRF export already
    /// reaches its target quality in a single pass. Doubles export wall-clock
    /// time, since the deterministic frame sequence is rendered twice (see
    /// <see cref="VideoExportPipeline.RunAsync"/>).
    /// </summary>
    [ObservableProperty]
    private bool _isTwoPassEnabled;

    [ObservableProperty]
    private HardwareEncoderOption _selectedHardwareEncoder = HardwareEncoderOption.Software;

    [ObservableProperty]
    private AudioCodecOption _selectedAudioCodec = AudioCodecOption.Aac;

    [ObservableProperty]
    private int _audioBitrateKbps = 192;

    /// <summary>
    /// Vrai lorsque le shader actuellement chargé déclare au moins un
    /// <c>iChannel</c> audio (<c>Music</c>/<c>MusicStream</c>) dont le
    /// fichier source résolu existe sur disque — c'est-à-dire lorsqu'une
    /// piste audio peut effectivement être muxée à l'export. Piloté par
    /// <see cref="ResolveExportAudioSourceFilePath"/>, recalculé à chaque
    /// chargement de shader. Contrôle l'affichage de l'option « Vidéo sans
    /// son » / « Vidéo avec son » dans le panneau de paramètres de rendu.
    /// </summary>
    [ObservableProperty]
    private bool _hasAudioChannel;

    /// <summary>
    /// Choix de l'utilisateur d'inclure ou non la piste audio détectée dans
    /// la vidéo exportée, lorsque <see cref="HasAudioChannel"/> est vrai.
    /// Actif par défaut : quand un shader avec `iChannel` audio est chargé,
    /// l'export inclut le son sauf décision explicite contraire. Sans effet
    /// tant qu'aucune piste audio n'est détectée.
    /// </summary>
    [ObservableProperty]
    private bool _includeAudioInExport = true;

    private const int LowSpecThrottleMillisecondsPerFrame = 50;

    public ObservableCollection<RecentShaderFile> RecentShaders { get; } = new();

    public ObservableCollection<ShaderIssueViewModel> ShaderIssues { get; } = new();

    public ObservableCollection<ExportPreset> ExportPresets { get; } = new();

    public ObservableCollection<ExportHistoryEntry> ExportHistory { get; } = new();

    public bool HasExportHistory => ExportHistory.Count > 0;

    /// <summary>
    /// File d'attente de rendu (export par lots) — persistée par
    /// <see cref="RenderQueueService"/> et traitée séquentiellement par
    /// <see cref="RenderQueueProcessor"/>. Chargée depuis le disque au
    /// démarrage (voir <see cref="ReloadRenderQueue"/>) ; les miniatures ne
    /// sont générées que lorsque le panneau est ouvert pour la première
    /// fois, afin de ne jamais ralentir le démarrage proportionnellement à
    /// la taille de la file restaurée.
    /// </summary>
    public ObservableCollection<RenderQueueItemViewModel> RenderQueue { get; } = new();

    public bool HasRenderQueueItems => RenderQueue.Count > 0;

    [ObservableProperty]
    private bool _isRenderQueueRunning;

    [ObservableProperty]
    private bool _isRenderQueuePaused;

    [ObservableProperty]
    private int _renderQueueCurrentItemIndex;

    [ObservableProperty]
    private int _renderQueueTotalItemCount;

    [ObservableProperty]
    private double _renderQueueOverallProgressPercent;

    private bool _renderQueueThumbnailsGenerated;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    /// <summary>
    /// Currently visible toast notifications (export success/failure, etc.),
    /// newest last. Each entry is removed automatically after
    /// <see cref="ToastDisplayDuration"/> via <see cref="ShowToast"/>'s
    /// dispatcher timer, or immediately via <see cref="DismissToastCommand"/>
    /// if the user closes it early.
    /// </summary>
    public ObservableCollection<ToastNotificationViewModel> Toasts { get; } = new();

    /// <summary>
    /// Groupes de sliders (un groupe par uniform custom, un slider par
    /// composant) reflétant les uniforms custom exposés par le shader
    /// actuellement chargé — voir <see cref="Videotoy.Core.CustomUniformParser"/>
    /// et <see cref="MultiPassRenderer.CustomUniformDeclarations"/>.
    /// Reconstruite entièrement à chaque chargement de shader ; vide tant
    /// qu'aucun shader n'expose d'uniform custom.
    /// </summary>
    public ObservableCollection<CustomUniformGroupViewModel> CustomUniformGroups { get; } = new();

    /// <summary>
    /// Vrai lorsque le shader chargé expose au moins un uniform custom,
    /// pilote l'affichage de la card "Custom Uniforms" du panneau de
    /// paramètres de rendu.
    /// </summary>
    [ObservableProperty]
    private bool _hasCustomUniforms;

    /// <summary>
    /// Un élément par channel vidéo détecté dans le shader actuellement
    /// chargé — voir <see cref="Videotoy.Core.ShaderModel.channelVideoPath"/>.
    /// Reconstruite entièrement à chaque chargement de shader ; vide tant
    /// qu'aucun shader ne référence de source vidéo.
    /// </summary>
    public ObservableCollection<VideoChannelViewModel> VideoChannels { get; } = new();

    /// <summary>
    /// Vrai lorsque le shader chargé référence au moins un channel vidéo,
    /// pilote l'affichage de la card "Video Channels" du panneau de
    /// paramètres de rendu.
    /// </summary>
    [ObservableProperty]
    private bool _hasVideoChannels;

    /// <summary>
    /// Non bloquant : au moins un channel vidéo dont la durée sondée ne
    /// correspond pas à <see cref="LoopDurationSeconds"/> en mode Boucle
    /// parfaite. Purement informatif — un channel vidéo boucle/se fige
    /// légitimement quel que soit l'écart, selon son
    /// <see cref="VideoTimeMappingOption"/>.
    /// </summary>
    [ObservableProperty]
    private bool _hasVideoLoopDurationMismatch;

    [ObservableProperty]
    private string _videoLoopDurationMismatchSummary = string.Empty;

    /// <summary>
    /// Preset currently selected in the "Load preset" combo box. Loading
    /// (<see cref="LoadExportPresetCommand"/>) applies it to every panel
    /// input it covers; it does not itself change any setting until the
    /// command runs.
    /// </summary>
    [ObservableProperty]
    private ExportPreset? _selectedExportPreset;

    /// <summary>
    /// Name typed into the "Save preset" field. Saving
    /// (<see cref="SaveExportPresetCommand"/>) replaces any existing preset
    /// with the same name (case-insensitive) rather than accumulating
    /// duplicates — see <see cref="ExportPresetService.SaveOrReplace"/>.
    /// </summary>
    [ObservableProperty]
    private string _newExportPresetName = string.Empty;

    public MainWindowViewModel(
        ShaderFileService shaderFileService,
        RecentFilesService recentFilesService,
        ExportPresetService exportPresetService,
        ExportHistoryService exportHistoryService,
        LoopSettingsService loopSettingsService,
        LocalizationService localizationService,
        PreviewMultiPassRenderer previewRenderer,
        ExportMultiPassRenderer exportRenderer,
        VideoExportPipeline exportPipeline,
        AnimatedImageExportPipeline animatedImageExportPipeline,
        AudioSpectrumTextureGenerator audioSpectrumTextureGenerator,
        VideoTextureLoader videoTextureLoader,
        BoundAssetsBuilder boundAssetsBuilder,
        RenderQueueService renderQueueService,
        RenderQueueProcessor renderQueueProcessor)
    {
        _shaderFileService = shaderFileService;
        _recentFilesService = recentFilesService;
        _exportPresetService = exportPresetService;
        _exportHistoryService = exportHistoryService;
        _loopSettingsService = loopSettingsService;
        _localizationService = localizationService;
        _previewRenderer = previewRenderer;
        _exportRenderer = exportRenderer;
        _exportPipeline = exportPipeline;
        _animatedImageExportPipeline = animatedImageExportPipeline;
        _audioSpectrumTextureGenerator = audioSpectrumTextureGenerator;
        _videoTextureLoader = videoTextureLoader;
        _boundAssetsBuilder = boundAssetsBuilder;
        _renderQueueService = renderQueueService;
        _renderQueueProcessor = renderQueueProcessor;
        _previewClock.LoopDurationSeconds = _loopDurationSeconds;
        _previewClock.TimeChanged += OnPreviewClockTimeChanged;

        _renderQueueProcessor.ItemProgressChanged += OnRenderQueueItemProgressChanged;
        _renderQueueProcessor.ItemStatusChanged += OnRenderQueueItemStatusChanged;
        _renderQueueProcessor.QueueCompleted += OnRenderQueueCompleted;

        _historyStack.StateChanged += OnHistoryStackStateChanged;

        ReloadRecentShaders();
        ReloadExportPresets();
        ReloadExportHistory();
        ReloadRenderQueue();
        RecalculateExportPreview();
    }

    /// <summary>
    /// Effective export resolution: the selected preset's fixed dimensions,
    /// or <see cref="CustomResolutionWidth"/>/<see cref="CustomResolutionHeight"/>
    /// when <see cref="SelectedResolutionPreset"/> is
    /// <see cref="ResolutionPresetOption.Custom"/>.
    /// </summary>
    private Resolution ResolveExportResolution() =>
        SelectedResolutionPreset.IsCustom
            ? new Resolution(Math.Max(0, CustomResolutionWidth), Math.Max(0, CustomResolutionHeight))
            : new Resolution(SelectedResolutionPreset.Width, SelectedResolutionPreset.Height);

    /// <summary>
    /// Effective export frame rate: the selected preset's fixed value, or
    /// <see cref="CustomFrameRateValue"/> when <see cref="SelectedFrameRatePreset"/>
    /// is <see cref="FrameRatePresetOption.Custom"/>.
    /// </summary>
    private FrameRate ResolveExportFrameRate() =>
        new(SelectedFrameRatePreset.IsCustom ? CustomFrameRateValue : SelectedFrameRatePreset.Value);

    /// <summary>
    /// Builds the effective <see cref="DurationMode"/> for the current panel
    /// state: "Seamless loop" always uses <see cref="LoopDurationSeconds"/>
    /// directly, while "Manual duration" converts <see cref="ManualDurationValue"/>
    /// from <see cref="ManualDurationUnit"/> (seconds or frames) into seconds
    /// against <paramref name="frameRate"/>.
    /// </summary>
    private DurationMode ResolveDurationMode(FrameRate frameRate)
    {
        if (IsSeamlessLoopModeEnabled)
        {
            return DurationMode.NewSeamlessLoop(LoopDurationSeconds, IsLoopEndFrameExclusive);
        }

        var seconds = ManualDurationUnit == DurationUnit.Frames && frameRate.Value > 0.0
            ? ManualDurationValue / frameRate.Value
            : ManualDurationValue;

        return DurationMode.NewManual(seconds);
    }

    /// <summary>
    /// Builds the effective <see cref="EncodingOptions"/> from the current
    /// render settings panel state (encoding speed preset, video profile,
    /// GOP size, two-pass mode, hardware encoder preference, audio
    /// codec/bitrate). Mirrors <see cref="ResolveExportResolution"/>/
    /// <see cref="ResolveDurationMode"/>'s "resolve effective value from
    /// panel state" pattern.
    /// </summary>
    private EncodingOptions ResolveEncodingOptions() =>
        new(
            SelectedSpeedPreset.Value,
            SelectedVideoProfile.Value,
            IsGopSizeEnabled ? Microsoft.FSharp.Core.FSharpOption<int>.Some(GopSizeValue) : Microsoft.FSharp.Core.FSharpOption<int>.None,
            IsTwoPassEnabled ? EncodingPassMode.TwoPass : EncodingPassMode.SinglePass,
            SelectedHardwareEncoder.Value,
            SelectedAudioCodec.Value,
            AudioBitrateKbps);

    /// <summary>
    /// Builds the effective <see cref="AnimatedImageEncodingOptions"/> from
    /// the current render settings panel state (GIF color count/dither,
    /// WebP quality/lossless). Mirrors <see cref="ResolveEncodingOptions"/>'s
    /// "resolve effective value from panel state" pattern.
    /// </summary>
    private AnimatedImageEncodingOptions ResolveAnimatedImageEncodingOptions() =>
        new(GifColorCount, SelectedGifDither.Value, WebPQuality, IsWebPLosslessEnabled);

    /// <summary>
    /// Applies <see cref="SuggestedLoopDurationSeconds"/> to
    /// <see cref="LoopDurationSeconds"/>, replacing the current loop duration
    /// with the nearest one that divides evenly into whole frames at the
    /// selected export frame rate — eliminating
    /// <see cref="HasLoopRoundingMismatch"/> entirely. Never invoked
    /// automatically: the mismatch warning offers this as an explicit,
    /// user-triggered action rather than a silent adjustment.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasLoopRoundingMismatch))]
    private void ApplyAssistedLoopRounding()
    {
        LoopDurationSeconds = SuggestedLoopDurationSeconds;
    }

    /// <summary>
    /// Applies <see cref="DetectedLoopPeriodSeconds"/> to
    /// <see cref="LoopDurationSeconds"/> — the only way the heuristically
    /// detected native loop period ever reaches an actual export setting.
    /// Purely a proposed, editable default: never applied automatically on
    /// shader load, and the user remains free to type any other value
    /// afterwards regardless of what was detected.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasDetectedLoopPeriod))]
    private void ApplyDetectedLoopPeriod()
    {
        LoopDurationSeconds = DetectedLoopPeriodSeconds;
    }

    [RelayCommand]
    private void BrowseOutputDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Multiselect = false,
            InitialDirectory = Directory.Exists(OutputDirectory)
                ? OutputDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            OutputDirectory = dialog.FolderName;
        }
    }

    /// <summary>
    /// Saves the current render settings panel state (resolution, frame
    /// rate, duration mode, low-spec mode — deliberately not the output
    /// folder/file name, which are per-export rather than part of a reusable
    /// preset) as an <see cref="ExportPreset"/> named
    /// <see cref="NewExportPresetName"/>. Replaces any existing preset with
    /// the same name. No-op if the name is blank.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveExportPreset))]
    private void SaveExportPreset()
    {
        var preset = new ExportPreset
        {
            Name = NewExportPresetName.Trim(),
            ResolutionPresetName = SelectedResolutionPreset.Key,
            CustomResolutionWidth = CustomResolutionWidth,
            CustomResolutionHeight = CustomResolutionHeight,
            FrameRatePresetName = SelectedFrameRatePreset.Key,
            CustomFrameRateValue = CustomFrameRateValue,
            IsSeamlessLoopModeEnabled = IsSeamlessLoopModeEnabled,
            ManualDurationUnit = ManualDurationUnit.ToString(),
            ManualDurationValue = ManualDurationValue,
            LoopDurationSeconds = LoopDurationSeconds,
            IsLoopEndFrameExclusive = IsLoopEndFrameExclusive,
            IsLowSpecModeEnabled = IsLowSpecModeEnabled,
            ContainerFormatKey = SelectedContainerFormat.Key,
            VideoCodecKey = SelectedVideoCodec.Key,
            IsTargetBitrateModeEnabled = IsTargetBitrateModeEnabled,
            TargetBitrateKbps = TargetBitrateKbps,
            ConstantRateFactorValue = ConstantRateFactorValue,
            SpeedPresetKey = SelectedSpeedPreset.Key,
            VideoProfileKey = SelectedVideoProfile.Key,
            IsGopSizeEnabled = IsGopSizeEnabled,
            GopSizeValue = GopSizeValue,
            IsTwoPassEnabled = IsTwoPassEnabled,
            HardwareEncoderKey = SelectedHardwareEncoder.Key,
            AudioCodecKey = SelectedAudioCodec.Key,
            AudioBitrateKbps = AudioBitrateKbps
        };

        _exportPresetService.SaveOrReplace(preset);
        ReloadExportPresets();
        SelectedExportPreset = ExportPresets.FirstOrDefault(
            entry => string.Equals(entry.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        NewExportPresetName = string.Empty;
        StatusMessage = $"Saved export preset '{preset.Name}'.";
    }

    private bool CanSaveExportPreset() => !string.IsNullOrWhiteSpace(NewExportPresetName);

    /// <summary>
    /// Applies <see cref="SelectedExportPreset"/> to every panel input it
    /// covers. Each setter goes through the normal property (rather than the
    /// backing field) so the usual <c>partial void On...Changed</c> hooks
    /// still fire and <see cref="RecalculateExportPreview"/> stays in sync.
    /// No-op if nothing is selected.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadExportPreset))]
    private void LoadExportPreset()
    {
        if (SelectedExportPreset is not { } preset)
        {
            return;
        }

        SelectedResolutionPreset = ResolutionPresetOption.FromKey(preset.ResolutionPresetName);
        CustomResolutionWidth = preset.CustomResolutionWidth;
        CustomResolutionHeight = preset.CustomResolutionHeight;
        SelectedFrameRatePreset = FrameRatePresetOption.FromKey(preset.FrameRatePresetName);
        CustomFrameRateValue = preset.CustomFrameRateValue;
        IsSeamlessLoopModeEnabled = preset.IsSeamlessLoopModeEnabled;
        ManualDurationUnit = Enum.TryParse<DurationUnit>(preset.ManualDurationUnit, out var unit)
            ? unit
            : DurationUnit.Seconds;
        ManualDurationValue = preset.ManualDurationValue;
        LoopDurationSeconds = preset.LoopDurationSeconds;
        IsLoopEndFrameExclusive = preset.IsLoopEndFrameExclusive;
        IsLowSpecModeEnabled = preset.IsLowSpecModeEnabled;
        SelectedContainerFormat = ContainerFormatOption.FromKey(preset.ContainerFormatKey);
        SelectedVideoCodec = VideoCodecOption.FromKey(preset.VideoCodecKey);
        IsTargetBitrateModeEnabled = preset.IsTargetBitrateModeEnabled;
        TargetBitrateKbps = preset.TargetBitrateKbps;
        ConstantRateFactorValue = preset.ConstantRateFactorValue;
        SelectedSpeedPreset = SpeedPresetOption.FromKey(preset.SpeedPresetKey);
        SelectedVideoProfile = VideoProfileOption.FromKey(preset.VideoProfileKey);
        IsGopSizeEnabled = preset.IsGopSizeEnabled;
        GopSizeValue = preset.GopSizeValue;
        IsTwoPassEnabled = preset.IsTwoPassEnabled;
        SelectedHardwareEncoder = HardwareEncoderOption.FromKey(preset.HardwareEncoderKey);
        SelectedAudioCodec = AudioCodecOption.FromKey(preset.AudioCodecKey);
        AudioBitrateKbps = preset.AudioBitrateKbps;

        StatusMessage = $"Loaded export preset '{preset.Name}'.";
    }

    private bool CanLoadExportPreset() => SelectedExportPreset is not null;

    [RelayCommand(CanExecute = nameof(CanLoadExportPreset))]
    private void DeleteExportPreset()
    {
        if (SelectedExportPreset is not { } preset)
        {
            return;
        }

        _exportPresetService.Delete(preset.Name);
        ReloadExportPresets();
        SelectedExportPreset = null;
        StatusMessage = $"Deleted export preset '{preset.Name}'.";
    }

    private void ReloadExportPresets()
    {
        ExportPresets.Clear();
        foreach (var preset in _exportPresetService.Load())
        {
            ExportPresets.Add(preset);
        }
    }

    /// <summary>
    /// Recomputes <see cref="EstimatedTotalFrames"/>,
    /// <see cref="EstimatedFileSizeText"/> and <see cref="HasLoopRoundingMismatch"/>
    /// from the current resolution/frame-rate/duration panel state. Called
    /// whenever any input feeding <see cref="LoopCalculator.computeFrameCount"/>
    /// or <see cref="ExportFileSizeEstimator.estimateFileSizeBytes"/> changes.
    /// </summary>
    private void RecalculateExportPreview()
    {
        var resolution = ResolveExportResolution();
        var frameRate = ResolveExportFrameRate();
        var durationMode = ResolveDurationMode(frameRate);

        var frameCountResult = CoreLoopCalculator.computeFrameCount(durationMode, frameRate);
        EstimatedTotalFrames = Math.Max(0, frameCountResult.FrameCount);
        HasLoopRoundingMismatch = IsSeamlessLoopModeEnabled && frameCountResult.HasRoundingMismatch;
        SuggestedLoopDurationSeconds = HasLoopRoundingMismatch
            ? CoreLoopCalculator.suggestAssistedLoopSeconds(LoopDurationSeconds, frameRate)
            : LoopDurationSeconds;

        UpdateVideoLoopDurationMismatch();

        if (IsAnimatedImageExportModeSelected)
        {
            var estimatedAnimatedImageBytes = CoreAnimatedImageFileSizeEstimator.estimateFileSizeBytes(
                resolution,
                frameRate,
                SelectedAnimatedImageFormat.Value,
                ResolveAnimatedImageEncodingOptions(),
                EstimatedTotalFrames);

            EstimatedFileSizeText = CoreAnimatedImageFileSizeEstimator.formatEstimatedFileSize(estimatedAnimatedImageBytes);
        }
        else
        {
            var estimatedBytes = CoreFileSizeEstimator.estimateFileSizeBytes(
                resolution,
                frameRate,
                ResolveRateControlMode(),
                SelectedVideoCodec.Value,
                ResolveEncodingOptions(),
                EstimatedTotalFrames,
                HasAudioChannel && IncludeAudioInExport);

            EstimatedFileSizeText = CoreFileSizeEstimator.formatEstimatedFileSize(estimatedBytes);
        }

        // Toute entrée qui affecte le nombre/le contenu des frames rendues
        // (résolution, fps, mode de durée, valeur/unité, frame de fin
        // exclusive) invalide une comparaison de raccord déjà affichée :
        // elle ne refléterait plus la configuration actuelle.
        HasLoopSeamPreview = false;
        GenerateLoopSeamPreviewCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Compare la durée sondée de chaque <see cref="VideoChannels"/> à
    /// <see cref="LoopDurationSeconds"/> lorsque "Boucle parfaite" est
    /// active, et publie un avertissement non bloquant en cas d'écart —
    /// même tolérance que <see cref="Core.LoopCalculator"/>'s notion
    /// d'arrondi de frame, ici appliquée à une simple comparaison de
    /// durées plutôt qu'à un nombre de frames exact.
    /// </summary>
    private void UpdateVideoLoopDurationMismatch()
    {
        const double toleranceSeconds = 0.05;

        if (!IsSeamlessLoopModeEnabled || VideoChannels.Count == 0)
        {
            HasVideoLoopDurationMismatch = false;
            VideoLoopDurationMismatchSummary = string.Empty;
            return;
        }

        var mismatched = VideoChannels
            .Where(channel => Math.Abs(channel.Source.Probe.DurationSeconds - LoopDurationSeconds) > toleranceSeconds)
            .ToList();

        HasVideoLoopDurationMismatch = mismatched.Count > 0;
        VideoLoopDurationMismatchSummary = mismatched.Count == 0
            ? string.Empty
            : string.Join("; ", mismatched.Select(channel =>
                $"{channel.DisplayLabel}: video is {channel.Source.Probe.DurationSeconds:0.00}s, loop is {LoopDurationSeconds:0.00}s"));
    }

    [RelayCommand]
    private void ToggleSettingsPanel()
    {
        IsSettingsPanelOpen = !IsSettingsPanelOpen;
    }

    [RelayCommand]
    private void ToggleExportHistoryPanel()
    {
        IsExportHistoryPanelOpen = !IsExportHistoryPanelOpen;
    }

    private void ReloadExportHistory()
    {
        ExportHistory.Clear();
        foreach (var entry in _exportHistoryService.Load())
        {
            ExportHistory.Add(entry);
        }

        OnPropertyChanged(nameof(HasExportHistory));
    }

    private void OnHistoryStackStateChanged(object? sender, EventArgs e)
    {
        CanUndo = _historyStack.CanUndo;
        CanRedo = _historyStack.CanRedo;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Ouvre une transaction d'historique si aucune n'est déjà en cours
    /// (profondeur 0 -&gt; 1) en capturant l'état "avant", puis incrémente la
    /// profondeur dans tous les cas. Appelée par chaque hook
    /// <c>On&lt;Prop&gt;Changing</c> des propriétés d'export undoable, ainsi que
    /// par <c>MainWindow.xaml.cs</c> au focus d'un <c>TextBox</c> undoable
    /// (regroupe toute la frappe jusqu'au <c>LostFocus</c> en une seule
    /// entrée d'historique, plutôt qu'une entrée par caractère tapé — les
    /// hooks <c>On&lt;Prop&gt;Changing</c> déclenchés pendant la frappe
    /// s'imbriquent alors dans cette transaction déjà ouverte sans en
    /// démarrer une nouvelle).
    /// </summary>
    internal void BeginHistoryTransaction()
    {
        if (_suppressHistoryCapture)
        {
            return;
        }

        if (_historyTransactionDepth == 0)
        {
            _historyTransactionBefore = Videotoy.App.History.ExportSettingsSnapshot.Capture(this);
        }

        _historyTransactionDepth++;
    }

    /// <summary>
    /// Décrémente la profondeur de transaction ; lorsqu'elle retombe à zéro,
    /// pousse une unique <see cref="Videotoy.App.History.ExportSettingsCommand"/>
    /// capturant tout ce qui a changé pendant la transaction (y compris les
    /// cascades), sauf si rien n'a réellement changé (égalité de record).
    /// </summary>
    internal void EndHistoryTransaction()
    {
        if (_suppressHistoryCapture)
        {
            return;
        }

        if (_historyTransactionDepth == 0)
        {
            return;
        }

        _historyTransactionDepth--;

        if (_historyTransactionDepth > 0)
        {
            return;
        }

        var before = _historyTransactionBefore;
        _historyTransactionBefore = null;

        if (before is null)
        {
            return;
        }

        var after = Videotoy.App.History.ExportSettingsSnapshot.Capture(this);
        if (before == after)
        {
            return;
        }

        _historyStack.Push(new Videotoy.App.History.ExportSettingsCommand(this, before, after));
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _suppressHistoryCapture = true;
        try
        {
            _historyStack.Undo();
        }
        finally
        {
            _suppressHistoryCapture = false;
        }

        RecalculateExportPreview();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _suppressHistoryCapture = true;
        try
        {
            _historyStack.Redo();
        }
        finally
        {
            _suppressHistoryCapture = false;
        }

        RecalculateExportPreview();
    }

    [RelayCommand]
    private void ToggleRenderQueuePanel()
    {
        IsRenderQueuePanelOpen = !IsRenderQueuePanelOpen;

        if (IsRenderQueuePanelOpen && !_renderQueueThumbnailsGenerated)
        {
            _renderQueueThumbnailsGenerated = true;
            GenerateMissingRenderQueueThumbnails();
        }
    }

    private void ReloadRenderQueue()
    {
        RenderQueue.Clear();
        foreach (var item in _renderQueueService.Load())
        {
            RenderQueue.Add(new RenderQueueItemViewModel(item));
        }

        OnPropertyChanged(nameof(HasRenderQueueItems));
        StartRenderQueueCommand.NotifyCanExecuteChanged();
    }

    private void GenerateMissingRenderQueueThumbnails()
    {
        if (IsRenderQueueRunning)
        {
            return;
        }

        foreach (var item in RenderQueue.Where(i => i.Thumbnail is null))
        {
            item.Thumbnail = _renderQueueProcessor.TryGenerateThumbnail(item.Model.ShaderFilePath);
        }
    }

    /// <summary>
    /// Ajoute l'état actuel du panneau de paramètres d'export à la file de
    /// rendu (voir <see cref="RenderQueueSettingsBuilder.CaptureFromCurrentPanelState"/>),
    /// sans démarrer immédiatement son traitement.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddCurrentToRenderQueue))]
    private void AddCurrentToRenderQueue(RenderQueueItemKind kind)
    {
        if (_loadedShaderFilePath is null)
        {
            return;
        }

        var item = RenderQueueSettingsBuilder.CaptureFromCurrentPanelState(
            this, _loadedShaderFilePath, LoadedShaderName, kind, Guid.NewGuid());

        _renderQueueService.Add(item);

        var itemViewModel = new RenderQueueItemViewModel(item)
        {
            Thumbnail = _renderQueueThumbnailsGenerated ? _renderQueueProcessor.TryGenerateThumbnail(item.ShaderFilePath) : null
        };
        RenderQueue.Add(itemViewModel);

        OnPropertyChanged(nameof(HasRenderQueueItems));
        StartRenderQueueCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveRenderQueueItem(RenderQueueItemViewModel item)
    {
        _renderQueueService.Remove(item.Id);
        RenderQueue.Remove(item);

        OnPropertyChanged(nameof(HasRenderQueueItems));
        StartRenderQueueCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ReorderRenderQueueItems(IReadOnlyList<Guid> orderedItemIds)
    {
        _renderQueueService.Reorder(orderedItemIds);

        var byId = RenderQueue.ToDictionary(item => item.Id);
        RenderQueue.Clear();
        foreach (var id in orderedItemIds)
        {
            if (byId.Remove(id, out var item))
            {
                RenderQueue.Add(item);
            }
        }

        foreach (var remaining in byId.Values)
        {
            RenderQueue.Add(remaining);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartRenderQueue))]
    private async Task StartRenderQueueAsync()
    {
        var models = RenderQueue.Select(item => item.Model).ToList();
        RenderQueueTotalItemCount = models.Count(item => item.Status == RenderQueueItemStatus.Pending);
        RenderQueueCurrentItemIndex = 0;
        IsRenderQueueRunning = true;

        try
        {
            await _renderQueueProcessor.StartAsync(models);
        }
        finally
        {
            IsRenderQueueRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRenderQueueRunning))]
    private void PauseRenderQueue()
    {
        _renderQueueProcessor.Pause();
        IsRenderQueuePaused = true;
    }

    [RelayCommand]
    private void ResumeRenderQueue()
    {
        _renderQueueProcessor.Resume();
        IsRenderQueuePaused = false;
    }

    [RelayCommand]
    private void CancelRenderQueueItem(RenderQueueItemViewModel item)
    {
        if (item.Status == RenderQueueItemStatus.Running)
        {
            _renderQueueProcessor.CancelCurrentItem();
        }
        else if (item.Status == RenderQueueItemStatus.Pending)
        {
            RemoveRenderQueueItem(item);
        }
    }

    [RelayCommand(CanExecute = nameof(IsRenderQueueRunning))]
    private void CancelRenderQueue()
    {
        _renderQueueProcessor.CancelAll();
    }

    private void OnRenderQueueItemProgressChanged(object? sender, RenderQueueItemProgressEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var itemViewModel = RenderQueue.FirstOrDefault(item => item.Id == e.ItemId);
            if (itemViewModel is not null)
            {
                itemViewModel.ProgressPercent = e.Progress.ProgressFraction * 100.0;
            }

            RenderQueueCurrentItemIndex = e.ItemIndex + 1;
            RenderQueueTotalItemCount = e.TotalItems;
            RenderQueueOverallProgressPercent =
                e.TotalItems <= 0 ? 0.0 : (e.ItemIndex + e.Progress.ProgressFraction) / e.TotalItems * 100.0;

            ExportCurrentFrame = e.Progress.CurrentFrameNumber;
            ExportTotalFrames = e.Progress.TotalFrameCount;
            ExportProgressPercent = e.Progress.ProgressFraction * 100.0;
            ExportRemainingTimeText = FormatRemainingTime(e.Progress.EstimatedRemainingSeconds);
        });
    }

    private void OnRenderQueueItemStatusChanged(object? sender, RenderQueueItemStatusEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var itemViewModel = RenderQueue.FirstOrDefault(item => item.Id == e.ItemId);
            if (itemViewModel is not null)
            {
                itemViewModel.Status = e.Status;
                itemViewModel.ErrorSummary = e.ErrorSummary;
                itemViewModel.ProgressPercent = e.Status is RenderQueueItemStatus.Succeeded ? 100.0 : itemViewModel.ProgressPercent;
            }

            StartRenderQueueCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnRenderQueueCompleted(object? sender, RenderQueueCompletedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsRenderQueueRunning = false;
            IsRenderQueuePaused = false;
            RenderQueueOverallProgressPercent = 0.0;

            var summary = _localizationService.GetFormattedString(
                "toast.renderQueue.completed.message", e.Succeeded, e.Failed);

            ShowToast(
                e.Failed > 0 ? ToastSeverity.Error : ToastSeverity.Success,
                _localizationService.GetString("toast.renderQueue.completed.title"),
                summary);

            StartRenderQueueCommand.NotifyCanExecuteChanged();
        });
    }

    [RelayCommand]
    private void OpenShader()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Shader files|" + string.Join(";", OpenFileDialogExtensions) + "|All files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadShaderFile(dialog.FileName);
        }
    }

    [RelayCommand]
    private void OpenRecentShader(string filePath)
    {
        LoadShaderFile(filePath);
    }

    [RelayCommand]
    private void CloseIssuesPanel()
    {
        IsIssuesPanelOpen = false;
    }

    [RelayCommand(CanExecute = nameof(IsShaderLoaded))]
    private void TogglePlayback()
    {
        _previewClock.TogglePlayback();
        IsPlaying = _previewClock.IsPlaying;
    }

    [RelayCommand(CanExecute = nameof(IsShaderLoaded))]
    private void StopPlayback()
    {
        _previewClock.Stop();
        IsPlaying = false;
        RenderCurrentFrame();
    }

    /// <summary>
    /// Débute un scrub manuel de la timeline : met la lecture en pause pendant
    /// que l'utilisateur fait glisser le curseur, sans perdre l'état "lecture en
    /// cours" une fois le scrub terminé.
    /// </summary>
    [RelayCommand]
    private void BeginScrub()
    {
        _isScrubbing = true;
        _previewClock.Pause();
    }

    /// <summary>
    /// Débute un geste d'édition d'un slider d'uniform custom (glisser ou
    /// modification atomique isolée) : capture l'état "avant" de tous les
    /// sliders, pour que <see cref="EndCustomUniformEdit"/> puisse pousser
    /// une unique entrée d'historique couvrant tout le geste plutôt qu'une
    /// entrée par changement de valeur (le binding XAML utilise
    /// <c>UpdateSourceTrigger=PropertyChanged</c> et déclenche donc un
    /// changement par tick de glissement).
    /// </summary>
    [RelayCommand]
    private void BeginCustomUniformEdit()
    {
        _customUniformEditBefore = CaptureCustomUniformValues();
    }

    /// <summary>
    /// Termine le geste d'édition en cours : pousse une unique
    /// <see cref="Videotoy.App.History.CustomUniformsCommand"/> si au moins
    /// une valeur a changé depuis <see cref="BeginCustomUniformEdit"/>.
    /// Se comporte comme un no-op si <c>Begin</c> n'a pas été appelé (ex.
    /// modification déclenchée au clavier plutôt qu'au glisser).
    /// </summary>
    [RelayCommand]
    private void EndCustomUniformEdit()
    {
        var before = _customUniformEditBefore;
        _customUniformEditBefore = null;

        if (before is null || _suppressHistoryCapture)
        {
            return;
        }

        var after = CaptureCustomUniformValues();
        if (before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var value) && value == pair.Value))
        {
            return;
        }

        _historyStack.Push(new Videotoy.App.History.CustomUniformsCommand(this, before, after));
    }

    private Dictionary<(string GroupName, int ComponentIndex), float> CaptureCustomUniformValues()
    {
        var values = new Dictionary<(string GroupName, int ComponentIndex), float>();
        foreach (var group in CustomUniformGroups)
        {
            foreach (var slider in group.Sliders)
            {
                values[(group.Name, slider.ComponentIndex)] = slider.Value;
            }
        }

        return values;
    }

    [RelayCommand]
    private void EndScrub()
    {
        _isScrubbing = false;
        if (IsPlaying)
        {
            _previewClock.Play();
        }
    }

    /// <summary>
    /// Positionne la lecture sur une valeur de timeline arbitraire (drag du curseur
    /// de progression), en secondes.
    /// </summary>
    public void Seek(double timeSeconds)
    {
        _previewClock.Seek(timeSeconds);
    }

    /// <summary>
    /// Renders the first (t = 0) and last (index EstimatedTotalFrames - 1)
    /// frames of the current "Seamless loop" configuration side by side,
    /// letting the user visually validate the loop seam before committing to
    /// a full export. Only enabled while a shader is loaded and "Seamless
    /// loop" duration mode is selected.
    /// </summary>
    /// <remarks>
    /// Walks <see cref="_previewRenderer"/> through the entire loop timeline
    /// (frame 0 through the last exported frame) — not just those two frames
    /// in isolation — because a shader with a self-referencing buffer (Buffer
    /// A/B/C/D ping-pong feedback) accumulates state frame by frame; jumping
    /// straight to the last frame's timestamp without having rendered every
    /// frame before it would show a buffer state the real export never
    /// produces. This uses the same deterministic timeline construction as
    /// the actual export (<see cref="CoreLoopCalculator.computeFrameCount"/> /
    /// <see cref="CoreLoopCalculator.buildFrameTimeline"/>), at the fixed
    /// preview resolution/renderer rather than the export resolution/renderer,
    /// so the comparison is representative of the loop's motion without
    /// paying the cost of a full-resolution render.
    ///
    /// Runs synchronously on the UI thread rather than on a background task:
    /// <see cref="_previewRenderer"/> owns a single, non-thread-safe D3D11
    /// device shared with the live viewport, so off-thread rendering would
    /// risk a device-context race with the live playback loop
    /// (<see cref="AdvancePreview"/>) rather than actually speeding anything
    /// up. For a long loop at a high frame rate this can briefly freeze the
    /// panel; <see cref="IsGeneratingLoopSeamPreview"/> at least disables the
    /// button and swaps its label to "Generating..." so the freeze reads as
    /// expected rather than as a hang.
    ///
    /// Advancing the shared <see cref="_previewRenderer"/> through this
    /// sequence leaves its internal ping-pong buffer state at the loop's end
    /// rather than at the live playback position; once done, the current
    /// live-playback frame is re-rendered immediately afterward so the
    /// viewport's next displayed frame is visually correct again (the ping-
    /// pong buffers briefly reflect a different frame index than
    /// <see cref="CurrentFrame"/>, which only affects self-referencing
    /// buffers' feedback for one further live frame and self-corrects
    /// immediately, since every subsequent live frame re-renders from the
    /// buffers' actual current contents).
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanGenerateLoopSeamPreview))]
    private void GenerateLoopSeamPreview()
    {
        if (_loadedShader is null)
        {
            return;
        }

        IsGeneratingLoopSeamPreview = true;
        HasLoopSeamPreview = false;

        try
        {
            var frameRate = ResolveExportFrameRate();
            var durationMode = ResolveDurationMode(frameRate);
            var frameCountResult = CoreLoopCalculator.computeFrameCount(durationMode, frameRate);

            if (frameCountResult.FrameCount <= 0)
            {
                return;
            }

            var timeline = CoreLoopCalculator.buildFrameTimeline(frameCountResult.FrameCount, frameRate);

            byte[]? startPixels = null;
            byte[]? endPixels = null;

            foreach (var frame in timeline)
            {
                var pixels = _previewRenderer.RenderFrame(frame.TimeSeconds, frame.DeltaSeconds, frame.Index);

                if (frame.Index == 0)
                {
                    startPixels = pixels;
                }

                if (frame.Index == frameCountResult.FrameCount - 1)
                {
                    endPixels = pixels;
                }
            }

            if (startPixels is { Length: > 0 } && endPixels is { Length: > 0 })
            {
                LoopSeamStartFrameImageSource = CreatePreviewBitmap(startPixels);
                LoopSeamEndFrameImageSource = CreatePreviewBitmap(endPixels);
                HasLoopSeamPreview = true;
            }
        }
        finally
        {
            // Resynchronise le viewport live sur sa position de lecture réelle
            // avant de rendre la main : le renderer partagé vient d'avancer à
            // travers toute la timeline de boucle pour générer la comparaison.
            RenderCurrentFrame();
            IsGeneratingLoopSeamPreview = false;
        }
    }

    private bool CanGenerateLoopSeamPreview() =>
        IsShaderLoaded && IsSeamlessLoopModeEnabled && !IsExporting && !IsGeneratingLoopSeamPreview;

    internal static WriteableBitmap CreatePreviewBitmap(byte[] pixelsRgba)
    {
        var bitmap = new WriteableBitmap(
            RenderTargetSize.PreviewDefault.Width,
            RenderTargetSize.PreviewDefault.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null);

        bitmap.WritePixels(
            new System.Windows.Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight),
            pixelsRgba,
            bitmap.PixelWidth * 4,
            0);

        return bitmap;
    }

    [RelayCommand(CanExecute = nameof(CanExportVideo))]
    private async Task ExportVideoAsync()
    {
        if (_loadedShader is null)
        {
            return;
        }

        var frameRate = ResolveExportFrameRate();
        var exportSettings = new ExportSettings(
            ResolveExportResolution(),
            frameRate,
            ResolveDurationMode(frameRate),
            OutputDirectory,
            OutputFileName,
            SelectedVideoCodec.Value,
            ResolveRateControlMode(),
            SelectedContainerFormat.Value,
            IsLowSpecModeEnabled
                ? PerformanceMode.NewLowSpec(LowSpecThrottleMillisecondsPerFrame)
                : PerformanceMode.Normal,
            ResolveEncodingOptions());

        var validationIssues = Videotoy.Core.ExportSettingsValidator.validate(exportSettings);
        if (!validationIssues.IsEmpty)
        {
            HasExportError = true;
            ExportErrorSummary = DescribeFirstValidationIssue(validationIssues);
            StatusMessage = $"Export failed: {ExportErrorSummary}";
            ShowToast(
                ToastSeverity.Error,
                _localizationService.GetString("toast.export.error.title"),
                ExportErrorSummary);
            return;
        }

        var outputFilePath = Videotoy.Core.ExportSettingsValidator.resolveOutputFilePath(exportSettings);

        IsExporting = true;
        ExportCurrentFrame = 0;
        ExportTotalFrames = 0;
        ExportProgressPercent = 0.0;
        ExportRemainingTimeText = string.Empty;
        HasExportError = false;
        ExportErrorSummary = string.Empty;
        StatusMessage = "Exporting...";

        _exportCancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<VideoExportProgress>(OnExportProgress);
        var audioSourceFilePath = HasAudioChannel && IncludeAudioInExport
            ? ResolveExportAudioSourceFilePath(_loadedShader)
            : null;

        var encodingStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var historyResult = ExportHistoryResult.Failed;
        string? historyErrorSummary = null;

        try
        {
            var (images, audioTracks, videoSources) = BuildBoundAssets(_loadedShader);
            _exportRenderer.Initialize(
                new RenderTargetSize(exportSettings.Resolution.Width, exportSettings.Resolution.Height),
                _loadedShader.Project,
                _loadedShader.HlslPasses,
                images,
                audioTracks,
                videoSources);

            await _exportPipeline.RunAsync(
                exportSettings,
                progress,
                _exportCancellationTokenSource.Token,
                audioSourceFilePath,
                onRetry: attempt => StatusMessage = $"Retrying export (attempt {attempt})...");

            historyResult = ExportHistoryResult.Succeeded;
            StatusMessage = $"Export complete: {outputFilePath}";
            ShowToast(
                ToastSeverity.Success,
                _localizationService.GetString("toast.export.success.title"),
                outputFilePath);
        }
        catch (OperationCanceledException)
        {
            historyResult = ExportHistoryResult.Cancelled;
            StatusMessage = "Export cancelled.";
        }
        catch (FfmpegEncodingException ex)
        {
            historyResult = ExportHistoryResult.Failed;
            historyErrorSummary = ex.Diagnosis.Summary;
            HasExportError = true;
            ExportErrorSummary = ex.Diagnosis.Summary;
            StatusMessage = $"Export failed: {ex.Diagnosis.Summary}";
            ShowToast(
                ToastSeverity.Error,
                _localizationService.GetString("toast.export.error.title"),
                ex.Diagnosis.Summary);
        }
        catch (Exception ex)
        {
            historyResult = ExportHistoryResult.Failed;
            historyErrorSummary = ex.Message;
            HasExportError = true;
            ExportErrorSummary = ex.Message;
            StatusMessage = $"Export failed: {ex.Message}";
            ShowToast(
                ToastSeverity.Error,
                _localizationService.GetString("toast.export.error.title"),
                ex.Message);
        }
        finally
        {
            encodingStopwatch.Stop();
            AppendExportHistoryEntry(exportSettings, outputFilePath, encodingStopwatch.Elapsed, historyResult, historyErrorSummary);

            IsExporting = false;
            _exportCancellationTokenSource?.Dispose();
            _exportCancellationTokenSource = null;
        }
    }

    private void AppendExportHistoryEntry(
        ExportSettings exportSettings,
        string outputFilePath,
        TimeSpan encodingDuration,
        ExportHistoryResult result,
        string? errorSummary)
    {
        var crf = Videotoy.Core.ExportSettingsValidator.tryResolveConstantRateFactor(exportSettings.RateControl);
        var targetBitrateKbps = Videotoy.Core.ExportSettingsValidator.tryResolveTargetBitrateKbps(exportSettings.RateControl);
        var rateControlSummary = crf.HasValue
            ? $"CRF {crf.Value}"
            : $"{targetBitrateKbps.GetValueOrDefault()} kbps";

        var entry = new ExportHistoryEntry
        {
            ShaderFilePath = _loadedShaderFilePath ?? string.Empty,
            ShaderDisplayName = LoadedShaderName,
            OutputFilePath = outputFilePath,
            ResolutionWidth = exportSettings.Resolution.Width,
            ResolutionHeight = exportSettings.Resolution.Height,
            FrameRateValue = exportSettings.FrameRate.Value,
            DurationSeconds = Videotoy.Core.ExportSettingsValidator.resolveDurationSeconds(exportSettings),
            CodecName = Videotoy.Core.ExportSettingsValidator.resolveCodecName(exportSettings.Codec),
            RateControlSummary = rateControlSummary,
            SpeedPresetName = Videotoy.Core.ExportSettingsValidator.resolveSpeedPresetName(exportSettings.Encoding.Speed),
            HardwareEncoderKey = Videotoy.Core.ExportSettingsValidator.resolveHardwareEncoderPreferenceKey(exportSettings.Encoding.HardwareEncoder),
            EncodingDuration = encodingDuration,
            Result = result,
            ErrorSummary = errorSummary
        };

        _exportHistoryService.Append(entry);
        ExportHistory.Insert(0, entry);
        OnPropertyChanged(nameof(HasExportHistory));
    }

    [RelayCommand(CanExecute = nameof(CanExportAnimatedImage))]
    private async Task ExportAnimatedImageAsync()
    {
        if (_loadedShader is null)
        {
            return;
        }

        if (!IsSeamlessLoopModeEnabled)
        {
            // Filet de sécurité côté Core/ViewModel : l'UI verrouille déjà
            // "Boucle parfaite" en mode Image animée (voir
            // OnSelectedExportKindChanged), mais un preset chargé ou tout
            // autre chemin programmatique pourrait encore laisser
            // IsSeamlessLoopModeEnabled à faux.
            HasExportError = true;
            ExportErrorSummary = _localizationService.GetString("export.animatedImage.error.manualModeUnsupported");
            StatusMessage = $"Export failed: {ExportErrorSummary}";
            ShowToast(
                ToastSeverity.Error,
                _localizationService.GetString("toast.export.error.title"),
                ExportErrorSummary);
            return;
        }

        var frameRate = ResolveExportFrameRate();
        var exportSettings = new AnimatedImageExportSettings(
            ResolveExportResolution(),
            frameRate,
            LoopDurationSeconds,
            IsLoopEndFrameExclusive,
            OutputDirectory,
            OutputFileName,
            SelectedAnimatedImageFormat.Value,
            ResolveAnimatedImageEncodingOptions());

        var validationIssues = Videotoy.Core.AnimatedImageExportSettingsValidator.validate(exportSettings);
        if (!validationIssues.IsEmpty)
        {
            HasExportError = true;
            ExportErrorSummary = Videotoy.Core.AnimatedImageExportSettingsValidator.describeFirstIssue(validationIssues);
            StatusMessage = $"Export failed: {ExportErrorSummary}";
            ShowToast(
                ToastSeverity.Error,
                _localizationService.GetString("toast.export.error.title"),
                ExportErrorSummary);
            return;
        }

        var outputFilePath = Videotoy.Core.AnimatedImageExportSettingsValidator.resolveOutputFilePath(exportSettings);

        IsExporting = true;
        ExportCurrentFrame = 0;
        ExportTotalFrames = 0;
        ExportProgressPercent = 0.0;
        ExportRemainingTimeText = string.Empty;
        HasExportError = false;
        ExportErrorSummary = string.Empty;
        StatusMessage = "Exporting...";

        _exportCancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<VideoExportProgress>(OnExportProgress);

        var encodingStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var historyResult = ExportHistoryResult.Failed;
        string? historyErrorSummary = null;

        try
        {
            var (images, audioTracks, videoSources) = BuildBoundAssets(_loadedShader);
            _exportRenderer.Initialize(
                new RenderTargetSize(exportSettings.Resolution.Width, exportSettings.Resolution.Height),
                _loadedShader.Project,
                _loadedShader.HlslPasses,
                images,
                audioTracks,
                videoSources);

            await _animatedImageExportPipeline.RunAsync(exportSettings, progress, _exportCancellationTokenSource.Token);

            historyResult = ExportHistoryResult.Succeeded;
            StatusMessage = $"Export complete: {outputFilePath}";
            ShowToast(
                ToastSeverity.Success,
                _localizationService.GetString("toast.export.success.title"),
                outputFilePath);
        }
        catch (OperationCanceledException)
        {
            historyResult = ExportHistoryResult.Cancelled;
            StatusMessage = "Export cancelled.";
        }
        catch (FfmpegEncodingException ex)
        {
            historyResult = ExportHistoryResult.Failed;
            historyErrorSummary = ex.Diagnosis.Summary;
            HasExportError = true;
            ExportErrorSummary = ex.Diagnosis.Summary;
            StatusMessage = $"Export failed: {ex.Diagnosis.Summary}";
            ShowToast(
                ToastSeverity.Error,
                _localizationService.GetString("toast.export.error.title"),
                ex.Diagnosis.Summary);
        }
        catch (Exception ex)
        {
            historyResult = ExportHistoryResult.Failed;
            historyErrorSummary = ex.Message;
            HasExportError = true;
            ExportErrorSummary = ex.Message;
            StatusMessage = $"Export failed: {ex.Message}";
            ShowToast(
                ToastSeverity.Error,
                _localizationService.GetString("toast.export.error.title"),
                ex.Message);
        }
        finally
        {
            encodingStopwatch.Stop();
            AppendAnimatedImageExportHistoryEntry(exportSettings, outputFilePath, encodingStopwatch.Elapsed, historyResult, historyErrorSummary);

            IsExporting = false;
            _exportCancellationTokenSource?.Dispose();
            _exportCancellationTokenSource = null;
        }
    }

    private void AppendAnimatedImageExportHistoryEntry(
        AnimatedImageExportSettings exportSettings,
        string outputFilePath,
        TimeSpan encodingDuration,
        ExportHistoryResult result,
        string? errorSummary)
    {
        var rateControlSummary = exportSettings.Format == AnimatedImageFormat.Gif
            ? $"{exportSettings.Encoding.GifColorCount} colors, {SelectedGifDither.DisplayName}"
            : exportSettings.Encoding.WebPLossless
                ? "Lossless"
                : $"Quality {exportSettings.Encoding.WebPQuality}";

        var entry = new ExportHistoryEntry
        {
            ShaderFilePath = _loadedShaderFilePath ?? string.Empty,
            ShaderDisplayName = LoadedShaderName,
            OutputFilePath = outputFilePath,
            ResolutionWidth = exportSettings.Resolution.Width,
            ResolutionHeight = exportSettings.Resolution.Height,
            FrameRateValue = exportSettings.FrameRate.Value,
            DurationSeconds = exportSettings.LoopSeconds,
            CodecName = exportSettings.Format == AnimatedImageFormat.Gif ? "GIF" : "WebP",
            RateControlSummary = rateControlSummary,
            SpeedPresetName = string.Empty,
            HardwareEncoderKey = "software",
            EncodingDuration = encodingDuration,
            Result = result,
            ErrorSummary = errorSummary
        };

        _exportHistoryService.Append(entry);
        ExportHistory.Insert(0, entry);
        OnPropertyChanged(nameof(HasExportHistory));
    }

    /// <summary>
    /// Strips characters invalid in a Windows file name from the loaded
    /// shader's title, so it can be used as the default
    /// <see cref="OutputFileName"/> without the user having to edit it first.
    /// Falls back to <c>"export"</c> if nothing usable remains.
    /// </summary>
    private static string SanitizeAsFileName(string title)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(title.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "export" : sanitized;
    }

    private bool CanExportVideo() => IsShaderLoaded && !IsExporting && !IsRenderQueueRunning && IsVideoExportModeSelected;

    private bool CanExportAnimatedImage() => IsShaderLoaded && !IsExporting && !IsRenderQueueRunning && IsAnimatedImageExportModeSelected;

    private bool CanAddCurrentToRenderQueue() => IsShaderLoaded && !string.IsNullOrEmpty(_loadedShaderFilePath);

    private bool CanStartRenderQueue() =>
        !IsExporting && !IsRenderQueueRunning && RenderQueue.Any(item => item.Status == RenderQueueItemStatus.Pending);

    /// <summary>
    /// Turns the first <c>Videotoy.Core.ExportSettingsValidator.ExportSettingsIssue</c>
    /// reported by <c>validate</c> into a short, UI-ready sentence. Only the
    /// first issue is shown — consistent with how other validation summaries
    /// are surfaced in this panel (e.g. <see cref="ExportErrorSummary"/> for
    /// FFmpeg failures). The actual pattern matching lives in F# via
    /// <c>ExportSettingsValidator.describeFirstIssue</c>, since matching on
    /// F# discriminated union cases directly from C# is fragile.
    /// </summary>
    private static string DescribeFirstValidationIssue(
        Microsoft.FSharp.Collections.FSharpList<Videotoy.Core.ExportSettingsValidator.ExportSettingsIssue> issues)
    {
        return Videotoy.Core.ExportSettingsValidator.describeFirstIssue(issues);
    }

    /// <summary>
    /// Résout le chemin absolu du fichier audio source à muxer avec la vidéo
    /// exportée, lorsque le shader chargé possède un <c>iChannel</c> audio
    /// (type <c>Music</c> ou <c>MusicStream</c>). Le chemin déclaré dans le
    /// shader (<see cref="Videotoy.Core.ShaderModel.firstAudioChannelPath"/>)
    /// est résolu par rapport au dossier du fichier shader, exactement comme
    /// le fait déjà <see cref="ShaderFileService"/> au chargement. Ne
    /// détermine délibérément aucune durée : c'est
    /// <see cref="VideoExportPipeline.RunAsync"/> qui calcule la durée
    /// effective à partir du nombre de frames réellement rendu, pour rester
    /// strictement aligné sur la timeline de rendu déterministe même en mode
    /// boucle parfaite. Retourne <c>null</c> si le shader n'utilise aucune
    /// entrée audio, ou si le fichier résolu n'existe plus sur disque.
    /// </summary>
    private static string? ResolveExportAudioSourceFilePath(LoadedShader loadedShader) =>
        BoundAssetsBuilder.ResolveExportAudioSourceFilePath(loadedShader);

    /// <summary>
    /// Annule proprement l'export en cours : signale le token d'annulation,
    /// que <see cref="Videotoy.Ffmpeg.VideoExportPipeline"/> propage jusqu'à
    /// <see cref="Videotoy.Ffmpeg.FfmpegService.Cancel"/> pour tuer le
    /// process FFmpeg (et son arbre) et nettoyer ses ressources.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsExporting))]
    private void CancelExport()
    {
        _exportCancellationTokenSource?.Cancel();
        StatusMessage = "Cancelling export...";
    }

    private void OnExportProgress(VideoExportProgress progress)
    {
        ExportCurrentFrame = progress.CurrentFrameNumber;
        ExportTotalFrames = progress.TotalFrameCount;
        ExportProgressPercent = progress.ProgressFraction * 100.0;
        ExportRemainingTimeText = FormatRemainingTime(progress.EstimatedRemainingSeconds);
    }

    /// <summary>
    /// How long a toast stays visible before auto-dismissing itself. Kept
    /// short for success confirmations and errors alike, so the panel never
    /// accumulates stale notifications the user has to clear by hand.
    /// </summary>
    private static readonly TimeSpan ToastDisplayDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Queues a non-intrusive toast notification (<see cref="Toasts"/>) and
    /// schedules its automatic removal after <see cref="ToastDisplayDuration"/>.
    /// Used for export outcomes (success, cancellation is intentionally
    /// silent, and failure) so the user gets a clear, unobtrusive signal
    /// without a blocking dialog interrupting their workflow.
    /// </summary>
    private void ShowToast(ToastSeverity severity, string title, string message)
    {
        var toast = new ToastNotificationViewModel
        {
            Id = Guid.NewGuid(),
            Severity = severity,
            Title = title,
            Message = message
        };

        Toasts.Add(toast);

        var timer = new DispatcherTimer { Interval = ToastDisplayDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(toast);
        };
        timer.Start();
    }

    [RelayCommand]
    private void DismissToast(ToastNotificationViewModel toast)
    {
        Toasts.Remove(toast);
    }

    private string FormatRemainingTime(double? remainingSeconds)
    {
        if (remainingSeconds is not { } seconds || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return _localizationService.GetString("export.progress.estimating");
        }

        var remaining = TimeSpan.FromSeconds(Math.Max(0.0, seconds));
        return remaining.TotalHours >= 1.0
            ? remaining.ToString(@"h\:mm\:ss")
            : remaining.ToString(@"mm\:ss");
    }

    public void LoadShaderFile(string filePath)
    {
        if (!ShaderFileService.IsSupportedShaderFile(filePath))
        {
            StatusMessage = $"Unsupported file type: {filePath}";
            return;
        }

        _suppressHistoryCapture = true;
        try
        {
            _historyStack.Clear();

            var loadedShader = _shaderFileService.Load(filePath);
            _loadedShaderFilePath = filePath;

            ShaderIssues.Clear();
            foreach (var issue in loadedShader.Issues)
            {
                ShaderIssues.Add(ShaderIssueViewModel.FromIssue(issue));
            }

            IsIssuesPanelOpen = ShaderIssues.Count > 0;
            LoadedShaderName = loadedShader.Project.Title;
            IsShaderLoaded = true;
            OutputFileName = SanitizeAsFileName(loadedShader.Project.Title);

            HasLoopSeamPreview = false;
            LoopSeamStartFrameImageSource = null;
            LoopSeamEndFrameImageSource = null;

            ApplyLoopPeriodDetection(loadedShader.Project);
            RestoreLoopSettings(filePath);

            StatusMessage = loadedShader.HasErrors
                ? $"Loaded '{LoadedShaderName}' with errors."
                : $"Loaded '{LoadedShaderName}'.";

            _recentFilesService.AddOrPromote(filePath);
            ReloadRecentShaders();

            if (!loadedShader.HasErrors)
            {
                InitializePreview(loadedShader);
                HasAudioChannel = ResolveExportAudioSourceFilePath(loadedShader) is not null;
                IncludeAudioInExport = true;
            }
            else
            {
                HasAudioChannel = false;
                CustomUniformGroups.Clear();
                HasCustomUniforms = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load shader: {ex.Message}";
        }
        finally
        {
            _suppressHistoryCapture = false;
        }
    }

    /// <summary>
    /// Runs <see cref="Videotoy.Core.LoopPeriodDetector.detectLoopPeriod"/>
    /// against the just-loaded shader's source and updates
    /// <see cref="HasDetectedLoopPeriod"/> / <see cref="DetectedLoopPeriodSeconds"/>
    /// / <see cref="HasMultipleDetectedLoopPeriods"/> /
    /// <see cref="DetectedLoopPeriodSourceText"/> accordingly. Purely
    /// informational: never touches <see cref="LoopDurationSeconds"/> or
    /// <see cref="IsSeamlessLoopModeEnabled"/> itself — applying the
    /// suggestion is always an explicit, user-triggered action via
    /// <see cref="ApplyDetectedLoopPeriodCommand"/>.
    /// </summary>
    private void ApplyLoopPeriodDetection(Videotoy.Core.ShaderModel.ShaderProject project)
    {
        var detection = Videotoy.Core.LoopPeriodDetector.detectLoopPeriod(project);

        if (detection.SuggestedCandidate is { Value: { } candidate })
        {
            HasDetectedLoopPeriod = true;
            DetectedLoopPeriodSeconds = candidate.PeriodSeconds;
            HasMultipleDetectedLoopPeriods = detection.AllCandidates.Length > 1;
            DetectedLoopPeriodSourceText = $"{candidate.SourceExpression} ({candidate.PassName})";
        }
        else
        {
            HasDetectedLoopPeriod = false;
            DetectedLoopPeriodSeconds = 0.0;
            HasMultipleDetectedLoopPeriods = false;
            DetectedLoopPeriodSourceText = string.Empty;
        }

        ApplyDetectedLoopPeriodCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Restores the last-used "Duration Mode" state for
    /// <paramref name="filePath"/> from <see cref="_loopSettingsService"/>,
    /// if one was ever saved for this exact shader file. No-op — leaving the
    /// current panel defaults untouched — the first time a given shader file
    /// is opened.
    /// </summary>
    private void RestoreLoopSettings(string filePath)
    {
        var saved = _loopSettingsService.TryGet(filePath);
        if (saved is null)
        {
            return;
        }

        IsSeamlessLoopModeEnabled = saved.IsSeamlessLoopModeEnabled;
        LoopDurationSeconds = saved.LoopDurationSeconds;
    }

    /// <summary>
    /// Persists the current "Duration Mode" selection
    /// (<see cref="IsSeamlessLoopModeEnabled"/> / <see cref="LoopDurationSeconds"/>)
    /// for the currently loaded shader, so it's restored next time this
    /// specific shader file is opened. Called on every change to either
    /// value rather than only on export, so the last state is captured even
    /// if the user never actually exports.
    /// </summary>
    private void PersistLoopSettings()
    {
        if (_loadedShader is null)
        {
            return;
        }

        _loopSettingsService.SaveOrReplace(
            _loadedShader.Project.SourceFilePath,
            IsSeamlessLoopModeEnabled,
            LoopDurationSeconds);
    }

    /// <summary>
    /// Reconstruit intégralement <see cref="CustomUniformGroups"/> à partir
    /// des uniforms custom exposés par le shader qui vient d'être initialisé
    /// dans <see cref="_previewRenderer"/> : un groupe par uniform déclaré,
    /// initialisé à sa valeur par défaut. Chaque changement de slider est
    /// répercuté en direct sur <see cref="MultiPassRenderer.SetCustomUniformComponent"/>,
    /// donc sur la prochaine frame de prévisualisation rendue — sans
    /// recompilation ni re-chargement du shader.
    /// </summary>
    private void ReloadCustomUniformGroups()
    {
        CustomUniformGroups.Clear();

        foreach (var declaration in _previewRenderer.CustomUniformDeclarations)
        {
            CustomUniformGroups.Add(CustomUniformGroupViewModel.FromDeclaration(
                declaration,
                (name, componentIndex, value) => _previewRenderer.SetCustomUniformComponent(name, componentIndex, value)));
        }

        HasCustomUniforms = CustomUniformGroups.Count > 0;
    }

    /// <summary>
    /// Reconstruit intégralement <see cref="VideoChannels"/> à partir des
    /// channels vidéo effectivement chargés dans <paramref name="loadedShader"/> :
    /// un élément par (passe, index de channel) dont
    /// <see cref="Videotoy.Core.ShaderModel.channelVideoPath"/> résout vers
    /// une entrée de <see cref="LoadedShader.VideoSources"/> — même
    /// convention d'énumération que <see cref="CustomUniformDeclarations"/>.
    /// </summary>
    private void ReloadVideoChannels(LoadedShader loadedShader)
    {
        VideoChannels.Clear();

        foreach (var pass in Videotoy.Core.ShaderModel.allPasses(loadedShader.Project))
        {
            var channels = new[] { pass.Channel0, pass.Channel1, pass.Channel2, pass.Channel3 };

            for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
            {
                var channel = channels[channelIndex];
                if (channel is null)
                {
                    continue;
                }

                var videoPath = Videotoy.Core.ShaderModel.channelVideoPath(channel.Value);
                if (videoPath is null || !loadedShader.VideoSources.TryGetValue(videoPath.Value, out var source))
                {
                    continue;
                }

                var viewModel = new VideoChannelViewModel(_videoTextureLoader)
                {
                    PassName = pass.Name,
                    ChannelIndex = channelIndex,
                    Source = source,
                    SelectedTimeMapping = VideoTimeMappingOption.FromValue(source.TimeMapping)
                };

                VideoChannels.Add(viewModel);
            }
        }

        HasVideoChannels = VideoChannels.Count > 0;
    }

    /// <summary>
    /// Convertit <see cref="LoadedShader.Textures"/>/<c>.AudioTracks</c>/
    /// <c>.VideoSources</c> vers les types neutres attendus par
    /// <see cref="MultiPassRenderer.Initialize"/>
    /// (<see cref="BoundImageAsset"/>/<see cref="BoundAudioAsset"/>/
    /// <see cref="BoundVideoAsset"/>) — cette conversion existe uniquement
    /// pour que <c>Videotoy.Rendering</c> n'ait jamais besoin de référencer
    /// <c>Videotoy.Media</c>/<c>Videotoy.Ffmpeg</c> (cycle de dépendances,
    /// puisque ces deux projets référencent déjà <c>Videotoy.Rendering</c>).
    /// </summary>
    private (
        IReadOnlyDictionary<string, BoundImageAsset> Images,
        IReadOnlyDictionary<string, BoundAudioAsset> AudioTracks,
        IReadOnlyDictionary<string, BoundVideoAsset> VideoSources)
        BuildBoundAssets(LoadedShader loadedShader) => _boundAssetsBuilder.Build(loadedShader);

    private void InitializePreview(LoadedShader loadedShader)
    {
        _loadedShader = loadedShader;

        var (images, audioTracks, videoSources) = BuildBoundAssets(loadedShader);
        _previewRenderer.Initialize(RenderTargetSize.PreviewDefault, loadedShader.Project, loadedShader.HlslPasses, images, audioTracks, videoSources);
        ReloadCustomUniformGroups();
        ReloadVideoChannels(loadedShader);

        _previewBitmap = new WriteableBitmap(
            RenderTargetSize.PreviewDefault.Width,
            RenderTargetSize.PreviewDefault.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null);
        PreviewImageSource = _previewBitmap;

        _previewClock.Stop();
        IsPlaying = false;
        _previewClock.Play();
        IsPlaying = true;

        TogglePlaybackCommand.NotifyCanExecuteChanged();
        StopPlaybackCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Avance l'horloge de lecture d'un delta de temps réel écoulé et rend la frame
    /// correspondante dans le viewport. À appeler depuis la boucle de rafraîchissement
    /// de la fenêtre (CompositionTarget.Rendering) tant qu'un shader est chargé.
    /// </summary>
    public void AdvancePreview(double realDeltaSeconds)
    {
        if (!IsShaderLoaded || _isScrubbing)
        {
            return;
        }

        _previewClock.Advance(realDeltaSeconds);
        CurrentFps = realDeltaSeconds > 0.0 ? 1.0 / realDeltaSeconds : 0.0;
    }

    private void OnPreviewClockTimeChanged(object? sender, EventArgs e)
    {
        PlaybackTimeSeconds = _previewClock.CurrentTimeSeconds;
        RenderCurrentFrame();
    }

    private void RenderCurrentFrame()
    {
        if (_previewBitmap is null || _loadedShader is null)
        {
            return;
        }

        var pixels = _previewRenderer.RenderFrame(_previewClock.CurrentTimeSeconds, 0.0, CurrentFrame);
        if (pixels.Length == 0)
        {
            return;
        }

        _previewBitmap.WritePixels(
            new System.Windows.Int32Rect(0, 0, _previewBitmap.PixelWidth, _previewBitmap.PixelHeight),
            pixels,
            _previewBitmap.PixelWidth * 4,
            0);

        CurrentFrame++;
    }

    private void ReloadRecentShaders()
    {
        RecentShaders.Clear();
        foreach (var entry in _recentFilesService.Load())
        {
            RecentShaders.Add(entry);
        }
    }

    partial void OnIsShaderLoadedChanged(bool value)
    {
        TogglePlaybackCommand.NotifyCanExecuteChanged();
        StopPlaybackCommand.NotifyCanExecuteChanged();
        ExportVideoCommand.NotifyCanExecuteChanged();
        ExportAnimatedImageCommand.NotifyCanExecuteChanged();
        GenerateLoopSeamPreviewCommand.NotifyCanExecuteChanged();
        AddCurrentToRenderQueueCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsExportingChanged(bool value)
    {
        ExportVideoCommand.NotifyCanExecuteChanged();
        ExportAnimatedImageCommand.NotifyCanExecuteChanged();
        CancelExportCommand.NotifyCanExecuteChanged();
        GenerateLoopSeamPreviewCommand.NotifyCanExecuteChanged();
        StartRenderQueueCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRenderQueueRunningChanged(bool value)
    {
        ExportVideoCommand.NotifyCanExecuteChanged();
        ExportAnimatedImageCommand.NotifyCanExecuteChanged();
        StartRenderQueueCommand.NotifyCanExecuteChanged();
        PauseRenderQueueCommand.NotifyCanExecuteChanged();
        CancelRenderQueueCommand.NotifyCanExecuteChanged();
    }

    // Hooks On<Prop>Changing des propriétés d'export undoable (Phase v1.6.0) :
    // ouvrent une transaction d'historique juste avant la mutation. Chaque
    // On<Prop>Changed correspondant appelle EndHistoryTransaction() en
    // dernière instruction pour la refermer — voir BeginHistoryTransaction/
    // EndHistoryTransaction pour le mécanisme de regroupement des cascades.
    partial void OnSelectedResolutionPresetChanging(ResolutionPresetOption value) => BeginHistoryTransaction();
    partial void OnCustomResolutionWidthChanging(int value) => BeginHistoryTransaction();
    partial void OnCustomResolutionHeightChanging(int value) => BeginHistoryTransaction();
    partial void OnSelectedFrameRatePresetChanging(FrameRatePresetOption value) => BeginHistoryTransaction();
    partial void OnCustomFrameRateValueChanging(double value) => BeginHistoryTransaction();
    partial void OnManualDurationUnitChanging(DurationUnit value) => BeginHistoryTransaction();
    partial void OnManualDurationValueChanging(double value) => BeginHistoryTransaction();
    partial void OnIsSeamlessLoopModeEnabledChanging(bool value) => BeginHistoryTransaction();
    partial void OnLoopDurationSecondsChanging(double value) => BeginHistoryTransaction();
    partial void OnIsLoopEndFrameExclusiveChanging(bool value) => BeginHistoryTransaction();
    partial void OnSelectedExportKindChanging(ExportKindOption value) => BeginHistoryTransaction();
    partial void OnSelectedAnimatedImageFormatChanging(AnimatedImageFormatOption value) => BeginHistoryTransaction();
    partial void OnGifColorCountChanging(int value) => BeginHistoryTransaction();
    partial void OnSelectedGifDitherChanging(GifDitherOption value) => BeginHistoryTransaction();
    partial void OnWebPQualityChanging(int value) => BeginHistoryTransaction();
    partial void OnIsWebPLosslessEnabledChanging(bool value) => BeginHistoryTransaction();
    partial void OnSelectedContainerFormatChanging(ContainerFormatOption value) => BeginHistoryTransaction();
    partial void OnSelectedVideoCodecChanging(VideoCodecOption value) => BeginHistoryTransaction();
    partial void OnIsTargetBitrateModeEnabledChanging(bool value) => BeginHistoryTransaction();
    partial void OnTargetBitrateKbpsChanging(int value) => BeginHistoryTransaction();
    partial void OnConstantRateFactorValueChanging(int value) => BeginHistoryTransaction();
    partial void OnSelectedSpeedPresetChanging(SpeedPresetOption value) => BeginHistoryTransaction();
    partial void OnSelectedVideoProfileChanging(VideoProfileOption value) => BeginHistoryTransaction();
    partial void OnIsGopSizeEnabledChanging(bool value) => BeginHistoryTransaction();
    partial void OnGopSizeValueChanging(int value) => BeginHistoryTransaction();
    partial void OnIsTwoPassEnabledChanging(bool value) => BeginHistoryTransaction();
    partial void OnSelectedHardwareEncoderChanging(HardwareEncoderOption value) => BeginHistoryTransaction();
    partial void OnSelectedAudioCodecChanging(AudioCodecOption value) => BeginHistoryTransaction();
    partial void OnAudioBitrateKbpsChanging(int value) => BeginHistoryTransaction();
    partial void OnIncludeAudioInExportChanging(bool value) => BeginHistoryTransaction();

    partial void OnSelectedExportKindChanged(ExportKindOption value)
    {
        OnPropertyChanged(nameof(IsVideoExportModeSelected));
        OnPropertyChanged(nameof(IsAnimatedImageExportModeSelected));
        OnPropertyChanged(nameof(IsSeamlessLoopModeToggleEnabled));
        OnPropertyChanged(nameof(IsAudioSectionVisible));

        if (value == ExportKindOption.AnimatedImage)
        {
            // L'export image animée n'a aucune notion de durée manuelle :
            // "Boucle parfaite" est verrouillée activée plutôt que
            // d'exposer un mode qui échouerait systématiquement à la
            // validation.
            IsSeamlessLoopModeEnabled = true;
        }

        ExportVideoCommand.NotifyCanExecuteChanged();
        ExportAnimatedImageCommand.NotifyCanExecuteChanged();
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnSelectedAnimatedImageFormatChanged(AnimatedImageFormatOption value)
    {
        OnPropertyChanged(nameof(IsGifFormatSelected));
        OnPropertyChanged(nameof(IsWebPFormatSelected));
        OnPropertyChanged(nameof(IsWebPQualitySectionVisible));
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnGifColorCountChanged(int value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnSelectedGifDitherChanged(GifDitherOption value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnWebPQualityChanged(int value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnIsWebPLosslessEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWebPQualitySectionVisible));
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnIsGeneratingLoopSeamPreviewChanged(bool value) => GenerateLoopSeamPreviewCommand.NotifyCanExecuteChanged();

    partial void OnLoopDurationSecondsChanged(double value)
    {
        _previewClock.LoopDurationSeconds = value;
        RecalculateExportPreview();
        PersistLoopSettings();
        EndHistoryTransaction();
    }

    partial void OnSelectedResolutionPresetChanged(ResolutionPresetOption value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnCustomResolutionWidthChanged(int value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnCustomResolutionHeightChanged(int value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnSelectedFrameRatePresetChanged(FrameRatePresetOption value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnCustomFrameRateValueChanged(double value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnSelectedContainerFormatChanged(ContainerFormatOption value)
    {
        // Le codec sélectionné peut ne plus être autorisé dans le nouveau
        // conteneur (ex. VP9 n'existe qu'en WebM) : on retombe alors sur le
        // premier codec valide plutôt que de laisser une combinaison
        // conteneur/codec incohérente. Le changement de codec (s'il a lieu)
        // déclenche déjà, via OnSelectedVideoCodecChanged, la réinitialisation
        // en cascade du profil et de l'encodeur matériel.
        OnPropertyChanged(nameof(VideoCodecOptions));

        if (!VideoCodecOptions.Contains(SelectedVideoCodec))
        {
            SelectedVideoCodec = VideoCodecOptions[0];
        }

        OnPropertyChanged(nameof(AudioCodecOptions));

        if (!AudioCodecOptions.Contains(SelectedAudioCodec))
        {
            SelectedAudioCodec = AudioCodecOptions[0];
        }

        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnSelectedVideoCodecChanged(VideoCodecOption value)
    {
        // Un profil H.264 ne peut pas s'appliquer à un export H.265/ProRes et
        // inversement : changer de codec réinitialise toujours le profil
        // sélectionné vers "Default" plutôt que de laisser une combinaison
        // codec/profil incohérente.
        SelectedVideoProfile = VideoProfileOption.None;
        OnPropertyChanged(nameof(VideoProfileOptions));

        // Aucun encodeur matériel n'existe pour VP9/ProRes dans ce pipeline :
        // retombe silencieusement sur "Software" plutôt que de conserver une
        // préférence matérielle sans effet.
        if (value != VideoCodecOption.H264 && value != VideoCodecOption.H265)
        {
            SelectedHardwareEncoder = HardwareEncoderOption.Software;
        }

        OnPropertyChanged(nameof(IsRateControlSectionVisible));
        OnPropertyChanged(nameof(IsGopSectionVisible));
        OnPropertyChanged(nameof(IsTwoPassCheckboxVisible));
        OnPropertyChanged(nameof(IsSpeedPresetVisible));
        OnPropertyChanged(nameof(IsHardwareEncoderSectionVisible));

        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnIsTargetBitrateModeEnabledChanged(bool value)
    {
        if (!value)
        {
            IsTwoPassEnabled = false;
        }

        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnTargetBitrateKbpsChanged(int value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnConstantRateFactorValueChanged(int value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnSelectedSpeedPresetChanged(SpeedPresetOption value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnSelectedVideoProfileChanged(VideoProfileOption value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnIsGopSizeEnabledChanged(bool value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnGopSizeValueChanged(int value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnIsTwoPassEnabledChanged(bool value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnSelectedHardwareEncoderChanged(HardwareEncoderOption value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnSelectedAudioCodecChanged(AudioCodecOption value)
    {
        OnPropertyChanged(nameof(IsAudioBitrateFieldVisible));
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnAudioBitrateKbpsChanged(int value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnIsSeamlessLoopModeEnabledChanged(bool value)
    {
        RecalculateExportPreview();
        PersistLoopSettings();
        EndHistoryTransaction();
    }

    partial void OnIsLoopEndFrameExclusiveChanged(bool value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnHasLoopRoundingMismatchChanged(bool value) => ApplyAssistedLoopRoundingCommand.NotifyCanExecuteChanged();

    partial void OnHasDetectedLoopPeriodChanged(bool value) => ApplyDetectedLoopPeriodCommand.NotifyCanExecuteChanged();

    partial void OnManualDurationUnitChanged(DurationUnit value)
    {
        OnPropertyChanged(nameof(ManualDurationUnitIndex));
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnManualDurationValueChanged(double value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnHasAudioChannelChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAudioSectionVisible));
        RecalculateExportPreview();
    }

    partial void OnIncludeAudioInExportChanged(bool value)
    {
        RecalculateExportPreview();
        EndHistoryTransaction();
    }

    partial void OnNewExportPresetNameChanged(string value) => SaveExportPresetCommand.NotifyCanExecuteChanged();

    partial void OnSelectedExportPresetChanged(ExportPreset? value)
    {
        LoadExportPresetCommand.NotifyCanExecuteChanged();
        DeleteExportPresetCommand.NotifyCanExecuteChanged();
    }
}
