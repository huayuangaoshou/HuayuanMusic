namespace SimpleAudio;
/// <summary>Optional startup-only cache location. Audio decoding and playback are unchanged.</summary>
public static class AudioCacheLocation
{
    private static readonly object Gate=new();
    private static string? _isolated;
    private static bool _observed;
    public static void ConfigureIsolatedLocalRoot(string path)
    {
        lock(Gate){if(_observed||_isolated!=null)throw new InvalidOperationException("Audio cache location already fixed");if(!Path.IsPathFullyQualified(path))throw new ArgumentException("Absolute cache root required");_isolated=Path.GetFullPath(path);}
    }
    public static string CurrentLocalRoot => GetLocalRoot();
    internal static string GetLocalRoot()
    {
        lock(Gate){_observed=true;if(_isolated!=null)return _isolated;var p=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);return string.IsNullOrWhiteSpace(p)?Path.GetTempPath():p;}
    }
}
