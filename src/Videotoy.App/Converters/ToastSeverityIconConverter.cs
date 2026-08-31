using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Videotoy.App.ViewModels;

namespace Videotoy.App.Converters;

/// <summary>
/// Maps a <see cref="ToastSeverity"/> to a small checkmark (success) or
/// cross (error) glyph, drawn directly as path data rather than pulled from
/// <c>Icons.xaml</c> since neither glyph is otherwise needed elsewhere.
/// </summary>
public sealed class ToastSeverityIconConverter : IValueConverter
{
    private static readonly Geometry CheckGeometry = Geometry.Parse("M4,12 L9,17 L18,6");
    private static readonly Geometry CrossGeometry = Geometry.Parse("M5,5 L15,15 M15,5 L5,15");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ToastSeverity.Success ? CheckGeometry : CrossGeometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
