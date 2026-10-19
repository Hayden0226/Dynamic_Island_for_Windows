using Microsoft.Win32;

namespace DynamicIsland.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "DynamicIsland";

    public static bool IsEnabled()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var value = key?.GetValue(RunValue) as string;
        return !string.IsNullOrEmpty(value) &&
               string.Equals(value.Trim('"'), path, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled && Environment.ProcessPath is { } path)
        {
            key.SetValue(RunValue, $"\"{path}\"");
        }
        else
        {
            key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}
