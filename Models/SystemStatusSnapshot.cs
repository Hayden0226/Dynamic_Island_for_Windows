namespace DynamicIsland.Models;

public readonly record struct SystemStatusSnapshot(
    bool CpuReady,
    double CpuPercent,
    double MemoryPercent,
    int? BatteryPercent,
    bool IsCharging);

