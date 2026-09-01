using CommunityToolkit.Mvvm.ComponentModel;

namespace Videotoy.App.ViewModels;

/// <summary>
/// Un chip de filtre par passe dans le panneau Shader Issues (Phase
/// v1.8.0) — une entrée par nom de passe distinct actuellement présent
/// dans <see cref="MainWindowViewModel.ShaderIssues"/> (ex. "Image",
/// "Buffer A"). <see cref="IsSelected"/> pilote la visibilité des lignes de
/// cette passe via le prédicat de filtre de la vue collection.
/// </summary>
public sealed partial class ShaderIssuePassFilterViewModel : ObservableObject
{
    public required string PassName { get; init; }

    [ObservableProperty]
    private bool _isSelected = true;
}
