using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Videotoy.App.ViewModels;

namespace Videotoy.App.Converters;

public sealed class ToastSeverityBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x2F, 0xA7, 0x65));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xD8, 0x48, 0x3C));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ToastSeverity.Success ? SuccessBrush : ErrorBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
