using System.Diagnostics;
using System.Windows.Media;

namespace DynamicIsland.Core;

public readonly record struct IslandFrame(
    double Width,
    double Height,
    double CornerRadius,
    double CollapsedOpacity,
    double ContentOpacity,
    double ContentOffsetY,
    double ControlsOpacity,
    double BottomOpacity,
    double BottomOffsetY);

public sealed class IslandAnimator : IDisposable
{
    private readonly Action<IslandFrame> _apply;
    private Transition? _transition;
    private TimeSpan? _transitionStart;
    private IslandFrame _current;
    private IslandState _state;
    private bool _isSubscribed;

    public IslandAnimator(Action<IslandFrame> apply, IslandState initialState = IslandState.Collapsed)
    {
        _apply = apply;
        _state = initialState;
        _current = FrameForState(initialState);
        _apply(_current);
    }

    public IslandFrame Current => _current;

    public IslandState State => _state;

    public static IslandFrame FrameForState(IslandState state)
    {
        var (width, height) = state switch
        {
            IslandState.Expanded => (IslandMetrics.ExpandedWidth, IslandMetrics.ExpandedHeight),
            IslandState.Peek => (IslandMetrics.PeekWidth, IslandMetrics.PeekHeight),
            _ => (IslandMetrics.CollapsedWidth, IslandMetrics.CollapsedHeight)
        };

        var cornerRadius = Math.Min(IslandMetrics.CornerRadius, height / 2);
        return state switch
        {
            IslandState.Expanded => new IslandFrame(width, height, cornerRadius, 0, 1, 0, 1, 1, 0),
            IslandState.Peek => new IslandFrame(width, height, cornerRadius, 0, 1, 0, 0, 0, 0),
            _ => new IslandFrame(width, height, cornerRadius, 1, 0, 0, 0, 0, 0)
        };
    }

    public void AnimateTo(IslandState state, bool reduceMotion)
    {
        if (state == _state && _transition is null)
        {
            return;
        }

        var fromState = _state;
        var target = FrameForState(state);
        _state = state;
        _transition = BuildTransition(_current, target, fromState, state, reduceMotion);
        _transitionStart = null;
        Subscribe();
    }

    public void SnapTo(IslandState state)
    {
        _state = state;
        _current = FrameForState(state);
        _transition = null;
        _transitionStart = null;
        Unsubscribe();
        _apply(_current);
    }

    public void Refresh(bool reduceMotion)
    {
        _transition = BuildTransition(_current, FrameForState(_state), _state, _state, reduceMotion);
        _transitionStart = null;
        Subscribe();
    }

    public void Dispose()
    {
        Unsubscribe();
    }

    private static Transition BuildTransition(
        IslandFrame from,
        IslandFrame to,
        IslandState fromState,
        IslandState toState,
        bool reduceMotion)
    {
        var toCollapsed = toState == IslandState.Collapsed;
        var toExpanded = toState == IslandState.Expanded;
        var fromCollapsed = fromState == IslandState.Collapsed;

        var reducedMilliseconds = IslandMetrics.ReducedMotionDuration.TotalMilliseconds;
        var sizeDuration = reduceMotion
            ? 0
            : toCollapsed
                ? IslandMetrics.CollapseDuration.TotalMilliseconds
                : toExpanded
                    ? IslandMetrics.ExpandDuration.TotalMilliseconds
                    : IslandMetrics.PeekExpandDuration.TotalMilliseconds;

        Func<double, double> sizeEase = Easing.Balanced;

        double collapsedDelay;
        double collapsedDuration;
        if (reduceMotion)
        {
            collapsedDelay = 0;
            collapsedDuration = reducedMilliseconds;
        }
        else if (toCollapsed)
        {
            collapsedDelay = 180;
            collapsedDuration = 150;
        }
        else
        {
            collapsedDelay = 0;
            collapsedDuration = 120;
        }

        double contentDelay;
        double contentDuration;
        if (reduceMotion)
        {
            contentDelay = 0;
            contentDuration = reducedMilliseconds;
        }
        else if (toCollapsed)
        {
            contentDelay = 0;
            contentDuration = 120;
        }
        else if (fromCollapsed)
        {
            contentDelay = toExpanded ? 160 : 150;
            contentDuration = 180;
        }
        else
        {
            contentDelay = 0;
            contentDuration = 0;
        }

        double controlsDelay;
        double controlsDuration;
        if (reduceMotion)
        {
            controlsDelay = 0;
            controlsDuration = reducedMilliseconds;
        }
        else if (toExpanded)
        {
            controlsDelay = fromCollapsed ? 230 : 80;
            controlsDuration = 150;
        }
        else
        {
            controlsDelay = 0;
            controlsDuration = 90;
        }

        double bottomDelay;
        double bottomDuration;
        if (reduceMotion)
        {
            bottomDelay = 0;
            bottomDuration = reducedMilliseconds;
        }
        else if (toExpanded)
        {
            bottomDelay = fromCollapsed ? 260 : 120;
            bottomDuration = 150;
        }
        else
        {
            bottomDelay = 0;
            bottomDuration = 90;
        }

        var contentOffsetFrom = toCollapsed ? from.ContentOffsetY : fromCollapsed ? 3 : from.ContentOffsetY;
        var contentOffsetTo = toCollapsed ? 3 : 0;
        var bottomOffsetFrom = toExpanded && from.BottomOpacity <= 0.5 ? 3 : from.BottomOffsetY;
        var bottomOffsetTo = toExpanded ? 0 : 3;

        var transition = new Transition
        {
            Width = new Track(from.Width, to.Width, 0, sizeDuration, sizeEase),
            Height = new Track(from.Height, to.Height, 0, sizeDuration, sizeEase),
            CornerRadius = new Track(from.CornerRadius, to.CornerRadius, 0, sizeDuration, sizeEase),
            CollapsedOpacity = new Track(
                from.CollapsedOpacity,
                to.CollapsedOpacity,
                collapsedDelay,
                collapsedDuration,
                Easing.OutCubic),
            ContentOpacity = new Track(
                from.ContentOpacity,
                to.ContentOpacity,
                contentDelay,
                contentDuration,
                Easing.OutCubic),
            ContentOffsetY = new Track(
                contentOffsetFrom,
                contentOffsetTo,
                toCollapsed ? 0 : contentDelay,
                toCollapsed ? 120 : contentDuration,
                Easing.OutCubic),
            ControlsOpacity = new Track(
                from.ControlsOpacity,
                to.ControlsOpacity,
                controlsDelay,
                controlsDuration,
                Easing.OutCubic),
            BottomOpacity = new Track(
                from.BottomOpacity,
                to.BottomOpacity,
                bottomDelay,
                bottomDuration,
                Easing.OutCubic),
            BottomOffsetY = new Track(
                bottomOffsetFrom,
                bottomOffsetTo,
                bottomDelay,
                bottomDuration,
                Easing.OutCubic),
            Target = to
        };

        transition.TotalMilliseconds = transition.MaximumMilliseconds();
        return transition;
    }

