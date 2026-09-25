using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Videotoy.Media;

namespace Videotoy.App.ViewModels;

/// <summary>
/// Panneau éditeur de code intégré (Phase 1 du ROADMAP) : édition directe du
/// code source d'un shader chargé (ou d'un nouveau shader créé sans fichier)
/// avec coloration syntaxique, compilation/aperçu à la volée, sauvegarde, et
/// suivi des modifications non enregistrées. Volontairement séparé du reste
/// de <see cref="MainWindowViewModel"/> (fichier partiel dédié) tant ce
/// dernier concentre déjà l'essentiel de l'état applicatif.
///
/// Portée volontairement limitée aux projets shader "raw" mono-fichier
/// (<c>.glsl</c>/<c>.frag</c>/<c>.wgsl</c>/<c>.hlsl</c>/<c>.hlsli</c>, ou un
/// nouveau shader jamais sauvegardé) : un projet JSON/Shadertoy multi-passes
/// peut être édité et compilé à la volée comme n'importe quel autre projet,
/// mais Sauvegarder/Sauvegarder sous... sont désactivés pour lui, faute de
/// sérialiseur JSON Shadertoy en écriture — voir <see cref="CanSaveShader"/>.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private static readonly string[] RawShaderExtensions = { ".glsl", ".frag", ".wgsl", ".hlsl", ".hlsli", ".txt" };

    /// <summary>
    /// Contenu de l'éditeur pour la passe actuellement sélectionnée
    /// (<see cref="SelectedEditorPass"/>) au moment du dernier chargement/
    /// changement d'onglet — sert de valeur de référence pour détecter des
    /// modifications non enregistrées (<see cref="IsEditorDirty"/>), et non
    /// comme source de vérité pour le texte affiché (qui vit dans le
    /// contrôle <c>ShaderEditorView</c> lui-même, voir sa remarque de
    /// classe : le binding ne se synchronise que sur <c>FlushPendingEdits</c>).
    /// </summary>
    private readonly Dictionary<string, string> _editorPassSourceAtLastSync = new();

    [ObservableProperty]
    private string _editorSourceText = string.Empty;

    [ObservableProperty]
    private bool _isEditorDirty;

    [ObservableProperty]
    private EditorPassOptionViewModel? _selectedEditorPass;

    public ObservableCollection<EditorPassOptionViewModel> EditorPasses { get; } = new();

    /// <summary>
    /// Bascule manuelle du panneau éditeur (barre d'outils) — même
    /// convention que <see cref="ToggleIssuesPanel"/>/
    /// <see cref="ToggleExportHistoryPanel"/>. À l'ouverture, si un shader
    /// est déjà chargé mais que l'éditeur n'a encore jamais été peuplé,
    /// initialise ses onglets depuis le shader courant.
    /// </summary>
    [RelayCommand]
    private void ToggleEditorPanel()
    {
        IsEditorPanelOpen = !IsEditorPanelOpen;

        if (IsEditorPanelOpen && EditorPasses.Count == 0 && _loadedShader is not null)
        {
            PopulateEditorFromProject(_loadedShader.Project);
        }
    }

    /// <summary>
    /// Reconstruit les onglets de l'éditeur (Image/Buffer A-D/Common, selon
    /// ce qui existe effectivement dans <paramref name="project"/>) — appelé
    /// après tout chargement de shader depuis le disque
    /// (<see cref="LoadShaderFile"/>) pour que l'éditeur reste synchronisé
    /// avec le shader affiché dans le viewport, ainsi qu'à la première
    /// ouverture du panneau éditeur.
    /// </summary>
    private void PopulateEditorFromProject(Videotoy.Core.ShaderModel.ShaderProject project)
    {
        _editorPassSourceAtLastSync.Clear();
        EditorPasses.Clear();

        foreach (var pass in Videotoy.Core.ShaderModel.allPasses(project))
        {
            EditorPasses.Add(new EditorPassOptionViewModel { PassName = pass.Name, SourceCode = pass.SourceCode });
            _editorPassSourceAtLastSync[pass.Name] = pass.SourceCode;
        }

        if (project.CommonCode is not null && Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(project.CommonCode))
        {
            var commonCode = project.CommonCode.Value;
            EditorPasses.Add(new EditorPassOptionViewModel { PassName = Videotoy.Core.ShaderModel.commonPassName, SourceCode = commonCode });
            _editorPassSourceAtLastSync[Videotoy.Core.ShaderModel.commonPassName] = commonCode;
        }

        SelectedEditorPass = EditorPasses.FirstOrDefault();
        IsEditorDirty = false;
        RefreshEditorIssueLines();
        OnPropertyChanged(nameof(HasMultipleEditorPasses));
        SaveShaderCommand.NotifyCanExecuteChanged();
        SaveShaderAsCommand.NotifyCanExecuteChanged();
        CompileFromEditorCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Un onglet du panneau éditeur possède plus d'une passe : pilote la
    /// visibilité de la rangée d'onglets dans <c>MainWindow.xaml</c>, cachée
    /// pour un shader mono-fichier où un seul onglet ("Image") existerait de
    /// toute façon.
    /// </summary>
    public bool HasMultipleEditorPasses => EditorPasses.Count > 1;

    [RelayCommand]
    private void SelectEditorPass(EditorPassOptionViewModel pass)
    {
        SelectedEditorPass = pass;
    }

    partial void OnSelectedEditorPassChanged(EditorPassOptionViewModel? value)
    {
        foreach (var pass in EditorPasses)
        {
            pass.IsSelected = ReferenceEquals(pass, value);
        }

        EditorSourceText = value?.SourceCode ?? string.Empty;
        RefreshEditorIssueLines();
    }

    /// <summary>
    /// Lignes d'erreur/avertissement (<see cref="ShaderIssues"/>) qui
    /// appartiennent à la passe actuellement éditée — recalculées à chaque
    /// changement d'onglet ou de compilation, et consommées par
    /// <c>ShaderEditorView.ErrorLines</c>/<c>WarningLines</c> pour ancrer les
    /// marqueurs de marge sur les bons numéros de ligne (Phase 1 du ROADMAP :
    /// « erreurs de compilation remontées ... ancrées sur les numéros de
    /// ligne de l'éditeur »).
    /// </summary>
    public IReadOnlyList<int> EditorErrorLines { get; private set; } = Array.Empty<int>();

    public IReadOnlyList<int> EditorWarningLines { get; private set; } = Array.Empty<int>();

    private void RefreshEditorIssueLines()
    {
        var passName = SelectedEditorPass?.PassName;

        EditorErrorLines = passName is null
            ? Array.Empty<int>()
            : ShaderIssues.Where(i => i.PassName == passName && i.IsError).Select(i => i.Line).ToArray();

        EditorWarningLines = passName is null
            ? Array.Empty<int>()
            : ShaderIssues.Where(i => i.PassName == passName && !i.IsError).Select(i => i.Line).ToArray();

        OnPropertyChanged(nameof(EditorErrorLines));
        OnPropertyChanged(nameof(EditorWarningLines));
    }

    /// <summary>
    /// Appelé par le code-behind (<c>ShaderEditorView.SourceText</c> lié en
    /// <c>TwoWay</c>) à chaque frappe — met à jour l'onglet actuellement
    /// sélectionné et recalcule <see cref="IsEditorDirty"/> par comparaison
    /// avec le texte tel qu'il était au dernier chargement/compilation
    /// réussie, plutôt qu'un simple "a changé une fois" qui resterait vrai
    /// même après un Ctrl+Z ramenant au texte d'origine.
    /// </summary>
    partial void OnEditorSourceTextChanged(string value)
    {
        if (SelectedEditorPass is not { } selected)
        {
            return;
        }

        selected.SourceCode = value;

        IsEditorDirty = _editorPassSourceAtLastSync.TryGetValue(selected.PassName, out var original)
            ? original != value
            : !string.IsNullOrEmpty(value);
    }

    private bool CanCompileFromEditor() => EditorPasses.Count > 0;

    /// <summary>
    /// Compile/aperçu à la volée (Ctrl+Entrée) : reconstruit le
    /// <see cref="Videotoy.Core.ShaderModel.ShaderProject"/> courant avec le
    /// contenu actuel de chaque onglet de l'éditeur substitué à son code
    /// source d'origine, puis relance validation → transpilation → aperçu —
    /// même séquence que <see cref="ForceShaderLanguage"/>, mais à partir du
    /// texte en mémoire plutôt que d'un simple changement de langage. Ne
    /// touche jamais le fichier sur disque : seul
    /// <see cref="SaveShader"/>/<see cref="SaveShaderAs"/> écrit.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCompileFromEditor))]
    private void CompileFromEditor()
    {
        if (_loadedShader is null || EditorPasses.Count == 0)
        {
            return;
        }

        var project = _loadedShader.Project;
        foreach (var pass in EditorPasses)
        {
            project = Videotoy.Core.ShaderModel.withPassSourceCode(pass.PassName, pass.SourceCode, project);
        }

        try
        {
            var recompiled = _shaderFileService.LoadFromProject(project, _loadedShader);

            ReplaceShaderIssues(recompiled.Issues);
            IsIssuesPanelOpen = ShaderIssues.Count > 0;
            RefreshEditorIssueLines();

            StatusMessage = recompiled.HasErrors
                ? $"Compiled '{LoadedShaderName}' with errors."
                : $"Compiled '{LoadedShaderName}'.";

            if (!recompiled.HasErrors)
            {
                InitializePreview(recompiled);
                HasAudioChannel = ResolveExportAudioSourceFilePath(recompiled) is not null;
            }

            foreach (var pass in EditorPasses)
            {
                _editorPassSourceAtLastSync[pass.PassName] = pass.SourceCode;
            }

            IsEditorDirty = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to compile from editor: {ex.Message}";
        }
    }

    /// <summary>
    /// Un nouveau shader créé directement dans l'éditeur (jamais sauvegardé
    /// sur disque) n'a de sens à sauvegarder que sous forme de fichier
    /// source brut mono-passe : un projet JSON/Shadertoy multi-passes
    /// n'a aujourd'hui aucun sérialiseur en écriture (voir la remarque de
    /// classe) et ne peut donc être ni sauvegardé, ni sauvegardé sous.
    /// </summary>
    private bool CanSaveShader() =>
        _loadedShader is not null
        && EditorPasses.Count == 1
        && !string.IsNullOrEmpty(_loadedShaderFilePath)
        && RawShaderExtensions.Contains(Path.GetExtension(_loadedShaderFilePath).ToLowerInvariant());

    [RelayCommand(CanExecute = nameof(CanSaveShader))]
    private void SaveShader()
    {
        if (_loadedShaderFilePath is null || SelectedEditorPass is not { } pass)
        {
            return;
        }

        try
        {
            File.WriteAllText(_loadedShaderFilePath, pass.SourceCode);
            _editorPassSourceAtLastSync[pass.PassName] = pass.SourceCode;
            IsEditorDirty = false;
            StatusMessage = $"Saved '{Path.GetFileName(_loadedShaderFilePath)}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save shader: {ex.Message}";
        }
    }

    private bool CanSaveShaderAs() => _loadedShader is not null && EditorPasses.Count == 1;

    [RelayCommand(CanExecute = nameof(CanSaveShaderAs))]
    private void SaveShaderAs()
    {
        if (SelectedEditorPass is not { } pass)
        {
            return;
        }

        var languageExtension = _loadedShader is null
            ? "glsl"
            : Videotoy.Core.ShaderModel.languageKey(_loadedShader.Project.SourceLanguage) switch
            {
                "Hlsl" => "hlsl",
                "Wgsl" => "wgsl",
                _ => "glsl"
            };

        var dialog = new SaveFileDialog
        {
            Filter = $"Shader files|*.{languageExtension}|All files|*.*",
            FileName = string.IsNullOrEmpty(_loadedShaderFilePath)
                ? $"{SanitizeAsFileName(LoadedShaderName)}.{languageExtension}"
                : Path.GetFileName(_loadedShaderFilePath)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, pass.SourceCode);
            _loadedShaderFilePath = dialog.FileName;
            LoadedShaderName = Path.GetFileNameWithoutExtension(dialog.FileName);
            _editorPassSourceAtLastSync[pass.PassName] = pass.SourceCode;
            IsEditorDirty = false;
            StatusMessage = $"Saved '{Path.GetFileName(dialog.FileName)}'.";

            _recentFilesService.AddOrPromote(dialog.FileName);
            ReloadRecentShaders();

            SaveShaderCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save shader: {ex.Message}";
        }
    }

    /// <summary>
    /// Ouvre un éditeur vide pré-rempli d'un template Shadertoy minimal, sans
    /// jamais toucher au disque — l'utilisateur peut coller du code copié
    /// depuis shadertoy.com et lancer l'aperçu (<see cref="CompileFromEditor"/>)
    /// sans jamais sauvegarder. Efface l'historique d'annulation comme
    /// <see cref="LoadShaderFile"/>, pour la même raison : un nouveau shader
    /// ne doit pas hériter de l'historique du précédent.
    /// </summary>
    [RelayCommand]
    private void NewShader()
    {
        const string template = """
            void mainImage(out vec4 fragColor, in vec2 fragCoord)
            {
                vec2 uv = fragCoord / iResolution.xy;
                fragColor = vec4(uv, 0.5 + 0.5 * sin(iTime), 1.0);
            }
            """;

        _suppressHistoryCapture = true;
        try
        {
            _historyStack.Clear();

            var project = Videotoy.Core.ShaderModel.fromRawSource(
                template, "untitled.glsl", Videotoy.Core.ShaderModel.ShaderSourceLanguage.Glsl);

            _loadedShaderFilePath = null;

            _suppressShaderLanguageOverride = true;
            SelectedShaderLanguage = ShaderLanguageOption.FromLanguage(project.SourceLanguage);
            _suppressShaderLanguageOverride = false;

            var loaded = _shaderFileService.LoadFromProject(project, EmptyLoadedShader());

            ReplaceShaderIssues(loaded.Issues);
            IsIssuesPanelOpen = ShaderIssues.Count > 0;
            LoadedShaderName = project.Title;
            IsShaderLoaded = true;
            OutputFileName = SanitizeAsFileName(project.Title);

            HasLoopSeamPreview = false;
            LoopSeamStartFrameImageSource = null;
            LoopSeamEndFrameImageSource = null;

            ApplyLoopPeriodDetection(loaded.Project);

            StatusMessage = "Created new shader.";

            if (!loaded.HasErrors)
            {
                InitializePreview(loaded);
                HasAudioChannel = false;
                IncludeAudioInExport = false;
            }

            PopulateEditorFromProject(loaded.Project);
            IsEditorPanelOpen = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create new shader: {ex.Message}";
        }
        finally
        {
            _suppressHistoryCapture = false;
        }
    }

    private static LoadedShader EmptyLoadedShader() => new()
    {
        Project = Videotoy.Core.ShaderModel.fromRawSource(string.Empty, "untitled.glsl", Videotoy.Core.ShaderModel.ShaderSourceLanguage.Glsl),
        Issues = Array.Empty<Videotoy.Core.ShaderModel.ShaderIssue>(),
        Textures = new Dictionary<string, Videotoy.Media.TextureAsset>(),
        Cubemaps = new Dictionary<string, Videotoy.Media.CubemapAsset>(),
        Volumes = new Dictionary<string, Videotoy.Media.VolumeAsset>(),
        AudioTracks = new Dictionary<string, Videotoy.Media.AudioTrack>(),
        VideoSources = new Dictionary<string, Videotoy.Ffmpeg.VideoTextureSource>(),
        HlslPasses = new Dictionary<string, Videotoy.Core.ShaderTranspiler.TranspileResult>()
    };
}
