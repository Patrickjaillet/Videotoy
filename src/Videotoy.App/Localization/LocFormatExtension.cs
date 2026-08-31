using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Videotoy.App.Localization;

/// <summary>
/// XAML markup extension resolving a localized composite format string and
/// applying it to a value from the current <c>DataContext</c>, e.g.
/// <c>Text="{loc:LocFormat Key=statusBar.currentFrame, Path=CurrentFrame}"</c>
/// replaces the previous hard-coded
/// <c>Text="{Binding CurrentFrame, StringFormat='Frame {0}'}"</c>. Combines
/// the localized string (which changes on language switch) with the bound
/// property (which changes normally) through a <see cref="MultiBinding"/>,
/// so both updates are reflected instantly.
/// </summary>
public sealed class LocFormatExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var multiBinding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = LocFormatConverter.Instance
        };

        multiBinding.Bindings.Add(new Binding($"[{Key}]")
        {
            Source = LocalizedStrings.Instance,
            Mode = BindingMode.OneWay
        });

        multiBinding.Bindings.Add(new Binding(Path)
        {
            Mode = BindingMode.OneWay
        });

        return multiBinding.ProvideValue(serviceProvider);
    }
}
