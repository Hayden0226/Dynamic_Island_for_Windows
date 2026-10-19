using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland.Controls;

public sealed class SquircleBorder : Decorator
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill),
        typeof(Brush),
        typeof(SquircleBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(SquircleBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(SquircleBorder),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(double),
        typeof(SquircleBorder),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnShapePropertyChanged));

    public static readonly DependencyProperty CornerSmoothingProperty = DependencyProperty.Register(
        nameof(CornerSmoothing),
        typeof(double),
        typeof(SquircleBorder),
        new FrameworkPropertyMetadata(0.6, FrameworkPropertyMetadataOptions.AffectsRender, OnShapePropertyChanged));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding),
        typeof(Thickness),
        typeof(SquircleBorder),
        new FrameworkPropertyMetadata(
            default(Thickness),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty ClipContentProperty = DependencyProperty.Register(
        nameof(ClipContent),
        typeof(bool),
        typeof(SquircleBorder),
        new FrameworkPropertyMetadata(true, OnClipContentChanged));

    public static readonly DependencyProperty SurfaceWidthProperty = DependencyProperty.Register(
        nameof(SurfaceWidth),
        typeof(double),
        typeof(SquircleBorder),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender, OnShapePropertyChanged));

    public static readonly DependencyProperty SurfaceHeightProperty = DependencyProperty.Register(
        nameof(SurfaceHeight),
        typeof(double),
        typeof(SquircleBorder),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender, OnShapePropertyChanged));

    private Geometry? _cachedGeometry;
    private Size _cachedSize;
    private double _cachedSurfaceOffsetX;
    private double _cachedCornerRadius;
    private double _cachedCornerSmoothing;
    private Pen? _cachedPen;
    private Brush? _cachedPenBrush;
    private double _cachedPenThickness;

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double CornerSmoothing
    {
        get => (double)GetValue(CornerSmoothingProperty);
        set => SetValue(CornerSmoothingProperty, value);
    }

    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    public bool ClipContent
    {
        get => (bool)GetValue(ClipContentProperty);
        set => SetValue(ClipContentProperty, value);
    }

    public double SurfaceWidth
    {
        get => (double)GetValue(SurfaceWidthProperty);
        set => SetValue(SurfaceWidthProperty, value);
    }

    public double SurfaceHeight
    {
        get => (double)GetValue(SurfaceHeightProperty);
        set => SetValue(SurfaceHeightProperty, value);
    }

    public Geometry GetGeometry()
    {
        var width = GetSurfaceWidth();
        var height = GetSurfaceHeight();
        if (width <= 0 || height <= 0)
        {
            return Geometry.Empty;
        }

        var radius = CornerRadius;
        var smoothing = CornerSmoothing;
        var offsetX = Math.Max(0, (ActualWidth - width) / 2);
        var size = new Size(width, height);
        if (_cachedGeometry is not null &&
            _cachedSize == size &&
            Math.Abs(_cachedSurfaceOffsetX - offsetX) < 0.001 &&
            Math.Abs(_cachedCornerRadius - radius) < 0.001 &&
            Math.Abs(_cachedCornerSmoothing - smoothing) < 0.001)
        {
            return _cachedGeometry;
        }

        _cachedGeometry = CreateSquircleGeometry(width, height, radius, smoothing, offsetX);
        _cachedSize = size;
        _cachedSurfaceOffsetX = offsetX;
        _cachedCornerRadius = radius;
        _cachedCornerSmoothing = smoothing;
        return _cachedGeometry;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var child = Child;
        if (child is null)
        {
            return new Size(0, 0);
        }

        var padding = Padding;
        var innerWidth = double.IsPositiveInfinity(constraint.Width)
            ? constraint.Width
            : Math.Max(0, constraint.Width - padding.Left - padding.Right);
        var innerHeight = double.IsPositiveInfinity(constraint.Height)
            ? constraint.Height
            : Math.Max(0, constraint.Height - padding.Top - padding.Bottom);

        child.Measure(new Size(innerWidth, innerHeight));
        return new Size(
            child.DesiredSize.Width + padding.Left + padding.Right,
            child.DesiredSize.Height + padding.Top + padding.Bottom);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var padding = Padding;
        var childWidth = Math.Max(0, arrangeSize.Width - padding.Left - padding.Right);
        var childHeight = Math.Max(0, arrangeSize.Height - padding.Top - padding.Bottom);
        Child?.Arrange(new Rect(padding.Left, padding.Top, childWidth, childHeight));
        return arrangeSize;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateGeometry();
        UpdateClip();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var geometry = GetGeometry();
        if (geometry == Geometry.Empty)
        {
            return;
        }

        if (Fill is not null)
        {
            drawingContext.DrawGeometry(Fill, null, geometry);
        }

        var pen = GetPen();
        if (pen is not null)
        {
            drawingContext.DrawGeometry(null, pen, geometry);
        }
    }

    private Pen? GetPen()
    {
        var brush = Stroke;
        var thickness = StrokeThickness;
        if (brush is null || !double.IsFinite(thickness) || thickness <= 0)
        {
            return null;
        }

        if (_cachedPen is not null &&
            ReferenceEquals(_cachedPenBrush, brush) &&
            Math.Abs(_cachedPenThickness - thickness) < 0.001)
        {
            return _cachedPen;
        }

        var pen = new Pen(brush, thickness);
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        _cachedPen = pen;
        _cachedPenBrush = brush;
        _cachedPenThickness = thickness;
        return pen;
    }

    private double GetSurfaceWidth() =>
        double.IsNaN(SurfaceWidth) ? ActualWidth : Math.Clamp(SurfaceWidth, 0, ActualWidth);

    private double GetSurfaceHeight() =>
        double.IsNaN(SurfaceHeight) ? ActualHeight : Math.Clamp(SurfaceHeight, 0, ActualHeight);

    public static Geometry CreateSquircleGeometry(
        double width,
        double height,
        double cornerRadius,
        double cornerSmoothing,
        double offsetX = 0)
    {
        if (width <= 0 || height <= 0)
        {
            return Geometry.Empty;
        }

        var budget = Math.Min(width, height) / 2.0;
        var radius = Math.Clamp(cornerRadius, 0, budget);
        if (radius <= 0)
        {
            var rectangle = new RectangleGeometry(new Rect(offsetX, 0, width, height));
            rectangle.Freeze();
            return rectangle;
        }

        var originalSmoothing = Math.Clamp(cornerSmoothing, 0, 1);
        var maxCornerSmoothing = Math.Max(0, budget / radius - 1);
        var smoothing = Math.Min(originalSmoothing, maxCornerSmoothing);
        var p = Math.Min((1 + smoothing) * radius, budget);

        var arcMeasure = 90 * (1 - smoothing);
        var arcSectionLength = Math.Sin(ToRadians(arcMeasure / 2)) * radius * Math.Sqrt(2);
        var angleAlpha = (90 - arcMeasure) / 2;
        var p3ToP4Distance = radius * Math.Tan(ToRadians(angleAlpha / 2));
        var angleBeta = 45 * smoothing;
        var c = p3ToP4Distance * Math.Cos(ToRadians(angleBeta));
        var d = c * Math.Tan(ToRadians(angleBeta));
        var b = (p - arcSectionLength - c - d) / 3;
        var a = 2 * b;

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(width - p, 0), true, true);

            var start = new Point(width - p, 0);
            var control1 = new Point(start.X + a, start.Y);
            var control2 = new Point(start.X + a + b, start.Y);
            var end = new Point(start.X + a + b + c, start.Y + d);
            context.BezierTo(control1, control2, end, true, false);

            var arcEnd = new Point(end.X + arcSectionLength, end.Y + arcSectionLength);
            context.ArcTo(
                arcEnd,
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                false);

            control1 = new Point(arcEnd.X + d, arcEnd.Y + c);
            control2 = new Point(arcEnd.X + d, arcEnd.Y + b + c);
            end = new Point(arcEnd.X + d, arcEnd.Y + a + b + c);
            context.BezierTo(control1, control2, end, true, false);

            context.LineTo(new Point(width, height - p), true, false);

            start = new Point(width, height - p);
            control1 = new Point(start.X, start.Y + a);
            control2 = new Point(start.X, start.Y + a + b);
            end = new Point(start.X - d, start.Y + a + b + c);
            context.BezierTo(control1, control2, end, true, false);

            arcEnd = new Point(end.X - arcSectionLength, end.Y + arcSectionLength);
            context.ArcTo(
                arcEnd,
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                false);

            control1 = new Point(arcEnd.X - c, arcEnd.Y + d);
            control2 = new Point(arcEnd.X - (b + c), arcEnd.Y + d);
            end = new Point(arcEnd.X - (a + b + c), arcEnd.Y + d);
            context.BezierTo(control1, control2, end, true, false);

            context.LineTo(new Point(p, height), true, false);

            start = new Point(p, height);
            control1 = new Point(start.X - a, start.Y);
            control2 = new Point(start.X - (a + b), start.Y);
            end = new Point(start.X - (a + b + c), start.Y - d);
            context.BezierTo(control1, control2, end, true, false);

            arcEnd = new Point(end.X - arcSectionLength, end.Y - arcSectionLength);
            context.ArcTo(
                arcEnd,
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                false);

            control1 = new Point(arcEnd.X - d, arcEnd.Y - c);
            control2 = new Point(arcEnd.X - d, arcEnd.Y - (b + c));
            end = new Point(arcEnd.X - d, arcEnd.Y - (a + b + c));
            context.BezierTo(control1, control2, end, true, false);

            context.LineTo(new Point(0, p), true, false);

            start = new Point(0, p);
            control1 = new Point(start.X, start.Y - a);
            control2 = new Point(start.X, start.Y - (a + b));
            end = new Point(start.X + d, start.Y - (a + b + c));
            context.BezierTo(control1, control2, end, true, false);

            arcEnd = new Point(end.X + arcSectionLength, end.Y - arcSectionLength);
            context.ArcTo(
                arcEnd,
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                false);

            control1 = new Point(arcEnd.X + c, arcEnd.Y - d);
            control2 = new Point(arcEnd.X + (b + c), arcEnd.Y - d);
            end = new Point(arcEnd.X + (a + b + c), arcEnd.Y - d);
            context.BezierTo(control1, control2, end, true, false);
        }

        if (offsetX != 0)
        {
            var transform = new TranslateTransform(offsetX, 0);
            transform.Freeze();
            geometry.Transform = transform;
        }

        geometry.Freeze();
        return geometry;
    }

    private static void OnShapePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var border = (SquircleBorder)dependencyObject;
        border.InvalidateGeometry();
        border.UpdateClip();
        border.InvalidateVisual();
    }

    private void InvalidateGeometry()
    {
        _cachedGeometry = null;
    }

    private void UpdateClip()
    {
        if (!ClipContent)
        {
            Clip = null;
            return;
        }

        var geometry = GetGeometry();
        Clip = geometry == Geometry.Empty ? null : geometry;
    }

    private static void OnClipContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((SquircleBorder)dependencyObject).UpdateClip();
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}