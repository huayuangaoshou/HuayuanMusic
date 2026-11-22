using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
namespace KugouAvaloniaPlayer.Services;
public sealed record LocalPlaylistEntry(long EntryId,LocalTrackItem Track);
public sealed partial class LocalMusicLibraryService {
 internal async Task<IReadOnlyList<LocalPlaylistEntry>> GetPlaylistEntriesAsync(long playlistId){
  await EnsureInitializedAsync(CancellationToken.None);await using var c=await AppDatabase.CreateConnectionAsync();using var q=c.CreateCommand();
  q.CommandText="SELECT t.id,t.title,t.artist,t.album,t.duration_seconds,t.source_type,t.local_path,t.cover_path,t.remote_url,p.entry_id FROM playlist_tracks p JOIN tracks t ON t.id=p.track_id WHERE p.playlist_id=$p ORDER BY p.position,p.entry_id";q.Parameters.AddWithValue("$p",playlistId);
  using var rd=await q.ExecuteReaderAsync();var rows=new List<LocalPlaylistEntry>();while(await rd.ReadAsync())rows.Add(new(rd.GetInt64(9),ReadTrackItem(rd)));return rows;
 }
 internal async Task EditPlaylistEntryAsync(long playlistId,string operation,long entryId=0,long trackId=0,int target=0){
  await EnsureInitializedAsync(CancellationToken.None);await using var c=await AppDatabase.CreateConnectionAsync();using var tx=c.BeginTransaction();
  if(await GetPlaylistAsync(c,tx,playlistId,CancellationToken.None)==null)throw new InvalidOperationException("歌单不存在");
  using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT entry_id FROM playlist_tracks WHERE playlist_id=$p ORDER BY position,entry_id";q.Parameters.AddWithValue("$p",playlistId);
  var entries=new List<long>();using(var rd=await q.ExecuteReaderAsync())while(await rd.ReadAsync())entries.Add(rd.GetInt64(0));
  if(operation=="add"){
   if(entries.Count>=1000)throw new InvalidOperationException("歌单最多1000条");await DemandTrackReferences(c,tx,[trackId]);
   await InsertPlaylistTrackAsync(c,tx,playlistId,trackId,entries.Count,DateTime.UtcNow,CancellationToken.None);
  }else{
   int from=entries.IndexOf(entryId);if(from<0)throw new InvalidOperationException("歌单条目已变化，请刷新");
   if(operation=="remove"){q.CommandText="DELETE FROM playlist_tracks WHERE playlist_id=$p AND entry_id=$e";q.Parameters.AddWithValue("$e",entryId);await q.ExecuteNonQueryAsync();entries.RemoveAt(from);}
   else if(operation=="move"){if(target<0||target>=entries.Count)throw new ArgumentOutOfRangeException(nameof(target));entries.RemoveAt(from);entries.Insert(target,entryId);}
   else throw new ArgumentException("Unknown membership operation");
   // Move positions out of the unique range first, then assign final positions transactionally.
   q.Parameters.Clear();q.CommandText="UPDATE playlist_tracks SET position=position+10000 WHERE playlist_id=$p";q.Parameters.AddWithValue("$p",playlistId);await q.ExecuteNonQueryAsync();
   for(int i=0;i<entries.Count;i++){q.Parameters.Clear();q.CommandText="UPDATE playlist_tracks SET position=$n WHERE playlist_id=$p AND entry_id=$e";q.Parameters.AddWithValue("$p",playlistId);q.Parameters.AddWithValue("$n",i);q.Parameters.AddWithValue("$e",entries[i]);await q.ExecuteNonQueryAsync();}
  }
  q.Parameters.Clear();q.CommandText="UPDATE playlists SET updated_at=$u WHERE id=$p";q.Parameters.AddWithValue("$p",playlistId);q.Parameters.AddWithValue("$u",AppDatabase.FormatDateTime(DateTime.UtcNow));await q.ExecuteNonQueryAsync();tx.Commit();
 }
}
