using Videotoy.App.ViewModels;

namespace Videotoy.App.History;

/// <summary>
/// Commande réversible capturant l'état complet des paramètres d'export
/// avant/après une action utilisateur (voir <see cref="ExportSettingsSnapshot"/>
/// pour la justification du choix "snapshot complet" plutôt que "delta par
/// champ" — nécessaire pour couvrir correctement les cascades existantes,
/// ex. changer le codec vidéo réinitialise aussi le profil vidéo).
/// </summary>
public sealed class ExportSettingsCommand : IUndoableCommand
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ExportSettingsSnapshot _before;
    private readonly ExportSettingsSnapshot _after;

    public ExportSettingsCommand(MainWindowViewModel viewModel, ExportSettingsSnapshot before, ExportSettingsSnapshot after)
    {
        _viewModel = viewModel;
        _before = before;
        _after = after;
    }

    public void Undo() => _before.ApplyTo(_viewModel);

    public void Redo() => _after.ApplyTo(_viewModel);
}
