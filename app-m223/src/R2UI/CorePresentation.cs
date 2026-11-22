using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
namespace HuayuanPreview.R2;
// All arrays below are read-only projections of PlaybackSnapshot, never mutable playback owners.
public sealed class TrackRow:ObservableObject {
 public CoreTrack Track {get;}public WorkspaceState Owner {get;}public Guid QueueId {get;}public long MemberId {get;}public long ListId {get;}
 public string Identity=>QueueId!=Guid.Empty?"q:"+QueueId:MemberId>0?$"m:{ListId}:{MemberId}":Owner.Local?$"t:{Track.Id}":Track.Key;
 public bool IsProviderTrack=>Track.Key.StartsWith("kugou-concept:",StringComparison.Ordinal);
 public string SourceLabel=>IsProviderTrack?"酷狗概念版":"本地音乐";
 public TrackRow Song=>this;public string Name=>Track.Title;public string Singer=>Track.Artist;public string AlbumName=>Track.Album;public string Duration=>Track.Duration<=0?"—":$"{(int)Track.Duration/60:00}:{(int)Track.Duration%60:00}";
 public bool IsPlaying=>Owner.Host.Player.State.CurrentEntryId==QueueId&&QueueId!=Guid.Empty&&Owner.Host.Player.State.Playing;
 public string RemoveLabel=>"移除队列条目 "+QueueId.ToString("N")[..8];public string QueuePlayLabel=>"播放队列条目 "+QueueId.ToString("N")[..8];
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
 public TrackRow(CoreTrack track,WorkspaceState owner,Guid queueId=default,long memberId=0,long listId=0){Track=track;Owner=owner;QueueId=queueId;MemberId=memberId;ListId=listId;PlayCommand=new RelayCommand(()=>{if(Owner.Local||P2AWorkspace.M223Normal)Owner.Host.Player.Play(this);
  else if(P2AWorkspace.M213RowMode)Owner.Host.Concept.M213Submit(Track.Key,true);
  else Owner.Host.Concept.M209Submit(Track.Key,true);});
 AddCommand=new RelayCommand(()=>{if(Owner.Local||P2AWorkspace.M223Normal)Owner.Host.Player.Append([this]);
  else if(P2AWorkspace.M213RowMode)Owner.Host.Concept.M213Submit(Track.Key,false);
  else Owner.Host.Concept.M209Submit(Track.Key,false);});
 InspectCommand=new RelayCommand(()=>Owner.Host.Concept.M212InspectMember(ListId,MemberId,Track.Key));}
 public void Refresh()=>OnPropertyChanged(nameof(IsPlaying));
 public void RefreshAccess()=>OnPropertyChanged(nameof(CanUsePlatform));
}
public sealed class CorePresentation:ObservableObject,IDisposable {
 readonly INightflightPlayback core;readonly Dictionary<Guid,TrackRow> queueViews=[];ShellModel? host;PlaybackSnapshot state;
 public PlaybackSnapshot State=>state;public Task Pending {get;private set;}=Task.CompletedTask;
 public TrackRow[] DisplayPlaybackQueue {get;private set;}=[];public int DisplayPlaybackQueueCount=>state.QueueEntries.Length;
 public TrackRow? DisplayedPlayingSong=>DisplayPlaybackQueue.FirstOrDefault(r=>r.QueueId==state.CurrentEntryId);
 public string ActiveLyric=>state.LyricIndex>=0&&state.LyricIndex<state.Lyrics.Length
  &&(state.Playing||state.Paused)?state.Lyrics[state.LyricIndex].Text:"";
 public string ProgressText=>$"{FormatSeconds(state.Position)} / {FormatSeconds(state.Duration)}";
 public string TransportStatus=>state.Playing?"播放中":state.Paused?"已暂停":"已停止";
 static string FormatSeconds(double value){var seconds=(int)Math.Max(0,value);return $"{seconds/60:00}:{seconds%60:00}";}
 public bool IsPlayingAudio=>state.Playing;public string PlaybackFeedback=>state.Error.Length>0?state.Error:uiError.Length>0?uiError:"播放队列 · "+state.QueueEntries.Length;
 string uiError="";public string UiError=>uiError;public event Action? Updated;
 public ICommand ClearQueueCommand {get;}public ICommand PlaySongCommand {get;}public ICommand RemoveFromQueueCommand {get;}public ICommand PlayPreviousCommand {get;}public ICommand PlayNextCommand {get;}public ICommand TogglePlayPauseCommand {get;}public ICommand StopCommand {get;}
 public CorePresentation(INightflightPlayback core){this.core=core;state=core.State;
  ClearQueueCommand=new RelayCommand(()=>Submit("clear",()=>core.QueueAsync("clear")));
  PlaySongCommand=new RelayCommand<TrackRow>(r=>{if(r!=null)Play(r);});RemoveFromQueueCommand=new RelayCommand<TrackRow>(r=>{if(r!=null)Remove([r]);});
  PlayPreviousCommand=new RelayCommand(()=>Step(false));PlayNextCommand=new RelayCommand(()=>Step(true));StopCommand=new RelayCommand(()=>Submit("stop",()=>P2AWorkspace.M213RowMode&&host!=null?host.Concept.M216StopAsync():core.StopAsync()));
  TogglePlayPauseCommand=new RelayCommand(()=>Submit("toggle",async()=>{if(state.CurrentEntryId==Guid.Empty)throw new InvalidOperationException("请先选择歌曲");DemandEntry(state.CurrentEntryId);await core.ToggleAsync(state.CurrentId);DemandResult();}));
  core.Changed+=Refresh;
 }
 public void Attach(ShellModel shell){host=shell;Refresh(core.State);}
 void Refresh(PlaybackSnapshot next){
  if(next.Revision<state.Revision)return;bool queueChanged=!state.QueueEntries.SequenceEqual(next.QueueEntries)||!ReferenceEquals(state.Tracks,next.Tracks);state=next;
  if(host!=null&&(queueChanged||DisplayPlaybackQueue.Length!=next.QueueEntries.Length)){
   var ids=next.QueueEntries.Select(e=>e.EntryId).ToHashSet();foreach(var id in queueViews.Keys.Where(k=>!ids.Contains(k)).ToArray())queueViews.Remove(id);
   DisplayPlaybackQueue=next.QueueEntries.Select(e=>{if(!queueViews.TryGetValue(e.EntryId,out var row)){var track=next.Tracks.Single(t=>t.Id==e.TrackId);row=new TrackRow(track,host.LocalWorkspace,e.EntryId);queueViews.Add(e.EntryId,row);}return row;}).ToArray();OnPropertyChanged(nameof(DisplayPlaybackQueue));
  }
  OnPropertyChanged(nameof(ProgressText));OnPropertyChanged(nameof(ActiveLyric));OnPropertyChanged(nameof(TransportStatus));
  OnPropertyChanged(nameof(State));foreach(var row in DisplayPlaybackQueue)row.Refresh();OnPropertyChanged(nameof(DisplayedPlayingSong));OnPropertyChanged(nameof(DisplayPlaybackQueueCount));OnPropertyChanged(nameof(IsPlayingAudio));OnPropertyChanged(nameof(PlaybackFeedback));Updated?.Invoke();
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
 public Task Seek(double seconds)
 {
  Submit("Seek", () => core.SeekAsync(seconds));
  return Pending;
 }
 public void Submit(string name,Func<Task> action){if(M219RStartup.Active&&name!="stop")return;var prior=Pending;Pending=Run();async Task Run(){await prior;uiError="";try{await action();DemandResult();}catch(Exception ex){uiError=ex.Message;Program.Log("R2_UI_FAILURE",new{name,type=ex.GetType().Name});}finally{Refresh(core.State);Program.Log("R2_UI_READBACK",new{name,ok=uiError.Length==0&&state.Error.Length==0,state.Revision,hasError=state.Error.Length>0||uiError.Length>0,state.Playing,state.Paused,state.Position,state.LyricIndex,state.CurrentEntryId,queue=state.QueueEntries});}}}
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
 public void Append(TrackRow[] rows)=>Submit("append-batch",async()=>{foreach(var r in rows){DemandPlayable(r);if(r.QueueId!=Guid.Empty)DemandEntry(r.QueueId);}foreach(var row in rows)await Admit(row);});
 public void Next(TrackRow[] rows)=>Submit("next-batch",async()=>{foreach(var r in rows){DemandPlayable(r);if(r.QueueId!=Guid.Empty)DemandEntry(r.QueueId);}foreach(var row in rows.Reverse()){var id=row.QueueId==Guid.Empty?await Admit(row):row.QueueId;await core.QueueEntryAsync("next",id);DemandResult();}});
 public void Remove(TrackRow[] rows)=>Submit("remove",async()=>{foreach(var row in rows){if(row.QueueId!=Guid.Empty){DemandEntry(row.QueueId);await core.QueueEntryAsync("remove",row.QueueId);}else{DemandTrack(row);if(row.MemberId<=0)throw new InvalidOperationException("不是本地歌单成员");await core.PlaylistEntryAsync("remove",row.ListId,row.MemberId);}DemandResult();}});
 public void Move(TrackRow row,TrackRow target)=>Submit("move",async()=>{if(row.QueueId!=Guid.Empty){DemandEntry(row.QueueId);DemandEntry(target.QueueId);await core.QueueEntryAsync("before",row.QueueId,target.QueueId);}else{DemandTrack(row);var list=state.Lists.Single(l=>l.Id==row.ListId);var index=Array.IndexOf(list.EntryIds??[],target.MemberId);if(row.MemberId<=0||index<0||row.ListId!=target.ListId)throw new InvalidOperationException("歌单条目已变化");await core.PlaylistEntryAsync("move",row.ListId,row.MemberId,index);}DemandResult();});
 public void AddMembership(long listId,TrackRow[] rows)=>Submit("local-add",async()=>{foreach(var row in rows)DemandTrack(row);foreach(var row in rows){await core.PlaylistAsync("add",listId:listId,songId:row.Track.Id);DemandResult();}});
 public void Favorite(TrackRow[] rows)=>Submit("favorite",async()=>{foreach(var row in rows){DemandTrack(row);await core.FavoriteAsync(row.Track.Id);DemandResult();}});
 void Step(bool next)=>Submit(next?"next":"previous",async()=>{if(state.CurrentId!=0)DemandEntry(state.CurrentEntryId);if(next)await core.NextAsync();else await core.PreviousAsync();DemandResult();});
 public void Dispose(){core.Changed-=Refresh;Updated=null;}
}
public partial class SongRowView:UserControl {public SongRowView(){InitializeComponent();}}
public partial class SongTableHeader:UserControl {public SongTableHeader(){InitializeComponent();}public SongTableHeader(bool readOnly):this(){this.FindControl<TextBlock>("ActionHeading")!.IsVisible=!readOnly;}}
public partial class PlaylistCard:UserControl {public PlaylistCard(){InitializeComponent();}}
public sealed record PlaylistCardModel(string Title,string Description,Control Cover,ICommand OpenCommand,string AutomationId="");
public static class PreviewAssets {static Bitmap? placeholder;public static Bitmap Get()=>placeholder??=new Bitmap(AssetLoader.Open(new Uri("avares://HuayuanMusic.P17.Nightflight/Assets/r2-placeholder.png")));public static void Dispose(){placeholder?.Dispose();placeholder=null;}}
public partial class QueuePreview:UserControl {public QueuePreview(){InitializeComponent();AttachedToVisualTree+=(_,_)=>{if(DataContext is ShellModel shell)InteractionController.Attach(this.FindControl<ListBox>("QueueItems")!,shell.Workspace,shell.Player,true);};}}
