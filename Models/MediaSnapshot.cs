namespace DynamicIsland.Models;

public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan EndTime,
    byte[]? Thumbnail)
{
    public double Progress
    {
        get
        {
            if (EndTime <= TimeSpan.Zero)
            {
                return 0;
            }

            return Math.Clamp(Position.TotalMilliseconds / EndTime.TotalMilliseconds, 0, 1);
        }
    }

    public string Identity => $"{Title}\u001F{Artist}\u001F{Album}";
}

