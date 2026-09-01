using System.Globalization;
using System.Windows.Data;
using Videotoy.App.Localization;
using Videotoy.App.ViewModels;

namespace Videotoy.App.Converters;

public sealed class ContainerFormatDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ContainerFormatOption option when option == ContainerFormatOption.Mp4 => "panel.container.description.mp4",
            ContainerFormatOption option when option == ContainerFormatOption.WebM => "panel.container.description.webm",
            ContainerFormatOption option when option == ContainerFormatOption.Mov => "panel.container.description.mov",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(key) ? string.Empty : LocalizationRuntime.Service.GetString(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
