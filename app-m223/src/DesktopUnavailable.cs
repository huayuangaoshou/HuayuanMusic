using HuayuanPreview.R2;
using KugouAvaloniaPlayer.ViewModels;

namespace HuayuanPreview;

internal sealed partial class CorePlaybackAdapter
{
    private readonly Dictionary<string,string> unavailable = new(StringComparer.OrdinalIgnoreCase);
    internal event Action<string>? DesktopUnavailableChanged;
    private string AvailabilityKey(SongItem song)
    {
        var id=Id(song);
        return providerTrackKeys.TryGetValue(id,out var key)?key:"local:"+id;
    }
    internal bool IsUnavailable(TrackRow row)=>unavailable.ContainsKey(row.CatalogIdentity);
    internal string? UnavailableReason(TrackRow row)=>unavailable.GetValueOrDefault(row.CatalogIdentity);
    private void TrackUnavailable(SongItem song,bool deviceFailure)
    {
        var reason=deviceFailure?"无法播放，请检查音频输出设备":
            song.LocalSourceType=="ProviderStream"?"暂时无法播放该歌曲，请稍后重试":"无法播放，音频文件可能已移动或损坏";
        if(!deviceFailure)unavailable[AvailabilityKey(song)]=reason;
        DesktopUnavailableChanged?.Invoke(reason);
    }
    private void TrackRecovered(SongItem song)
    {
        if(unavailable.Remove(AvailabilityKey(song)))DesktopUnavailableChanged?.Invoke("");
    }
    private void EnableDesktopUnavailableHandling()
    {
        if(!Program.Desktop)return;
        root.Player.UseDesktopUnavailableHandling=true;
        root.Player.DesktopSourceUnavailable+=TrackUnavailable;
        root.Player.DesktopPlaybackStarted+=TrackRecovered;
        root.Player.DesktopCanPlayAutomatically=song=>!unavailable.ContainsKey(AvailabilityKey(song));
    }
}
