using System.Globalization;
using System.Windows.Data;
using Videotoy.App.Localization;
using Videotoy.Media;

namespace Videotoy.App.Converters;

public sealed class RenderQueueItemStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            RenderQueueItemStatus.Pending => "renderQueue.status.pending",
            RenderQueueItemStatus.Running => "renderQueue.status.running",
            RenderQueueItemStatus.Succeeded => "renderQueue.status.succeeded",
            RenderQueueItemStatus.Failed => "renderQueue.status.failed",
            RenderQueueItemStatus.Cancelled => "renderQueue.status.cancelled",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(key) ? string.Empty : LocalizationRuntime.Service.GetString(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
