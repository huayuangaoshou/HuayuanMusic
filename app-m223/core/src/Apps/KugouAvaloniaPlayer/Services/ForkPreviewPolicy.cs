namespace KugouAvaloniaPlayer.Services;

internal static class ForkPreviewPolicy
{
    // Bundled native lyric helpers still carry upstream shared identities.
    // Keep them disabled until independently rebuilt and isolation-tested.
    public static bool NativeLyricHelpersEnabled => false;
}
