namespace Videotoy.Rendering;

/// <summary>
/// Traduit un échec Direct3D 11 bas niveau (typiquement
/// <c>SharpGen.Runtime.SharpGenException</c>, levée pour tout HRESULT en
/// échec — pilote qui plante, timeout TDR, GPU débranché/changé en cours
/// d'export) survenu pendant le rendu ou la lecture des pixels en un
/// message actionnable pour l'utilisateur, plutôt que de laisser remonter
/// l'exception COM opaque telle quelle. Ne prétend identifier ni la cause
/// exacte ni si le device est spécifiquement "removed" (l'API bas niveau
/// exacte dépend de la version de Vortice) : seulement que le pipeline de
/// rendu GPU a échoué de façon non récupérable pour la frame en cours.
/// </summary>
public sealed class GpuDeviceLostException : Exception
{
    public GpuDeviceLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
