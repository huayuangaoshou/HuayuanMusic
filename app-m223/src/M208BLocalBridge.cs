using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using KugouAvaloniaPlayer.Services.Startup;

namespace HuayuanPreview;

// M2-08B P0: isolated proof that a visible song-list UI routes to the SINGLE
// production INightflightPlayback/CorePlaybackAdapter, never PreviewPlayer.
public sealed partial class PreviewWindow {
 Grid? bridgeSurface;
 ListBox? bridgeCatalog;
 StackPanel? bridgeQueue;
 TextBlock? bridgeStatus,bridgeNow;
 Button? bridgeAppend,bridgePlay,bridgeRemove,bridgeMove;
 Task bridgePending=Task.CompletedTask;
 bool bridgeAttached;
 public Task BridgePending=>bridgePending;
 static Button BridgeButton(string label,Action onClick){
  var button=new Button{Content=label,Margin=new Thickness(4),MinHeight=32};
  AutomationProperties.SetName(button,label);
  button.Click+=(_,_)=>onClick();
  return button;
 }
 void BridgeCheck(bool ok,string label,ref int checks){
  Program.Log("M208B_P0_CHECK",new{ok,label});
  if(!ok)throw new InvalidOperationException("M208B_P0_"+label);
  checks++;
 }
 public void AttachM208BLocalBridge(){
  if(bridgeAttached)throw new InvalidOperationException("Bridge already attached");
  if(Environment.GetEnvironmentVariable("M208B_LOCAL_BRIDGE")!="1"||
     !Path.GetFileName(OfflineStartupProfile.Current.Root).StartsWith("p17-body-r3-manual-test-",StringComparison.Ordinal))
   throw new UnauthorizedAccessException("Isolated synthetic test profile required");
  bridgeAttached=true;
  Title="HuayuanMusic · M2-08B P0 真实本地 Core 接线 · 隔离测试";
  var host=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),Background=Brushes.White};
  var header=new StackPanel{Spacing=5,Margin=new Thickness(16,12)};
  header.Children.Add(new TextBlock{Text="HuayuanMusic · 本地音乐",FontSize=22,Foreground=Brushes.Black});
  header.Children.Add(new TextBlock{Text="P0 隔离接线验证：实际 Core/BASS/SQLite，只有合成测试曲目；酷狗概念版尚未接入",FontSize=12,Foreground=Brushes.DarkSlateGray});
  Grid.SetRow(header,0);host.Children.Add(header);
  var body=new Grid{ColumnDefinitions=new ColumnDefinitions("*,*"),Margin=new Thickness(12)};
  var left=new StackPanel{Spacing=8};
  left.Children.Add(new TextBlock{Text="本地曲库（单击选择，不改变播放队列）",FontSize=17});
  bridgeCatalog=new ListBox{MinHeight=240,ItemsSource=core.State.Tracks};
  left.Children.Add(bridgeCatalog);
  var buttons=new WrapPanel();
  bridgeAppend=BridgeButton("追加到队尾",()=>BridgeAction("append",async()=>{if(SelectedBridgeTrack() is { } t)await core.QueueAsync("append",t.Id);}));
  bridgePlay=BridgeButton("播放所选且保留队列",()=>BridgeAction("play-single",async()=>{
    if(SelectedBridgeTrack() is not { } t)return;
    await core.QueueAsync("append",t.Id);
    if(core.State.Error.Length!=0)return;
    var entry=core.State.QueueEntries.LastOrDefault();
    if(entry is null || entry.EntryId==Guid.Empty)throw new InvalidOperationException("No queue entry to play");
    await core.QueueEntryAsync("play",entry.EntryId);
  }));
  buttons.Children.Add(bridgeAppend!);buttons.Children.Add(bridgePlay!);
  left.Children.Add(buttons);
  var right=new StackPanel{Spacing=7};
  right.Children.Add(new TextBlock{Text="唯一真实播放队列（按独立 QueueEntryId）",FontSize=17});
  var queueScroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=480};
  bridgeQueue=new StackPanel{Spacing=3};queueScroll.Content=bridgeQueue;right.Children.Add(queueScroll);
  var actions=new WrapPanel();
  bridgeRemove=BridgeButton("移除队列末条",()=>BridgeAction("remove-one",async()=>{
    var entry=core.State.QueueEntries.LastOrDefault();
    if(entry is not null && entry.EntryId!=Guid.Empty)await core.QueueEntryAsync("remove",entry.EntryId);
  }));
  bridgeMove=BridgeButton("末条上移",()=>BridgeAction("move-up",async()=>{
    var entry=core.State.QueueEntries.LastOrDefault();
    if(entry is not null && entry.EntryId!=Guid.Empty)await core.QueueEntryAsync("up",entry.EntryId);
  }));
  actions.Children.Add(bridgeRemove!);actions.Children.Add(bridgeMove!);
  right.Children.Add(actions);
  body.Children.Add(left);Grid.SetColumn(right,1);body.Children.Add(right);
  Grid.SetRow(body,1);host.Children.Add(body);
  var footer=new StackPanel{Spacing=4,Margin=new Thickness(15)};
  bridgeNow=new TextBlock{Text="当前：无",FontSize=15};
  bridgeStatus=new TextBlock{Text="就绪",FontSize=12};
  footer.Children.Add(bridgeNow);footer.Children.Add(bridgeStatus);
  Grid.SetRow(footer,2);host.Children.Add(footer);
  bridgeSurface=host;Content=host;
  bridgeCatalog.SelectedItem=core.State.Tracks.FirstOrDefault();
  core.Changed+=OnBridgeCoreChanged;
  Closed+=(_,_)=>core.Changed-=OnBridgeCoreChanged;
  UpdateBridgeState(core.State);
 }
 CoreTrack? SelectedBridgeTrack()=>bridgeCatalog?.SelectedItem as CoreTrack;
 void BridgeAction(string name,Func<Task> action){
  var previous=bridgePending;
  bridgePending=RunAsync();
  async Task RunAsync(){
   await previous;
   try{
    await action();
    Program.Log("M208B_P0_UI_CORE",new{name,core.State.Revision,core.State.Error,queue=core.State.QueueEntries.Length,current=core.State.CurrentEntryId});
   }catch(Exception e){Program.Log("M208B_P0_UI_ERROR",new{name,type=e.GetType().Name});if(bridgeStatus!=null)bridgeStatus.Text="UI 错误："+e.GetType().Name;}
   finally{UpdateBridgeState(core.State);}
  }
 }
 void OnBridgeCoreChanged(PlaybackSnapshot state){
  if(bridgeAttached)Avalonia.Threading.Dispatcher.UIThread.Post(()=>UpdateBridgeState(state));
 }
 void UpdateBridgeState(PlaybackSnapshot state){
  if(!bridgeAttached||bridgeQueue==null||bridgeCatalog==null)return;
  if(!ReferenceEquals(bridgeCatalog.ItemsSource,state.Tracks)){
   var prior=SelectedBridgeTrack()?.Id;
   bridgeCatalog.ItemsSource=state.Tracks;
   bridgeCatalog.SelectedItem=state.Tracks.FirstOrDefault(x=>x.Id==prior)??state.Tracks.FirstOrDefault();
  }
  bridgeQueue.Children.Clear();
  foreach(var entry in state.QueueEntries){
   var track=state.Tracks.FirstOrDefault(t=>t.Id==entry.TrackId);
   bridgeQueue.Children.Add(new TextBlock{
    Text=$"{entry.EntryId.ToString("N")[..8]} | {track?.Title??"无效歌曲"}"+(entry.EntryId==state.CurrentEntryId?"  ▶":""),
    FontSize=13,Foreground=Brushes.Black
   });
  }
  if(bridgeNow!=null){var current=state.Tracks.FirstOrDefault(t=>t.Id==state.CurrentId);
   bridgeNow.Text=$"当前：{current?.Title??"无"} | 正在播放：{state.Playing} | 队列：{state.QueueEntries.Length}"; }
  if(bridgeStatus!=null)bridgeStatus.Text=state.Error.Length==0?"UI → CorePlaybackAdapter → BASS/SQLite → Snapshot 回读":"错误："+state.Error;
 }
 public async Task M208BLocalBridgeSelfTest(){
  var checks=0;
  try{
   BridgeCheck(OfflineGuiApplication.SelfTest&&bridgeAttached,"admitted_synthetic_bridge",ref checks);
   var initialIds=core.State.QueueEntries.Select(e=>e.EntryId).ToArray();
   Program.Log("M208B_P0_START",new{initialIds,noAutoPlay=!core.State.Playing});
   BridgeCheck(!core.State.Playing,"startup_does_not_autoplay",ref checks);
   BridgeCheck(ReferenceEquals(core,core)&&bridgeQueue!=null&&bridgeCatalog!=null,"one_core_snapshot",ref checks);
   var file=Path.Combine(Program.Root,"fixtures","formats","plain.wav");
   if(core.State.Tracks.Length==0)await core.ImportFilesAsync([file],CancellationToken.None);
   BridgeCheck(core.State.Tracks.Length>0&&core.State.Error.Length==0,"production_core_import_fixture",ref checks);
   UpdateBridgeState(core.State);
   bridgeCatalog!.SelectedIndex=0;
   var id=SelectedBridgeTrack()!.Id;
   var starting=core.State.QueueEntries.ToArray();
   bridgeAppend!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await bridgePending;
   BridgeCheck(core.State.QueueEntries.Length==starting.Length+1&&core.State.Error.Length==0,"ui_button_to_production_append",ref checks);
   bridgeAppend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await bridgePending;
   var q=core.State.QueueEntries;
   BridgeCheck(q.Length==starting.Length+2&&q.Select(e=>e.EntryId).Distinct().Count()==q.Length,"duplicate_entries_are_distinct",ref checks);
   var before=q.Select(e=>e.EntryId).ToArray();
   bridgeMove!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await bridgePending;
   BridgeCheck(core.State.QueueEntries.Length==before.Length&&core.State.QueueEntries[^2].EntryId==before[^1],"ui_order_move_up_real_core",ref checks);
   bridgeRemove!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await bridgePending;
   BridgeCheck(core.State.QueueEntries.Length==before.Length-1&&core.State.QueueEntries.All(e=>e.EntryId!=before[^2]),"ui_remove_one_occurrence_not_all",ref checks);
   await core.VolumeAsync(0);
   bridgePlay!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await bridgePending;
   BridgeCheck(core.State.QueueEntries.Length==before.Length&&core.State.CurrentId==id&&core.State.Playing&&core.State.Error.Length==0,"ui_play_with_real_BASS_no_queue_clear",ref checks);
   await core.StopAsync();
   BridgeCheck(!core.State.Playing,"real_core_stop",ref checks);
   Program.Log("M208B_P0_COMPLETE",new{checks,coreType=core.GetType().Name,tracks=core.State.Tracks.Length,queue=core.State.QueueEntries.Length,finalIds=core.State.QueueEntries.Select(e=>e.EntryId).ToArray(),singleCore=true,realProvider=false});
  }finally{Close();}
 }
}
