using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Videotoy.App.Converters;

/// <summary>
/// Converts an export progress percentage (0-100, as bound from
/// <see cref="Videotoy.App.ViewModels.MainWindowViewModel.ExportProgressPercent"/>)
/// into the <see cref="Geometry"/> of a circular arc, for the animated
/// progress ring shown over the preview viewport while a video is
/// rendering. The ring is centered at (18, 18) with a 15px radius; at 0% no
/// arc is drawn (empty geometry, so the ring reads as "just started" rather
/// than a full circle), and the arc sweeps clockwise from the top as
/// progress increases, reaching a full circle at 100%.
/// </summary>
public sealed class ProgressRingArcConverter : IValueConverter
{
    private const double CenterX = 18.0;
    private const double CenterY = 18.0;
    private const double Radius = 15.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            _ => 0.0
        };

        var clamped = Math.Clamp(percent, 0.0, 100.0);
        if (clamped <= 0.0)
        {
            return Geometry.Empty;
        }

        // Treat values effectively at 100% as a full circle, drawn as two
        // half-arcs since a single ArcSegment cannot express a 360° sweep.
        if (clamped >= 99.95)
        {
            var figure = new PathFigure { StartPoint = new System.Windows.Point(CenterX, CenterY - Radius), IsClosed = false };
            figure.Segments.Add(new ArcSegment(
                new System.Windows.Point(CenterX, CenterY + Radius),
                new System.Windows.Size(Radius, Radius),
                0,
                false,
                SweepDirection.Clockwise,
                true));
            figure.Segments.Add(new ArcSegment(
                new System.Windows.Point(CenterX, CenterY - Radius),
                new System.Windows.Size(Radius, Radius),
                0,
                false,
                SweepDirection.Clockwise,
                true));
            var fullGeometry = new PathGeometry();
            fullGeometry.Figures.Add(figure);
            return fullGeometry;
        }

        var sweepAngleDegrees = clamped / 100.0 * 360.0;
        var startPoint = PointOnCircle(0);
        var endPoint = PointOnCircle(sweepAngleDegrees);
        var isLargeArc = sweepAngleDegrees > 180.0;

        var pathFigure = new PathFigure { StartPoint = startPoint, IsClosed = false };
        pathFigure.Segments.Add(new ArcSegment(
            endPoint,
            new System.Windows.Size(Radius, Radius),
            0,
            isLargeArc,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(pathFigure);
        return geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Point on the ring's circle at <paramref name="angleDegrees"/>
    /// clockwise from the top (12 o'clock = 0°).
    /// </summary>
    private static System.Windows.Point PointOnCircle(double angleDegrees)
    {
        var radians = (angleDegrees - 90.0) * Math.PI / 180.0;
        return new System.Windows.Point(
            CenterX + Radius * Math.Cos(radians),
            CenterY + Radius * Math.Sin(radians));
    }
}
