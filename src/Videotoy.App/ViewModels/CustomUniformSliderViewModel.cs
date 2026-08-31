using CommunityToolkit.Mvvm.ComponentModel;

namespace Videotoy.App.ViewModels;

/// <summary>
/// Représente un slider unique dans le panneau "Custom Uniforms" du panneau
/// de paramètres de rendu : un composant scalaire (x/y/z/w) d'un uniform
/// custom déclaré par le shader chargé (voir
/// <see cref="Videotoy.Core.CustomUniformParser"/>). Un uniform `vec3`
/// produit trois instances de ce ViewModel, une par composant.
/// </summary>
public sealed partial class CustomUniformSliderViewModel : ObservableObject
{
    private readonly Action<int, float> _onValueChanged;

    /// <summary>
    /// Étiquette affichée au-dessus du slider : l'étiquette déclarée par le
    /// shader (ou son nom, à défaut), suffixée de la composante (" · X",
    /// " · Y", ...) uniquement lorsque l'uniform a plus d'un composant.
    /// </summary>
    public required string DisplayLabel { get; init; }

    public required float Minimum { get; init; }

    public required float Maximum { get; init; }

    /// <summary>
    /// Index du composant (0 = x, 1 = y, 2 = z, 3 = w) au sein de l'uniform
    /// parent, transmis à <see cref="Rendering.MultiPassRenderer.SetCustomUniformComponent"/>
    /// à chaque changement.
    /// </summary>
    public required int ComponentIndex { get; init; }

    [ObservableProperty]
    private float _value;

    public CustomUniformSliderViewModel(Action<int, float> onValueChanged)
    {
        _onValueChanged = onValueChanged;
    }

    partial void OnValueChanged(float value)
    {
        _onValueChanged(ComponentIndex, value);
    }
}
