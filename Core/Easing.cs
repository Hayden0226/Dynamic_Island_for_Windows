namespace DynamicIsland.Core;

public static class Easing
{
    public static double OutCubic(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var inverse = 1 - t;
        return 1 - inverse * inverse * inverse;
    }

    public static double InOutCubic(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5
            ? 4 * t * t * t
            : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    public static double OutQuint(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var inverse = 1 - t;
        return 1 - inverse * inverse * inverse * inverse * inverse;
    }

    public static double InOutQuart(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5
            ? 8 * t * t * t * t
            : 1 - Math.Pow(-2 * t + 2, 4) / 2;
    }

    public static double SmootherStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    public static double SpringOut(double t, double stiffness = 6.0)
    {
        t = Math.Clamp(t, 0, 1);
        if (t <= 0)
        {
            return 0;
        }

        if (t >= 1)
        {
            return 1;
        }

        stiffness = Math.Clamp(stiffness, 1, 20);
        var normalizer = 1 - (1 + stiffness) * Math.Exp(-stiffness);
        return (1 - (1 + stiffness * t) * Math.Exp(-stiffness * t)) / normalizer;
    }

    public static double Balanced(double t) => CubicBezier(t, 0.32, 0, 0.24, 1);

    public static double CubicBezier(double t, double x1, double y1, double x2, double y2)
    {
        t = Math.Clamp(t, 0, 1);
        if (t <= 0)
        {
            return 0;
        }

        if (t >= 1)
        {
            return 1;
        }

        var u = t;
        for (var i = 0; i < 6; i++)
        {
            var x = Bezier(u, x1, x2);
            var slope = BezierSlope(u, x1, x2);
            if (Math.Abs(slope) < 0.000001)
            {
                break;
            }

            u = Math.Clamp(u - (x - t) / slope, 0, 1);
        }

        return Bezier(u, y1, y2);
    }

    private static double Bezier(double u, double p1, double p2)
    {
        var inverse = 1 - u;
        return 3 * inverse * inverse * u * p1 + 3 * inverse * u * u * p2 + u * u * u;
    }

    private static double BezierSlope(double u, double p1, double p2)
    {
        var inverse = 1 - u;
        return 3 * inverse * inverse * p1 + 6 * inverse * u * (p2 - p1) + 3 * u * u * (1 - p2);
    }

    public static double Linear(double t) => Math.Clamp(t, 0, 1);
}