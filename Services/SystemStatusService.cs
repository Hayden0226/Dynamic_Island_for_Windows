using System.Runtime.InteropServices;
using DynamicIsland.Interop;
using DynamicIsland.Models;

namespace DynamicIsland.Services;

public sealed class SystemStatusService
{
    private ulong? _lastIdle;
    private ulong? _lastKernel;
    private ulong? _lastUser;

    public void Reset()
    {
        _lastIdle = null;
        _lastKernel = null;
        _lastUser = null;
    }

    public void Prime()
    {
        GetSnapshot();
    }

    public SystemStatusSnapshot GetSnapshot()
    {
        var cpuReady = false;
        var cpuPercent = 0.0;

        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var idleTicks = ToUInt64(idle);
            var kernelTicks = ToUInt64(kernel);
            var userTicks = ToUInt64(user);

            if (_lastIdle is ulong lastIdle &&
                _lastKernel is ulong lastKernel &&
                _lastUser is ulong lastUser)
            {
                var idleDelta = idleTicks - lastIdle;
                var kernelDelta = kernelTicks - lastKernel;
                var userDelta = userTicks - lastUser;
                var totalDelta = kernelDelta + userDelta;

                if (totalDelta > 0)
                {
                    cpuPercent = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
                    cpuReady = true;
                }
            }

            _lastIdle = idleTicks;
            _lastKernel = kernelTicks;
            _lastUser = userTicks;
        }

        var memoryPercent = 0.0;
        var memory = new NativeMethods.MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>()
        };
        if (NativeMethods.GlobalMemoryStatusEx(ref memory) && memory.TotalPhys > 0)
        {
            memoryPercent = Math.Clamp((1.0 - (double)memory.AvailPhys / memory.TotalPhys) * 100.0, 0, 100);
        }

        int? batteryPercent = null;
        var charging = false;
        if (NativeMethods.GetSystemPowerStatus(out var power))
        {
            if (power.BatteryLifePercent != 255)
            {
                batteryPercent = power.BatteryLifePercent;
            }

            charging = power.AclLineStatus == 1;
        }

        return new SystemStatusSnapshot(cpuReady, cpuPercent, memoryPercent, batteryPercent, charging);
    }

    private static ulong ToUInt64(NativeMethods.FileTime time) =>
        ((ulong)time.HighDateTime << 32) | time.LowDateTime;
}

