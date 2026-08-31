using System.Collections.ObjectModel;

namespace Videotoy.App.ViewModels;

/// <summary>
/// Regroupe les sliders (un par composant) d'un seul uniform custom déclaré
/// par le shader chargé, pour affichage dans le panneau "Custom Uniforms" du
/// panneau de paramètres de rendu. Un uniform `float` produit un groupe avec
/// un seul slider ; un `vec3`/`vec4` en produit trois/quatre.
/// </summary>
public sealed class CustomUniformGroupViewModel
{
    public required string Name { get; init; }

    public required ObservableCollection<CustomUniformSliderViewModel> Sliders { get; init; }

    public static CustomUniformGroupViewModel FromDeclaration(
        Videotoy.Core.CustomUniformParser.CustomUniformDeclaration declaration,
        Action<string, int, float> onComponentChanged)
    {
        var componentCount = Videotoy.Core.CustomUniformParser.componentCount(declaration.UniformType);
        var componentSuffixes = new[] { "X", "Y", "Z", "W" };

        var sliders = new ObservableCollection<CustomUniformSliderViewModel>();

        for (var componentIndex = 0; componentIndex < componentCount; componentIndex++)
        {
            var displayLabel = componentCount > 1
                ? $"{declaration.Label} · {componentSuffixes[componentIndex]}"
                : declaration.Label;

            sliders.Add(new CustomUniformSliderViewModel(
                (index, value) => onComponentChanged(declaration.Name, index, value))
            {
                DisplayLabel = displayLabel,
                Minimum = declaration.MinValues[componentIndex],
                Maximum = declaration.MaxValues[componentIndex],
                ComponentIndex = componentIndex,
                Value = declaration.DefaultValues[componentIndex]
            });
        }

        return new CustomUniformGroupViewModel
        {
            Name = declaration.Name,
            Sliders = sliders
        };
    }
}
