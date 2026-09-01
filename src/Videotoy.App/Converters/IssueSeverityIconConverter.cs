using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Videotoy.App.Converters;

/// <summary>
/// Icône (triangle avertissement / cercle erreur) accompagnant la couleur
/// déjà fournie par <see cref="IssueSeverityBrushConverter"/> — la sévérité
/// n'était auparavant distinguée que par la couleur, un manque
/// d'accessibilité corrigé en Phase v1.8.0.
/// </summary>
public sealed class IssueSeverityIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isError = value is bool booleanValue && booleanValue;
        var key = isError ? "IconError" : "IconWarning";
        return Application.Current.TryFindResource(key) as Geometry ?? Geometry.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
