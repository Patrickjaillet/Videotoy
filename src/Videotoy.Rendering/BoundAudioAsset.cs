namespace Videotoy.Rendering;

/// <summary>
/// Piste audio liée à un <c>iChannel</c> audio, sous une forme neutre
/// suffisante pour que <see cref="MultiPassRenderer"/> délègue la
/// génération de la texture de spectre à chaque frame sans référencer
/// <c>Videotoy.Media</c> directement (même raison de cycle de dépendances
/// que <see cref="BoundImageAsset"/>). <see cref="GenerateSpectrumTextureBgra"/>
/// doit être une fonction pure de <paramref name="timeSeconds"/> pour
/// rester compatible avec le pipeline de rendu déterministe.
/// </summary>
public sealed record BoundAudioAsset(Func<double, byte[]> GenerateSpectrumTextureBgra)
{
    public const int TextureWidth = 512;
    public const int TextureHeight = 2;
}
