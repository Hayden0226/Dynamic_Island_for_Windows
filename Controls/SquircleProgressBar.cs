using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Controls;

public sealed class SquircleProgressBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(SquircleProgressBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(SquircleProgressBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush),
        typeof(Brush),
        typeof(SquircleProgressBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerSmoothingProperty = DependencyProperty.Register(
        nameof(CornerSmoothing),
        typeof(double),
        typeof(SquircleProgressBar),
        new FrameworkPropertyMetadata(0.6, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public double CornerSmoothing
    {
        get => (double)GetValue(CornerSmoothingProperty);
        set => SetValue(CornerSmoothingProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var radius = height / 2;
        if (TrackBrush is not null)
        {
            var track = SquircleBorder.CreateSquircleGeometry(width, height, radius, CornerSmoothing);
            drawingContext.DrawGeometry(TrackBrush, null, track);
        }

        var value = Math.Clamp(Value, 0, 1);
        var fillWidth = width * value;
        if (FillBrush is null || fillWidth < 0.25)
        {
            return;
        }

        var fillRadius = Math.Min(radius, fillWidth / 2);
        var fill = SquircleBorder.CreateSquircleGeometry(fillWidth, height, fillRadius, CornerSmoothing);
        drawingContext.DrawGeometry(FillBrush, null, fill);
    }
}
