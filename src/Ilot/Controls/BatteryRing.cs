using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Ilot.Controls;

/// <summary>Jauge circulaire de batterie avec une lettre au centre.</summary>
internal sealed class BatteryRing : FrameworkElement
{
    private const double StrokeThickness = 2.5;
    private const int LowLevel = 20;

    private static readonly Pen NormalPen = CreatePen(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly Pen LowPen = CreatePen(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static readonly Pen ChargingPen = CreatePen(Color.FromRgb(0x60, 0xCD, 0xFF));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private readonly string _label;
    private int _level;
    private bool _isCharging;

    public BatteryRing(string label, string toolTip)
    {
        _label = label;
        Width = 26;
        Height = 26;
        ToolTip = toolTip;
        Visibility = Visibility.Collapsed;
    }

    public void Update(int? level, bool isCharging)
    {
        Visibility = level.HasValue ? Visibility.Visible : Visibility.Collapsed;
        _level = level ?? 0;
        _isCharging = isCharging;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (Math.Min(ActualWidth, ActualHeight) - StrokeThickness) / 2;
        var trackPen = new Pen((Brush)FindResource("PressedBrush"), StrokeThickness);
        context.DrawEllipse(null, trackPen, center, radius, radius);

        var pen = _isCharging ? ChargingPen : _level <= LowLevel ? LowPen : NormalPen;
        if (_level >= 100)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
        }
        else if (_level > 0)
        {
            context.DrawGeometry(null, pen, CreateArc(center, radius, _level / 100.0));
        }

        var text = new FormattedText(_label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 10,
            (Brush)FindResource("TextPrimaryBrush"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private static StreamGeometry CreateArc(Point center, double radius, double fraction)
    {
        var angle = fraction * 2 * Math.PI;
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(new Point(center.X, center.Y - radius), false, false);
            stream.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Pen CreatePen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}
