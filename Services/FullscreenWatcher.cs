using System.Runtime.InteropServices;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

public sealed class FullscreenWatcher : IDisposable
{
    private readonly IntPtr _ownWindow;
    private readonly NativeMethods.WinEventDelegate _callback;
    private IntPtr _hook;
    private bool _isFullscreen;

    public FullscreenWatcher(IntPtr ownWindow)
    {
        _ownWindow = ownWindow;
        _callback = OnForegroundChanged;
    }

    public event EventHandler<bool>? FullscreenChanged;

    public void Start()
    {
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeMethods.WineventOutofcontext | NativeMethods.WineventSkipownprocess);

        CheckForeground();
    }

    public void Refresh()
    {
        CheckForeground();
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void OnForegroundChanged(
        IntPtr winEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        CheckForeground();
    }

    private void CheckForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _ownWindow)
        {
            SetFullscreen(false);
            return;
        }

        if (NativeMethods.IsZoomed(foreground))
        {
            SetFullscreen(false);
            return;
        }

        if (!NativeMethods.GetWindowRect(foreground, out var windowRect))
        {
            SetFullscreen(false);
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            SetFullscreen(false);
            return;
        }

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            SetFullscreen(false);
            return;
        }

        const int tolerance = 2;
        var fullscreen =
            windowRect.Left <= info.Monitor.Left + tolerance &&
            windowRect.Top <= info.Monitor.Top + tolerance &&
            windowRect.Right >= info.Monitor.Right - tolerance &&
            windowRect.Bottom >= info.Monitor.Bottom - tolerance;

        SetFullscreen(fullscreen);
    }

    private void SetFullscreen(bool value)
    {
        if (_isFullscreen == value)
        {
            return;
        }

        _isFullscreen = value;
        FullscreenChanged?.Invoke(this, value);
    }
}
