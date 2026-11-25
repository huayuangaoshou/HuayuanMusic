using HuayuanPreview.R2;
using Avalonia.Threading;
using KugouAvaloniaPlayer.ViewModels;

namespace HuayuanPreview;

internal sealed partial class CorePlaybackAdapter
{
    private readonly Dictionary<string,string> unavailable = new(StringComparer.OrdinalIgnoreCase);
    // R12-1 有界保护：连续自动跳歌计数，DesktopPlaybackStarted(TrackRecovered) 时归零；≥5 停跳。
    private int autoSkipStreak;
    // R12-1 连接假死看门狗状态：记录上次观察到的 CurrentId/Position 与连续无进度次数。
    private DispatcherTimer? watchdog;
    private int watchdogCurrentId;
    private double watchdogLastPosition;
    private int watchdogStallCount;
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
        // R12-1 A：事件型自动跳歌。设备问题绝不跳（跳谁都失败）。
        if(!deviceFailure)AutoSkipToNext(song);
    }
    private void TrackUnavailableWithReason(SongItem song,string reason)
    {
        unavailable[AvailabilityKey(song)]=reason;
        DesktopUnavailableChanged?.Invoke(reason);
        AutoSkipToNext(song);
    }
    private void TrackRecovered(SongItem song)
    {
        if(unavailable.Remove(AvailabilityKey(song)))DesktopUnavailableChanged?.Invoke("");
        // R12-1：成功播放（恢复）时熔断归零。
        autoSkipStreak=0;
        ResetWatchdog();
    }
    // R12-1 A：定位失败歌的下一个可用位置，直接 PlaySongAsync，不走 PlayNext/GetNext(null) 路径。
    private void AutoSkipToNext(SongItem failedSong)
    {
        if(!Program.Desktop)return;
        var queue=root.Player.PlaybackQueue;
        if(queue.Count==0)return;
        // 有界保护：连续 5 次失败停止自动跳歌。
        if(autoSkipStreak>=5)
        {
            DesktopUnavailableChanged?.Invoke("连续 5 首无法播放，已停止自动切歌");
            return;
        }
        autoSkipStreak++;
        // Dispatcher.UIThread.Post 执行，避开 _playSongLock 重入（core OnPlaybackEnded 同款姿势）。
        Dispatcher.UIThread.Post(async()=>
        {
            try
            {
                // 竞态防护：执行前复核当前选择仍是失败歌（用户已手动点别的歌就不跳）。
                if(!Equals(root.Player.CurrentPlayingSong,failedSong))return;
                var idx=queue.IndexOf(failedSong);
                if(idx<0)return;
                // 从下一位置循环找第一个未标 unavailable 的歌。
                SongItem? next=null;
                for(int i=1;i<=queue.Count;i++)
                {
                    var candidate=queue[(idx+i)%queue.Count];
                    if(!unavailable.ContainsKey(AvailabilityKey(candidate)))
                    {
                        next=candidate;
                        break;
                    }
                }
                if(next==null)
                {
                    // 队列里全是不可用歌：停下并提示，不 hang 不死循环。
                    commands.Stop();
                    DesktopUnavailableChanged?.Invoke("队列中没有可播放的歌曲");
                    return;
                }
                await root.Player.PlaySongAsync(next, queue.ToList());
            }
            catch(Exception error)
            {
                Program.Log("R12_AUTOSKIP_FAILED",new{type=error.GetType().Name,message=error.Message});
            }
        });
    }
    private void EnableDesktopUnavailableHandling()
    {
        if(!Program.Desktop)return;
        root.Player.UseDesktopUnavailableHandling=true;
        root.Player.DesktopSourceUnavailable+=TrackUnavailable;
        root.Player.DesktopPlaybackStarted+=TrackRecovered;
        root.Player.DesktopCanPlayAutomatically=song=>!unavailable.ContainsKey(AvailabilityKey(song));
        StartWatchdog();
    }
    // R12-1 B：连接假死看门狗。每 5s 检查；Playing==true 且 CurrentId 不变且 Position 纹丝不动，
    // 连续 3 次（约 15s）判定假死 → 标不可用 + 走 A 的跳歌（计入同一熔断）。
    private void StartWatchdog()
    {
        if(watchdog!=null)return;
        watchdog=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
        watchdog.Tick+=(_,_)=>WatchdogTick();
        watchdog.Start();
    }
    private void ResetWatchdog()
    {
        watchdogCurrentId=0;
        watchdogLastPosition=0;
        watchdogStallCount=0;
    }
    private void WatchdogTick()
    {
        try
        {
            var state=State;
            var p=root.Player;
            var current=p.CurrentPlayingSong;
            if(!state.Playing||current==null)
            {
                ResetWatchdog();
                return;
            }
            var id=Id(current);
            var position=state.Position;
            // State.Revision 变化不算进度，只认 Position 位移与 CurrentId。
            if(id!=watchdogCurrentId)
            {
                watchdogCurrentId=id;
                watchdogLastPosition=position;
                watchdogStallCount=0;
                return;
            }
            if(Math.Abs(position-watchdogLastPosition)<0.05)
            {
                watchdogStallCount++;
                if(watchdogStallCount>=3)
                {
                    watchdogStallCount=0;
                    TrackUnavailableWithReason(current,"连接超时，无法获取音频数据");
                }
            }
            else
            {
                watchdogLastPosition=position;
                watchdogStallCount=0;
            }
        }
        catch(Exception error)
        {
            Program.Log("R12_WATCHDOG_ERROR",new{type=error.GetType().Name});
        }
    }
    private void StopWatchdog()
    {
        watchdog?.Stop();
        watchdog=null;
    }
}
