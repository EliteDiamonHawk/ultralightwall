namespace UltralightWall;

internal static class WallpaperSuspensionPolicy
{
    public static bool ShouldSuspend(
        bool manualSuspendEnabled,
        bool pauseWhenFullscreenEnabled,
        bool fullscreenOccluded) =>
        manualSuspendEnabled || (pauseWhenFullscreenEnabled && fullscreenOccluded);
}
