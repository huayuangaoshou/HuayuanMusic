using Avalonia.Controls;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 async Task ExtraEntryChecks(OfflineWindowRoot root,CorePlaybackAdapter adapter,Action<bool,string> check){
  var store=(LocalMusicLibraryService)root.Library;var catalog=(await store.GetAllLocalTracksAsync()).Take(2).ToArray();
  var allSame=Enumerable.Range(0,1000).Select(_=>LocalMusicLibraryViewModel.ToSongItem(catalog[0]).NewQueueEntry()).ToArray();root.Player.SetControlledQueue(allSame);SetQueue(true);await Task.Delay(150);
  check(queueRows.Count==1000&&queueRows.Select(x=>x.EntryId).Distinct().Count()==1000&&nativeQueue!.GetVisualDescendants().OfType<ListBoxItem>().Count()<60,"1000 occurrences of ONE track are independently virtualized");
  await adapter.VolumeAsync(0);await adapter.QueueEntryAsync("play",allSame[0].QueueEntryId);await adapter.NextAsync();await Task.Delay(100);
  check(nativeQueue!.SelectedIndex==1&&root.Player.CurrentPlayingSong?.QueueEntryId==allSame[1].QueueEntryId,"same-track next updates native current occurrence selection");
  var position=root.Coordinator.Player.GetPosition().TotalSeconds;await adapter.QueueEntryAsync("next",allSame[5].QueueEntryId);check(root.Player.PlaybackQueue[2].QueueEntryId==allSame[5].QueueEntryId&&root.Player.CurrentPlayingSong?.QueueEntryId==allSame[1].QueueEntryId&&root.Coordinator.Player.GetPosition().TotalSeconds>=position,"specific duplicate inserts next without reloading current audio");
  var ids=root.Player.PlaybackQueue.Select(x=>x.QueueEntryId).ToArray();await adapter.QueueAsync("next",(int)catalog[0].Id);check(adapter.State.Error.Length>0&&root.Player.PlaybackQueue.Select(x=>x.QueueEntryId).SequenceEqual(ids),"ambiguous catalog next refuses instead of picking an arbitrary duplicate");
  string name="R2 one track1000";if(!(await store.GetPlaylistsAsync()).Any(x=>x.Name==name))await adapter.PlaylistAsync("save",name);
  var list=(await store.GetPlaylistsAsync()).Single(x=>x.Name==name);var entries=await store.GetPlaylistEntriesAsync(list.Id);
  check(entries.Count==1000&&entries.All(x=>x.Track.Id==catalog[0].Id)&&entries.Select(x=>x.EntryId).Distinct().Count()==1000,"one-track1000 playlist preserves multiplicity in real SQLite");
  var priorIds=AppSqliteStore.LoadValue("r2","one1000");var entryIds=string.Join(",",entries.Select(x=>x.EntryId));if(priorIds==null)AppSqliteStore.SaveValue("r2","one1000",entryIds);else check(priorIds==entryIds,"all1000 identical-track playlist entry IDs survive restart");
  localList=name;Go("本地歌单");await Task.Delay(100);mainSongs!.SelectedIndex=5;long selected=((ListSongView)mainSongs.SelectedItem!).Track.PlaylistEntryId;RefreshRows();check(((ListSongView)mainSongs.SelectedItem!).Track.PlaylistEntryId==selected,"playlist selection stays on exact duplicate entry after incremental refresh");
  await adapter.PlayPlaylistEntryAsync(list.Id,entries[5].EntryId);check(root.Player.CurrentPlayingSong?.QueueEntryId==root.Player.PlaybackQueue[5].QueueEntryId&&root.Player.PlaybackQueue.Count==1000,"play specific permanent duplicate entry preserves full context");await adapter.StopAsync();
  var unique=catalog.Select(t=>LocalMusicLibraryViewModel.ToSongItem(t).NewQueueEntry()).ToArray();root.Player.SetControlledQueue(unique);root.Player.FlushControlledQueue();var node=JsonNode.Parse(AppSqliteStore.LoadValue("playback_queue","app")!)!;node["SchemaVersion"]=2;node["CurrentSongKey"]=PlaybackQueueCacheService.BuildSongKey(unique[1]);foreach(var item in node["Items"]!.AsArray().Concat(node["OriginalItems"]!.AsArray()))item!.AsObject().Remove("QueueEntryId");
  AppSqliteStore.SaveValue("playback_queue","app",node.ToJsonString());var cache=new PlaybackQueueCacheService(NullLogger<PlaybackQueueCacheService>.Instance);var old=cache.Load();check(old!=null&&old.Songs.Count==2&&old.Songs.Select(x=>x.QueueEntryId).Distinct().Count()==2&&old.CurrentSongKey==old.Songs[1].QueueEntryId.ToString("D"),"legacy v2 cache canonicalizes unique tracks with shared entry IDs and current occurrence");root.Player.FlushControlledQueue();
  var two=Enumerable.Range(0,2).Select(_=>LocalMusicLibraryViewModel.ToSongItem(catalog[0]).NewQueueEntry()).ToArray();root.Player.SetControlledQueue(two);await adapter.QueueEntryAsync("play",two[1].QueueEntryId);await adapter.SetModeAsync(0);await adapter.SeekAsync(adapter.State.Duration-.12);await Task.Delay(850);check(root.Player.CurrentPlayingSong?.QueueEntryId==two[0].QueueEntryId&&root.Coordinator.Player.IsPlaying,"list loop wraps to first duplicate occurrence on actual EOF");
  await adapter.SetModeAsync(1);var same=root.Player.CurrentPlayingSong!.QueueEntryId;await adapter.SeekAsync(adapter.State.Duration-.12);await Task.Delay(850);check(root.Player.CurrentPlayingSong?.QueueEntryId==same&&root.Coordinator.Player.IsPlaying,"repeat-one retains exact occurrence on actual EOF");
  await adapter.SetModeAsync(3);await adapter.QueueEntryAsync("play",two[1].QueueEntryId);await adapter.SeekAsync(adapter.State.Duration-.12);await Task.Delay(850);check(root.Coordinator.Player.IsStopped&&!adapter.State.Playing,"sequential stops after last repeated occurrence on actual EOF");
  string source=Path.Combine(OfflineStartupProfile.Current.Temp,"legacy-copy.sqlite");
  check(File.Exists(source),"positively identified prior synthetic DB copy supplied");
  if(File.Exists(source)){
   string path=Path.Combine(OfflineStartupProfile.Current.Temp,"legacy-copy-run-"+Guid.NewGuid().ToString("N")+".sqlite");File.Copy(source,path);using var c=new SqliteConnection("Data Source="+path);c.Open();c.CreateFunction("huayuan_entry_writer",()=>4);using var q=c.CreateCommand();q.CommandText="SELECT playlist_id,track_id,position FROM playlist_tracks ORDER BY playlist_id,position";var before=new List<string>();using(var rd=q.ExecuteReader())while(rd.Read())before.Add($"{rd.GetInt64(0)}:{rd.GetInt64(1)}");
   PlaylistEntrySchema.Ensure(c,path);q.CommandText="SELECT playlist_id,track_id,position FROM playlist_tracks ORDER BY playlist_id,position";var after=new List<string>();using(var rd=q.ExecuteReader())while(rd.Read())after.Add($"{rd.GetInt64(0)}:{rd.GetInt64(1)}");check(before.Count>=2000&&before.SequenceEqual(after),"real prior owned v0 database copy migrates thousands of memberships without order loss");
  }
  Go("本地音乐");SetQueue(false);
 }
}
