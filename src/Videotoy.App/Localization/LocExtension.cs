using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Videotoy.App.Localization;

/// <summary>
/// XAML markup extension resolving a single localized string by key, e.g.
/// <c>Text="{loc:Loc Key=menu.file}"</c>. Produces an indexer binding onto
/// the shared <see cref="LocalizedStrings.Instance"/>, so every element
/// using it refreshes instantly when
/// <see cref="Videotoy.Media.LocalizationService.SetLanguage"/> is called —
/// no window reload or application restart required.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizedStrings.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
