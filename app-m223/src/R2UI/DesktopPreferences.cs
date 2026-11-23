using System.Text.Json;
using KugouAvaloniaPlayer.Services;

namespace HuayuanPreview.R2;

internal sealed record DesktopLyricOptions(bool Visible = false, bool Locked = false, int? X = null, int? Y = null, double FontSize = 30);
internal enum DesktopDlcState { Enabled, Disabled, Removed }
internal sealed record DesktopOptions(bool TrayOnClose = true, bool RestoreQueue = true, bool Dark = false,
    bool MicroFade = true, bool Translation = true, DesktopDlcState Kugou = DesktopDlcState.Enabled,
    float PreMuteVolume = 0.8f);

internal static class DesktopPreferences
{
    private static readonly object Gate = new();
    private static readonly string FileName = Path.Combine(AppDataPaths.RoamingRoot, "r2-desktop.json");
    private static Options Current = Read();
    internal static string Quality { get { lock (Gate) return Current.Quality; } }
    internal static string[] Recent { get { lock (Gate) return Current.Recent.ToArray(); } }
    internal static DesktopLyricOptions DesktopLyrics { get { lock (Gate) return Current.Lyrics ?? new(); } }
    internal static DesktopOptions Behavior { get { lock (Gate) return Current.Behavior ?? new(); } }
    internal static Task SetBehaviorAsync(Func<DesktopOptions, DesktopOptions> change) =>
        UpdateAsync(options => options with { Behavior = change(options.Behavior ?? new()) });
    internal static Task SetDesktopLyricsAsync(DesktopLyricOptions options) =>
        UpdateAsync(current => current with { Lyrics = options });

    internal static Task ChooseQualityAsync(string quality) =>
        UpdateAsync(options => options with { Quality = quality });
    internal static Task RememberAsync(string query) => UpdateAsync(options => options with
    {
        Recent = new[] { query }.Concat(options.Recent).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray()
    });
    internal static Task ClearRecentAsync() => UpdateAsync(options => options with { Recent = [] });

    private static Task UpdateAsync(Func<Options, Options> update) => Task.Run(() =>
    {
        lock (Gate)
        {
            var next = update(Current);
            Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);
            var temp = FileName + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(next));
            File.Move(temp, FileName, overwrite: true);
            Current = next;
        }
    });

    private static Options Read()
    {
        try
        {
            if (File.Exists(FileName) && new FileInfo(FileName).Length < 8192)
            {
                var result = JsonSerializer.Deserialize<Options>(File.ReadAllText(FileName));
                if (result is { Quality: "standard" or "320" or "flac", Recent: not null }) return result;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new("standard", []);
    }
    private sealed record Options(string Quality, string[] Recent, DesktopLyricOptions? Lyrics = null, DesktopOptions? Behavior = null);
}