    private void Subscribe()
    {
        if (_isSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering += CompositionTarget_Rendering;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        _isSubscribed = false;
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        var transition = _transition;
        if (transition is null)
        {
            Unsubscribe();
            return;
        }

        if (e is not RenderingEventArgs rendering)
        {
            return;
        }

        _transitionStart ??= rendering.RenderingTime;
        var elapsed = (rendering.RenderingTime - _transitionStart.Value).TotalMilliseconds;
        if (elapsed >= transition.TotalMilliseconds)
        {
            _current = transition.Target;
            _transition = null;
            _transitionStart = null;
            Unsubscribe();
            _apply(_current);
            return;
        }

        _current = new IslandFrame(
            transition.Width.ValueAt(elapsed),
            transition.Height.ValueAt(elapsed),
            transition.CornerRadius.ValueAt(elapsed),
            transition.CollapsedOpacity.ValueAt(elapsed),
            transition.ContentOpacity.ValueAt(elapsed),
            transition.ContentOffsetY.ValueAt(elapsed),
            transition.ControlsOpacity.ValueAt(elapsed),
            transition.BottomOpacity.ValueAt(elapsed),
            transition.BottomOffsetY.ValueAt(elapsed));
        _apply(_current);
    }

    private readonly record struct Track(
        double From,
        double To,
        double DelayMilliseconds,
        double DurationMilliseconds,
        Func<double, double> Ease)
    {
        public double ValueAt(double elapsedMilliseconds)
        {
            if (elapsedMilliseconds <= DelayMilliseconds)
            {
                return From;
            }

            if (DurationMilliseconds <= 0)
            {
                return To;
            }

            var progress = (elapsedMilliseconds - DelayMilliseconds) / DurationMilliseconds;
            return From + (To - From) * Ease(progress);
        }
    }

    private sealed class Transition
    {
        public required Track Width { get; init; }

        public required Track Height { get; init; }

        public required Track CornerRadius { get; init; }

        public required Track CollapsedOpacity { get; init; }

        public required Track ContentOpacity { get; init; }

        public required Track ContentOffsetY { get; init; }

        public required Track ControlsOpacity { get; init; }

        public required Track BottomOpacity { get; init; }

        public required Track BottomOffsetY { get; init; }

        public required IslandFrame Target { get; init; }

        public double TotalMilliseconds { get; set; }

        public double MaximumMilliseconds()
        {
            return new[]
            {
                Width, Height, CornerRadius, CollapsedOpacity,
                ContentOpacity, ContentOffsetY, ControlsOpacity, BottomOpacity, BottomOffsetY
            }.Max(track => track.DelayMilliseconds + track.DurationMilliseconds);
        }
    }
}