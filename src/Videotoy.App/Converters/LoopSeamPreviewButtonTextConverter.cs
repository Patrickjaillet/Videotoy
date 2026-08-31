using System.Globalization;
using System.Windows.Data;

namespace Videotoy.App.Converters;

public sealed class LoopSeamPreviewButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isGenerating = value is bool booleanValue && booleanValue;
        return isGenerating ? "Generating..." : "Generate loop seam preview";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
