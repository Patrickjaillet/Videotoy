using System;
using System.Globalization;
using System.Windows.Data;

namespace Videotoy.App.Localization;

/// <summary>
/// Formats <c>values[1]</c> (the bound <c>Path</c> value, e.g.
/// <c>CurrentFrame</c>) using <c>values[0]</c> as a composite format string
/// (e.g. the localized <c>"Frame {0}"</c> / <c>"Frame {0}"</c> resource).
/// Used exclusively by <see cref="LocFormatExtension"/>; both input values
/// are re-supplied automatically by the owning <see cref="MultiBinding"/>
/// whenever either the language or the source property changes.
/// </summary>
internal sealed class LocFormatConverter : IMultiValueConverter
{
    public static readonly LocFormatConverter Instance = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [string format, var value] || format is null)
        {
            return string.Empty;
        }

        try
        {
            return string.Format(culture, format, value);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("LocFormat bindings are one-way.");
    }
}
