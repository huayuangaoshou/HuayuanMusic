namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 IEnumerable<Song> PlaylistSongs(){var list=core.State.Lists.FirstOrDefault(x=>x.Id==ActiveListId);if(list is null)return [];return list.SongIds.Select((id,i)=>Track(id) with {PlaylistEntryId=list.EntryIds?[i]??0});}
 void PlayPlaylistEntry(Song song)=>RunCore(()=>((CorePlaybackAdapter)core).PlayPlaylistEntryAsync(ActiveListId,song.PlaylistEntryId));
}
internal sealed partial class CorePlaybackAdapter {
 internal Task PlayPlaylistEntryAsync(long listId,long entryId)=>Run("PlaylistEntryPlay",async()=>{
  var entries=await Store.GetPlaylistEntriesAsync(listId);int at=entries.ToList().FindIndex(x=>x.EntryId==entryId);if(at<0)throw new InvalidOperationException("歌单条目已变化");
  var songs=entries.Select(e=>Song((int)e.Track.Id).NewQueueEntry()).ToArray();
  await commands.ReplaceQueueAsync(songs,songs[at]);if(!root.Player.IsPlayingAudio)throw new IOException("音频未开始");await root.ViewModel.History.LoadAsync();RefreshRecords();if(PersistQueue)root.Player.FlushControlledQueue();
 });
}
