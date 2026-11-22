using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Avalonia.VisualTree;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 internal async Task R2EntryTest(OfflineWindowRoot root,CorePlaybackAdapter adapter){
  int checks=0;void Check(bool ok,string name){Program.Log("R2_ENTRY_CHECK",new{ok,name});if(!ok)throw new InvalidOperationException(name);checks++;}
  var store=(LocalMusicLibraryService)root.Library;var baseTracks=adapter.State.Tracks.Take(2).ToArray();var catalog=(await store.GetAllLocalTracksAsync()).Take(2).ToArray();
  bool restart=Environment.GetEnvironmentVariable("R2_RESTART")=="1";
  if(restart){var expected=AppSqliteStore.LoadValue("r2","entries");Check(expected==string.Join(",",root.Player.PlaybackQueue.Select(x=>x.QueueEntryId)),"exact queue occurrence IDs cross-process");Check(!root.Player.IsPlayingAudio&&root.Coordinator.Player.IsStopped,"restored queue never autoplays");}
  foreach(int n in new[]{2,3,50,1000}){
   string name="R2 duplicates "+n;
   if(!restart){var rows=Enumerable.Range(0,n).Select(i=>LocalMusicLibraryViewModel.ToSongItem(catalog[i%2]).NewQueueEntry()).ToArray();root.Player.SetControlledQueue(rows);await Task.Delay(60);SetQueue(true);await Task.Delay(100);
    Check(root.Player.PlaybackQueue.Select(x=>x.QueueEntryId).Distinct().Count()==n&&queueRows.Count==n,"real core and UI preserve distinct occurrences "+n);
    Check(nativeQueue!.GetVisualDescendants().OfType<Avalonia.Controls.ListBoxItem>().Count()<60,"duplicate rows virtualized "+n);
    await adapter.PlaylistAsync("save",name);Check(adapter.State.Error.Length==0,"save duplicate playlist "+n);
   }
   var list=(await store.GetPlaylistsAsync()).Single(x=>x.Name==name);var entries=await store.GetPlaylistEntriesAsync(list.Id);
   Check(entries.Count==n&&entries.Select(x=>x.EntryId).Distinct().Count()==n,"SQL entry IDs and count "+n);
   Check(entries.Select(x=>x.Track.Id).SequenceEqual(Enumerable.Range(0,n).Select(i=>catalog[i%2].Id)),"exact track order including duplicates "+n);
   string key="playlist-"+n;string ids=string.Join(",",entries.Select(x=>x.EntryId));if(restart)Check(AppSqliteStore.LoadValue("r2",key)==ids,"playlist occurrence IDs survive process "+n);else AppSqliteStore.SaveValue("r2",key,ids);
  }
  var list2=(await store.GetPlaylistsAsync()).Single(x=>x.Name=="R2 duplicates 50");var saved=await store.GetPlaylistEntriesAsync(list2.Id);long first=saved[0].EntryId;
  await store.EditPlaylistEntryAsync(list2.Id,"move",first,target:2);var moved=await store.GetPlaylistEntriesAsync(list2.Id);Check(moved[2].EntryId==first&&moved.Select(x=>x.EntryId).ToHashSet().SetEquals(saved.Select(x=>x.EntryId)),"move exact playlist entry preserves IDs");
  bool refused=false;try{await store.RemoveTrackFromPlaylistAsync(list2.Id,saved[0].Track.Id);}catch(InvalidOperationException){refused=true;}Check(refused&&(await store.GetPlaylistEntriesAsync(list2.Id)).Count==50,"ambiguous legacy removal refused atomically");
  await store.EditPlaylistEntryAsync(list2.Id,"remove",first);Check((await store.GetPlaylistEntriesAsync(list2.Id)).Count==49,"delete one membership not same-track siblings");
  // Restore this test playlist through the existing transaction API for next process's expected baseline.
  await store.ReplaceOrderedTracksAsync(list2.Id,saved.Select(x=>x.Track.Id).ToArray());var restored=await store.GetPlaylistEntriesAsync(list2.Id);AppSqliteStore.SaveValue("r2","playlist-50",string.Join(",",restored.Select(x=>x.EntryId)));
  var pair=Enumerable.Range(0,3).Select(_=>LocalMusicLibraryViewModel.ToSongItem(catalog[0]).NewQueueEntry()).ToArray();root.Player.SetControlledQueue(pair);await adapter.VolumeAsync(0);await adapter.QueueEntryAsync("play",pair[0].QueueEntryId);await Task.Delay(250);
  Check(root.Coordinator.Player.IsPlaying&&root.Coordinator.Player.GetActiveAnalysisSnapshot().Rms>0,"real BASS plays repeated-track first occurrence");
  await adapter.NextAsync();Check(root.Player.CurrentPlayingSong?.QueueEntryId==pair[1].QueueEntryId,"next selects second same-song occurrence");await adapter.PreviousAsync();Check(root.Player.CurrentPlayingSong?.QueueEntryId==pair[0].QueueEntryId,"previous selects first occurrence");
  await adapter.SetModeAsync(2);Check(root.Player.PlaybackQueue.Count==3,"shuffle keeps all duplicate occurrences");await adapter.NextAsync();var current=root.Player.CurrentPlayingSong!;await adapter.PreviousAsync();Check(root.Player.CurrentPlayingSong?.QueueEntryId==pair[0].QueueEntryId,"shuffle history occurrence aware");await adapter.SetModeAsync(0);Check(root.Player.PlaybackQueue.Select(x=>x.QueueEntryId).SequenceEqual(pair.Select(x=>x.QueueEntryId)),"shuffle off restores exact occurrence order");
  await adapter.QueueEntryAsync("remove",pair[1].QueueEntryId);Check(root.Player.PlaybackQueue.Count==2&&root.Player.PlaybackQueue.All(x=>x.QueueEntryId!=pair[1].QueueEntryId),"remove one queue occurrence while playing");
  await adapter.StopAsync();root.Player.SetControlledQueue(pair);await adapter.QueueEntryAsync("play",pair[0].QueueEntryId);await adapter.SetModeAsync(3);await adapter.SeekAsync(adapter.State.Duration-.12);await Task.Delay(800);Check(root.Player.CurrentPlayingSong?.QueueEntryId==pair[1].QueueEntryId,"real EOF moves to distinct same-track occurrence");await adapter.StopAsync();
  var cache=new PlaybackQueueCacheService(NullLogger<PlaybackQueueCacheService>.Instance);root.Player.FlushControlledQueue();Check(cache.Load()?.Songs.Select(x=>x.QueueEntryId).SequenceEqual(pair.Select(x=>x.QueueEntryId))==true,"cache serializes exact queue entry IDs");
  var validCache=AppSqliteStore.LoadValue("playback_queue","app")!;
  foreach(var bad in new[]{"missing-id","duplicate-id","wrong-map"}){
   var json=System.Text.Json.Nodes.JsonNode.Parse(validCache)!;
   if(bad=="missing-id")json["Items"]![0]!["QueueEntryId"]=Guid.Empty.ToString();
   else if(bad=="duplicate-id")json["Items"]![1]!["QueueEntryId"]=pair[0].QueueEntryId.ToString();
   else{var other=catalog[1];json["OriginalItems"]![0]!["LocalTrackId"]=other.Id;json["OriginalItems"]![0]!["LocalFilePath"]=other.LocalPath;}
   AppSqliteStore.SaveValue("playback_queue","app",json.ToJsonString());Check(cache.Load()==null&&cache.LastReadError!=null,"cache rejects occurrence corruption "+bad);
  }
  // CORE-02: reconstruct the exact legacy Schema 1 shape from synthetic local fixtures.
  // A successful read must never rewrite the old bytes; only explicitly validated entries
  // receive in-memory occurrence IDs. Unknown, duplicate, or forged paths fail closed.
  var v1=System.Text.Json.Nodes.JsonNode.Parse(validCache)!;
  v1["SchemaVersion"]=1;v1["Mode"]=0;v1.AsObject().Remove("OriginalItems");
  var oldItems=v1["Items"]!.AsArray();while(oldItems.Count>2)oldItems.RemoveAt(oldItems.Count-1);
  for(int i=0;i<2;i++){
   var item=oldItems[i]!.AsObject();item.Remove("QueueEntryId");
   item["LocalTrackId"]=catalog[i].Id;item["LocalFilePath"]=catalog[i].LocalPath;
   item["LocalSourceType"]="LocalFile";item["RemoteUrl"]=null;item["Hash"]="";
  }
  v1["CurrentSongKey"]=PlaybackQueueCacheService.BuildSongKey(LocalMusicLibraryViewModel.ToSongItem(catalog[1]));
  var oldBytes=v1.ToJsonString();AppSqliteStore.SaveValue("playback_queue","app",oldBytes);
  var legacy=cache.Load();
  Check(legacy?.Songs.Count==2&&legacy.Songs.Select(x=>x.QueueEntryId).Distinct().Count()==2
        &&legacy.Songs.All(x=>x.QueueEntryId!=Guid.Empty),"CORE02 schema1 unique local entries receive occurrence IDs");
  Check(legacy?.OriginalSongs.Select(x=>x.QueueEntryId).SequenceEqual(legacy.Songs.Select(x=>x.QueueEntryId))==true
        &&legacy.CurrentSongKey==legacy.Songs[1].QueueEntryId.ToString("D"),"CORE02 schema1 original order and current song preserved");
  Check(AppSqliteStore.LoadValue("playback_queue","app")==oldBytes,"CORE02 schema1 read does not overwrite legacy bytes");
  var invalidV1=System.Text.Json.Nodes.JsonNode.Parse(oldBytes)!;
  invalidV1["Items"]![1]!["LocalTrackId"]=catalog[0].Id;
  var ambiguous=invalidV1.ToJsonString();AppSqliteStore.SaveValue("playback_queue","app",ambiguous);
  Check(cache.Load()==null&&cache.LastReadError!=null&&AppSqliteStore.LoadValue("playback_queue","app")==ambiguous,
        "CORE02 ambiguous schema1 occurrence fails closed and preserves bytes");
  invalidV1=System.Text.Json.Nodes.JsonNode.Parse(oldBytes)!;
  invalidV1["Items"]![1]!["LocalFilePath"]=Path.Combine(OfflineStartupProfile.Current.Temp,"not-admitted.wav");
  var forged=invalidV1.ToJsonString();AppSqliteStore.SaveValue("playback_queue","app",forged);
  Check(cache.Load()==null&&cache.LastReadError!=null&&AppSqliteStore.LoadValue("playback_queue","app")==forged,
        "CORE02 forged schema1 local file fails closed and preserves bytes");
  invalidV1=System.Text.Json.Nodes.JsonNode.Parse(oldBytes)!;invalidV1["SchemaVersion"]=999;
  var unknown=invalidV1.ToJsonString();AppSqliteStore.SaveValue("playback_queue","app",unknown);
  Check(cache.Load()==null&&cache.LastReadError!=null&&AppSqliteStore.LoadValue("playback_queue","app")==unknown,
        "CORE02 unknown cache schema fails closed and preserves bytes");
  AppSqliteStore.SaveValue("playback_queue","app",validCache);
  // Pre-version writer lacks the UDF. Trigger rejects before destructive DML.
  using(var old=new SqliteConnection("Data Source="+AppDatabase.DatabasePath)){old.Open();using var q=old.CreateCommand();q.CommandText="DELETE FROM playlist_tracks WHERE playlist_id="+list2.Id;refused=false;try{q.ExecuteNonQuery();}catch(SqliteException){refused=true;}Check(refused&&(await store.GetPlaylistEntriesAsync(list2.Id)).Count==50,"old SQLite writer cannot alter v4 memberships");}
  using(var old=new SqliteConnection("Data Source="+AppDatabase.DatabasePath)){old.Open();using var q=old.CreateCommand();foreach(var table in new[]{"playlists","tracks"}){q.CommandText="DELETE FROM "+table;refused=false;try{q.ExecuteNonQuery();}catch(SqliteException){refused=true;}Check(refused,"legacy parent writes refused "+table);}}
  MigrationChecks(Check);
  await I1CoreChecks(root,adapter,Check);
  await ExtraEntryChecks(root,adapter,Check);
  var large=Enumerable.Range(0,1000).Select(i=>LocalMusicLibraryViewModel.ToSongItem(catalog[i%2]).NewQueueEntry()).ToArray();root.Player.SetControlledQueue(large);root.Player.FlushControlledQueue();AppSqliteStore.SaveValue("r2","entries",string.Join(",",large.Select(x=>x.QueueEntryId)));
  Check(OfflineStartupNetwork.Requests==1,"entry tests never reach real transport");Program.Log("R2_ENTRY_COMPLETE",new{checks,restart});Close();
 }
 void MigrationChecks(Action<bool,string> check){
  string path=Path.Combine(OfflineStartupProfile.Current.Temp,"legacy-"+Guid.NewGuid().ToString("N")+".sqlite");
  using var c=new SqliteConnection("Data Source="+path);c.Open();c.CreateFunction("huayuan_entry_writer",()=>4);using var q=c.CreateCommand();q.CommandText="PRAGMA foreign_keys=ON;CREATE TABLE tracks(id INTEGER PRIMARY KEY);CREATE TABLE playlists(id INTEGER PRIMARY KEY);CREATE TABLE playlist_tracks(playlist_id INTEGER,track_id INTEGER,position INTEGER,added_at TEXT,PRIMARY KEY(playlist_id,track_id));INSERT INTO tracks VALUES(1);INSERT INTO playlists VALUES(1);INSERT INTO playlist_tracks VALUES(1,1,0,'2026-10-28');";q.ExecuteNonQuery();
  bool fault=false;try{PlaylistEntrySchema.Ensure(c,path,true);}catch(IOException){fault=true;}
  q.CommandText="PRAGMA user_version";check(fault&&Convert.ToInt32(q.ExecuteScalar())==0,"migration failure rolls back schema version");q.CommandText="SELECT COUNT(*) FROM playlist_tracks";check(Convert.ToInt32(q.ExecuteScalar())==1,"failure preserves original membership");
  PlaylistEntrySchema.Ensure(c,path);q.CommandText="PRAGMA user_version";check(Convert.ToInt32(q.ExecuteScalar())==4,"isolated legacy DB migration v4");q.CommandText="PRAGMA integrity_check";check((string?)q.ExecuteScalar()=="ok","migrated DB integrity");q.CommandText="PRAGMA foreign_key_check";using(var rd=q.ExecuteReader())check(!rd.Read(),"migrated DB foreign keys");check(Directory.GetFiles(OfflineStartupProfile.Current.Temp,Path.GetFileName(path)+".v0-before-entries-*.sqlite").Length==2,"separate backup retained for failed and successful migration");
 }
}
