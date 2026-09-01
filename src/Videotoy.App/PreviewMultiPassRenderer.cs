using Videotoy.Rendering;

namespace Videotoy.App;

/// <summary>
/// Sous-classe marqueur sans comportement additionnel : sert uniquement à
/// distinguer, côté injection de dépendances, l'instance singleton de
/// <see cref="MultiPassRenderer"/> dédiée à la prévisualisation en direct
/// de celle dédiée à l'export (<see cref="ExportMultiPassRenderer"/>) —
/// les deux ont des tailles de rendu différentes et un état GPU
/// indépendant, donc ne peuvent pas partager une seule instance.
/// </summary>
public sealed class PreviewMultiPassRenderer : MultiPassRenderer;
