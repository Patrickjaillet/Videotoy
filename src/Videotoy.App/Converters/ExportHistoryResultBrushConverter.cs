using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Videotoy.Media;

namespace Videotoy.App.Converters;

public sealed class ExportHistoryResultBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x2F, 0xA7, 0x65));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xD8, 0x48, 0x3C));
    private static readonly SolidColorBrush NeutralBrush = new(Color.FromRgb(0x8A, 0x8A, 0x8A));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            ExportHistoryResult.Succeeded => SuccessBrush,
            ExportHistoryResult.Failed => ErrorBrush,
            _ => NeutralBrush
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
