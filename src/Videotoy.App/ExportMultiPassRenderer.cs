using Videotoy.Rendering;

namespace Videotoy.App;

/// <summary>
/// Sous-classe marqueur sans comportement additionnel : voir
/// <see cref="PreviewMultiPassRenderer"/> pour la raison de cette
/// distinction côté injection de dépendances.
/// </summary>
public sealed class ExportMultiPassRenderer : MultiPassRenderer;
