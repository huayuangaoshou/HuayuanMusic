using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
namespace HuayuanPreview.R2;
// All arrays below are read-only projections of PlaybackSnapshot, never mutable playback owners.
public sealed class TrackRow:ObservableObject {
 public CoreTrack Track {get;}public WorkspaceState Owner {get;}public Guid QueueId {get;}public long MemberId {get;}public long ListId {get;}
 public string Identity=>QueueId!=Guid.Empty?"q:"+QueueId:MemberId>0?$"m:{ListId}:{MemberId}":Owner.Local?$"t:{Track.Id}":Track.Key;
 public bool IsProviderTrack=>Track.Key.StartsWith("kugou-concept:",StringComparison.Ordinal);
 internal string CatalogIdentity { get; }
 public string SourceLabel=>IsProviderTrack?"在线":"本地";
 public bool CanUseOnlineActions => Program.Desktop && IsProviderTrack && Owner.Host.KugouEnabled &&
     Owner.Host.Concept.M223CanQueue(Track.Key);
 public bool IsLosslessFile => !IsProviderTrack && Track.Key.EndsWith(".flac", StringComparison.OrdinalIgnoreCase);
 public bool IsDownloaded => Program.Desktop && Owner.Host.IsDownloaded(this);
 public TrackRow Song=>this;public string Name=>Track.Title;public string Singer=>Track.Artist;public string AlbumName=>Track.Album;public string Duration=>Track.Duration<=0?"—":$"{(int)Track.Duration/60:00}:{(int)Track.Duration%60:00}";
 public int Number { get; internal set; }
 private bool isSelected;
 public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
 private bool isBatchMode;
 public bool IsBatchMode { get => isBatchMode; internal set { if (SetProperty(ref isBatchMode, value)) { OnPropertyChanged(nameof(IsNumberVisible)); OnPropertyChanged(nameof(IsWaveVisible)); } } }
 public bool IsNumberVisible => !isBatchMode && !IsCurrent;
 public bool IsWaveVisible => !isBatchMode && IsCurrent;
 public bool IsLocalTrack => Owner.Local && !IsProviderTrack;
 public bool IsPlaylistMember => Owner.Local && MemberId > 0;
 public bool IsCurrent => QueueId != Guid.Empty
     ? Owner.Host.Player.State.CurrentEntryId == QueueId
     : Owner.Host.Player.DisplayedPlayingSong is { } playing &&
       CatalogIdentity==playing.CatalogIdentity;
 public bool IsPlaying => IsCurrent && Owner.Host.Player.State.Playing;
 public string? UnavailableReason => Owner.Host.Adapter.UnavailableReason(this);
 public bool IsUnavailable => Owner.Host.Adapter.IsUnavailable(this);
 public bool IsFavorite => IsProviderTrack ? Owner.Host.Concept.IsDesktopFavorite(Track.Key) :
     Owner.Host.Player.State.Favorites.Contains(Track.Id);
 public string FavoriteLabel => IsFavorite ? "取消收藏" : "收藏歌曲";
 public ICommand FavoriteCommand { get; }
 public ICommand NextCommand { get; }
 public ICommand RemoveCommand { get; }
 public string RemoveLabel=>"移除 "+Name;public string QueuePlayLabel=>"播放 "+Name;
 public string TrialPlayId=>!Owner.Local&&ListId==2&&MemberId==1&&P2AWorkspace.M218BTarget&&CanUsePlatform?"M222.Target.Play":"";
 public string TrialAddId=>!Owner.Local&&ListId==2&&MemberId==1&&P2AWorkspace.M218BTarget&&CanUsePlatform?"M222.Target.Add":"";
 public string PlayLabel=>P2AWorkspace.M211StrictMemberIntent&&!Owner.Local?
    "播放 "+Name+" · 歌单第"+Owner.Host.Concept.M211MemberNumber(Track.Key)+"条":"播放 "+Name;
 public string AddLabel=>P2AWorkspace.M211StrictMemberIntent&&!Owner.Local?
    "入队 "+Name+" · 歌单第"+Owner.Host.Concept.M211MemberNumber(Track.Key)+"条":"入队 "+Name;
 public bool CanUsePlatform=>Owner.Local||
   (P2AWorkspace.M223Normal?Owner.Host.Concept.M223CanQueue(Track.Key):
    P2AWorkspace.M213RowMode?Owner.Host.Concept.M213CanQueue(Track.Key):
    Owner.Host.Concept.M209CanQueue(Track.Key));
 public bool CanInspectOffline=>P2AWorkspace.M212SnapshotPreview&&!Owner.Local&&MemberId>0;
 public string InspectLabel=>"核验歌单第"+MemberId+"条";
 public ICommand InspectCommand {get;}
 public ICommand PlayCommand {get;}public ICommand AddCommand {get;}
 public bool CanRemoveLocally => IsLocalTrack || IsPlaylistMember;
 public string RemoveLocalLabel => IsPlaylistMember ? "从歌单移除" : "从列表移除";
 public ICommand RemoveLocalCommand {get;}
 public TrackRow(CoreTrack track,WorkspaceState owner,Guid queueId=default,long memberId=0,long listId=0){Track=track;Owner=owner;QueueId=queueId;MemberId=memberId;ListId=listId;
  var key=track.Key;
  var accountEnd=IsProviderTrack?key.IndexOf(':',"kugou-concept:".Length):-1;
  CatalogIdentity=IsProviderTrack&&accountEnd>0 ? key[..accountEnd]+key[key.LastIndexOf(':')..] : "local:"+track.Id;
  PlayCommand=new RelayCommand(()=>{if(Owner.Local||P2AWorkspace.M223Normal)Owner.Host.Player.Play(this);
  else if(P2AWorkspace.M213RowMode)Owner.Host.Concept.M213Submit(Track.Key,true);
  else Owner.Host.Concept.M209Submit(Track.Key,true);});
 AddCommand=new RelayCommand(()=>{if(Owner.Local||P2AWorkspace.M223Normal)Owner.Host.Player.Append([this]);
  else if(P2AWorkspace.M213RowMode)Owner.Host.Concept.M213Submit(Track.Key,false);
  else Owner.Host.Concept.M209Submit(Track.Key,false);});
 FavoriteCommand=new RelayCommand(()=>Owner.Host.Player.Favorite([this]));
 NextCommand=new RelayCommand(()=>Owner.Host.Player.Next([this]));
 RemoveCommand=new RelayCommand(()=>Owner.Host.Player.Remove([this]));
 RemoveLocalCommand=new RelayCommand(()=>_ = Owner.RequestRemoveTracksAsync([this]));
 InspectCommand=new RelayCommand(()=>Owner.Host.Concept.M212InspectMember(ListId,MemberId,Track.Key));}
 public void Refresh(){OnPropertyChanged(nameof(IsDownloaded));OnPropertyChanged(nameof(Number));OnPropertyChanged(nameof(IsCurrent));OnPropertyChanged(nameof(IsPlaying));OnPropertyChanged(nameof(IsUnavailable));OnPropertyChanged(nameof(UnavailableReason));OnPropertyChanged(nameof(IsNumberVisible));OnPropertyChanged(nameof(IsWaveVisible));}
 public void RefreshFavorite(){OnPropertyChanged(nameof(IsFavorite));OnPropertyChanged(nameof(FavoriteLabel));}
 public void RefreshAccess(){OnPropertyChanged(nameof(CanUsePlatform));OnPropertyChanged(nameof(CanUseOnlineActions));RefreshFavorite();}
}
public sealed class CorePresentation:ObservableObject,IDisposable {
 readonly INightflightPlayback core;readonly Dictionary<Guid,TrackRow> queueViews=[];ShellModel? host;PlaybackSnapshot state;
 public PlaybackSnapshot State=>state;public Task Pending {get;private set;}=Task.CompletedTask;
 public TrackRow[] DisplayPlaybackQueue {get;private set;}=[];public int DisplayPlaybackQueueCount=>state.QueueEntries.Length;
 public string QueueBadgeText=>DisplayPlaybackQueueCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
 public string QueueCountTooltip=>$"播放队列 · {DisplayPlaybackQueueCount:N0} 首";
 public bool HasQueueTracks=>DisplayPlaybackQueueCount>0;
 public TrackRow? DisplayedPlayingSong=>DisplayPlaybackQueue.FirstOrDefault(r=>r.QueueId==state.CurrentEntryId);
 public string ActiveLyric=>state.LyricIndex>=0&&state.LyricIndex<state.Lyrics.Length
  &&(state.Playing||state.Paused)?state.Lyrics[state.LyricIndex].Text:"";
 private double progressPosition;
 private long lastProgressSample;
 internal double ProgressPosition => progressPosition;
 internal event Action? ProgressUpdated;
 internal event Action? LyricsClockUpdated;
 public string ProgressText=>$"{PositionText} / {DurationText}";
 public string PositionText=>FormatSeconds(progressPosition);
 public string DurationText=>FormatSeconds(state.Duration);
 public string NowPlayingCredit=>DisplayedPlayingSong is { } row
  ? string.Join("  ·  ", new[]{row.Singer,row.AlbumName}.Where(text=>!string.IsNullOrWhiteSpace(text))) : "";
 public string TransportStatus=>state.Playing?"播放中":state.Paused?"已暂停":"已停止";
 static string FormatSeconds(double value){var seconds=(int)Math.Max(0,value);return $"{seconds/60:00}:{seconds%60:00}";}
 public bool IsPlayingAudio=>state.Playing;public string PlaybackFeedback=>uiError.Length>0?uiError:"播放队列 · "+state.QueueEntries.Length;
 long failureNotices;
 string uiError="";public string UiError=>uiError;public event Action? Updated;
 public bool HasCurrentSong => DisplayedPlayingSong != null;
 public string NowPlayingTitle => DisplayedPlayingSong?.Name ?? "未播放";
 public bool HasPlaybackError => uiError.Length > 0 || State.Error.Length > 0;
 public bool IsMuted => State.Volume <= 0;
 public string ModeLabel => (PlayMode)State.Mode switch
 {
  PlayMode.RepeatOne => "单曲循环", PlayMode.Shuffle => "随机播放", PlayMode.Sequential => "顺序播放", _ => "列表循环"
 };
 public Geometry ModeIcon => Geometry.Parse((PlayMode)State.Mode switch
 {
  // 列表循环: 首尾相接的循环箭头
  PlayMode.Normal => "M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4z",
  // 单曲循环: 循环箭头带 1
  PlayMode.RepeatOne => "M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4zm-4-2V9h-1l-2 1v1h1.5v4H13z",
  // 随机播放: 交叉箭头
  PlayMode.Shuffle => "M10.59 9.17L5.41 4 4 5.41l5.17 5.17 1.42-1.41zM14.5 4l2.04 2.04L4 18.59 5.41 20 17.96 7.46 20 9.5V4h-5.5zm.33 9.41l-1.41 1.41 3.13 3.13L14.5 20H20v-5.5l-2.04 2.04-3.13-3.13z",
  // 顺序播放: 右箭头 + 终止线
  _ => "M3 11h10.17l-3.58-3.59L11 6l6 6-6 6-1.41-1.41L13.17 13H3v-2zM19 6h2v12h-2V6z"
 });
 public bool IsBuffering { get; private set; }
 internal event Action<string>? Toast;
 internal void NotifyUnavailable(string? reason=null)
 {
  failureNotices++;
  Notify(reason??"暂时无法播放该歌曲，请稍后重试");
 }
 internal void Notify(string message)=>Toast?.Invoke(message);
 public ICommand FavoriteCurrentCommand { get; }
 public ICommand CycleModeCommand { get; }
 public ICommand MuteCommand { get; }
 public ICommand ClearQueueCommand {get;}public ICommand PlaySongCommand {get;}public ICommand RemoveFromQueueCommand {get;}public ICommand PlayPreviousCommand {get;}public ICommand PlayNextCommand {get;}public ICommand TogglePlayPauseCommand {get;}public ICommand StopCommand {get;}
 public CorePresentation(INightflightPlayback core){this.core=core;state=core.State;progressPosition=state.Position;
  FavoriteCurrentCommand=new RelayCommand(()=>{if(DisplayedPlayingSong is { } row)Favorite([row]);});
  CycleModeCommand=new RelayCommand(()=>Submit("mode",()=>{
   // Match the core cycle order: 列表循环 → 顺序播放 → 随机播放 → 单曲循环 → 列表循环
   var next=(PlayMode)State.Mode switch {
    PlayMode.Normal=>PlayMode.Sequential,
    PlayMode.Sequential=>PlayMode.Shuffle,
    PlayMode.Shuffle=>PlayMode.RepeatOne,
    _=>PlayMode.Normal
   };
   return core.SetModeAsync((int)next);
  }));
  MuteCommand=new RelayCommand(()=>Submit("mute",core.MuteAsync));
  ClearQueueCommand=new RelayCommand(()=>Submit("clear",()=>core.QueueAsync("clear")));
  PlaySongCommand=new RelayCommand<TrackRow>(r=>{if(r!=null)Play(r);});RemoveFromQueueCommand=new RelayCommand<TrackRow>(r=>{if(r!=null)Remove([r]);});
  PlayPreviousCommand=new RelayCommand(()=>Step(false));PlayNextCommand=new RelayCommand(()=>Step(true));StopCommand=new RelayCommand(()=>Submit("stop",()=>P2AWorkspace.M213RowMode&&host!=null?host.Concept.M216StopAsync():core.StopAsync()));
  TogglePlayPauseCommand=new RelayCommand(()=>Submit("toggle",async()=>{if(state.CurrentEntryId==Guid.Empty)throw new InvalidOperationException("请先选择歌曲");DemandEntry(state.CurrentEntryId);await core.ToggleAsync(state.CurrentId);DemandResult();}));
  core.Changed+=Refresh;
 }
 public void Attach(ShellModel shell){host=shell;Refresh(core.State);}
 void Refresh(PlaybackSnapshot next){
  if(next.Revision<state.Revision)return;
  var previous=state;
  bool transportChanged=next.Playing!=previous.Playing||next.Paused!=previous.Paused||next.CurrentEntryId!=previous.CurrentEntryId;
  bool durationChanged=next.Duration!=previous.Duration;
  bool modeChanged=next.Mode!=previous.Mode;
  bool queueChanged=!ReferenceEquals(previous.Tracks,next.Tracks)||
   (!ReferenceEquals(previous.QueueEntries,next.QueueEntries)&&!previous.QueueEntries.SequenceEqual(next.QueueEntries));
  bool lyricsChanged=!ReferenceEquals(previous.Lyrics,next.Lyrics);
  bool uiChanged=transportChanged||durationChanged||modeChanged||queueChanged||lyricsChanged||
   !ReferenceEquals(previous.Lists,next.Lists)||next.Importing!=previous.Importing||next.ImportStatus!=previous.ImportStatus||
   next.Error!=previous.Error||next.Volume!=previous.Volume||!previous.Favorites.SequenceEqual(next.Favorites)||
   !previous.History.SequenceEqual(next.History);
  state=next;
  if(durationChanged)OnPropertyChanged(nameof(DurationText));
  if(transportChanged||lyricsChanged||next.LyricIndex!=previous.LyricIndex)OnPropertyChanged(nameof(ActiveLyric));
  if(host!=null&&(queueChanged||DisplayPlaybackQueue.Length!=next.QueueEntries.Length)){
   var ids=next.QueueEntries.Select(e=>e.EntryId).ToHashSet();foreach(var id in queueViews.Keys.Where(k=>!ids.Contains(k)).ToArray())queueViews.Remove(id);
   var tracks=next.Tracks.ToDictionary(track=>track.Id);
   DisplayPlaybackQueue=next.QueueEntries.Select(e=>{if(!queueViews.TryGetValue(e.EntryId,out var row)){row=new TrackRow(tracks[e.TrackId],host.LocalWorkspace,e.EntryId);queueViews.Add(e.EntryId,row);}return row;}).ToArray();for(int i=0;i<DisplayPlaybackQueue.Length;i++){DisplayPlaybackQueue[i].Number=i+1;DisplayPlaybackQueue[i].Refresh();}OnPropertyChanged(nameof(DisplayPlaybackQueue));
  }
  SampleProgress(transportChanged||durationChanged||(!next.Playing&&next.Position!=previous.Position));
  if(next.Position!=previous.Position||transportChanged) LyricsClockUpdated?.Invoke();
  if(!uiChanged)return;
  OnPropertyChanged(nameof(TransportStatus));
  OnPropertyChanged(nameof(State));if(transportChanged||queueChanged){foreach(var row in DisplayPlaybackQueue)row.Refresh();OnPropertyChanged(nameof(DisplayedPlayingSong));OnPropertyChanged(nameof(NowPlayingTitle));OnPropertyChanged(nameof(NowPlayingCredit));OnPropertyChanged(nameof(HasCurrentSong));}if(modeChanged){OnPropertyChanged(nameof(ModeLabel));OnPropertyChanged(nameof(ModeIcon));}if(queueChanged){OnPropertyChanged(nameof(DisplayPlaybackQueueCount));OnPropertyChanged(nameof(QueueBadgeText));OnPropertyChanged(nameof(QueueCountTooltip));OnPropertyChanged(nameof(HasQueueTracks));}OnPropertyChanged(nameof(IsPlayingAudio));OnPropertyChanged(nameof(PlaybackFeedback));OnPropertyChanged(nameof(HasPlaybackError));OnPropertyChanged(nameof(IsMuted));Updated?.Invoke();
 }
 private void SampleProgress(bool force)
 {
  var now=Stopwatch.GetTimestamp();
  if(!force&&(state.Position==progressPosition||Stopwatch.GetElapsedTime(lastProgressSample,now)<TimeSpan.FromMilliseconds(250)))return;
  var textChanged=PositionText!=FormatSeconds(state.Position);
  progressPosition=state.Position;
  lastProgressSample=now;
  if(textChanged)OnPropertyChanged(nameof(PositionText));
  if(textChanged||force)OnPropertyChanged(nameof(ProgressText));
  ProgressUpdated?.Invoke();
 }
 void DemandResult(){if(core.State.Error.Length>0)throw new InvalidOperationException(core.State.Error);}
 void DemandTrack(TrackRow row){if(!row.Owner.Local||!state.Tracks.Any(t=>t.Id==row.Track.Id&&t.Key==row.Track.Key))throw new InvalidOperationException("P2A 平台曲目只读 · 播放仅限隔离本地曲目");}
 void DemandPlayable(TrackRow row)
 {
  if(row.Owner.Local){DemandTrack(row);return;}
  if(!P2AWorkspace.M223Normal||host?.Concept.M223CanQueue(row.Track.Key)!=true)
   throw new UnauthorizedAccessException(Program.Desktop ? "请登录酷狗并刷新曲目" : "请确认当前合成会话的播放权限");
 }
 CoreQueueEntry DemandEntry(Guid id)=>state.QueueEntries.SingleOrDefault(e=>e.EntryId==id)??throw new InvalidOperationException("队列条目已变化，请重新选择");
 public void Import(string[] paths) => Submit("Import", () => core.ImportFilesAsync(paths, CancellationToken.None));
 public Task SetVolume(double volume)
 {
  Submit("volume", () => core.VolumeAsync(volume));
  return Pending;
 }
 public void PlayAll(TrackRow[] rows) => Submit("play-all", async () =>
 {
  var playable = rows.Where(row => row.CanUsePlatform).ToArray();
  if (playable.Length == 0) return;
  if (Program.Desktop && host != null)
  {
   await host.Adapter.PlayDesktopListAsync(playable, 0, host.Concept);
   return;
  }
  if (playable.Length + State.QueueEntries.Length > PlaybackQueueManager.MaxQueueSize)
   throw new InvalidOperationException("当前队列容纳不下整张歌单，可选中部分曲目入队");
  var first = await Admit(playable[0]);
  await core.QueueEntryAsync("play", first);
  DemandResult();
  foreach (var row in playable.Skip(1))
   await Admit(row);
 });
 public Task Seek(double seconds)
 {
  Submit("Seek", async () => { await core.SeekAsync(seconds); SampleProgress(force:true); });
  return Pending;
 }
 public void Submit(string name,Func<Task> action){if(M219RStartup.Active&&name!="stop")return;var prior=Pending;Pending=Run();async Task Run(){await prior;uiError="";var noticesBefore=failureNotices;bool transport=name is "play" or "play-all" or "next" or "previous";if(transport){IsBuffering=true;OnPropertyChanged(nameof(IsBuffering));}try{await action();DemandResult();}catch(Exception ex){uiError=name is "play" or "play-all" or "next" or "previous" or "toggle"?"暂时无法播放该歌曲，请稍后重试":"操作暂未完成，请重试";if(noticesBefore==failureNotices)Notify(uiError);Program.Log("R2_UI_FAILURE",new{name,type=ex.GetType().Name});}finally{if(transport){IsBuffering=false;OnPropertyChanged(nameof(IsBuffering));}Refresh(core.State);OnPropertyChanged(nameof(PlaybackFeedback));OnPropertyChanged(nameof(HasPlaybackError));Program.Log("R2_UI_READBACK",new{name,ok=uiError.Length==0&&state.Error.Length==0,state.Revision,hasError=state.Error.Length>0||uiError.Length>0,state.Playing,state.Paused,state.Position,state.LyricIndex,state.CurrentEntryId,queue=state.QueueEntries});}}}
 async Task<Guid> Admit(TrackRow row)
 {
  DemandPlayable(row);
  var before=core.State.QueueEntries.Select(e=>e.EntryId).ToHashSet();
  var trackId=row.Owner.Local?row.Track.Id:await host!.Concept.M223AdmitAsync(row.Track.Key);
  await core.QueueAsync("append",trackId);DemandResult();
  var added=core.State.QueueEntries.Where(e=>!before.Contains(e.EntryId)).ToArray();
  if(added.Length!=1||added[0].TrackId!=trackId)throw new InvalidOperationException("队列追加回读不符");
  return added[0].EntryId;
 }
 public void Play(TrackRow row)=>Submit("play",async()=>{DemandPlayable(row);var id=row.QueueId;if(id==Guid.Empty)id=await Admit(row);else if(DemandEntry(id).TrackId!=row.Track.Id)throw new InvalidOperationException("条目身份不匹配");await core.QueueEntryAsync("play",id);DemandResult();if(!core.State.Playing||core.State.CurrentEntryId!=id)throw new InvalidOperationException("音频未开始");});
 internal void PlayFromList(TrackRow row, TrackRow[] visible, bool search)
 {
  if(!Program.Desktop||host==null||row.QueueId!=Guid.Empty){Play(row);return;}
  if(search)
  {
   Submit("play",async()=>
   {
    var id=await InsertNextAsync(row);
    await core.QueueEntryAsync("play",id);
    DemandResult();
   });
   return;
  }
  var rows=visible.Where(item=>item.CanUsePlatform).ToArray();
  var start=Array.IndexOf(rows,row);
  Submit("play-all",()=>host.Adapter.PlayDesktopListAsync(rows,start,host.Concept));
 }
 private async Task<Guid> InsertNextAsync(TrackRow row)
 {
  DemandPlayable(row);
  var current=state.CurrentEntryId;
  bool Matches(TrackRow existing)=>existing.CatalogIdentity==row.CatalogIdentity;
  var id=row.QueueId;
  if(id==Guid.Empty)
  {
   var match=DisplayPlaybackQueue.FirstOrDefault(entry=>entry.QueueId!=current&&Matches(entry))
    ?? DisplayPlaybackQueue.FirstOrDefault(entry=>entry.QueueId==current&&Matches(entry));
   id=match?.QueueId??await Admit(row);
  }
  var entries=core.State.QueueEntries;
  if(id==current)return id;
  if(current!=Guid.Empty&&entries.Any(entry=>entry.EntryId==current))await core.QueueEntryAsync("next",id);
  else if(entries.Length>0&&entries[0].EntryId!=id)await core.QueueEntryAsync("before",id,entries[0].EntryId);
  DemandResult();
  return id;
 }
 public void Append(TrackRow[] rows)
 {
  if(Program.Desktop&&rows.Length==1){Next(rows);return;}
  Submit("append-batch",async()=>{foreach(var r in rows){DemandPlayable(r);if(r.QueueId!=Guid.Empty)DemandEntry(r.QueueId);}foreach(var row in rows)await Admit(row);});
 }
 public void Next(TrackRow[] rows)=>Submit("next-batch",async()=>
 {
  foreach(var r in rows){DemandPlayable(r);if(r.QueueId!=Guid.Empty)DemandEntry(r.QueueId);}
  foreach(var row in rows.Reverse())
  {
   if(Program.Desktop){await InsertNextAsync(row);continue;}
   var id=row.QueueId==Guid.Empty?await Admit(row):row.QueueId;
   await core.QueueEntryAsync("next",id);DemandResult();
  }
  if(Program.Desktop&&rows.Length>0)Toast?.Invoke(rows.Length==1?"已安排下一首 · "+rows[0].Name:$"已安排 {rows.Length} 首接续播放");
 });
 public void Remove(TrackRow[] rows)=>Submit("remove",async()=>{foreach(var row in rows){if(row.QueueId!=Guid.Empty){DemandEntry(row.QueueId);await core.QueueEntryAsync("remove",row.QueueId);}else{DemandTrack(row);if(row.MemberId<=0)throw new InvalidOperationException("不是本地歌单成员");await core.PlaylistEntryAsync("remove",row.ListId,row.MemberId);}DemandResult();}});
 public void Move(TrackRow row,TrackRow target)=>Submit("move",async()=>{if(row.QueueId!=Guid.Empty){DemandEntry(row.QueueId);DemandEntry(target.QueueId);await core.QueueEntryAsync("before",row.QueueId,target.QueueId);}else{DemandTrack(row);var list=state.Lists.Single(l=>l.Id==row.ListId);var index=Array.IndexOf(list.EntryIds??[],target.MemberId);if(row.MemberId<=0||index<0||row.ListId!=target.ListId)throw new InvalidOperationException("歌单条目已变化");await core.PlaylistEntryAsync("move",row.ListId,row.MemberId,index);}DemandResult();});
 public void AddMembership(long listId,TrackRow[] rows)=>Submit("local-add",async()=>{foreach(var row in rows)DemandTrack(row);foreach(var row in rows){await core.PlaylistAsync("add",listId:listId,songId:row.Track.Id);DemandResult();}});
 public void Favorite(TrackRow[] rows)=>Submit("favorite",async()=>
 {
  foreach(var row in rows)
  {
   if(row.IsProviderTrack)
   {
    if(host==null||!host.Concept.Connected)throw new UnauthorizedAccessException("请先登录酷狗");
    await host.Concept.ToggleDesktopFavoriteAsync(row.Track.Key,CancellationToken.None);
   }
   else { DemandTrack(row);await core.FavoriteAsync(row.Track.Id);DemandResult(); }
   row.RefreshFavorite();
  }
  if(host!=null){host.LocalWorkspace.RefreshFavoriteRows();host.KugouConceptWorkspace.RefreshFavoriteRows();}
  DisplayedPlayingSong?.RefreshFavorite();
 });
 void Step(bool next)=>Submit(next?"next":"previous",async()=>{if(state.CurrentId!=0)DemandEntry(state.CurrentEntryId);if(next)await core.NextAsync();else await core.PreviousAsync();DemandResult();});
 public void Dispose(){core.Changed-=Refresh;Updated=null;}
}
public partial class SongTableHeader:UserControl {public SongTableHeader(){InitializeComponent();}}
public partial class PlaylistCard:UserControl {public PlaylistCard(){InitializeComponent();}}
public sealed record PlaylistCardModel(string Title,string Description,Control Cover,ICommand OpenCommand,string AutomationId="",ICommand? PlayCommand=null)
{
 public bool CanPlay => PlayCommand != null;
}
public static class PreviewAssets {static Bitmap? placeholder;public static Bitmap Get()=>placeholder??=new Bitmap(AssetLoader.Open(new Uri("avares://HuayuanMusic.P17.Nightflight/Assets/r2-placeholder.png")));public static void Dispose(){placeholder?.Dispose();placeholder=null;}}
public partial class QueuePreview:UserControl {public QueuePreview(){InitializeComponent();AttachedToVisualTree+=(_,_)=>{if(DataContext is ShellModel shell)InteractionController.Attach(this.FindControl<ListBox>("QueueItems")!,shell.Workspace,shell.Player,true);};}}
