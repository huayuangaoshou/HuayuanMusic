using System.ComponentModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using KugouAvaloniaPlayer.ViewModels;
namespace HuayuanPreview;
public sealed record CoreTrack(int Id,string Key,string Title,string Artist,string Album,double Duration,string? Cover);
public sealed record CoreLyric(string Text,double StartMilliseconds,string? Translation=null);
public sealed record CoreList(string Name,int[] SongIds,long Id=0,long[]? EntryIds=null);
public sealed record CoreQueueEntry(Guid EntryId,int TrackId);
public sealed record PlaybackSnapshot {
 public CoreTrack[] Tracks {get;init;}=[]; public CoreLyric[] Lyrics {get;init;}=[];
 public CoreQueueEntry[] QueueEntries {get;init;}=[];public Guid CurrentEntryId {get;init;}
 public int[] Queue {get;init;}=[];public int[] Favorites {get;init;}=[];public int[] History {get;init;}=[];public CoreList[] Lists {get;init;}=[];
 public string ImportStatus {get;init;}=""; public bool Importing {get;init;}
 public int Mode {get;init;}
 public int CurrentId {get;init;}public double Position {get;init;}public double Duration {get;init;}public float Volume {get;init;}
 public bool Playing {get;init;}public bool Paused {get;init;}public int LyricIndex {get;init;}=-1;public string Error {get;init;}="";public long Revision {get;init;}
}
public interface INightflightPlayback:IDisposable {
 PlaybackSnapshot State{get;}event Action<PlaybackSnapshot>? Changed;
 Task QueueEntryAsync(string op,Guid entryId,Guid target=default);Task PlaylistEntryAsync(string op,long listId,long entryId,int target=0);
 Task QueueAsync(string op,int id=0,int target=0);Task SetModeAsync(int mode);Task PlaylistAsync(string op,string name="",long listId=0,int songId=0,int target=0);
 Task ImportFilesAsync(string[] files,CancellationToken cancel);Task InitializeAsync();Task PlayAsync(int id);Task ToggleAsync(int selected);Task PreviousAsync();Task NextAsync();Task StopAsync();
 Task SeekAsync(double seconds);Task SeekLyricAsync(int index);Task VolumeAsync(double value);Task MuteAsync();Task FavoriteAsync(int id);Task ClearHistoryAsync();Task AddNextAsync(int id);
}
// The ONLY UI-to-production adapter. Arrays are display snapshots, never a second queue/database.
internal sealed partial class CorePlaybackAdapter:INightflightPlayback {
 readonly OfflineWindowRoot root;readonly IPlaybackCommands commands;readonly Dictionary<int,SongItem> items=new();
 internal double TrialDecodedRms=>root.Coordinator.Player.GetActiveAnalysisSnapshot().Rms;
 internal int SeekSubmissions,VolumeSubmissions;CoreTrack[] trackSnapshot=[];CoreLyric[] lyricSnapshot=[];CoreQueueEntry[] queueEntrySnapshot=[];int[] queueSnapshot=[];bool collectionDirty=true;int posted,disposed;long revision;int[] favorites=[],history=[];CoreList[] lists=[];string error="";
 public PlaybackSnapshot State{get;private set;}=new();public event Action<PlaybackSnapshot>? Changed;
 internal CorePlaybackAdapter(OfflineWindowRoot root){this.root=root;commands=root.Player;root.Player.PersistControlledLocalSubset=P2AWorkspace.M223Normal;InitializeKugou();EnableDesktopUnavailableHandling();root.Player.PropertyChanged+=OnProperty;root.Player.PlaybackQueue.CollectionChanged+=OnCollection;root.Player.LyricLines.CollectionChanged+=OnCollection;LocalMusicRecordStore.FavoritesChanged+=OnFavorites;root.ViewModel.History.Songs.CollectionChanged+=OnHistory;}
 public async Task InitializeAsync(){await root.ViewModel.InitializeAsync();await ReloadAllAsync();RefreshRecords();if(PersistQueue){root.Player.ApplySavedPlaybackModePreference();if(!Program.Desktop||R2.DesktopPreferences.Behavior.RestoreQueue){await root.Player.RestoreCachedPlaybackQueueAsync();if(root.Player.ControlledQueueReadError!=null)error="队列恢复已拒绝："+root.Player.ControlledQueueReadError;}}Publish();}
 internal bool ChangeDesktopOutput(int id)
 {
  if(!root.Player.SetOutputDevice(id))return false;
  var previous=SettingsManager.Settings.AudioOutputDeviceId;
  SettingsManager.Settings.AudioOutputDeviceId=id;
  try { SettingsManager.Save();return true; }
  catch(IOException){SettingsManager.Settings.AudioOutputDeviceId=previous;root.Player.SetOutputDevice(previous);return false;}
 }
 int Id(SongItem? s)=>s==null?0:items.FirstOrDefault(x=>string.Equals(x.Value.LocalFilePath,s.LocalFilePath,StringComparison.OrdinalIgnoreCase)).Key;
 SongItem Song(int id){if(!items.TryGetValue(id,out var s))throw new InvalidOperationException("仅允许当前隔离测试曲目");if(s.LocalSourceType=="ProviderStream"){if(!ProviderStreamRegistry.Allows(s.LocalFilePath))throw new UnauthorizedAccessException("平台许可失效");return s;}if(s.LocalSourceType!="LocalFile"||s.LocalTrackId<=0||!string.IsNullOrEmpty(s.RemoteUrl))throw new IOException("曲目来源不属于受控本地媒体");if(OfflineStartupProfile.Active)OfflineStartupProfile.Current.DemandMedia(s.LocalFilePath!);return s;}
 internal async Task ReloadAllAsync(){var all=await Task.Run(()=>Store.GetAllLocalTracksAsync());var ephemeral=items.Where(x=>x.Value.LocalSourceType=="ProviderStream").ToArray();items.Clear();foreach(var t in all)items[checked((int)t.Id)]=LocalMusicLibraryViewModel.ToSongItem(t);foreach(var row in ephemeral)items.Add(row.Key,row.Value);RebuildTrackSnapshot();var loaded=new List<CoreList>();foreach(var list in await root.Library.GetPlaylistsAsync()){var entries=await Store.GetPlaylistEntriesAsync(list.Id);loaded.Add(new(list.Name,entries.Select(t=>(int)t.Track.Id).ToArray(),list.Id,entries.Select(t=>t.EntryId).ToArray()));}lists=loaded.ToArray();}
 public async Task ImportFilesAsync(string[] files,CancellationToken cancel){if(OfflineStartupProfile.Active&&!OfflineStartupProfile.Current.Import)throw new IOException("Import capability required");foreach(var chunk in files.Chunk(20)){cancel.ThrowIfCancellationRequested();await root.ViewModel.ImportSelectedAsync(chunk,cancel);}await ReloadAllAsync();RefreshRecords();Publish();}
 internal string ImportStatus=>root.ViewModel.Status;
 void RefreshRecords(){favorites=LocalMusicRecordStore.LoadFavorites().Select(Id).Where(i=>i>0).ToArray();history=LocalMusicRecordStore.LoadHistory().Select(Id).Where(i=>i>0).ToArray();}
 void OnHistory(object? sender,NotifyCollectionChangedEventArgs e){history=root.ViewModel.History.Songs.Select(Id).Where(x=>x>0).ToArray();Schedule();}
 void OnProperty(object? sender,PropertyChangedEventArgs e)=>Schedule();void OnCollection(object? s,NotifyCollectionChangedEventArgs e){collectionDirty=true;Schedule();}void OnFavorites(){RefreshRecords();Schedule();}
 void Schedule(){if(disposed!=0||Interlocked.Exchange(ref posted,1)!=0)return;Dispatcher.UIThread.Post(()=>{Interlocked.Exchange(ref posted,0);if(disposed==0)Publish();});}
 void Publish(){var p=root.Player;if(collectionDirty){collectionDirty=false;lyricSnapshot=p.LyricLines.Select(x=>new CoreLyric(x.Content,x.StartTime,x.Translation)).ToArray();queueEntrySnapshot=p.PlaybackQueue.Select(x=>new CoreQueueEntry(x.QueueEntryId,Id(x))).ToArray();queueSnapshot=p.PlaybackQueue.Select(Id).Where(x=>x>0).ToArray();}State=new(){QueueEntries=queueEntrySnapshot,CurrentEntryId=p.CurrentPlayingSong?.QueueEntryId??Guid.Empty,Tracks=trackSnapshot,Lyrics=lyricSnapshot,Queue=queueSnapshot,Mode=(int)root.Player.CurrentPlayMode,ImportStatus=root.ViewModel.Status,Importing=root.ViewModel.IsImporting,Favorites=favorites.ToArray(),History=history.ToArray(),Lists=lists,CurrentId=Id(p.CurrentPlayingSong),Position=p.CurrentPositionSeconds,Duration=p.TotalDurationSeconds,Volume=p.MusicVolume,Playing=p.IsPlayingAudio,Paused=root.Coordinator.Player.IsPaused,LyricIndex=p.CurrentLyricIndex,Error=error,Revision=++revision};Changed?.Invoke(State);}
 async Task Run(string name,Func<Task> action){if(disposed!=0)return;var watch=System.Diagnostics.Stopwatch.StartNew();try{error="";await action();if(name is "Play" or "Previous" or "Next" or "Favorite" or "ClearHistory")RefreshRecords();}catch(Exception e){error=e.GetType().Name+"："+e.Message;Program.Log("CORE_SAFE_ERROR",new{name,type=e.GetType().Name});}finally{Publish();watch.Stop();Program.Log("CORE_ACTION",new{name,ms=watch.Elapsed.TotalMilliseconds,State.CurrentId,State.Playing,State.Position,error=error.Length>0});}}
 public Task PlayAsync(int id)=>Run("Play",async()=>{PlaybackQueueManager.DemandCapacity(items.Values.ToArray());await commands.PlayAsync(Song(id),items.Values.ToArray());await root.ViewModel.History.LoadAsync();if(!root.Player.IsPlayingAudio)throw new InvalidOperationException("音频未开始，请检查受控音源或设备");});
 public Task ToggleAsync(int selected)=>State.CurrentId==0?(root.Player.PlaybackQueue.Count>0?QueueEntryAsync("play",root.Player.PlaybackQueue[0].QueueEntryId):PlayAsync(selected)):Run("Toggle",async()=>{if(root.Coordinator.Player.IsStopped){await commands.PlayAsync(root.Player.CurrentPlayingSong??Song(State.CurrentId));if(!root.Player.IsPlayingAudio)throw new InvalidOperationException("音频未开始，请检查受控音源或设备");await root.ViewModel.History.LoadAsync();RefreshRecords();}else await root.Player.ToggleDesktopTransportAsync();});
 public Task PreviousAsync()=>Run("Previous",()=>commands.PlayPreviousAsync());public Task NextAsync()=>Run("Next",()=>commands.PlayNextAsync());
 public Task StopAsync()=>Run("Stop",()=>{commands.Stop();return Task.CompletedTask;});
 public Task SeekAsync(double seconds)=>Run("Seek",()=>{SeekSubmissions++;var target=Math.Clamp(seconds,0,Math.Max(0,root.Player.TotalDurationSeconds));root.Player.CurrentPositionSeconds=target;if(Math.Abs(root.Coordinator.Player.GetPosition().TotalSeconds-target)>.025)root.Coordinator.Player.SetPosition(TimeSpan.FromSeconds(target));return Task.CompletedTask;});
 public Task SeekLyricAsync(int index)=>Run("SeekLyric",()=>{if(index<0||index>=root.Player.LyricLines.Count)throw new ArgumentOutOfRangeException(nameof(index));root.Player.SeekToLyricLineCommand.Execute(TimeSpan.FromMilliseconds(root.Player.LyricLines[index].StartTime));return Task.CompletedTask;});
 public Task VolumeAsync(double value)=>Run("Volume",async()=>{VolumeSubmissions++;var target=(float)Math.Clamp(value,0,1);if(target==0&&root.Player.MusicVolume>0){await RememberPreMuteAsync();root.Player.ToggleMute(true);}else root.Player.MusicVolume=target;});
 public Task MuteAsync()=>Run("Mute",async()=>{if(Program.Desktop&&root.Player.MusicVolume<=0){root.Player.MusicVolume=Math.Clamp(R2.DesktopPreferences.Behavior.PreMuteVolume,0.01f,1);return;}await RememberPreMuteAsync();root.Player.ToggleMute();if(root.Player.MusicVolume<=0&&State.Volume==0)root.Player.MusicVolume=.35f;});
 private Task RememberPreMuteAsync()
 {
  var volume=root.Player.MusicVolume;
  return Program.Desktop&&volume>0?R2.DesktopPreferences.SetBehaviorAsync(options=>options with {PreMuteVolume=volume}):Task.CompletedTask;
 }
 public Task FavoriteAsync(int id)=>Run("Favorite",()=>{var song=Song(id);if(song.LocalSourceType=="ProviderStream")throw new InvalidOperationException("平台收藏尚未接入，不写入本地收藏");bool expected=!LocalMusicRecordStore.IsFavorite(song);LocalMusicRecordStore.ToggleFavorite(song);if(LocalMusicRecordStore.IsFavorite(song)!=expected)throw new IOException("收藏未保存，保留原状态");root.ViewModel.Favorites.Refresh();return Task.CompletedTask;});
 public Task ClearHistoryAsync()=>Run("ClearHistory",()=>root.ViewModel.History.ClearHistoryCommand.ExecuteAsync(null));
 public Task AddNextAsync(int id)=>Run("AddNext",()=>{commands.AddToNext(NextCatalogSong(id));return Task.CompletedTask;});
 public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;try{if(PersistQueue)root.Player.FlushControlledQueue();}catch(Exception e){Program.Result=2;Program.Log("QUEUE_CLOSE_WRITE_ERROR",new{type=e.GetType().Name,oldCacheRetained=true});}root.Player.PropertyChanged-=OnProperty;root.Player.PlaybackQueue.CollectionChanged-=OnCollection;root.Player.LyricLines.CollectionChanged-=OnCollection;LocalMusicRecordStore.FavoritesChanged-=OnFavorites;root.ViewModel.History.Songs.CollectionChanged-=OnHistory;Kugou.Revoked-=ProviderRevoked;ProviderRevoked();Kugou.Dispose();Providers.Dispose();commands.Stop();}
}
