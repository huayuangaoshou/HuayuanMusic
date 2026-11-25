using Avalonia;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
namespace HuayuanPreview.R2;
// Adapted from I1-P1 and QueueMotion: only browser selection/sort/drag state lives here.
public sealed class InteractionController:ObservableObject {
 static readonly Dictionary<ListBox,InteractionController> attached=[];readonly ListBox list;readonly WorkspaceState owner;readonly CorePresentation player;readonly bool queue;
 TrackRow[] original=[];int sort;string field="";bool publishing;TrackRow? dragging,target;Point origin;bool moved;string[] snapshot=[];IPointer? pointer;DateTime ignoreDouble;
 readonly Border line=new(){Height=2,Background=Brushes.DodgerBlue,IsHitTestVisible=false,IsVisible=false,VerticalAlignment=VerticalAlignment.Top};
 public bool Editable=>!queue&&owner.Local&&owner.DetailPage!=null&&owner.Lists[owner.DetailPage]==list;public bool Dragging=>moved;public bool InsertionVisible=>line.IsVisible;public MenuFlyout? LastMenu {get;private set;}
 public string StatusText {get;private set;}="默认顺序 · 单击选择，双击播放";
 public ICommand SortTitleCommand {get;}public ICommand SortArtistCommand {get;}public ICommand SortAlbumCommand {get;}
 public RelayCommand LocateCurrentCommand {get;}
 private bool hasCurrentTrack;
 private static readonly TimeSpan ManualScrollPause = TimeSpan.FromSeconds(3);
 private readonly DispatcherTimer followTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
 private long? lastManualScroll;
 private int scrollRequest;
 private Vector? pendingOffset;
 private Action? scrollStep;
 internal sealed record ViewState(Vector Offset, HashSet<string> Selected);
 public bool HasCurrentTrack {get=>hasCurrentTrack;private set=>SetProperty(ref hasCurrentTrack,value);}
 private bool batchMode;
 public bool BatchMode {get=>batchMode;private set=>SetProperty(ref batchMode,value);}
 internal bool BatchDeletable {get;set;}
 public bool BatchAvailable => !queue && owner.Local && BatchDeletable;
 private int selectedCount;
 public int SelectedCount {get=>selectedCount;private set=>SetProperty(ref selectedCount,value);}
 public string SelectedCountLabel => $"已选 {selectedCount} 首";
 public ICommand ToggleBatchModeCommand {get;}
 public ICommand SelectAllCommand {get;}
 public ICommand DeleteSelectedCommand {get;}
 public ICommand ExitBatchCommand {get;}
 public bool AllSelected => selectedCount > 0 && selectedCount == Items().Length;
 public string SelectAllLabel => AllSelected ? "取消全选" : "全选";
 public static InteractionController Attach(ListBox list,WorkspaceState owner,CorePresentation player,bool queue){if(attached.TryGetValue(list,out var old))return old;var c=new InteractionController(list,owner,player,queue);attached.Add(list,c);return c;}
 public static InteractionController For(ListBox list)=>attached[list];
 internal static InteractionController? Find(ListBox list)=>attached.GetValueOrDefault(list);
 internal bool IsOnlineSearch {get;set;}
 internal void PlayRow(TrackRow row)=>player.PlayFromList(row,Items(),IsOnlineSearch);
 InteractionController(ListBox l,WorkspaceState o,CorePresentation p,bool q){list=l;owner=o;player=p;queue=q;SetStatus();l.SelectionMode=SelectionMode.Multiple;l.AutoScrollToSelectedItem=false;original=Items();
  SortTitleCommand=new RelayCommand(()=>Sort("title"));SortArtistCommand=new RelayCommand(()=>Sort("artist"));SortAlbumCommand=new RelayCommand(()=>Sort("album"));
  LocateCurrentCommand=new RelayCommand(()=>LocateCurrent(),()=>HasCurrentTrack);
  ToggleBatchModeCommand=new RelayCommand(()=>SetBatchMode(!BatchMode));
  SelectAllCommand=new RelayCommand(()=>{ if(AllSelected) list.SelectedItems?.Clear(); else list.SelectAll(); });
  DeleteSelectedCommand=new RelayCommand(()=>_=DeleteSelectedAsync());
  ExitBatchCommand=new RelayCommand(()=>SetBatchMode(false));
  l.PropertyChanged+=(_,e)=>{if(e.Property==ItemsControl.ItemsSourceProperty&&!publishing){original=Items();SyncBatchRows();}};
  l.SelectionChanged+=(_,_)=>SyncSelection();
  followTimer.Tick+=(_,_)=>scrollStep?.Invoke();
  l.AddHandler(InputElement.PointerWheelChangedEvent,(_,_)=>MarkManualScroll(),Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
  l.AddHandler(InputElement.PointerPressedEvent,(_,e)=>{if(OnScrollBar(e.Source))MarkManualScroll();},Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
  l.AddHandler(InputElement.PointerMovedEvent,(_,e)=>{if(e.GetCurrentPoint(l).Properties.IsLeftButtonPressed&&(e.Pointer.Type==PointerType.Touch||OnScrollBar(e.Source)))MarkManualScroll();},Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
  l.AddHandler(InputElement.KeyDownEvent,(_,e)=>{if(e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)MarkManualScroll();},Avalonia.Interactivity.RoutingStrategies.Tunnel,true);
  l.DetachedFromVisualTree+=(_,_)=>CancelFollow();
  l.DoubleTapped+=(_,e)=>{if(BatchMode||DateTime.UtcNow<ignoreDouble||FromButton(e.Source))return;var row=Hit(e.Source);if(row!=null&&(queue||row.CanUsePlatform))PlayRow(row);e.Handled=true;};
  l.KeyDown+=(_,e)=>{if(e.Key==Key.Escape){if(BatchMode){SetBatchMode(false);e.Handled=true;return;}Cancel();e.Handled=true;}else if(e.Key==Key.A&&e.KeyModifiers.HasFlag(KeyModifiers.Control)){l.SelectAll();e.Handled=true;}else if(e.Key==Key.Enter&&!FromButton(e.Source)){if(l.SelectedItem is TrackRow row&&(queue||row.CanUsePlatform))PlayRow(row);e.Handled=true;}else if(e.Key==Key.Delete&&(queue||Editable||BatchMode)){if(BatchMode)_=DeleteSelectedAsync();else player.Remove(Selected());e.Handled=true;}else if(e.KeyModifiers.HasFlag(KeyModifiers.Control)&&e.Key is Key.Up or Key.Down&&(queue||Editable)){MoveSelected(e.Key==Key.Up?-1:1);e.Handled=true;}};
  l.ContextRequested+=(_,e)=>{var row=Hit(e.Source);if(row==null)return;if(!Selected().Contains(row))l.SelectedItem=row;var selected=Selected();var menu=new MenuFlyout();LastMenu=menu;
   void Add(string label,Action action,bool enabled=true)=>menu.Items.Add(new MenuItem{Header=label,Command=new RelayCommand(action),IsEnabled=enabled});
   bool local=selected.Length>0&&selected.All(r=>r.Owner.Local&&!r.IsProviderTrack);
   if(selected.Length>0&&selected.All(r=>r.CanUsePlatform)){
    if(selected.Length==1)Add("立即播放",()=>PlayRow(selected[0]));
    Add("下一首播放",()=>player.Next(selected));
    Add(selected.Length==1&&row.IsFavorite?"取消红心":"收藏 / 红心",()=>player.Favorite(selected));
    Add("追加到播放队列",()=>player.Append(selected));
   }
   if (row.IsProviderTrack && owner.Host.KugouEnabled)
   {
    Add("查看歌手",()=>_ = owner.Host.ShowTrackCollectionAsync(row,true),!string.IsNullOrWhiteSpace(row.Singer));
    Add("查看专辑",()=>_ = owner.Host.ShowTrackCollectionAsync(row,false),!string.IsNullOrWhiteSpace(row.AlbumName));
   }
   if (row.CanUseOnlineActions && list.FindAncestorOfType<R2ShellView>() is { } shell)
   {
    Add("查看评论",()=>shell.OpenSongComments(row));
    if(selected.Length==1)Add(row.IsDownloaded?"已下载":"下载歌曲",()=>_=shell.DownloadSongAsync(row),!row.IsDownloaded);
   }
   void Copy(string label,string text) => menu.Items.Add(new MenuItem
   { Header=label, IsEnabled=!string.IsNullOrWhiteSpace(text), Command=new AsyncRelayCommand(()=>R2ShellView.CopyTextAsync(list,text)) });
   string title=row.Name;
   string artistPrefix=row.Singer+" - ";
   if(row.IsProviderTrack&&!string.IsNullOrWhiteSpace(row.Singer)&&title.StartsWith(artistPrefix,StringComparison.OrdinalIgnoreCase))
    title=title[artistPrefix.Length..];
   Copy("复制歌名",title);
   Copy("复制歌手",row.Singer);
   Copy("复制歌名 - 歌手",string.Join(" - ",new[]{title,row.Singer}.Where(text=>!string.IsNullOrWhiteSpace(text))));
   if(!row.IsProviderTrack)Add("打开文件所在位置",()=>RevealLocalFile(row));
   if(local){var target=new MenuItem{Header="添加到歌单"};foreach(var pl in player.State.Lists.Where(pl=>pl.IsCustom)){long id=pl.Id;target.Items.Add(new MenuItem{Header=pl.Name,Command=new RelayCommand(()=>player.AddMembership(id,selected))});}if(target.Items.Count==0){var create=new MenuItem{Header="+ 新建歌单"};create.Click+=async(_,_)=>await owner.Host.LocalWorkspace.EditLocalPlaylistAsync(list,0);target.Items.Add(create);}menu.Items.Add(target);
    if(!queue)Add(selected.Length==1?"从列表移除":"从列表移除",()=>_=owner.RequestRemoveTracksAsync(selected,list));
   }
   if(queue)Add("从播放队列移除",()=>player.Remove(selected));else if(Editable)Add("从本地歌单移除（不删除文件）",()=>_=owner.RequestRemoveTracksAsync(selected,list));
   if(queue||Editable){Add("上移",()=>MoveSelected(-1),sort==0);Add("下移",()=>MoveSelected(1),sort==0);}menu.ShowAt(list,true);e.Handled=true;};
  l.AddHandler(InputElement.PointerPressedEvent,(_,e)=>{if(!(queue||Editable)||sort!=0||FromButton(e.Source)||!e.GetCurrentPoint(l).Properties.IsLeftButtonPressed)return;var row=Hit(e.Source);if(row==null)return;Cancel();dragging=row;origin=e.GetPosition(l);snapshot=Items().Select(r=>r.Identity).ToArray();pointer=e.Pointer;},Avalonia.Interactivity.RoutingStrategies.Bubble,handledEventsToo:true);
  // Batch mode: a plain left click toggles the row's checkbox state (Ctrl/Shift keep
  // the default multi-select behaviour for range/interval selection).
  l.AddHandler(InputElement.PointerPressedEvent,(_,e)=>{if(!BatchMode||e.KeyModifiers!=KeyModifiers.None||FromButton(e.Source))return;if(!e.GetCurrentPoint(l).Properties.IsLeftButtonPressed)return;var row=Hit(e.Source);if(row==null||!row.CanRemoveLocally)return;ToggleRowSelection(row);e.Handled=true;},Avalonia.Interactivity.RoutingStrategies.Tunnel,handledEventsToo:true);
  l.PointerMoved+=(_,e)=>DragMove(e);l.PointerReleased+=(_,_)=>Commit();l.PointerCaptureLost+=(_,_)=>Cancel();l.DetachedFromVisualTree+=(_,_)=>Cancel();
  l.AttachedToVisualTree+=(_,_)=>{if(line.Parent==null&&l.Parent is Grid grid){Grid.SetRow(line,Grid.GetRow(l));grid.Children.Add(line);}};
 }
 static void RevealLocalFile(TrackRow row)
 {
  var path=row.Track.Key;
  if(!Path.IsPathFullyQualified(path)||!File.Exists(path))return;
  try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\""+path+"\"") { UseShellExecute=false }); }
  catch(System.ComponentModel.Win32Exception error) { Program.Log("R2_REVEAL_LOCAL_FAILED",new { type=error.GetType().Name }); }
 }
 TrackRow[] Items()=>list.Items.OfType<TrackRow>().ToArray();public TrackRow[] Selected(){var set=list.SelectedItems?.OfType<TrackRow>().ToHashSet()??[];return Items().Where(set.Contains).ToArray();}
 static bool FromButton(object? source)=>source is Button||source is Control c&&c.GetVisualAncestors().OfType<Button>().Any();
 static TrackRow? Hit(object? source){if(source is not Control c)return null;return (c as ListBoxItem??c.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault())?.Content as TrackRow;}
 public void SetRows(TrackRow[] rows){if(original.SequenceEqual(rows))return;original=rows;Display(Sorted());}
 void Display(TrackRow[] rows)
 {
  var state=CaptureView();
  publishing=true;
  try{list.ItemsSource=rows;}
  finally{publishing=false;}
  RestoreView(state);
 }
 private ScrollViewer? Scroller=>list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
 internal ViewState CaptureView()=>new(pendingOffset??Scroller?.Offset??default,
     list.SelectedItems?.OfType<TrackRow>().Select(row=>row.Identity).ToHashSet()??[]);
 internal void RestoreView(ViewState state)
 {
  CancelFollow();
  list.SelectedItems?.Clear();
  foreach(var row in list.Items.OfType<TrackRow>())
   if(state.Selected.Contains(row.Identity))list.SelectedItems?.Add(row);
  var request=scrollRequest;
  pendingOffset=state.Offset;
  Dispatcher.UIThread.Post(()=>{
   if(request!=scrollRequest)return;
   if(Scroller is { } scroll)scroll.Offset=state.Offset;
   pendingOffset=null;
  },DispatcherPriority.Loaded);
 }
 /// <summary>Recompute whether this list currently contains the playing track, and notify.</summary>
 public void RefreshCurrentState(){HasCurrentTrack=Items().Any(r=>r.IsCurrent);LocateCurrentCommand.NotifyCanExecuteChanged();}
 private static bool OnScrollBar(object? source)=>source is Visual visual &&
     (visual is ScrollBar||visual.FindAncestorOfType<ScrollBar>()!=null);
 private void MarkManualScroll()
 {
  lastManualScroll=Stopwatch.GetTimestamp();
  CancelFollow();
 }
 internal void CancelFollow()
 {
  scrollRequest++;
  pendingOffset=null;
  followTimer.Stop();
  scrollStep=null;
 }
 public void LocateCurrent()=>QueueFollow(ignoreManual:true);
 internal void FollowCurrent(bool firstOpen=false)=>QueueFollow(ignoreManual:firstOpen);
 private void QueueFollow(bool ignoreManual)
 {
  if(!ignoreManual&&lastManualScroll is { } manual&&Stopwatch.GetElapsedTime(manual)<ManualScrollPause)return;
  var entry=player.State.CurrentEntryId;
  if(entry==Guid.Empty)return;
  // Let a pending view restore finish before using its scroll offset as our starting point.
  var request=scrollRequest;
  Dispatcher.UIThread.Post(()=>{
   if(request!=scrollRequest||player.State.CurrentEntryId!=entry||!CanFollow()||Scroller is not { } scroll)return;
   int index=Array.FindIndex(Items(),row=>row.IsCurrent);
   if(index<0||scroll.Viewport.Height<=0)return;
   var pitch=queue?DesignTokens.QueueRowHeight:DesignTokens.SongRowHeight;
   var rowTop=index*pitch;
   if(list.ContainerFromIndex(index) is Control row&&row.TranslatePoint(default,scroll) is { } at)
    rowTop=scroll.Offset.Y+at.Y;
   var from=scroll.Offset.Y;
   var target=Math.Clamp(rowTop+(pitch-scroll.Viewport.Height)/2,0,Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height));
   if(Math.Abs(from-target)<1)return;
   CancelFollow();
   if(DesignTokens.ShaderDegrade)
   {
    scroll.Offset=new Vector(scroll.Offset.X,target);
    return;
   }
   var start=Stopwatch.GetTimestamp();
   var duration=TimeSpan.FromMilliseconds(DesignTokens.MotionOpen);
   scrollStep=()=>{
    if(player.State.CurrentEntryId!=entry||!CanFollow()){CancelFollow();return;}
    var t=Math.Clamp(Stopwatch.GetElapsedTime(start)/duration,0,1);
    scroll.Offset=new Vector(scroll.Offset.X,from+(target-from)*(1-Math.Pow(1-t,3)));
    if(t>=1)CancelFollow();
   };
   followTimer.Start();
  },DispatcherPriority.Loaded);
 }
 private bool CanFollow()
 {
  if(!list.IsEffectivelyVisible||!list.IsAttachedToVisualTree())return false;
  return list.FindAncestorOfType<R2ShellView>() is not { } shell || shell.IsPlaybackListExposed(queue);
 }
 TrackRow[] Sorted()=>sort==0?original:sort==1?original.OrderBy(r=>Value(r,field),StringComparer.CurrentCultureIgnoreCase).ToArray():original.OrderByDescending(r=>Value(r,field),StringComparer.CurrentCultureIgnoreCase).ToArray();
 static string Value(TrackRow r,string key)=>key=="artist"?r.Singer:key=="album"?r.AlbumName:r.Name;
 public void Sort(string key){sort=field==key?(sort+1)%3:1;field=key;Display(Sorted());SetStatus();}
 void SetStatus(){StatusText=sort==0?(queue||owner.Local||P2AWorkspace.M223Normal?"默认顺序 · 单击选择，双击播放":"默认顺序 · 单击选择 · 只读"):(field=="title"?"歌曲":field=="artist"?"歌手":"专辑")+(sort==1?"升序":"降序")+(queue||Editable?" · 恢复默认顺序后可拖动":" · 仅调整当前视图");OnPropertyChanged(nameof(StatusText));}
 void MoveSelected(int delta){if(sort!=0)return;var rows=Items();if(list.SelectedItem is not TrackRow row)return;int index=Array.IndexOf(rows,row);int to=Math.Clamp(index+delta,0,rows.Length-1);player.Move(row,rows[to]);}
 void SetBatchMode(bool enabled)
 {
  if(enabled&&!BatchAvailable)return;
  if(BatchMode==enabled)return;
  BatchMode=enabled;
  if(enabled){ Cancel(); list.SelectionMode=SelectionMode.Multiple; }
  else list.SelectedItems?.Clear();
  SyncBatchRows();
  SyncSelection();
  OnPropertyChanged(nameof(BatchAvailable));
 }
 void ToggleRowSelection(TrackRow row)
 {
  var selection=list.SelectedItems;
  if(selection==null)return;
  if(selection.Contains(row)) selection.Remove(row);
  else selection.Add(row);
 }
 void SyncSelection()
 {
  var selected=Selected().Select(r=>r.Identity).ToHashSet();
  foreach(var row in Items()){ var on=selected.Contains(row.Identity); if(row.IsSelected!=on) row.IsSelected=on; }
  SelectedCount=selected.Count;
  OnPropertyChanged(nameof(SelectedCountLabel));
  OnPropertyChanged(nameof(AllSelected));
  OnPropertyChanged(nameof(SelectAllLabel));
 }
 void SyncBatchRows(){ var on=BatchMode; foreach(var row in Items()) if(row.IsBatchMode!=on) row.IsBatchMode=on; }
 async Task DeleteSelectedAsync()
 {
  var rows=Selected();
  if(rows.Length==0)return;
  if(await owner.RequestRemoveTracksAsync(rows,list)) SetBatchMode(false);
 }
 public void Cancel(){dragging=null;target=null;moved=false;var old=pointer;pointer=null;old?.Capture(null);line.IsVisible=false;}
 void DragMove(PointerEventArgs e){if(dragging==null)return;var point=e.GetPosition(list);if(!moved){if(Math.Abs(point.Y-origin.Y)<7)return;moved=true;pointer?.Capture(list);ignoreDouble=DateTime.UtcNow.AddMilliseconds(350);}if(point.X<0||point.X>list.Bounds.Width||point.Y<0||point.Y>list.Bounds.Height){target=null;line.IsVisible=false;return;}var scroll=Scroller;if(scroll!=null&&(point.Y<28||point.Y>list.Bounds.Height-28)){MarkManualScroll();scroll.Offset=new Vector(scroll.Offset.X,Math.Clamp(scroll.Offset.Y+(point.Y<28?-16:16),0,Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height)));}
  target=null;line.IsVisible=false;foreach(var row in list.GetVisualDescendants().OfType<ListBoxItem>()){var at=row.TranslatePoint(default,list);if(at!=null&&point.Y>=at.Value.Y&&point.Y<at.Value.Y+row.Bounds.Height&&row.Content is TrackRow item){target=item;var down=Array.IndexOf(snapshot,item.Identity)>Array.IndexOf(snapshot,dragging.Identity);line.Margin=new Thickness(6,Math.Clamp(at.Value.Y+(down?row.Bounds.Height:0),0,list.Bounds.Height-2),6,0);line.IsVisible=item.Identity!=dragging.Identity;break;}}
 }
 void Commit(){var source=dragging;var to=target;bool valid=moved&&source!=null&&to!=null&&snapshot.SequenceEqual(Items().Select(r=>r.Identity));Cancel();if(valid&&source!.Identity!=to!.Identity)player.Move(source,to);}
}
