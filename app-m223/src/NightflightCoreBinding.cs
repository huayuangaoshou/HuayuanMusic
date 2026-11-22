using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 readonly INightflightPlayback core;PlaybackSnapshot? displayed;
 bool coreUiDeferred;long deferredCoreStates,coreResumeCount;
 bool CanPresentCore=>IsVisible&&WindowState!=WindowState.Minimized;
 void ResumeCorePresentation(){if(!CanPresentCore||!coreUiDeferred)return;coreUiDeferred=false;coreResumeCount++;ApplyCoreState(core.State);UpdateQueueSurface();}
 public Task PendingCoreOperations{get;private set;}=Task.CompletedTask;
 void RunCore(Func<Task> operation){var previous=PendingCoreOperations;PendingCoreOperations=Run();async Task Run(){await previous;try{await operation();if(core.State.Error.Length>0)Notify(KugouAvaloniaPlayer.Services.Startup.OfflineGuiApplication.SelfTest?core.State.Error:"播放未完成，请检查网络或歌曲权限后重试。");}catch(Exception e){Program.Log("UI_COMMAND_ERROR",e.GetType().Name);Notify("操作失败："+e.GetType().Name);}}}
 void CoreSeek(double target)=>RunCore(()=>core.SeekAsync(target));void CoreVolume(double value)=>RunCore(()=>core.VolumeAsync(value));
 Button CoreSelectedPlayButton(bool available=true){var b=NB("▷ 播放",()=>Play(selectedSong),true);b.Name="CorePlaySelected";b.IsEnabled=available;AutomationProperties.SetName(b,"播放选中隔离歌曲");return b;}
 Button CoreStopButton(){var b=NI("pause","停止播放",()=>RunCore(core.StopAsync));b.Name="CoreStop";b.Opacity=.62;ToolTip.SetTip(b,"停止播放");b.Content=new Avalonia.Controls.Shapes.Rectangle{Width=12,Height=12,Stroke=R("Text"),StrokeThickness=1.6};return b;}
 Button CoreUnavailableButton(string label){var b=NB(label,()=>{});b.IsEnabled=false;return b;}
 bool IsInteractiveKeySource(object? source)=>source is Control c&&c.GetVisualAncestors().Append(c).Any(x=>x is Button or TabControl or TextBox or Slider or MenuItem or ListBox or ListBoxItem);
 internal void ApplyCoreState(PlaybackSnapshot state){if(R2Enabled)return;
  // A disabled/minimized renderer retains dirty visuals until a presentation is possible.
  // Keep the authoritative adapter state live; defer only control creation and visual updates.
  if(!CanPresentCore){coreUiDeferred=true;deferredCoreStates++;return;}
  var previous=displayed;displayed=state;if(previous?.CurrentId!=state.CurrentId||previous.Playing!=state.Playing)Program.Log("UI_STATE_APPLIED",new{state.CurrentId,state.Playing,state.Position,state.Revision});if(!ReferenceEquals(previous?.Tracks,state.Tracks))songs=state.Tracks.Select(t=>new Song(t.Id,t.Title,t.Artist,t.Album,(int)Math.Round(t.Duration),t.Key.StartsWith("kugou-concept:")?((core as CorePlaybackAdapter)?.Kugou.SdkConnected==true?"酷狗 SDK":"酷狗 Mock"):"本地",t.Key.StartsWith("kugou-concept:")?(core as CorePlaybackAdapter)?.Kugou.Account??"":"")).ToArray();
  nowSong=state.CurrentId;if(!songs.Any(x=>x.Id==selectedSong))selectedSong=songs.FirstOrDefault()?.Id??0;
  if((previous?.CurrentId!=state.CurrentId||previous?.CurrentEntryId!=state.CurrentEntryId)&&seek is CommitSlider editing)editing.CancelPreview();
  playing=state.Playing;seconds=state.Position;volume=state.Volume;
  if(!ReferenceEquals(previous?.Queue,state.Queue)){queue.Clear();queue.AddRange(state.Queue.Select(id=>new QueueEntry(id,Track(id).Origin,Track(id).Account)));}queueIndex=Array.FindIndex(state.QueueEntries,x=>x.EntryId==state.CurrentEntryId);
  favorites.Clear();foreach(var id in state.Favorites)favorites.Add(id);history.Clear();history.AddRange(state.History);
  if(!ReferenceEquals(previous?.Lists,state.Lists)){localLists.Clear();foreach(var list in state.Lists)localLists[list.Name]=list.SongIds.Select(id=>new QueueEntry(id,Track(id).Origin,Track(id).Account)).ToList();if(!localLists.ContainsKey(localList))localList=localLists.Keys.FirstOrDefault()??"";}
  if(!ReferenceEquals(previous?.Lyrics,state.Lyrics))LyricLines=state.Lyrics.Select(l=>l.Text).ToArray();
  bool tracksChanged=!ReferenceEquals(previous?.Tracks,state.Tracks);bool currentChanged=previous?.CurrentId!=state.CurrentId||previous?.CurrentEntryId!=state.CurrentEntryId;bool queueChanged=previous==null||!previous.QueueEntries.SequenceEqual(state.QueueEntries);
  if(previous==null){Render();}else{
   if(tracksChanged&&page is not ("平台" or "平台歌单")&&nightBody!=null&&(mainSongs==null||page=="本地歌单"||nightTab=="歌单"))nightBody.Content=NightPage();
   if(tracksChanged||(page=="收藏"&&!previous.Favorites.SequenceEqual(state.Favorites))||(page=="历史"&&!previous.History.SequenceEqual(state.History)))RefreshRows();
   if(currentChanged||!previous.Lyrics.SequenceEqual(state.Lyrics))RefreshCurrentPage();else UpdatePlayback();
   if(queueOpen&&queueChanged)UpdateQueueSurface();else if(queueOpen&&currentChanged)RefreshQueueCurrent();
  }
  if(previous!=null&&!ReferenceEquals(previous.Lists,state.Lists)&&sidebarHost!=null)sidebarHost.Content=NightSidebar(narrow||page=="正在播放");
  RefreshMode();RefreshBarState();if(state.Error.Length>0)Notify(KugouAvaloniaPlayer.Services.Startup.OfflineGuiApplication.SelfTest?state.Error:"播放未完成，请检查网络或歌曲权限后重试。");else if(previous?.Error.Length>0)Notify("已恢复：受控本地播放可用");
 }
}
