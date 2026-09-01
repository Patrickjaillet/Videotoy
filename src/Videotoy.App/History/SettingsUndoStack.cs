namespace Videotoy.App.History;

/// <summary>
/// Pile d'historique générique (undo/redo) partagée par toutes les
/// commandes réversibles portant sur les paramètres de rendu/export —
/// aussi bien les réglages d'export (<see cref="ExportSettingsCommand"/>)
/// que les valeurs des sliders d'uniforms custom
/// (<see cref="CustomUniformsCommand"/>). Un seul <see cref="Push"/> par
/// domaine confondu garantit qu'un Ctrl+Z annule toujours la dernière
/// action utilisateur, quel que soit le domaine dont elle provient.
/// </summary>
public sealed class SettingsUndoStack
{
    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public event EventHandler? StateChanged;

    /// <summary>
    /// Empile <paramref name="command"/> sur la pile d'annulation et vide la
    /// pile de rétablissement : toute action de rétablissement disponible
    /// avant cette nouvelle modification devient invalide, comme dans tout
    /// éditeur standard (Word, VS Code, etc.).
    /// </summary>
    public void Push(IUndoableCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!_undoStack.TryPop(out var command))
        {
            return;
        }

        command.Undo();
        _redoStack.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!_redoStack.TryPop(out var command))
        {
            return;
        }

        command.Redo();
        _undoStack.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_undoStack.Count == 0 && _redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
