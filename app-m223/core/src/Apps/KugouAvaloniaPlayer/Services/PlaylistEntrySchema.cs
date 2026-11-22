using System;
using System.IO;
using Microsoft.Data.Sqlite;
namespace KugouAvaloniaPlayer.Services;
internal static class PlaylistEntrySchema {
 internal static void Ensure(SqliteConnection c,string path,bool injectFailure=false){
  if(Startup.OfflineStartupProfile.Active&&!Path.GetFileName(Startup.OfflineStartupProfile.Current.Root).StartsWith("p17-body-r3-",StringComparison.Ordinal))throw new InvalidOperationException("Entry migration is unavailable in this diagnostic profile");
  var dataRoot = Startup.OfflineStartupProfile.Active ? Startup.OfflineStartupProfile.Current.Root : AppDataPaths.RoamingRoot;
  path=Path.GetFullPath(path);
  if(!Startup.OfflineStartupProfile.Under(path,dataRoot)||!string.Equals(Path.GetFullPath(c.DataSource),path,StringComparison.OrdinalIgnoreCase))throw new IOException("Migration database is outside the application data root");
  Startup.OfflineStartupProfile.RejectLinks(path);
  using var version=c.CreateCommand();version.CommandText="PRAGMA user_version";var v=Convert.ToInt32(version.ExecuteScalar());
  if(v==4)return;if(v!=0)throw new InvalidDataException("Unsupported database version; no migration performed");
  using(var check=c.CreateCommand()){check.CommandText="PRAGMA integrity_check";if((string?)check.ExecuteScalar()!="ok")throw new InvalidDataException("Database integrity failure");}
  string backup=path+".v0-before-entries-"+Guid.NewGuid().ToString("N")+".sqlite";
  using(var copy=new SqliteConnection("Data Source="+backup)){copy.Open();c.BackupDatabase(copy);}
  using var tx=c.BeginTransaction();using var q=c.CreateCommand();q.Transaction=tx;
  q.CommandText="""
   ALTER TABLE playlist_tracks RENAME TO playlist_tracks_v0;
   CREATE TABLE playlist_tracks(
    entry_id INTEGER PRIMARY KEY AUTOINCREMENT,
    playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    track_id INTEGER NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
    position INTEGER NOT NULL, added_at TEXT NOT NULL, UNIQUE(playlist_id,position));
   INSERT INTO playlist_tracks(playlist_id,track_id,position,added_at)
    SELECT playlist_id,track_id,ROW_NUMBER() OVER(PARTITION BY playlist_id ORDER BY position,track_id)-1,added_at FROM playlist_tracks_v0;
   DROP TABLE playlist_tracks_v0;
   CREATE INDEX IX_playlist_tracks_track_id ON playlist_tracks(track_id);
   CREATE INDEX IX_playlist_tracks_playlist_id_position ON playlist_tracks(playlist_id,position);
   CREATE TRIGGER entry_writer_insert BEFORE INSERT ON playlist_tracks BEGIN SELECT CASE WHEN huayuan_entry_writer()!=4 THEN RAISE(ABORT,'ENTRY_WRITER_VERSION') END; END;
   CREATE TRIGGER entry_writer_update BEFORE UPDATE ON playlist_tracks BEGIN SELECT CASE WHEN huayuan_entry_writer()!=4 THEN RAISE(ABORT,'ENTRY_WRITER_VERSION') END; END;
   CREATE TRIGGER entry_writer_delete BEFORE DELETE ON playlist_tracks BEGIN SELECT CASE WHEN huayuan_entry_writer()!=4 THEN RAISE(ABORT,'ENTRY_WRITER_VERSION') END; END;
   PRAGMA user_version=4;
   """;q.ExecuteNonQuery();
  foreach(var table in new[]{"tracks","playlists"})foreach(var op in new[]{"INSERT","UPDATE","DELETE"}){
   q.CommandText=$"CREATE TRIGGER entry_writer_{table}_{op} BEFORE {op} ON {table} BEGIN SELECT CASE WHEN huayuan_entry_writer()!=4 THEN RAISE(ABORT,'ENTRY_WRITER_VERSION') END; END;";q.ExecuteNonQuery();
  }
  if(injectFailure)throw new IOException("Injected migration failure before commit");
  q.CommandText="PRAGMA foreign_key_check";using(var rd=q.ExecuteReader())if(rd.Read())throw new InvalidDataException("Foreign key failure");
  q.CommandText="PRAGMA integrity_check";if((string?)q.ExecuteScalar()!="ok")throw new InvalidDataException("Post migration integrity failure");tx.Commit();
 }
}
