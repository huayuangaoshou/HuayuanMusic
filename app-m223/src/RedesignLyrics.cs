using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 Color Mix(Color a,Color b,double t)=>Color.FromRgb((byte)(a.R*(1-t)+b.R*t),(byte)(a.G*(1-t)+b.G*t),(byte)(a.B*(1-t)+b.B*t));
 IBrush Mood(){if(HighContrast||!MaterialsEnabled)return R("Base");var c=Color.Parse(ArtColors[(nowSong-1)%8]);var b=((ISolidColorBrush)R("Base")).Color;return Gradient(Mix(b,c,dark?.30:.22).ToString(),Mix(b,c,.06).ToString());}
 Control C3Lyrics(){
  c3LyricButtons.Clear();c3ActiveLyric=-1;var track=Track(nowSong);
  var title=V(T("NOW LISTENING / 模拟歌词 · 32秒一句",10,true,true),T(track.Title,gallery?23:28,false,true),T(track.Artist+" / "+track.Album,12,true));title.Spacing=8;
  var lines=new StackPanel{Spacing=12};
  for(int i=0;i<LyricLines.Length;i++){var idx=i;var b=C3TextButton(LyricLines[i],()=>{seconds=idx*32;UpdatePlayback();Notify("已定位模拟歌词 "+Time((int)seconds));},gallery?28:24);b.MinHeight=62;b.Padding=new(16,8);b.HorizontalContentAlignment=gallery?HorizontalAlignment.Center:HorizontalAlignment.Left;b.HorizontalAlignment=HorizontalAlignment.Stretch;b.Background=Brushes.Transparent;b.CornerRadius=new(4);StyleMotion(b);c3LyricButtons.Add(b);lines.Children.Add(b);}
  c3LyricViewport=new ScrollViewer{Content=lines,Height=gallery?(narrow?225:280):(narrow?290:350),VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Name="LyricViewport"};
  if(MotionEnabled)c3LyricViewport.Transitions=new Transitions{new VectorTransition{Property=ScrollViewer.OffsetProperty,Duration=DesignTokens.MotionSpatialDefault.Duration,Easing=DesignTokens.MotionSpatialDefault.Curve}};
  var toggle=C3TextButton(c31CoverOnly?"显示歌词":"专注封面",()=>{c31CoverOnly=!c31CoverOnly;Render();});var toggleRow=H(toggle,C3TextButton("返回资料库",()=>Go("本地音乐")));toggleRow.HorizontalAlignment=gallery?HorizontalAlignment.Center:HorizontalAlignment.Left;
  Control content;
  if(c31CoverOnly){var focus=V(Cover(nowSong,narrow?280:360,narrow?280:360),title,toggleRow);focus.HorizontalAlignment=HorizontalAlignment.Center;content=focus;}
  else if(gallery){
   var poster=H(Cover(nowSong,narrow?86:118,narrow?86:118),title);poster.HorizontalAlignment=HorizontalAlignment.Center;poster.Spacing=22;
   var theatre=V(poster,new Border{Height=8},c3LyricViewport,toggleRow);theatre.Spacing=20;theatre.MaxWidth=850;theatre.HorizontalAlignment=HorizontalAlignment.Center;content=theatre;
  }else{
   var artSize=narrow?224:310;var left=V(Cover(nowSong,artSize,artSize),title,toggleRow);left.Spacing=22;left.VerticalAlignment=VerticalAlignment.Center;
   var lyric=V(T("同步歌词",12,true,true),c3LyricViewport,T("点击歌词定位 · 自动跟随当前句",11,true));lyric.Spacing=20;lyric.VerticalAlignment=VerticalAlignment.Center;
   var split=new Grid{ColumnDefinitions=ColumnDefinitions.Parse($"{artSize+8},*"),ColumnSpacing=narrow?24:52,Margin=new(8,14)};split.Children.Add(left);Grid.SetColumn(lyric,1);split.Children.Add(lyric);content=split;
  }
  var frame=new Border{Background=Mood(),CornerRadius=new(22),Padding=new Thickness(narrow?18:30,24),Child=content,MinHeight=narrow?400:480};
  C3RefreshLyricHighlight();return frame;
 }
 void DonorRefreshLyricHighlight(){
  if(page!="正在播放"||c3LyricButtons.Count!=LyricLines.Length)return;var idx=(int)(seconds/32)%LyricLines.Length;if(idx==c3ActiveLyric)return;
  for(int i=0;i<c3LyricButtons.Count;i++){var active=i==idx;var b=c3LyricButtons[i];b.Content=LyricLines[i];AutomationProperties.SetName(b,(active?"当前歌词 ":"歌词 ")+(i+1)+" "+LyricLines[i]);b.FontWeight=active?FontWeight.Bold:FontWeight.Normal;b.Foreground=R(active?"Text":"Sub");b.Background=Brushes.Transparent;b.BorderThickness=new(active&&!gallery?2:0,0,0,0);b.BorderBrush=active?PearlBrush():Brushes.Transparent;Move(b,active?-1:0,1);}
  c3ActiveLyric=idx;var viewport=c3LyricViewport;Dispatcher.UIThread.Post(()=>{
   if(page!="正在播放"||c31CoverOnly||viewport==null||!ReferenceEquals(viewport,c3LyricViewport)||c3ActiveLyric!=idx)return;
   var line=c3LyricButtons[idx];var point=line.TranslatePoint(default,(Control)viewport.Content!);if(point!=null)viewport.Offset=new Vector(0,Math.Max(0,point.Value.Y-viewport.Bounds.Height*.33));
  },DispatcherPriority.Loaded);
 }
 Control C3QueuePanel(){
  var header=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,Auto")};header.Children.Add(V(T("接下来播放",22,false,true),T($"{queue.Count:000} TRACKS / 临时模拟队列",10,true,true)));var close=IconButton("close","关闭播放队列",()=>SetQueue(false));Grid.SetColumn(close,1);header.Children.Add(close);
  var song=Track(nowSong);var current=Panel(H(Cover(nowSong,52,52),V(T("正在播放",10,true,true),T(song.Title,13,false,true),T(song.Artist,11,true))),10,true);
  var display=queue.Select((q,i)=>new QueueDisplay(i,q.SongId,q.Origin,q.Account,Track(q.SongId).Title,Track(q.SongId).Artist,Track(q.SongId).Duration)).ToArray();
  var list=new ListBox{ItemsSource=display,Height=narrow?260:310,SelectedIndex=Math.Clamp(queueIndex,0,Math.Max(0,queue.Count-1)),Background=Brushes.Transparent,BorderThickness=new(0),Name="C31Queue"};
  list.ItemTemplate=new FuncDataTemplate<QueueDisplay>((x,_)=>{if(x==null)return T("无效曲目",12,true);var row=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("34,*,26"),ColumnSpacing=9,MinHeight=52};row.Children.Add(Cover(x.SongId,32,32));var text=V(T(x.Title,12,false,true),T(x.Origin+(x.Account.Length>0?" / "+x.Account:" · "+x.Artist),10,true));text.Spacing=3;Grid.SetColumn(text,1);row.Children.Add(text);var t=T((x.Index+1).ToString("00"),9,true);Grid.SetColumn(t,2);row.Children.Add(t);return row;},true);
  list.DoubleTapped+=(_,_)=>{if(list.SelectedItem is QueueDisplay q){queueIndex=q.Index;Play(q.SongId);}};
  var actions=H(C3TextButton("播放选中",()=>{if(list.SelectedItem is QueueDisplay q){queueIndex=q.Index;Play(q.SongId);}}),C3TextButton("移除选中",()=>{if(list.SelectedItem is QueueDisplay q){queue.RemoveAt(q.Index);queueIndex=Math.Clamp(queueIndex,0,Math.Max(0,queue.Count-1));Render();}}));
  return new ScrollViewer{Content=V(header,current,T("后续歌曲 / 来源按账号保留",11,true,true),list,actions,B("保存当前队列为本地歌单",SaveQueue,true),T("仅保存为本次预览的内存快照。\n永久保存尚未接入，不会写回官方账号。",10,true)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
 }
}
