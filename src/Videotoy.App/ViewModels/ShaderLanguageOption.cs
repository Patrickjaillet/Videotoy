using Videotoy.Core;

namespace Videotoy.App.ViewModels;

/// <summary>
/// Enveloppe bindable d'un <see cref="ShaderModel.ShaderSourceLanguage"/>
/// pour le sélecteur manuel de langage de la barre de statut (Phase v1.7.0)
/// — même convention <c>Key</c>/<c>DisplayName</c>/<c>Value</c> que les
/// autres <c>*Option</c> de <see cref="EncodingPresets"/>/<see cref="ExportPresets"/>.
/// </summary>
public sealed record ShaderLanguageOption(string Key, string DisplayName, ShaderModel.ShaderSourceLanguage Value)
{
    public static readonly ShaderLanguageOption Glsl = new("Glsl", "GLSL", ShaderModel.ShaderSourceLanguage.Glsl);
    public static readonly ShaderLanguageOption Wgsl = new("Wgsl", "WGSL", ShaderModel.ShaderSourceLanguage.Wgsl);
    public static readonly ShaderLanguageOption Hlsl = new("Hlsl", "HLSL", ShaderModel.ShaderSourceLanguage.Hlsl);

    public static readonly IReadOnlyList<ShaderLanguageOption> All = [Glsl, Wgsl, Hlsl];

    public static ShaderLanguageOption FromKey(string key) =>
        All.FirstOrDefault(option => option.Key == key) ?? Glsl;

    public static ShaderLanguageOption FromLanguage(ShaderModel.ShaderSourceLanguage language) =>
        All.FirstOrDefault(option => option.Key == ShaderModel.languageKey(language)) ?? Glsl;
}
