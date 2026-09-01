namespace Videotoy.App.History;

/// <summary>
/// Une action utilisateur réversible poussée sur <see cref="SettingsUndoStack"/>.
/// Portée strictement limitée aux paramètres de rendu/export — jamais au
/// contenu du fichier shader lui-même (voir Phase v1.6.0 du roadmap).
/// </summary>
public interface IUndoableCommand
{
    void Undo();

    void Redo();
}
