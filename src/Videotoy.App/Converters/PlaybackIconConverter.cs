using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Videotoy.App.Converters;

public sealed class PlaybackIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isPlaying = value is bool booleanValue && booleanValue;
        var resourceKey = isPlaying ? "IconPause" : "IconPlay";
        return (Geometry)Application.Current.Resources[resourceKey];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
