using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
namespace HuayuanPreview;
internal sealed partial class CorePlaybackAdapter {
 LocalMusicLibraryService Store=>(LocalMusicLibraryService)root.Library;
 bool PersistQueue=>Program.Desktop||Path.GetFileName(OfflineStartupProfile.Current.Root).StartsWith("p17-body-r3-entries-")||Path.GetFileName(OfflineStartupProfile.Current.Root).StartsWith("p17-body-r3-manual-")||Path.GetFileName(OfflineStartupProfile.Current.Root).StartsWith("p17-body-r3-suitequeue-");
 SongItem NextCatalogSong(int id){var song=Song(id).NewQueueEntry();song.QueueEntryId=Guid.Empty;return song;}
 SongItem QueueSong(int trackId){var matches=root.Player.PlaybackQueue.Where(x=>x.LocalTrackId==trackId).ToArray();if(matches.Length!=1)throw new InvalidOperationException("队列歌曲有多个条目或已移除，请选择条目");return matches[0];}
 internal void M216RestoreStoppedSelection(Guid entry){
  if(State.Playing||State.Paused)throw new InvalidOperationException("Stopped selection required");
  var song=entry==Guid.Empty?null:root.Player.PlaybackQueue.Single(s=>s.QueueEntryId==entry);
  root.Player.SelectQueueSongWithoutAudio(song);
  if(M219RStartup.Active&&PersistQueue)root.Player.FlushControlledQueue();
  Publish();
 }
 internal Task M217PlayBoundedAsync(Guid entryId,SimpleAudio.PlaybackCutoff cutoff)=>Run("BoundedQueuePlay",async()=>{
  var entry=root.Player.PlaybackQueue.Single(song=>song.QueueEntryId==entryId);
  Song(Id(entry));
  await root.Player.PlayControlledQueueEntryAsync(entry,cutoff);
  if(!root.Player.IsPlayingAudio)throw new IOException("Bounded playback failed");
  await root.ViewModel.History.LoadAsync();
  RefreshRecords();
  if(PersistQueue)root.Player.FlushControlledQueue();
 });
 public Task QueueEntryAsync(string op,Guid entryId,Guid target=default)=>Run("QueueEntry:"+op,async()=>{
  var rows=root.Player.PlaybackQueue;var entry=rows.SingleOrDefault(x=>x.QueueEntryId==entryId)??throw new InvalidOperationException("条目已变化，请重试");if(op is "play" or "next")Song(Id(entry));
  int from=rows.IndexOf(entry);switch(op){case "play":await root.Player.PlayControlledQueueEntryAsync(entry);if(!root.Player.IsPlayingAudio)throw new IOException("播放失败");await root.ViewModel.History.LoadAsync();RefreshRecords();break;
   case "next":if(Program.Desktop)root.Player.PrioritizeControlledQueueEntry(entry);else commands.AddToNext(entry);break;case "remove":await root.Player.RemoveFromQueueCommand.ExecuteAsync(entry);break;case "up":case "down":root.Player.MoveControlledQueue(from,Math.Clamp(from+(op=="up"?-1:1),0,rows.Count-1));break;
   case "before":var at=rows.ToList().FindIndex(x=>x.QueueEntryId==target);if(at<0)throw new InvalidOperationException("目标条目已变化");root.Player.MoveControlledQueue(from,at);break;default:throw new ArgumentException("Invalid entry operation");}
  if(PersistQueue)root.Player.FlushControlledQueue();
 });
 public Task PlaylistEntryAsync(string op,long listId,long entryId,int target=0)=>Run("PlaylistEntry:"+op,async()=>{await Store.EditPlaylistEntryAsync(listId,op,entryId,target:target);await ReloadAllAsync();});
 public Task SetModeAsync(int mode)=>Run("Mode",()=>{if(!Enum.IsDefined((PlayMode)mode))throw new ArgumentException("无效模式");root.Player.ApplyPlayMode((PlayMode)mode,true);if(PersistQueue)root.Player.FlushControlledQueue();return Task.CompletedTask;});
 public Task QueueAsync(string op,int id=0,int target=0)=>Run("Queue:"+op,async()=>{
  switch(op){case "next":commands.AddToNext(NextCatalogSong(id));break;case "append":commands.AddToQueue([Song(id)]);break;case "remove":await root.Player.RemoveFromQueueCommand.ExecuteAsync(QueueSong(id));break;case "clear":root.Player.ClearQueueCommand.Execute(null);break;case "move":root.Player.MoveControlledQueue(id,target);break;case "up":case "down":case "before":var current=root.Player.PlaybackQueue.ToList();int from=current.FindIndex(x=>x.LocalTrackId==id);int to=op=="before"?current.FindIndex(x=>x.LocalTrackId==target):Math.Clamp(from+(op=="up"?-1:1),0,current.Count-1);if(from<0||to<0)throw new InvalidOperationException("排序曲目已变化，请重试");root.Player.MoveControlledQueue(from,to);break;case "play":await root.Player.PlayControlledQueueEntryAsync(QueueSong(id));if(!root.Player.IsPlayingAudio)throw new InvalidOperationException("音频未开始，请检查受控音源或设备");await root.ViewModel.History.LoadAsync();RefreshRecords();break;case "replace":root.Player.SetControlledQueue(items.Values.ToArray());break;default:throw new ArgumentException("未知队列操作");}
  if(PersistQueue)root.Player.FlushControlledQueue();
 });
 public Task PlaylistAsync(string op,string name="",long listId=0,int songId=0,int target=0)=>Run("Playlist:"+op,async()=>{
  var snapshot=root.Player.PlaybackQueue.ToArray();
  switch(op){
   case "save":if(snapshot.Any(s=>ProviderStreamRegistry.IsProvider(s.LocalFilePath)))throw new InvalidOperationException("平台临时曲目暂不能保存为永久本地歌单，原歌单未改变");await Task.Run(()=>Store.SaveOrderedPlaylistAsync(name,snapshot.Select(s=>s.LocalTrackId).ToArray()));break;
   case "create":name=LocalMusicLibraryService.DemandPlaylistName(name);if((await root.Library.GetPlaylistsAsync()).Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("已有同名歌单");await root.Library.CreatePlaylistAsync(name);break;
   case "rename":await Task.Run(()=>Store.RenameStrictAsync(listId,name));break;
   case "delete":await root.Library.DeletePlaylistAsync(listId);break;
   case "play":var tracks=(await Store.GetPlaylistEntriesAsync(listId)).Select(t=>Song((int)t.Track.Id).NewQueueEntry()).ToArray();PlaybackQueueManager.DemandCapacity(tracks);if(OfflineStartupProfile.Active)foreach(var t in tracks)OfflineStartupProfile.Current.DemandMedia(t.LocalFilePath!);if(tracks.Length==0)throw new InvalidOperationException("歌单为空");commands.Stop();await commands.ReplaceQueueAsync(tracks,songId>0?tracks.First(x=>x.LocalTrackId==songId):tracks[0]);if(!root.Player.IsPlayingAudio)throw new InvalidOperationException("音频未开始，请检查受控音源或设备");await root.ViewModel.History.LoadAsync();RefreshRecords();break;
   case "add":if(Song(songId).LocalSourceType!="LocalFile")throw new InvalidOperationException("只允许本地曲目写入本地歌单");await Store.EditPlaylistEntryAsync(listId,"add",trackId:songId);break;
   case "remove":case "move":var match=(await Store.GetPlaylistEntriesAsync(listId)).Where(x=>x.Track.Id==songId).ToArray();if(match.Length!=1)throw new InvalidOperationException("歌曲有多个条目，必须选择PlaylistEntryId");await Store.EditPlaylistEntryAsync(listId,op,match[0].EntryId,target:target);break;
   default:throw new ArgumentException("未知歌单操作");
  }
  if(op is "create" or "delete")await root.ViewModel.ActAsync("Refresh");await ReloadAllAsync();if(PersistQueue)root.Player.FlushControlledQueue();
 });
}
