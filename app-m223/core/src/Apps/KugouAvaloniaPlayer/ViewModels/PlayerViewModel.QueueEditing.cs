using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
namespace KugouAvaloniaPlayer.ViewModels;
public partial class PlayerViewModel {
 private volatile bool _controlledAudition;
 internal bool PersistControlledLocalSubset {get;set;}
 internal void RemoveControlledProviderEntries()
 {
  if(ProviderStreamRegistry.IsProvider(CurrentPlayingSong?.LocalFilePath))
  {
   StopControlledPlayback();
   SelectQueueSongWithoutAudio(null);
  }
  foreach(var song in _queueManager.OriginalQueue.Where(s=>ProviderStreamRegistry.IsProvider(s.LocalFilePath)).ToArray())
   _queueManager.Remove(song);
 }
 internal string? ControlledQueueReadError=>_queueCacheService.LastReadError;
 readonly object _controlledCacheGate=new();long _cacheGeneration;long _queuePlaybackEpoch;long _controlledTransportEpoch;
 internal void StopControlledPlayback(){System.Threading.Interlocked.Increment(ref _controlledTransportEpoch);System.Threading.Interlocked.Increment(ref _playRequestVersion);CancelAndDisposeLoadCancellation();StopAndReset();}
 internal void SelectQueueSongWithoutAudio(SongItem? song){CurrentPlayingSong=song;DisplayedPlayingSong=song;TotalDurationSeconds=song?.DurationSeconds??0;CurrentPositionSeconds=0;IsPlayingAudio=false;}
 internal void SetControlledQueue(IReadOnlyList<SongItem> songs){PlaybackQueueManager.DemandCapacity(songs.ToArray());foreach(var s in songs)if(!ProviderStreamRegistry.Allows(s.LocalFilePath))OfflineStartupProfile.Current.DemandMedia(s.LocalFilePath!);StopControlledPlayback();_queueManager.RestoreQueue(songs);if(songs.Count>0)SelectQueueSongWithoutAudio(songs[0]);FlushControlledQueue();}
 internal void MoveControlledQueue(int from,int to){_queueManager.Move(from,to);FlushControlledQueue();}
 internal async Task PlayControlledQueueEntryAsync(SongItem song, SimpleAudio.PlaybackCutoff? cutoff=null){if(!_queueManager.PlaybackQueue.Contains(song))throw new InvalidOperationException("曲目不在队列");_queueManager.RecordNavigation(CurrentPlayingSong,song);await PlaySongAsync(song, cutoff:cutoff);}
 internal void FlushControlledQueue(){System.Threading.Interlocked.Increment(ref _cacheGeneration);CancelAndDisposeQueueCacheSaveCancellation();lock(_controlledCacheGate){_queueCacheService.Save(_queueManager.PlaybackQueue.ToArray(),CurrentPlayingSong,_queueManager.OriginalQueue.ToArray(),CurrentPlayMode,PersistControlledLocalSubset);}if(_queueCacheService.LastWriteError!=null)throw new InvalidOperationException("队列缓存保存失败："+_queueCacheService.LastWriteError);}
}
