using Videotoy.App.ViewModels;

namespace Videotoy.App.History;

/// <summary>
/// Commande réversible capturant les valeurs des sliders d'uniforms custom
/// avant/après un geste utilisateur continu (glisser un slider, ou une
/// modification atomique isolée — voir
/// <see cref="MainWindowViewModel.BeginCustomUniformEdit"/>/
/// <see cref="MainWindowViewModel.EndCustomUniformEdit"/>). Les valeurs sont
/// indexées par <c>(GroupName, ComponentIndex)</c> plutôt que par référence
/// d'instance de <see cref="CustomUniformSliderViewModel"/> : ces instances
/// sont entièrement reconstruites à chaque rechargement de shader, mais la
/// pile d'historique est de toute façon vidée à ce moment-là (défense en
/// profondeur, pas une nécessité stricte).
/// </summary>
public sealed class CustomUniformsCommand : IUndoableCommand
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IReadOnlyDictionary<(string GroupName, int ComponentIndex), float> _before;
    private readonly IReadOnlyDictionary<(string GroupName, int ComponentIndex), float> _after;

    public CustomUniformsCommand(
        MainWindowViewModel viewModel,
        IReadOnlyDictionary<(string GroupName, int ComponentIndex), float> before,
        IReadOnlyDictionary<(string GroupName, int ComponentIndex), float> after)
    {
        _viewModel = viewModel;
        _before = before;
        _after = after;
    }

    public void Undo() => Apply(_before);

    public void Redo() => Apply(_after);

    private void Apply(IReadOnlyDictionary<(string GroupName, int ComponentIndex), float> values)
    {
        foreach (var group in _viewModel.CustomUniformGroups)
        {
            foreach (var slider in group.Sliders)
            {
                if (values.TryGetValue((group.Name, slider.ComponentIndex), out var value))
                {
                    slider.Value = value;
                }
            }
        }
    }
}
