using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using Videotoy.Core;

namespace Videotoy.App.Views;

/// <summary>
/// Contrôle éditeur de code intégré (Phase 1 du ROADMAP) : encapsule
/// AvalonEdit avec coloration syntaxique GLSL/HLSL/WGSL, numérotation de
/// ligne, et des marqueurs de marge pour les erreurs/avertissements du
/// panneau Shader Issues, ancrés sur les numéros de ligne du texte affiché.
/// <see cref="SourceText"/> est un binding TwoWay live (chaque frappe pousse
/// le texte au ViewModel) : <see cref="_isSyncingFromSourceText"/> évite la
/// boucle de rétroaction qui, sans lui, réassignerait
/// <c>Editor.Document.Text</c> (et donc réinitialiserait le curseur) à
/// chaque frappe alors que le document est déjà à jour.
/// </summary>
public partial class ShaderEditorView : UserControl
{
    private static readonly Dictionary<string, IHighlightingDefinition> HighlightingCache = new();

    private bool _isSyncingFromSourceText;

    public ShaderEditorView()
    {
        InitializeComponent();
        Editor.TextArea.TextView.BackgroundRenderers.Add(new IssueLineBackgroundRenderer(this));
        Editor.TextChanged += OnEditorTextChanged;

        // Applique la coloration par défaut (GLSL) immédiatement plutôt que
        // d'attendre que le binding ShaderLanguage se déclenche, au cas où
        // celui-ci n'assigne jamais explicitement la valeur par défaut (WPF
        // ne rappelle pas toujours un PropertyChangedCallback quand une
        // valeur liée coïncide avec la valeur par défaut déclarée).
        Editor.SyntaxHighlighting = LoadHighlighting(ShaderLanguage);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isSyncingFromSourceText)
        {
            return;
        }

        SourceText = Editor.Document.Text;
    }

    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText),
        typeof(string),
        typeof(ShaderEditorView),
        new FrameworkPropertyMetadata(string.Empty, OnSourceTextChanged));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public static readonly DependencyProperty ShaderLanguageProperty = DependencyProperty.Register(
        nameof(ShaderLanguage),
        typeof(ShaderModel.ShaderSourceLanguage),
        typeof(ShaderEditorView),
        new FrameworkPropertyMetadata(ShaderModel.ShaderSourceLanguage.Glsl, OnShaderLanguageChanged));

    public ShaderModel.ShaderSourceLanguage ShaderLanguage
    {
        get => (ShaderModel.ShaderSourceLanguage)GetValue(ShaderLanguageProperty);
        set => SetValue(ShaderLanguageProperty, value);
    }

    /// <summary>
    /// Numéros de ligne (1-indexés) des erreurs/avertissements à surligner
    /// dans la marge — alimenté par <c>MainWindowViewModel.ShaderIssues</c>
    /// filtré sur la passe actuellement éditée.
    /// </summary>
    public static readonly DependencyProperty ErrorLinesProperty = DependencyProperty.Register(
        nameof(ErrorLines),
        typeof(IReadOnlyList<int>),
        typeof(ShaderEditorView),
        new FrameworkPropertyMetadata(Array.Empty<int>(), OnIssueLinesChanged));

    public IReadOnlyList<int> ErrorLines
    {
        get => (IReadOnlyList<int>)GetValue(ErrorLinesProperty);
        set => SetValue(ErrorLinesProperty, value);
    }

    public static readonly DependencyProperty WarningLinesProperty = DependencyProperty.Register(
        nameof(WarningLines),
        typeof(IReadOnlyList<int>),
        typeof(ShaderEditorView),
        new FrameworkPropertyMetadata(Array.Empty<int>(), OnIssueLinesChanged));

    public IReadOnlyList<int> WarningLines
    {
        get => (IReadOnlyList<int>)GetValue(WarningLinesProperty);
        set => SetValue(WarningLinesProperty, value);
    }

    private static void OnSourceTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ShaderEditorView)d;
        var newText = (string)e.NewValue ?? string.Empty;
        if (view.Editor.Document.Text == newText)
        {
            return;
        }

        view._isSyncingFromSourceText = true;
        try
        {
            var caretOffset = view.Editor.CaretOffset;
            view.Editor.Document.Text = newText;
            view.Editor.CaretOffset = Math.Min(caretOffset, newText.Length);
        }
        finally
        {
            view._isSyncingFromSourceText = false;
        }
    }

    private static void OnShaderLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ShaderEditorView)d;
        view.Editor.SyntaxHighlighting = LoadHighlighting((ShaderModel.ShaderSourceLanguage)e.NewValue);
    }

    private static void OnIssueLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ShaderEditorView)d;
        view.Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private static IHighlightingDefinition LoadHighlighting(ShaderModel.ShaderSourceLanguage language)
    {
        var resourceName = ShaderModel.languageKey(language) switch
        {
            "Hlsl" => "Hlsl",
            "Wgsl" => "Wgsl",
            _ => "Glsl"
        };

        if (HighlightingCache.TryGetValue(resourceName, out var cached))
        {
            return cached;
        }

        var uri = new Uri($"pack://application:,,,/Videotoy;component/Resources/Editor/{resourceName}.xshd");
        var streamInfo = Application.GetResourceStream(uri) ?? throw new FileNotFoundException($"Missing embedded syntax definition: {resourceName}.xshd");

        using var reader = new XmlTextReader(streamInfo.Stream);
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        HighlightingCache[resourceName] = definition;
        return definition;
    }

    /// <summary>
    /// Peint un fond translucide rouge/jaune derrière les lignes listées
    /// dans <see cref="ErrorLines"/>/<see cref="WarningLines"/>, à la manière
    /// des indicateurs d'erreur inline d'un IDE classique.
    /// </summary>
    private sealed class IssueLineBackgroundRenderer : IBackgroundRenderer
    {
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromArgb(40, 224, 64, 64));
        private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromArgb(32, 224, 176, 64));

        private readonly ShaderEditorView _owner;

        public IssueLineBackgroundRenderer(ShaderEditorView owner)
        {
            _owner = owner;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid)
            {
                return;
            }

            DrawLines(textView, drawingContext, _owner.ErrorLines, ErrorBrush);
            DrawLines(textView, drawingContext, _owner.WarningLines, WarningBrush);
        }

        private static void DrawLines(TextView textView, DrawingContext drawingContext, IReadOnlyList<int> lineNumbers, Brush brush)
        {
            foreach (var lineNumber in lineNumbers)
            {
                if (lineNumber < 1 || lineNumber > textView.Document.LineCount)
                {
                    continue;
                }

                var line = textView.Document.GetLineByNumber(lineNumber);
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
                {
                    drawingContext.DrawRectangle(brush, null, new Rect(0, rect.Top, textView.ActualWidth, rect.Height));
                }
            }
        }
    }
}
