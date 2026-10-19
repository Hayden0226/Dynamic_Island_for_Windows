using System.Windows.Threading;
using DynamicIsland.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

public sealed class MediaSessionService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private DateTime _lastManagerAttemptUtc = DateTime.MinValue;
    private string _thumbnailKey = string.Empty;
    private byte[]? _thumbnail;
    private bool _refreshRunning;
    private bool _refreshQueued;
    private bool _disposed;

    public MediaSessionService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public event EventHandler<MediaSnapshot?>? SnapshotChanged;

    public MediaSnapshot? Current { get; private set; }

    public async Task StartAsync()
    {
        await EnsureManagerAsync();
        await RefreshAsync();
    }

    public async Task RestartAsync()
    {
        DetachSession();
        DetachManager();
        _lastManagerAttemptUtc = DateTime.MinValue;
        _thumbnailKey = string.Empty;
        _thumbnail = null;
        await EnsureManagerAsync();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_refreshRunning)
        {
            _refreshQueued = true;
            return;
        }

        _refreshRunning = true;
        try
        {
            do
            {
                _refreshQueued = false;
                var snapshot = await ReadSnapshotAsync();
                Current = snapshot;
                SnapshotChanged?.Invoke(this, snapshot);
            }
            while (_refreshQueued && !_disposed);
        }
        finally
        {
            _refreshRunning = false;
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        var session = GetCurrentSession();
        var handled = false;

        if (session is not null)
        {
            try
            {
                handled = await session.TryTogglePlayPauseAsync();
            }
            catch
            {
            }
        }

        if (!handled)
        {
            MediaKeys.PlayPause();
        }
    }

    public async Task SkipPreviousAsync()
    {
        var session = GetCurrentSession();
        var handled = false;

        if (session is not null)
        {
            try
            {
                handled = await session.TrySkipPreviousAsync();
            }
            catch
            {
            }
        }

        if (!handled)
        {
            MediaKeys.Previous();
        }
    }

    public async Task SkipNextAsync()
    {
        var session = GetCurrentSession();
        var handled = false;

        if (session is not null)
        {
            try
            {
                handled = await session.TrySkipNextAsync();
            }
            catch
            {
            }
        }

        if (!handled)
        {
            MediaKeys.Next();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        DetachSession();
        DetachManager();
        _manager = null;
        _thumbnail = null;
        _thumbnailKey = string.Empty;
        Current = null;
    }

    private async Task EnsureManagerAsync()
    {
        if (_manager is not null || _disposed)
        {
            return;
        }

        if (DateTime.UtcNow - _lastManagerAttemptUtc < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _lastManagerAttemptUtc = DateTime.UtcNow;
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;
            AttachSession(_manager.GetCurrentSession());
        }
        catch
        {
            _manager = null;
        }
    }

    private async Task<MediaSnapshot?> ReadSnapshotAsync()
    {
        var session = GetCurrentSession();
        if (session is null)
        {
            return null;
        }

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            if (properties is null)
            {
                return null;
            }

            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var title = properties.Title ?? string.Empty;
            var artist = properties.Artist ?? string.Empty;
            var album = properties.AlbumTitle ?? string.Empty;
            var key = $"{title}\u001F{artist}\u001F{album}";

            if (!string.Equals(key, _thumbnailKey, StringComparison.Ordinal))
            {
                _thumbnailKey = key;
                _thumbnail = await TryReadThumbnailAsync(properties.Thumbnail);
            }

            return new MediaSnapshot(
                title,
                artist,
                album,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                timeline.Position,
                timeline.EndTime,
                _thumbnail);
        }
        catch
        {
            return null;
        }
    }

    private GlobalSystemMediaTransportControlsSession? GetCurrentSession()
    {
        try
        {
            return _manager?.GetCurrentSession();
        }
        catch
        {
            return null;
        }
    }

    private void Manager_CurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        AttachSession(sender.GetCurrentSession());
        QueueRefresh();
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_session, session))
        {
            return;
        }

        DetachSession();
        _session = session;

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
            _session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
            _session.TimelinePropertiesChanged += Session_TimelinePropertiesChanged;
        }

        QueueRefresh();
    }

    private void DetachSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
        _session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= Session_TimelinePropertiesChanged;
        _session = null;
    }

    private void DetachManager()
    {
        if (_manager is null)
        {
            return;
        }

        _manager.CurrentSessionChanged -= Manager_CurrentSessionChanged;
        _manager = null;
    }

    private void Session_MediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => QueueRefresh();

    private void Session_PlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => QueueRefresh();

    private void Session_TimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) => QueueRefresh();

    private void QueueRefresh()
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            _ = RefreshAsync();
        }
        else
        {
            _dispatcher.BeginInvoke(new Action(() => _ = RefreshAsync()), DispatcherPriority.Background);
        }
    }

    private static async Task<byte[]?> TryReadThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 8 * 1024 * 1024)
            {
                return null;
            }

            var bytes = new byte[(int)stream.Size];
            using var reader = new DataReader(stream);
            var loaded = await reader.LoadAsync((uint)stream.Size);
            if (loaded < (uint)bytes.Length)
            {
                return null;
            }

            reader.ReadBytes(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }
}
