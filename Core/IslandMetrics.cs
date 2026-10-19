namespace DynamicIsland.Core;

public static class IslandMetrics
{
    public const double CollapsedWidth = 240;
    public const double CollapsedHeight = 48;
    public const double PeekWidth = 380;
    public const double PeekHeight = 150;
    public const double ExpandedWidth = 380;
    public const double ExpandedHeight = 220;
    public const double CornerRadius = 30;
    public const double CornerSmoothing = 0.6;
    public const double ShadowMargin = 26;
    public const double ShadowBleed = 3;
    public const double ShadowShellWidth = ExpandedWidth + ShadowBleed * 2;
    public const double ShadowShellHeight = ExpandedHeight + ShadowBleed * 2;
    public const double TopOffset = 14;
    public const double ContentInset = 18;

    public static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(360);
    public static readonly TimeSpan CollapseDuration = TimeSpan.FromMilliseconds(340);
    public static readonly TimeSpan PeekExpandDuration = TimeSpan.FromMilliseconds(340);
    public static readonly TimeSpan ContentFadeDuration = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan CollapsedFadeDuration = TimeSpan.FromMilliseconds(170);
    public static readonly TimeSpan ReducedMotionDuration = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan HoverFeedbackDuration = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan PeekDuration = TimeSpan.FromSeconds(3.2);
    public static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(900);
    public static readonly TimeSpan TrackCrossfadeDuration = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan ProgressSmoothingDuration = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan ProgressUpdateInterval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan MenuAnimationDuration = TimeSpan.FromMilliseconds(110);
    public static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(2);
}