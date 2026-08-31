using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Videotoy.App.Converters;

public sealed class IssueSeverityBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xD8, 0x48, 0x3C));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0xC8, 0x8A, 0x2E));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isError = value is bool booleanValue && booleanValue;
        return isError ? ErrorBrush : WarningBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
