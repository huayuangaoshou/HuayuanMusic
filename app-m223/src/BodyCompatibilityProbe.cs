using System.Text.Json;
using Microsoft.Data.Sqlite;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using KugouAvaloniaPlayer.ViewModels;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 // Deliberately never invoked by normal/manual startup. A failed compatibility
 // probe restores its physical backup; it cannot enable the proposed schema.
 internal async Task BodyCompatibilityProbe(OfflineWindowRoot root,CorePlaybackAdapter adapter){
  int checks=0;bool backedUp=false;string backup=Path.Combine(OfflineStartupProfile.Current.Temp,"body-membership-backup.sqlite");
  void Check(bool ok,string name){Program.Log("BODY_COMPAT_CHECK",new{ok,name});if(!ok)throw new InvalidOperationException(name);checks++;}
  string Snapshot(){using var c=AppDatabase.CreateConnection();var data=new Dictionary<string,List<string>>();foreach(var t in new[]{"tracks","playlists","playlist_tracks"}){using var q=c.CreateCommand();q.CommandText="SELECT * FROM "+t+" ORDER BY 1,2";using var rd=q.ExecuteReader();var rows=new List<string>();while(rd.Read()){var values=new object[rd.FieldCount];rd.GetValues(values);rows.Add(JsonSerializer.Serialize(values));}data[t]=rows;}return JsonSerializer.Serialize(data);}
  object Scalar(string sql){using var c=AppDatabase.CreateConnection();using var q=c.CreateCommand();q.CommandText=sql;return q.ExecuteScalar()!;}
  void Sql(string sql){using var c=AppDatabase.CreateConnection();using var q=c.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  var original=Snapshot();long originalVersion=Convert.ToInt64(Scalar("PRAGMA user_version"));bool unsafeDelete=false,queueRejected=false;
  try{
   Check(OfflineGuiApplication.SelfTest&&Path.GetFileName(OfflineStartupProfile.Current.Root).StartsWith("p17-body-r3-"),"isolated actual production services only");
   using(var c=AppDatabase.CreateConnection())using(var target=new SqliteConnection("Data Source="+backup)){target.Open();c.BackupDatabase(target);backedUp=true;}
   Check(File.Exists(backup)&&new FileInfo(backup).Length>0,"physical SQLite online backup exists before DDL");
   try{using var faultConnection=AppDatabase.CreateConnection();using var faultTransaction=faultConnection.BeginTransaction();using var faultCommand=faultConnection.CreateCommand();faultCommand.Transaction=faultTransaction;faultCommand.CommandText="CREATE TABLE body_fault_rollback(value INTEGER)";faultCommand.ExecuteNonQuery();throw new IOException("owned injected DDL failure");}catch(IOException){}
   Check(Convert.ToInt64(Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='body_fault_rollback'"))==0,"actual DDL transaction rolls back after injected exception");
   var store=(LocalMusicLibraryService)root.Library;var first=(await store.GetAllLocalTracksAsync()).First();var list=await store.SaveOrderedPlaylistAsync("隔离迁移兼容探针",new[]{first.Id});
   Check((await store.GetPlaylistTracksAsync(list.Id)).Count==1,"real legacy service writes original membership");
   using(var c=AppDatabase.CreateConnection())using(var tx=c.BeginTransaction()){
    using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="""
ALTER TABLE playlist_tracks RENAME TO body_membership_v1_before;
CREATE TABLE playlist_tracks(entry_id INTEGER PRIMARY KEY AUTOINCREMENT,playlist_id INTEGER NOT NULL,track_id INTEGER NOT NULL,position INTEGER NOT NULL,added_at TEXT NOT NULL,
FOREIGN KEY(playlist_id) REFERENCES playlists(id) ON DELETE CASCADE,FOREIGN KEY(track_id) REFERENCES tracks(id) ON DELETE CASCADE,UNIQUE(playlist_id,position));
INSERT INTO playlist_tracks(playlist_id,track_id,position,added_at) SELECT playlist_id,track_id,position,added_at FROM body_membership_v1_before ORDER BY playlist_id,position;
PRAGMA user_version=3;
""";q.ExecuteNonQuery();tx.Commit();
   }
   Check(Convert.ToInt64(Scalar("SELECT COUNT(*) FROM pragma_foreign_key_check"))==0&&Scalar("PRAGMA integrity_check").ToString()=="ok","candidate schema actual SQLite integrity and foreign keys");
   Sql($"INSERT INTO playlist_tracks(playlist_id,track_id,position,added_at) VALUES({list.Id},{first.Id},1,'synthetic')");
   Check(Convert.ToInt64(Scalar($"SELECT COUNT(DISTINCT entry_id) FROM playlist_tracks WHERE playlist_id={list.Id}"))==2,"two durable independent entry IDs for same song");
   Sql($"DELETE FROM playlist_tracks WHERE entry_id=(SELECT MIN(entry_id) FROM playlist_tracks WHERE playlist_id={list.Id})");
   Check(Convert.ToInt64(Scalar($"SELECT COUNT(*) FROM playlist_tracks WHERE playlist_id={list.Id}"))==1,"entry-specific delete keeps other appearance");
   Sql($"INSERT INTO playlist_tracks(playlist_id,track_id,position,added_at) VALUES({list.Id},{first.Id},0,'synthetic')");
   var entries=await store.GetPlaylistTracksAsync(list.Id);
   Check(entries.Count==2,"actual old reader returns both rows");
   await store.RemoveTrackFromPlaylistAsync(list.Id,first.Id);
   long remaining=Convert.ToInt64(Scalar($"SELECT COUNT(*) FROM playlist_tracks WHERE playlist_id={list.Id}"));unsafeDelete=remaining==0;
   Program.Log("BODY_COMPAT_HARD_BLOCK",new{kind="legacy-delete-by-track",before=2,after=remaining,expectedSingleEntryRemaining=1});
   Check(unsafeDelete,"detector proves legacy remove deletes both occurrences; migration must remain disabled");
   var pair=entries.Select(LocalMusicLibraryViewModel.ToSongItem).ToArray();
   try{root.Queue.SetupQueue(pair[0],pair);}catch(InvalidOperationException){queueRejected=true;}
   Check(queueRejected,"actual original queue rejects duplicate source identities");
   Check(pair[0].Equals(pair[1])&&PlaybackQueueCacheService.BuildSongKey(pair[0])==PlaybackQueueCacheService.BuildSongKey(pair[1]),"legacy equality/cache lack occurrence identity");
  }finally{
   if(backedUp){SqliteConnection.ClearAllPools();using(var source=new SqliteConnection("Data Source="+backup))using(var target=AppDatabase.CreateConnection()){source.Open();source.BackupDatabase(target);}SqliteConnection.ClearAllPools();}
  }
  Check(original==Snapshot(),"all original track/playlist/membership rows restored after unsafe compatibility result");
  Check(Convert.ToInt64(Scalar("PRAGMA user_version"))==originalVersion&&Scalar("PRAGMA integrity_check").ToString()=="ok","schema version and integrity restored; no migration published");
  Program.Log("BODY_COMPAT_COMPLETE",new{checks,feature="BLOCKED",unsafeDelete,queueRejected,backup,rollback=true});Close();
 }
}
