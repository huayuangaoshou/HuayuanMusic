using HuayuanPreview.R2;
using KugouAvaloniaPlayer.ViewModels;

namespace HuayuanPreview;

internal sealed partial class CorePlaybackAdapter
{
    private readonly HashSet<string> unavailable = new(StringComparer.OrdinalIgnoreCase);
    internal event Action? DesktopUnavailableChanged;
    private string AvailabilityKey(SongItem song)
    {
        var id=Id(song);
        return providerTrackKeys.TryGetValue(id,out var key)?key:"local:"+id;
    }
    internal bool IsUnavailable(TrackRow row)=>unavailable.Contains(row.CatalogIdentity);
    private void TrackUnavailable(SongItem song)
    {
        unavailable.Add(AvailabilityKey(song));
        DesktopUnavailableChanged?.Invoke();
    }
    private void EnableDesktopUnavailableHandling()
    {
        if(!Program.Desktop)return;
        root.Player.UseDesktopUnavailableHandling=true;
        root.Player.DesktopSourceUnavailable+=TrackUnavailable;
        root.Player.DesktopCanPlayAutomatically=song=>!unavailable.Contains(AvailabilityKey(song));
    }
}
