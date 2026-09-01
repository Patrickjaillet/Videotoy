using System.Globalization;
using System.Windows.Data;
using Videotoy.App.Localization;
using Videotoy.Media;

namespace Videotoy.App.Converters;

public sealed class ExportHistoryResultTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ExportHistoryResult.Succeeded => "export.history.result.succeeded",
            ExportHistoryResult.Failed => "export.history.result.failed",
            ExportHistoryResult.Cancelled => "export.history.result.cancelled",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(key) ? string.Empty : LocalizationRuntime.Service.GetString(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
