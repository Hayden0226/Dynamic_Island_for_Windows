using DynamicIsland.Interop;

namespace DynamicIsland.Services;

internal static class MediaKeys
{
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPreviousTrack = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyEventFKeyUp = 0x0002;

    public static void PlayPause() => Press(VkMediaPlayPause);

    public static void Previous() => Press(VkMediaPreviousTrack);

    public static void Next() => Press(VkMediaNextTrack);

    private static void Press(byte key)
    {
        NativeMethods.keybd_event(key, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(key, 0, KeyEventFKeyUp, UIntPtr.Zero);
    }
}

