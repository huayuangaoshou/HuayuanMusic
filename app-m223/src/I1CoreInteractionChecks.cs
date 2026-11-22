using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 async Task I1CoreChecks(OfflineWindowRoot root,CorePlaybackAdapter adapter,Action<bool,string> check){
  var store=(LocalMusicLibraryService)root.Library;var tracks=(await store.GetAllLocalTracksAsync()).Take(2).ToArray();int a=(int)tracks[0].Id,b=(int)tracks[1].Id;
  var files=Directory.GetFiles(OfflineStartupProfile.Current.Media,"*.wav",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))));
  await adapter.QueueAsync("clear");await adapter.QueueAsync("append",a);await adapter.QueueAsync("append",b);var before=adapter.State.QueueEntries.Select(e=>e.EntryId).ToArray();
  await adapter.QueueAsync("append",a);var target=adapter.State.QueueEntries.Last().EntryId;await adapter.VolumeAsync(0);await adapter.QueueEntryAsync("play",target);
  check(adapter.State.Error.Length==0&&before.All(id=>adapter.State.QueueEntries.Any(e=>e.EntryId==id))&&adapter.State.QueueEntries.Length==3&&adapter.State.CurrentEntryId==target,"I1 catalog double-click mapping append then exact-entry play retains prior queue");
  await adapter.QueueAsync("append",a);var duplicate=adapter.State.QueueEntries.Last().EntryId;check(duplicate!=target&&adapter.State.QueueEntries.Select(e=>e.EntryId).Distinct().Count()==4,"I1 repeat admission has unique entry identities");
  await adapter.QueueAsync("append",b);var nextId=adapter.State.QueueEntries.Last().EntryId;await adapter.QueueEntryAsync("next",nextId);var afterNext=adapter.State.QueueEntries;int current=Array.FindIndex(afterNext,e=>e.EntryId==target);check(afterNext[current+1].TrackId==b&&afterNext[current+1].EntryId!=before[1],"I1 next differs from append without replacing current entry");
  await adapter.QueueEntryAsync("remove",duplicate);check(adapter.State.QueueEntries.Length==4&&adapter.State.QueueEntries.Any(e=>e.EntryId==target)&&!adapter.State.QueueEntries.Any(e=>e.EntryId==duplicate),"I1 remove exactly one duplicate");
  var ids=adapter.State.QueueEntries.Select(e=>e.EntryId).ToHashSet();await adapter.QueueEntryAsync("before",before[1],before[0]);check(adapter.State.QueueEntries[0].EntryId==before[1]&&ids.SetEquals(adapter.State.QueueEntries.Select(e=>e.EntryId))&&adapter.State.CurrentEntryId==target,"I1 move retains current playback and all identities");
  var unchanged=adapter.State.QueueEntries.Select(e=>e.EntryId).ToArray();await adapter.QueueEntryAsync("play",Guid.NewGuid());check(adapter.State.Error.Length>0&&unchanged.SequenceEqual(adapter.State.QueueEntries.Select(e=>e.EntryId)),"I1 stale entry failure publishes error without clearing queue");
  var active=adapter.State.CurrentEntryId;var at=Array.FindIndex(adapter.State.QueueEntries,e=>e.EntryId==active);var expectedNext=adapter.State.QueueEntries[(at+1)%adapter.State.QueueEntries.Length].EntryId;await adapter.QueueEntryAsync("remove",active);check(adapter.State.CurrentEntryId==expectedNext&&adapter.State.Playing,"I1 removing active occurrence follows existing Core next-entry rule");
  string name="I1 fixture "+Guid.NewGuid().ToString("N");await adapter.PlaylistAsync("create",name);var playlist=(await store.GetPlaylistsAsync()).Single(p=>p.Name==name);for(int i=0;i<3;i++)await adapter.PlaylistAsync("add",listId:playlist.Id,songId:a);var entries=await store.GetPlaylistEntriesAsync(playlist.Id);check(entries.Count==3&&entries.Select(e=>e.EntryId).Distinct().Count()==3,"I1 local membership duplicates have SQL entry identities");
  var removed=entries[1].EntryId;await adapter.PlaylistEntryAsync("remove",playlist.Id,removed);var read=await store.GetPlaylistEntriesAsync(playlist.Id);check(read.Count==2&&read.Select(e=>e.EntryId).SequenceEqual(new[]{entries[0].EntryId,entries[2].EntryId}),"I1 membership remove preserves duplicate siblings");
  await adapter.PlaylistEntryAsync("move",playlist.Id,read[0].EntryId,1);read=await store.GetPlaylistEntriesAsync(playlist.Id);check(read[1].EntryId==entries[0].EntryId,"I1 local membership move readback");
  check(files.All(kv=>File.Exists(kv.Key)&&Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(kv.Key)))==kv.Value),"I1 queue and membership operations never alter disk audio");
  bool favorite=adapter.State.Favorites.Contains(a);await adapter.FavoriteAsync(a);check(adapter.State.Favorites.Contains(a)!=favorite,"I1 favorite actual store readback");await adapter.FavoriteAsync(a);
  Program.Log("I1_CORE_MAP_COMPLETE",new{queue=adapter.State.QueueEntries.Select(e=>new{e.EntryId,e.TrackId}),playlistId=playlist.Id,members=read.Select(e=>e.EntryId),fileCount=files.Count});
 }
}
