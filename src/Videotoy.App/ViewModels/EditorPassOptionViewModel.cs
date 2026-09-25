using CommunityToolkit.Mvvm.ComponentModel;

namespace Videotoy.App.ViewModels;

/// <summary>
/// Un onglet du panneau éditeur intégré (Phase 1 du ROADMAP), représentant
/// une passe éditable d'un projet shader — Image, Buffer A-D, ou Common
/// (<see cref="Videotoy.Core.ShaderModel.commonPassName"/>). Une passe qui
/// n'existe pas dans le projet chargé (ex. Buffer B absent) n'a simplement
/// pas d'onglet correspondant. <see cref="IsSelected"/> est purement
/// cosmétique (mis à jour par <c>MainWindowViewModel</c> à chaque changement
/// d'onglet) et ne pilote aucune autre logique que le style du bouton
/// d'onglet dans <c>MainWindow.xaml</c>. <see cref="SourceCode"/> est tenu à
/// jour en direct par la frappe dans l'éditeur (voir
/// <c>MainWindowViewModel.OnEditorSourceTextChanged</c>), et non recréé à
/// chaque caractère tapé.
/// </summary>
public sealed partial class EditorPassOptionViewModel : ObservableObject
{
    public required string PassName { get; init; }

    [ObservableProperty]
    private string _sourceCode = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
