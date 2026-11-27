using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Automation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HuayuanPreview.R2;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 TextBlock? queueCountText;
 ContentControl? sidebarHost;ComboBox? modePicker;int? dragQueueIndex;Guid? dragQueueSong;Point queueDragOrigin;
 long ActiveListId=>core.State.Lists.FirstOrDefault(x=>x.Name==localList)?.Id??0;
 void RefreshMode(){if(modePicker!=null&&modePicker.SelectedIndex!=core.State.Mode)modePicker.SelectedIndex=core.State.Mode;}
 void ConfirmDanger(string message, Func<Task> action)
 {
  var confirm = new Button { Content = "确认操作", Classes = { "primary" } };
  var cancel = new Button { Content = "取消" };
  AutomationProperties.SetName(confirm, "确认操作");
  AutomationProperties.SetName(cancel, "取消");
  var content = new StackPanel { Spacing = DesignTokens.Space4, Children =
   { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, H(confirm, cancel) } };
  ShowPlaylistOperation(content, new("确认本地操作", 440, InitialFocus: cancel), confirm, cancel, action);
 }

 void PlaylistNameDialog(string op)
 {
  var id = ActiveListId;
  var title = op == "save" ? "保存当前队列为本地歌单" : "本地歌单名称";
  var input = new TextBox
  {
   Text = op == "rename" ? localList : op == "save" ? "队列 " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") : "新建歌单",
   MaxLength = 100
  };
  AutomationProperties.SetName(input, "本地歌单名称");
  var hint = new TextBlock { Text = "1–80字符；同名不覆盖；重复曲目按独立条目完整保存", Classes = { "muted" },
   TextWrapping = Avalonia.Media.TextWrapping.Wrap };
  var save = new Button { Content = "保存", Classes = { "primary" } };
  var cancel = new Button { Content = "取消" };
  AutomationProperties.SetName(save, "保存");
  AutomationProperties.SetName(cancel, "取消");
  var content = new StackPanel { Spacing = DesignTokens.Space4, Children = { input, hint, H(save, cancel) } };
  ShowPlaylistOperation(content, new(title, 480, InitialFocus: input), save, cancel,
   () => core.PlaylistAsync(op, input.Text ?? "", id), hint,
   () => Notify(op == "create" ? "已创建本地歌单" : op == "rename" ? "已重命名本地歌单" : "当前完整队列已保存为本地歌单"));
  input.SelectAll();
 }

 void ShowPlaylistOperation(Control content, ModalOptions options, Button confirm, Button cancel,
  Func<Task> action, TextBlock? feedback = null, Action? onSuccess = null)
 {
  var session = ModalOverlay.TryShow<bool>(this, content, options, () => content.IsEnabled);
  if (session == null) return;
  confirm.IsDefault = true;
  cancel.IsCancel = true;
  cancel.Click += (_, _) => session.Dismiss();
  confirm.Click += async (_, _) =>
  {
   if (!session.IsOpen || !content.IsEnabled) return;
   content.IsEnabled = false;
   try
   {
    RunCore(action);
    await PendingCoreOperations;
    if (!session.IsOpen) return;
    if (core.State.Error.Length == 0)
    {
     session.Complete(true);
     onSuccess?.Invoke();
    }
    else if (feedback != null) feedback.Text = core.State.Error;
   }
   catch (Exception error)
   {
    if (feedback != null) feedback.Text = error.Message;
   }
   finally
   {
    if (session.IsOpen)
    {
     content.IsEnabled = true;
     options.InitialFocus?.Focus();
    }
   }
  };
 }
 Control NightQueue(){dragQueueIndex=null;dragQueueSong=null;var p=V();p.Spacing=12;p.Children.Add(H(T("播放队列",19,false,true),NI("close","关闭队列",()=>SetQueue(false))));queueCountText=T($"{core.State.Queue.Length} 首 · 本地受控副本 · 上限1000",10,true);p.Children.Add(queueCountText);modePicker=new ComboBox{Name="QueueMode",ItemsSource=new[]{"列表循环","单曲循环","随机播放","顺序播放（末尾停止）"},SelectedIndex=core.State.Mode,HorizontalAlignment=HorizontalAlignment.Stretch};AutomationProperties.SetName(modePicker,"播放模式");modePicker.SelectionChanged+=(_,_)=>{if(modePicker.SelectedIndex>=0&&modePicker.SelectedIndex!=core.State.Mode)RunCore(()=>core.SetModeAsync(modePicker.SelectedIndex));};p.Children.Add(modePicker);
  p.Children.Add(H(NB("保存队列为歌单",()=>PlaylistNameDialog("save"),true),NB("清空",()=>ConfirmDanger("清空临时队列并停止；不删除永久歌单或音频",()=>core.QueueAsync("clear")))));
  queueCurrentArtwork=new ContentControl{Content=Cover(nowSong,40,40)};queueCurrentTitle=T(Track(nowSong).Title,12,false,true);queueCurrentArtist=T(Track(nowSong).Artist,10,true);p.Children.Add(H(queueCurrentArtwork,V(queueCurrentTitle,queueCurrentArtist)));
  queueRows.Clear();foreach(var row in QueueModels())queueRows.Add(row);var list=new ListBox{Name="C31Queue",Focusable=true,ItemsSource=queueRows,Height=narrow?260:350,Background=Avalonia.Media.Brushes.Transparent,BorderThickness=new(0),SelectedIndex=queueIndex};nativeQueue=list;
  list.ItemTemplate=new FuncDataTemplate<QueueDisplay>((e,_)=>{if(e==null)return T("");var row=new Grid{Tag=e,ColumnDefinitions=ColumnDefinitions.Parse("*,Auto"),MinHeight=52,ClipToBounds=false};var label=T((e.Index+1)+"  "+e.Title,11);label.Tag="QueueLabel";label.MaxWidth=narrow?112:150;ToolTip.SetTip(label,e.Title+" · "+e.Artist+" · "+e.Origin);label.Cursor=new Cursor(StandardCursorType.SizeAll);label.PointerPressed+=(_,x)=>StartQueueDrag(list,e,x);row.Children.Add(label);var nextItem=new MenuItem{Header="此条目下一首播放"};nextItem.Click+=(_,_)=>RunCore(()=>core.QueueEntryAsync("next",e.EntryId));row.ContextMenu=new ContextMenu{ItemsSource=new[]{nextItem}};var tools=H(NB("↑",()=>RunCore(()=>core.QueueEntryAsync("up",e.EntryId))),NB("↓",()=>RunCore(()=>core.QueueEntryAsync("down",e.EntryId))),NB("×",()=>RunCore(()=>core.QueueEntryAsync("remove",e.EntryId))));tools.Spacing=0;foreach(var button in tools.Children.OfType<Button>()){button.Padding=new(2);button.Width=32;button.MinWidth=32;button.Height=36;ToolTip.SetTip(button,button.Content?.ToString()=="×"?"从临时队列移除":"调整真实播放顺序");}Grid.SetColumn(tools,1);row.Children.Add(tools);var line=new Border{Tag="DropLine",Height=3,Background=R("Primary"),VerticalAlignment=VerticalAlignment.Top,IsVisible=false,IsHitTestVisible=false};Grid.SetColumnSpan(line,2);row.Children.Add(line);return row;},false);
  list.PointerMoved+=(_,e)=>MoveQueueDrag(list,e);list.AddHandler(PointerReleasedEvent,(_,e)=>EndQueueDrag(list,e),RoutingStrategies.Tunnel,true);list.PointerCaptureLost+=(_,e)=>{if(ReferenceEquals(e.Source,list))CancelQueueDrag();};list.DetachedFromVisualTree+=(_,_)=>CancelQueueDrag();
  list.DoubleTapped+=(_,e)=>{if(DateTime.UtcNow<ignoreQueueDoubleTap)return;if(list.SelectedItem is QueueDisplay item)RunCore(()=>core.QueueEntryAsync("play",item.EntryId));};list.KeyDown+=(_,e)=>{if(e.Key==Key.Escape&&dragQueueSong!=null){CancelQueueDrag();e.Handled=true;return;}if(list.SelectedItem is not QueueDisplay row)return;if(e.Key==Key.Enter){RunCore(()=>core.QueueEntryAsync("play",row.EntryId));e.Handled=true;}else if(e.KeyModifiers.HasFlag(KeyModifiers.Control)&&e.Key is Key.Up or Key.Down){RunCore(()=>core.QueueEntryAsync(e.Key==Key.Up?"up":"down",row.EntryId));e.Handled=true;}};
  var queueHost=new Grid{ClipToBounds=true};queueHost.Children.Add(list);queueDropIndicator=new Border{Height=4,Background=R("Primary"),VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,IsVisible=false};queueHost.Children.Add(queueDropIndicator);list.LayoutUpdated+=(_,_)=>RefreshQueueSemantics(list);p.Children.Add(queueHost);p.Children.Add(T("双击/Enter播放；拖动或↑↓排序；Ctrl+↑↓键盘排序",10,true));return new Border{Width=narrow?306:350,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Stretch,Margin=new(0,0,10,10),Padding=new(16),Background=R("Surface"),BorderBrush=R("Line"),BorderThickness=new(1),CornerRadius=new(16),Child=new ScrollViewer{Content=p}};
 }
}
