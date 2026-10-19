using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Interop;
using DynamicIsland.Models;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    private const double FocusMinutes = 25;

    private static readonly Brush PinActiveBrush = CreateBrush(Color.FromRgb(0x7C, 0x9C, 0xFF));
    private static readonly Brush PinInactiveBrush = CreateBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));

    private readonly DispatcherTimer _clockTimer = new();
    private readonly DispatcherTimer _collapseTimer = new();
    private readonly DispatcherTimer _peekTimer = new();
    private readonly DispatcherTimer _progressTimer = new();
    private readonly DispatcherTimer _statusTimer = new();
    private readonly DispatcherTimer _focusTimer = new();
    private readonly MediaSessionService _mediaService;
    private readonly SystemStatusService _systemStatus = new();
    private readonly WindowPlacementService _placement = new();
    private readonly IslandAnimator _animator;
    private readonly AppSettings _settings;
    private readonly Brush _artGradientBrush;

    private HwndSource? _hwndSource;
    private FullscreenWatcher? _fullscreenWatcher;
    private MediaSnapshot? _lastSnapshot;
    private DateTime _snapshotTimestampUtc;
    private string _lastIdentity = string.Empty;
    private bool _reduceMotion;
    private bool _pinned;
    private bool _peekActive;
    private bool _isFullscreen;
    private bool _focusRunning;
    private bool _receivedFirstSnapshot;
    private bool _isClosing;
    private bool _activeLayerIsA = true;
    private int _crossfadeGeneration;
    private TimeSpan _focusRemaining = TimeSpan.FromMinutes(FocusMinutes);
    private byte[]? _activeThumbnail;
    private bool _activeHasMedia;
    private byte[]? _collapsedThumbnail;
    private bool _collapsedHasMedia;

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsService.Load();
        _artGradientBrush = (Brush)FindResource("ArtGradient");
        _reduceMotion = _settings.ReduceMotion;
        _mediaService = new MediaSessionService(Dispatcher);
        _mediaService.SnapshotChanged += MediaService_SnapshotChanged;
        _animator = new IslandAnimator(ApplyFrame);

        _clockTimer.Tick += ClockTimer_Tick;
        _collapseTimer.Tick += CollapseTimer_Tick;
        _peekTimer.Tick += PeekTimer_Tick;
        _progressTimer.Interval = IslandMetrics.ProgressUpdateInterval;
        _progressTimer.Tick += (_, _) => UpdateProgressFromSnapshot(animate: true);
        _statusTimer.Interval = IslandMetrics.StatusInterval;
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _focusTimer.Interval = TimeSpan.FromSeconds(1);
        _focusTimer.Tick += FocusTimer_Tick;

        MenuPopup.Opened += (_, _) =>
        {
            _collapseTimer.Stop();
            AnimateMenuOpen();
        };
        MenuPopup.Closed += (_, _) =>
        {
            if (!_pinned && !IsMouseOver)
            {
                ScheduleCollapse(IslandMetrics.CollapseDelay);
            }
        };

        SetTextLayer(true, null);
        SetArtLayer(true, null);
        TrackTextB.Visibility = Visibility.Collapsed;
        AlbumArtB.Visibility = Visibility.Collapsed;
        CollapsedArt.Visibility = Visibility.Collapsed;
        PlayButton.Content = "\uE768";
        UpdateProgressFromSnapshot(animate: false);
        UpdateFocusUi();
        UpdateClock();
        ScheduleClockTick();
        _systemStatus.Prime();

        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(handle, NativeMethods.GwlExStyle, style | NativeMethods.WsExToolWindow);

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        _placement.Initialize(this);
        _placement.Refresh();
        _placement.Apply(IslandMetrics.ExpandedWidth, IslandMetrics.ExpandedHeight);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _placement.Initialize(this);
        _placement.Refresh();
        _placement.Apply(IslandMetrics.ExpandedWidth, IslandMetrics.ExpandedHeight);

        _fullscreenWatcher = new FullscreenWatcher(new WindowInteropHelper(this).Handle);
        _fullscreenWatcher.FullscreenChanged += FullscreenWatcher_FullscreenChanged;
        _fullscreenWatcher.Start();

        try
        {
            await _mediaService.StartAsync();
        }
        catch
        {
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _isClosing = true;
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        _mediaService.SnapshotChanged -= MediaService_SnapshotChanged;
        _mediaService.Dispose();
        _fullscreenWatcher?.Dispose();
        _clockTimer.Stop();
        _collapseTimer.Stop();
        _peekTimer.Stop();
        _progressTimer.Stop();
        _statusTimer.Stop();
        _focusTimer.Stop();
        _hwndSource?.RemoveHook(WndProc);
        _animator.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case NativeMethods.WmNcHitTest:
                handled = true;
                return new IntPtr(IsPointInsideShell(lParam) ? NativeMethods.HtClient : NativeMethods.HtTransparent);

            case NativeMethods.WmPowerBroadcast:
                var reason = wParam.ToInt32();
                if (reason is NativeMethods.PbtApmResumeAutomatic or NativeMethods.PbtApmResumeSuspend)
                {
                    HandleResume();
                }
                break;

            case NativeMethods.WmDisplayChange:
            case NativeMethods.WmDpiChanged:
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _placement.Refresh();
                    _placement.Apply(IslandMetrics.ExpandedWidth, IslandMetrics.ExpandedHeight);
                }), DispatcherPriority.Background);
                break;
        }

        return IntPtr.Zero;
    }

    private bool IsPointInsideShell(IntPtr lParam)
    {
        if (Shell.ActualWidth <= 0 || Shell.ActualHeight <= 0)
        {
            return false;
        }

        var packed = lParam.ToInt64();
        var screenX = (short)(packed & 0xFFFF);
        var screenY = (short)((packed >> 16) & 0xFFFF);

        try
        {
            var point = Shell.PointFromScreen(new Point(screenX, screenY));
            return Shell.GetGeometry().FillContains(point);
        }
        catch
        {
            return false;
        }
    }

    private async void HandleResume()
    {
        if (_isClosing)
        {
            return;
        }

        _systemStatus.Reset();
        _systemStatus.Prime();
        _placement.Refresh();
        _placement.Apply(IslandMetrics.ExpandedWidth, IslandMetrics.ExpandedHeight);
        UpdateClock();
        ScheduleClockTick();
        _fullscreenWatcher?.Refresh();

        try
        {
            await _mediaService.RestartAsync();
        }
        catch
        {
        }
    }

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.WorkArea) or nameof(SystemParameters.PrimaryScreenWidth))
        {
            _placement.Refresh();
            _placement.Apply(IslandMetrics.ExpandedWidth, IslandMetrics.ExpandedHeight);
        }
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        if (_isFullscreen)
        {
            return;
        }

        _peekActive = false;
        _peekTimer.Stop();
        if (_animator.State != IslandState.Expanded)
        {
            SetMode(IslandState.Expanded);
        }
    }

    private void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_pinned || _isFullscreen || MenuPopup.IsOpen)
        {
            return;
        }

        ScheduleCollapse(IslandMetrics.CollapseDelay);
    }

    private void SetMode(IslandState state)
    {
        if (_isFullscreen && state != IslandState.Collapsed)
        {
            return;
        }

        _animator.AnimateTo(state, _reduceMotion);
        UpdateModeTimers();
    }

    private void ApplyFrame(IslandFrame frame)
    {
        Shell.SurfaceWidth = frame.Width;
        Shell.SurfaceHeight = frame.Height;
        Shell.CornerRadius = frame.CornerRadius;
        TopHighlightScale.ScaleX = Math.Clamp(frame.Width / IslandMetrics.ExpandedWidth, 0, 1);
        ShadowShell.SurfaceWidth = frame.Width;
        ShadowShell.SurfaceHeight = frame.Height;
        ShadowShell.CornerRadius = frame.CornerRadius;
        ShadowRim.SurfaceWidth = frame.Width;
        ShadowRim.SurfaceHeight = frame.Height;
        ShadowRim.CornerRadius = frame.CornerRadius;
        CollapsedView.Opacity = frame.CollapsedOpacity;
        ExpandedView.Opacity = frame.ContentOpacity;
        PlaybackRow.Opacity = frame.ControlsOpacity;
        ExpandedTranslate.Y = frame.ContentOffsetY;
        BottomSection.Opacity = frame.BottomOpacity;
        BottomTranslate.Y = frame.BottomOffsetY;
        BottomDivider.Opacity = frame.BottomOpacity;
        ExpandedView.IsHitTestVisible = frame.ControlsOpacity > 0.6;
        PlaybackRow.IsHitTestVisible = frame.ControlsOpacity > 0.6;
        BottomSection.IsHitTestVisible = frame.BottomOpacity > 0.6;
    }

    private void UpdateModeTimers()
    {
        UpdateStatusTimer();
        UpdateProgressTimer();
    }

    private void UpdateStatusTimer()
    {
        if (_animator.State == IslandState.Expanded)
        {
            if (!_statusTimer.IsEnabled)
            {
                _systemStatus.Reset();
                _systemStatus.Prime();
                _statusTimer.Start();
            }

            UpdateStatus();
        }
        else
        {
            _statusTimer.Stop();
        }
    }

    private void UpdateStatus()
    {
        if (_animator.State != IslandState.Expanded)
        {
            return;
        }

        var status = _systemStatus.GetSnapshot();
        CpuValueText.Text = status.CpuReady ? $"{status.CpuPercent:0}%" : "--";
        RamValueText.Text = $"{status.MemoryPercent:0}%";
        BatteryValueText.Text = status.BatteryPercent is int battery ? $"{battery}%" : "--";
    }

    private void ScheduleCollapse(TimeSpan delay)
    {
        _collapseTimer.Stop();
        _collapseTimer.Interval = delay;
        _collapseTimer.Start();
    }

    private void CollapseTimer_Tick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (!_pinned && !IsMouseOver && !_peekActive && !MenuPopup.IsOpen)
        {
            SetMode(IslandState.Collapsed);
        }
    }

    private void PeekTimer_Tick(object? sender, EventArgs e)
    {
        _peekTimer.Stop();
        _peekActive = false;
        if (!_pinned && !IsMouseOver && !MenuPopup.IsOpen)
        {
            SetMode(IslandState.Collapsed);
        }
    }

    private void ShowPeek()
    {
        if (_pinned || IsMouseOver)
        {
            SetMode(IslandState.Expanded);
            return;
        }

        _peekActive = true;
        _peekTimer.Stop();
        _peekTimer.Interval = IslandMetrics.PeekDuration;
        _peekTimer.Start();
        SetMode(IslandState.Peek);
    }

    private void ExtendPeek()
    {
        if (_animator.State == IslandState.Peek)
        {
            _peekActive = true;
            _peekTimer.Stop();
            _peekTimer.Start();
        }
        else
        {
            ShowPeek();
        }
    }

    private void SetPinned(bool pinned)
    {
        _pinned = pinned;
        PinButton.Foreground = pinned ? PinActiveBrush : PinInactiveBrush;
        PinButton.ToolTip = pinned ? "取消保持展开" : "保持展开";

        if (pinned)
        {
            _collapseTimer.Stop();
            _peekTimer.Stop();
            _peekActive = false;
            SetMode(IslandState.Expanded);
        }
        else if (!IsMouseOver && !MenuPopup.IsOpen)
        {
            ScheduleCollapse(IslandMetrics.CollapseDelay);
        }
    }

    private void FullscreenWatcher_FullscreenChanged(object? sender, bool fullscreen)
    {
        _isFullscreen = fullscreen;

        if (fullscreen)
        {
            _collapseTimer.Stop();
            _peekTimer.Stop();
            _peekActive = false;
            _animator.SnapTo(IslandState.Collapsed);
            UpdateModeTimers();
            Visibility = Visibility.Hidden;
            Topmost = false;
        }
        else
        {
            Visibility = Visibility.Visible;
            Topmost = true;
            _placement.Refresh();
            _placement.Apply(IslandMetrics.ExpandedWidth, IslandMetrics.ExpandedHeight);
            UpdateClock();
        }
    }

    private void MediaService_SnapshotChanged(object? sender, MediaSnapshot? snapshot)
    {
        if (_isClosing)
        {
            return;
        }

        ApplyMediaSnapshot(snapshot);
    }

    private void ApplyMediaSnapshot(MediaSnapshot? snapshot)
    {
        var hasMedia = snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.Title);
        UpdateCollapsedArt(snapshot);

        if (!hasMedia)
        {
            CompleteMediaCrossfade();
            SetTextLayer(_activeLayerIsA, null);
            SetArtLayer(_activeLayerIsA, null);
            _lastSnapshot = null;
            _lastIdentity = string.Empty;
            _activeThumbnail = null;
            _activeHasMedia = false;
            PlayButton.Content = "\uE768";
            SetProgress(0, animate: false);
            UpdateProgressTimer();

            if (_receivedFirstSnapshot && _animator.State == IslandState.Peek && !IsMouseOver && !_pinned)
            {
                SetMode(IslandState.Collapsed);
            }

            _receivedFirstSnapshot = true;
            return;
        }

        var identity = snapshot!.Identity;
        var isNewTrack = _receivedFirstSnapshot && !string.Equals(identity, _lastIdentity, StringComparison.Ordinal);
        _lastSnapshot = snapshot;
        _snapshotTimestampUtc = DateTime.UtcNow;

        if (!_receivedFirstSnapshot || string.IsNullOrEmpty(_lastIdentity))
        {
            CompleteMediaCrossfade();
            SetTextLayer(_activeLayerIsA, snapshot);
            SetArtLayer(_activeLayerIsA, snapshot.Thumbnail);
            _activeThumbnail = snapshot.Thumbnail;
            _activeHasMedia = true;
        }
        else if (isNewTrack)
        {
            CrossFadeTo(snapshot);
        }
        else
        {
            SetTextLayer(_activeLayerIsA, snapshot);
            if (!ReferenceEquals(snapshot.Thumbnail, _activeThumbnail) || !_activeHasMedia)
            {
                SetArtLayer(_activeLayerIsA, snapshot.Thumbnail);
                _activeThumbnail = snapshot.Thumbnail;
                _activeHasMedia = true;
            }
        }

        _lastIdentity = identity;
        PlayButton.Content = snapshot.IsPlaying ? "\uE769" : "\uE768";
        UpdateProgressFromSnapshot(animate: false);
        UpdateProgressTimer();

        if (isNewTrack)
        {
            if (_animator.State == IslandState.Collapsed && !_pinned)
            {
                ShowPeek();
            }
            else if (_animator.State == IslandState.Peek)
            {
                ExtendPeek();
            }
        }

        _receivedFirstSnapshot = true;
    }

    private void SetTextLayer(bool layerA, MediaSnapshot? snapshot)
    {
        var title = layerA ? TrackTitleA : TrackTitleB;
        var artist = layerA ? TrackArtistA : TrackArtistB;

        if (snapshot is null)
        {
            title.Text = "未在播放";
            artist.Text = "打开任意音乐应用";
            return;
        }

        title.Text = snapshot.Title;
        artist.Text = string.IsNullOrWhiteSpace(snapshot.Artist) ? "未知艺术家" : snapshot.Artist;
    }

    private void SetArtLayer(bool layerA, byte[]? thumbnail)
    {
        var art = layerA ? AlbumArtA : AlbumArtB;
        var glyph = layerA ? AlbumArtGlyphA : AlbumArtGlyphB;
        var bitmap = thumbnail is { Length: > 0 } ? CreateBitmap(thumbnail) : null;

        if (bitmap is not null)
        {
            art.Fill = CreateImageBrush(bitmap);
            glyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            art.Fill = _artGradientBrush;
            glyph.Visibility = Visibility.Visible;
        }
    }

    private void UpdateCollapsedArt(MediaSnapshot? snapshot)
    {
        var hasMedia = snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.Title);
        if (!hasMedia)
        {
            CollapsedArt.Visibility = Visibility.Collapsed;
            CollapsedArt.Fill = _artGradientBrush;
            CollapsedArtGlyph.Visibility = Visibility.Visible;
            _collapsedThumbnail = null;
            _collapsedHasMedia = false;
            return;
        }

        CollapsedArt.Visibility = Visibility.Visible;
        if (ReferenceEquals(snapshot!.Thumbnail, _collapsedThumbnail) && _collapsedHasMedia)
        {
            return;
        }

        var bitmap = snapshot.Thumbnail is { Length: > 0 } ? CreateBitmap(snapshot.Thumbnail) : null;
        if (bitmap is not null)
        {
            CollapsedArt.Fill = CreateImageBrush(bitmap);
            CollapsedArtGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            CollapsedArt.Fill = _artGradientBrush;
            CollapsedArtGlyph.Visibility = Visibility.Visible;
        }

        _collapsedThumbnail = snapshot.Thumbnail;
        _collapsedHasMedia = true;
    }

    private void CrossFadeTo(MediaSnapshot snapshot)
    {
        CompleteMediaCrossfade();

        var outgoingIsA = _activeLayerIsA;
        var incomingIsA = !outgoingIsA;
        SetTextLayer(incomingIsA, snapshot);
        SetArtLayer(incomingIsA, snapshot.Thumbnail);
        _activeLayerIsA = incomingIsA;
        _activeThumbnail = snapshot.Thumbnail;
        _activeHasMedia = true;

        var outgoingText = outgoingIsA ? TrackTextA : TrackTextB;
        var incomingText = incomingIsA ? TrackTextA : TrackTextB;
        var outgoingArt = outgoingIsA ? AlbumArtA : AlbumArtB;
        var incomingArt = incomingIsA ? AlbumArtA : AlbumArtB;
        var outgoingTranslate = outgoingIsA ? TrackTextATranslate : TrackTextBTranslate;
        var incomingTranslate = incomingIsA ? TrackTextATranslate : TrackTextBTranslate;

        incomingText.Visibility = Visibility.Visible;
        incomingText.Opacity = 0;
        incomingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        incomingTranslate.Y = 2.5;
        incomingArt.Visibility = Visibility.Visible;
        incomingArt.Opacity = 0;
        outgoingText.Visibility = Visibility.Visible;
        outgoingText.Opacity = 1;
        outgoingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        outgoingTranslate.Y = 0;
        outgoingArt.Visibility = Visibility.Visible;
        outgoingArt.Opacity = 1;

        var generation = ++_crossfadeGeneration;
        var duration = IslandMetrics.TrackCrossfadeDuration;
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var textOut = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        var textIn = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };
        var artOut = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        var artIn = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };

        textIn.Completed += (_, _) =>
        {
            if (_crossfadeGeneration != generation || _isClosing)
            {
                return;
            }

            outgoingText.Visibility = Visibility.Collapsed;
            outgoingText.Opacity = 0;
            incomingText.Visibility = Visibility.Visible;
            incomingText.Opacity = 1;
            outgoingArt.Visibility = Visibility.Collapsed;
            outgoingArt.Opacity = 0;
            incomingArt.Visibility = Visibility.Visible;
            incomingArt.Opacity = 1;
            outgoingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            outgoingTranslate.Y = 0;
            incomingTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            incomingTranslate.Y = 0;
            _activeLayerIsA = incomingIsA;
        };
        var outgoingTranslateAnimation = new DoubleAnimation(0, -1.5, duration) { EasingFunction = ease };
        var incomingTranslateAnimation = new DoubleAnimation(2.5, 0, duration) { EasingFunction = ease };

        outgoingText.BeginAnimation(UIElement.OpacityProperty, textOut);
        incomingText.BeginAnimation(UIElement.OpacityProperty, textIn);
        outgoingArt.BeginAnimation(UIElement.OpacityProperty, artOut);
        incomingArt.BeginAnimation(UIElement.OpacityProperty, artIn);
        outgoingTranslate.BeginAnimation(TranslateTransform.YProperty, outgoingTranslateAnimation);
        incomingTranslate.BeginAnimation(TranslateTransform.YProperty, incomingTranslateAnimation);
    }

    private void CompleteMediaCrossfade()
    {
        _crossfadeGeneration++;
        TrackTextA.BeginAnimation(UIElement.OpacityProperty, null);
        TrackTextB.BeginAnimation(UIElement.OpacityProperty, null);
        AlbumArtA.BeginAnimation(UIElement.OpacityProperty, null);
        AlbumArtB.BeginAnimation(UIElement.OpacityProperty, null);
        TrackTextATranslate.BeginAnimation(TranslateTransform.YProperty, null);
        TrackTextBTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        TrackTextATranslate.Y = 0;
        TrackTextBTranslate.Y = 0;

        var activeText = _activeLayerIsA ? TrackTextA : TrackTextB;
        var inactiveText = _activeLayerIsA ? TrackTextB : TrackTextA;
        var activeArt = _activeLayerIsA ? AlbumArtA : AlbumArtB;
        var inactiveArt = _activeLayerIsA ? AlbumArtB : AlbumArtA;

        activeText.Visibility = Visibility.Visible;
        activeText.Opacity = 1;
        inactiveText.Visibility = Visibility.Collapsed;
        inactiveText.Opacity = 0;
        activeArt.Visibility = Visibility.Visible;
        activeArt.Opacity = 1;
        inactiveArt.Visibility = Visibility.Collapsed;
        inactiveArt.Opacity = 0;
    }

    private void UpdateProgressFromSnapshot(bool animate)
    {
        if (_lastSnapshot is null)
        {
            TrackTime.Text = "0:00 / 0:00";
            SetProgress(0, animate);
            return;
        }

        var position = _lastSnapshot.Position;
        if (_lastSnapshot.IsPlaying)
        {
            position += DateTime.UtcNow - _snapshotTimestampUtc;
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        if (_lastSnapshot.EndTime > TimeSpan.Zero && position > _lastSnapshot.EndTime)
        {
            position = _lastSnapshot.EndTime;
        }

        var timeText = $"{FormatTime(position)} / {FormatTime(_lastSnapshot.EndTime)}";
        if (!string.Equals(TrackTime.Text, timeText, StringComparison.Ordinal))
        {
            TrackTime.Text = timeText;
        }
        var progress = _lastSnapshot.EndTime > TimeSpan.Zero
            ? Math.Clamp(position.TotalMilliseconds / _lastSnapshot.EndTime.TotalMilliseconds, 0, 1)
            : 0;
        SetProgress(progress, animate);
    }

    private void SetProgress(double value, bool animate)
    {
        value = Math.Clamp(value, 0, 1);
        var current = Math.Clamp(TrackProgressBar.Value, 0, 1);

        if (!animate)
        {
            TrackProgressBar.BeginAnimation(Controls.SquircleProgressBar.ValueProperty, null);
            TrackProgressBar.Value = value;
            return;
        }

        var animation = new DoubleAnimation(current, value, IslandMetrics.ProgressSmoothingDuration)
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        TrackProgressBar.BeginAnimation(Controls.SquircleProgressBar.ValueProperty, animation);
    }

    private void UpdateProgressTimer()
    {
        var shouldRun = _animator.State != IslandState.Collapsed && _lastSnapshot?.IsPlaying == true;
        if (shouldRun)
        {
            if (!_progressTimer.IsEnabled)
            {
                _progressTimer.Start();
            }
        }
        else
        {
            _progressTimer.Stop();
        }
    }

    private void UpdateClock()
    {
        CollapsedTime.Text = DateTime.Now.ToString("HH:mm");
    }

    private void ScheduleClockTick()
    {
        var now = DateTime.Now;
        var nextMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1);
        var delay = nextMinute - now + TimeSpan.FromMilliseconds(50);

        _clockTimer.Stop();
        _clockTimer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
        _clockTimer.Start();
    }

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        _clockTimer.Stop();
        UpdateClock();
        ScheduleClockTick();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        await _mediaService.TogglePlayPauseAsync();
        await RefreshAfterCommandAsync();
    }

    private async void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        await _mediaService.SkipPreviousAsync();
        await RefreshAfterCommandAsync();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await _mediaService.SkipNextAsync();
        await RefreshAfterCommandAsync();
    }

    private async Task RefreshAfterCommandAsync()
    {
        await Task.Delay(180);
        await _mediaService.RefreshAsync();
    }

    private void VolumeButton_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:sound");

    private void ScreenshotButton_Click(object sender, RoutedEventArgs e) => OpenUri("ms-screenclip:");

    private void LockButton_Click(object sender, RoutedEventArgs e) => NativeMethods.LockWorkStation();

    private void FocusTimer_Tick(object? sender, EventArgs e)
    {
        if (!_focusRunning)
        {
            return;
        }

        _focusRemaining -= TimeSpan.FromSeconds(1);
        if (_focusRemaining <= TimeSpan.Zero)
        {
            _focusRemaining = TimeSpan.Zero;
            _focusRunning = false;
            _focusTimer.Stop();
        }

        UpdateFocusUi(animate: true);
    }

    private void FocusToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_focusRemaining <= TimeSpan.Zero)
        {
            _focusRemaining = TimeSpan.FromMinutes(FocusMinutes);
        }

        _focusRunning = !_focusRunning;
        if (_focusRunning)
        {
            _focusTimer.Start();
        }
        else
        {
            _focusTimer.Stop();
        }

        UpdateFocusUi(animate: false);
    }

    private void FocusResetButton_Click(object sender, RoutedEventArgs e)
    {
        _focusRunning = false;
        _focusTimer.Stop();
        _focusRemaining = TimeSpan.FromMinutes(FocusMinutes);
        UpdateFocusUi(animate: false);
    }

    private void UpdateFocusUi(bool animate = false)
    {
        FocusTimeText.Text = $"{(int)_focusRemaining.TotalMinutes:00}:{_focusRemaining.Seconds:00}";
        FocusToggleButton.Content = _focusRunning ? "\uE769" : "\uE768";
        FocusToggleButton.ToolTip = _focusRunning ? "暂停计时" : "开始计时";

        var total = TimeSpan.FromMinutes(FocusMinutes).TotalMilliseconds;
        var progress = total > 0 ? 1 - _focusRemaining.TotalMilliseconds / total : 0;
        SetFocusProgress(progress, animate && _focusRunning);
    }

    private void SetFocusProgress(double value, bool animate)
    {
        value = Math.Clamp(value, 0, 1);
        var current = Math.Clamp(FocusProgressBar.Value, 0, 1);

        if (!animate)
        {
            FocusProgressBar.BeginAnimation(Controls.SquircleProgressBar.ValueProperty, null);
            FocusProgressBar.Value = value;
            return;
        }

        var animation = new DoubleAnimation(current, value, IslandMetrics.ProgressSmoothingDuration)
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        FocusProgressBar.BeginAnimation(Controls.SquircleProgressBar.ValueProperty, animation);
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => SetPinned(!_pinned);

    private void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenMenu();
        e.Handled = true;
    }

    private void OpenMenu()
    {
        if (_isFullscreen)
        {
            return;
        }

        SetMode(IslandState.Expanded);
        Activate();
        UpdateMenuChecks();
        MenuPopup.IsOpen = true;
    }

    private void AnimateMenuOpen()
    {
        var duration = IslandMetrics.MenuAnimationDuration;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        MenuSurface.BeginAnimation(UIElement.OpacityProperty, null);
        MenuSurface.Opacity = 0;
        MenuSurfaceTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        MenuSurfaceTranslate.Y = -3;

        MenuSurface.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        MenuSurfaceTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(-3, 0, duration) { EasingFunction = ease });
    }

    private void UpdateMenuChecks()
    {
        MenuPinCheck.Visibility = _pinned ? Visibility.Visible : Visibility.Collapsed;
        MenuReduceMotionCheck.Visibility = _reduceMotion ? Visibility.Visible : Visibility.Collapsed;
        MenuStartupCheck.Visibility = StartupService.IsEnabled() ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MenuPin_Click(object sender, RoutedEventArgs e)
    {
        SetPinned(!_pinned);
        MenuPopup.IsOpen = false;
    }

    private void MenuReduceMotion_Click(object sender, RoutedEventArgs e)
    {
        _reduceMotion = !_reduceMotion;
        _settings.ReduceMotion = _reduceMotion;
        SettingsService.Save(_settings);

        if (_reduceMotion)
        {
            _animator.SnapTo(_animator.State);
            UpdateModeTimers();
        }

        MenuPopup.IsOpen = false;
    }

    private void MenuStartup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupService.SetEnabled(!StartupService.IsEnabled());
        }
        catch
        {
        }

        MenuPopup.IsOpen = false;
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        Close();
        Application.Current.Shutdown();
    }

    private static BitmapSource? CreateBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static ImageBrush CreateImageBrush(BitmapSource bitmap)
    {
        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
        brush.Freeze();
        return brush;
    }

    private static string FormatTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch
        {
        }
    }

}
