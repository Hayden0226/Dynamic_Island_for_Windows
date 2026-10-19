using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DynamicIsland.Core;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

public sealed class WindowPlacementService
{
    private IntPtr _hwnd;
    private int _workLeft;
    private int _workTop;
    private int _workWidth;
    private double _scale = 1;

    public void Initialize(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        Refresh();
    }

    public void Refresh()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        _scale = dpi > 0 ? dpi / 96.0 : 1;
        _workLeft = info.Work.Left;
        _workTop = info.Work.Top;
        _workWidth = info.Work.Right - info.Work.Left;
    }

    public void Apply(double shellWidth, double shellHeight)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var windowWidth = (int)Math.Round((shellWidth + IslandMetrics.ShadowMargin * 2) * _scale);
        var windowHeight = (int)Math.Round((shellHeight + IslandMetrics.ShadowMargin * 2) * _scale);
        var x = _workLeft + (int)Math.Round((_workWidth - windowWidth) / 2.0);
        var y = _workTop + (int)Math.Round((IslandMetrics.TopOffset - IslandMetrics.ShadowMargin) * _scale);

        NativeMethods.SetWindowPos(
            _hwnd,
            IntPtr.Zero,
            x,
            y,
            windowWidth,
            windowHeight,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }
}

