using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HuayuanPreview;

internal sealed class QueueDisplay(int index,int songId,string origin,string account,string title,string artist,int duration,Guid entryId=default){public Guid EntryId{get;}=entryId;public int Index{get;set;}=index;public int SongId{get;}=songId;public string Origin{get;}=origin;public string Account{get;}=account;public string Title{get;}=title;public string Artist{get;}=artist;public int Duration{get;}=duration;}

public sealed partial class PreviewWindow {
 bool? c3ShownPlay;
 Control LegacyC3Icon(string name,bool primary=false){
  var data=name switch{
   "play"=>"M8 4L20 12L8 20Z", "pause"=>"M8 5V19 M16 5V19",
   "prev"=>"M5 5V19 M19 5L8 12L19 19Z", "next"=>"M19 5V19 M5 5L16 12L5 19Z",
   "logo"=>"M5 5V19 M19 5V19 M5 12Q12 4 19 12 M5 12Q12 20 19 12",
   "home"=>"M3 11L12 3L21 11 M5 10V21H10V15H14V21H19V10",
   "music"=>"M10 18V5L20 3V16 M10 18C10 23 2 23 2 18C2 14 10 14 10 18 M20 16C20 21 12 21 12 17C12 13 20 12 20 16",
   "album"=>"M4 4H20V20H4Z M12 8A4 4 0 1 0 12 16A4 4 0 1 0 12 8 M12 11V13",
   "heart"=>"M12 21C8 18 2 13 2 8C2 3 8 2 12 6C16 2 22 3 22 8C22 13 16 18 12 21Z",
   "history"=>"M3 10A9 9 0 1 1 5 19 M3 4V10H9 M12 7V12L16 15",
   "playlist"=>"M3 5H21 M3 11H21 M3 17H14", _=>"M5 5H11V11H5Z M15 5H21V11H15Z M5 15H11V21H5Z M15 15H21V21H15Z"
  };
  return new Avalonia.Controls.Shapes.Path{Data=Geometry.Parse(name=="logo"&&c31LogoVariant?"M5 7V19 M19 7V19 M5 13H19 M12 2L15 6L12 10L9 6Z":data),Stroke=primary?(HighContrast?Brushes.Black:(name=="logo"&&dark?Brush.Parse("#111624"):Brushes.White)):R("Text"),StrokeThickness=1.7,StrokeLineCap=PenLineCap.Round,StrokeJoin=PenLineJoin.Round,Width=22,Height=22,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
 }
 readonly List<Button> c3LyricButtons=new();
 int c3ActiveLyric=-1;
 ScrollViewer? c3LyricViewport;

 Control LegacyC3Top(){
  var grid=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"76,*,Auto":"208,*,Auto"),
   ColumnSpacing=12,Margin=new Thickness(18,11,22,7)};
  var mark=new Border{Width=31,Height=31,Background=R("Primary"),
   CornerRadius=new CornerRadius(9),Child=C3Icon("logo",true)};
  var brand=narrow?H(mark):H(mark,T("HUAYUAN MUSIC",16,false,true));
  brand.Spacing=9;
  Grid.SetColumn(brand,0);grid.Children.Add(brand);
  var search=new TextBox{PlaceholderText="搜索本地音乐、歌手、专辑",Text=query,
   Width=narrow?220:330,FontSize=13,CornerRadius=new CornerRadius(11)};
  search.TextChanged+=(_,_)=>query=search.Text??"";
  search.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){page="本地音乐";Render();Notify("仅搜索本地曲库："+query);}};
  var actions=H(C3TextButton("‹",()=>Notify("预览暂无历史返回栈"),18),
    C3TextButton("›",()=>Notify("预览暂无历史前进栈"),18),search);
  Grid.SetColumn(actions,1);grid.Children.Add(actions);
  var right=H(T("C3.1 · PREVIEW",11,true),
    C3TextButton("设置",()=>Go("设置")));
  Grid.SetColumn(right,2);grid.Children.Add(right);
  return Plain(grid);
 }
 Button C3TextButton(string label,Action action,int fontSize=13){
  var b=Quiet(label,action);
  b.FontSize=fontSize;
  b.MinHeight=30;
  b.Foreground=R("Text");
  b.BorderThickness=new Thickness(0);
  AutomationProperties.SetName(b,label);
  return b;
 }
 Border C3Tile(Control content,double rounding=12,bool emphasized=false)=>new(){
  Child=content,Background=emphasized?R("Selected"):CeramicBrush(),
  BorderBrush=emphasized?PearlBrush():R("Line"),BorderThickness=new Thickness(.7),
  CornerRadius=new CornerRadius(rounding),Padding=new Thickness(12)
 };
 Control LegacyC3Home(){
  var songsToShow=history.Concat(new[]{4,7,8,12,15,18,20}).Distinct().Take(6).ToArray();
  var hero=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,230"),MinHeight=158,ColumnSpacing=15};
  var info=V(
   T("现在，听点喜欢的",24,false,true),
   T("你的音乐，随时接着播放。",13,true),
   H(B("▶  继续播放",()=>Play(nowSong),true),
     C3TextButton("打开本地曲库  ›",()=>Go("本地音乐"))));
  info.VerticalAlignment=VerticalAlignment.Center;
  info.Spacing=12;Grid.SetColumn(info,0);hero.Children.Add(info);
  var album=H(Cover(nowSong,100,100),
   V(T("最近听到",11,true),T(Track(nowSong).Title,15,false,true),
     T(Track(nowSong).Artist,12,true),
     C3TextButton("查看歌词  ›",()=>Go("正在播放"),12)));
  Grid.SetColumn(album,1);hero.Children.Add(album);
  var heroPanel=new Border{
   Child=hero,Padding=new Thickness(18,18),
   Background=Gradient(dark?"#222B43":"#E9EDFE",dark?"#302E48":"#F4E9F5"),
   CornerRadius=new CornerRadius(16)
  };
  var recentTitle=H(T("最近播放",19,false,true),C3TextButton("全部历史  ›",()=>Go("历史"),12));
  var recent=new StackPanel{Orientation=Orientation.Horizontal,Spacing=13};
  foreach(var id in songsToShow){
   int songId=id;
   var track=Track(id);
   var art=Cover(songId,97,97);
   var item=V(art,T(track.Title,13,false,true),T(track.Artist,11,true));
   item.Width=113;item.Spacing=6;
   var button=new Button{Content=item,Background=Brushes.Transparent,BorderThickness=new Thickness(0),
    Padding=new Thickness(3),HorizontalContentAlignment=HorizontalAlignment.Left};
   AutomationProperties.SetName(button,"播放 "+track.Title);
   button.Click+=(_,_)=>Play(songId);
   StyleMotion(button);recent.Children.Add(button);
  }
  var historyScroll=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
    VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=recent};
  var playlistTitle=H(T("我的常用歌单",19,false,true),C3TextButton("管理歌单  ›",()=>Go("本地歌单"),12));
  var grid=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,*,*"),RowDefinitions=RowDefinitions.Parse("Auto,Auto"),
   ColumnSpacing=12,RowSpacing=10};
  int index=0;
  foreach(var name in localLists.Keys.Take(4).ToArray()){
   var captured=name;
   int coverId=localLists[name].FirstOrDefault()?.SongId??1;
   var contents=H(Cover(coverId,52,52),
     V(T(name,14,false,true),T(localLists[name].Count+" 首歌曲  ·  本地",11,true)));
   contents.Spacing=10;
   var button=new Button{Content=contents,Background=R("Surface"),
    BorderThickness=new Thickness(.6),BorderBrush=R("Line"),
    CornerRadius=new CornerRadius(11),Padding=new Thickness(8),
    HorizontalContentAlignment=HorizontalAlignment.Left};
   AutomationProperties.SetName(button,name+" 本地歌单");
   button.Click+=(_,_)=>{localList=captured;Go("本地歌单");};
   StyleMotion(button);button.HorizontalAlignment=HorizontalAlignment.Stretch;
   button.MinHeight=65;
   Grid.SetRow(button,index/3);Grid.SetColumn(button,index%3);grid.Children.Add(button);
   index++;
  }
  return V(heroPanel,new Border{Height=4},recentTitle,historyScroll,
    new Border{Height=3},playlistTitle,grid);
 }

 Control LegacyC3PlayerBar(){
  var bar=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"205,*,154":"275,*,258"),
    ColumnSpacing=8,Margin=new Thickness(18,10,18,4)};
  var id=nowSong;
  var music=V(T(Track(id).Title,14,false,true),T(Track(id).Artist,11,true));music.Spacing=2;
  var left=H(Cover(id,46,46),music);
  left.Spacing=11;
  Grid.SetColumn(left,0);bar.Children.Add(left);
  playButton=B("",TogglePlay,true,40);
  playButton.Content=C3Icon(playing?"pause":"play",true);
  c3ShownPlay=playing;
  playButton.CornerRadius=new CornerRadius(24);
  playButton.Width=42;playButton.Height=42;
  AutomationProperties.SetName(playButton,playing?"暂停":"播放");
  var prev=C3TextButton("",()=>Skip(-1),18);prev.Content=C3Icon("prev");AutomationProperties.SetName(prev,"上一首");
  var next=C3TextButton("",()=>Skip(1),18);next.Content=C3Icon("next");AutomationProperties.SetName(next,"下一首");
  var controls=H(prev,playButton,next);
  controls.HorizontalAlignment=HorizontalAlignment.Center;
  nowTime=T(Time((int)seconds),11,true);
  var duration=T(Time(Track(id).Duration),11,true);
  seek=new Slider{Minimum=0,Maximum=Track(nowSong).Duration,Value=seconds,
     Width=narrow?175:Math.Max(300,Width*.30),VerticalAlignment=VerticalAlignment.Center};
  AutomationProperties.SetName(seek,"播放进度");
  seek.PropertyChanged+=(_,e)=>{
   if(e.Property==RangeBase.ValueProperty&&!seekUpdating&&seek!=null&&Math.Abs(seconds-seek.Value)>.5){
    seconds=seek.Value;UpdatePlaybackText();C3RefreshLyricHighlight();
   }
  };
  var progress=H(nowTime,seek,duration);
  progress.HorizontalAlignment=HorizontalAlignment.Center;
  var center=V(controls,progress);center.Spacing=1;
  Grid.SetColumn(center,1);bar.Children.Add(center);
  var like=C3TextButton("",()=>Like(nowSong),17);like.Content=C3Icon("heart");AutomationProperties.SetName(like,favorites.Contains(id)?"取消收藏":"收藏");
  var right=H(
   like,
   C3TextButton("歌词",()=>Go("正在播放")),
   C3TextButton(queueOpen?"关闭队列":"队列",()=>{ToggleQueue();}));
  if(!narrow){
   var volumeSlider=new Slider{Minimum=0,Maximum=1,Value=volume,Width=66};
   AutomationProperties.SetName(volumeSlider,"音量");
   volumeSlider.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty)volume=volumeSlider.Value;};
   right.Children.Insert(1,volumeSlider);
  }
  right.HorizontalAlignment=HorizontalAlignment.Right;
  Grid.SetColumn(right,2);bar.Children.Add(right);
  return new Border{Child=bar,Background=GlassBrush(),
    BorderBrush=R("Line"),BorderThickness=new Thickness(0,1,0,0),
    Padding=new Thickness(0,4)};
 }
 Control LegacyC3Lyrics(){
  c3LyricButtons.Clear();
  c3ActiveLyric=-1;
  var track=Track(nowSong);
  var full=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"200,*":"310,*"),
    ColumnSpacing=narrow?16:34,Margin=new Thickness(16,14,18,12)};
  var coverSize=narrow?175:256;
  var art=V(Cover(nowSong,coverSize,coverSize),
    T(track.Title,24,false,true),T(track.Artist+"  ·  "+track.Album,13,true),
    T("封面与歌词均为原创演示内容",11,true));
  art.Spacing=12;
  Grid.SetColumn(art,0);full.Children.Add(art);
  var heading=H(T("同步歌词",13,true,true),T("模拟歌词 · 可点击定位",11,true));
  var lines=V();lines.Spacing=13;
  for(int i=0;i<LyricLines.Length;i++){
   var idx=i;
   var button=C3TextButton(LyricLines[i],()=>{
    seconds=idx*32;
    UpdatePlayback();
    Notify("已定位歌词："+LyricLines[idx]);
   },18);
   button.HorizontalAlignment=HorizontalAlignment.Stretch;
   button.HorizontalContentAlignment=HorizontalAlignment.Left;
   button.Padding=new Thickness(14,16);
   button.CornerRadius=new CornerRadius(9);
   c3LyricButtons.Add(button);
   lines.Children.Add(button);
  }
  c3LyricViewport=new ScrollViewer{Content=lines,
   VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=narrow?305:350};
  if(MotionEnabled)c3LyricViewport.Transitions=new Avalonia.Animation.Transitions{new Avalonia.Animation.VectorTransition{Property=ScrollViewer.OffsetProperty,Duration=TimeSpan.FromMilliseconds(220),Easing=new Avalonia.Animation.Easings.CubicEaseOut()}};
  var lyric=V(heading,c3LyricViewport,
   H(C3TextButton("← 返回曲库",()=>Go("本地音乐")),
     C3TextButton("播放队列  ›",()=>{ToggleQueue();})));
  lyric.Spacing=13;
  Grid.SetColumn(lyric,1);full.Children.Add(lyric);
  var ambient=new Border{Child=full,
   Background=Gradient(dark?"#232942":"#EBEFFC",dark?"#242438":"#F7F1FA"),
   CornerRadius=new CornerRadius(20),Padding=new Thickness(10,14,10,12)};
  C3RefreshLyricHighlight();
  var switcher=C3TextButton(c31CoverOnly?"显示歌词":"专注封面",()=>{c31CoverOnly=!c31CoverOnly;Render();});
  if(c31CoverOnly){lyric.IsVisible=false;Grid.SetColumnSpan(art,2);art.HorizontalAlignment=HorizontalAlignment.Center;}
  art.Children.Add(switcher);return ambient;
 }
 void LegacyC3RefreshLyricHighlight(){
  if(page!="正在播放"||c3LyricButtons.Count!=LyricLines.Length)return;
  var idx=(int)(seconds/32)%LyricLines.Length;
  if(idx==c3ActiveLyric)return;
  for(int i=0;i<c3LyricButtons.Count;i++){
   var active=i==idx;
   var b=c3LyricButtons[i];
   b.Content=(active?"♪   ":"     ")+LyricLines[i];
   AutomationProperties.SetName(b,(string)b.Content!);
   b.FontSize=20;b.Opacity=1;Move(b,active?4:0,1);
   b.FontWeight=active?FontWeight.Bold:FontWeight.Medium;
   b.Foreground=R(active?"Text":"Sub");
   b.Background=active?R("Selected"):Brushes.Transparent;
   b.BorderBrush=active?PearlBrush():Brushes.Transparent;b.BorderThickness=new Thickness(0,0,0,active?1:0);
  }
  c3ActiveLyric=idx;
  var viewport=c3LyricViewport;
  if(viewport!=null){
   var scrollTo=Math.Max(0,idx*71-115);
   Avalonia.Threading.Dispatcher.UIThread.Post(()=>{
    if(page=="正在播放" && ReferenceEquals(c3LyricViewport,viewport) && c3ActiveLyric==idx){
     viewport.Offset=new Vector(0,scrollTo);
    }
   });
  }
 }
 Control LegacyC3QueuePanel(){
  var header=H(T("接下来播放",19,false,true),C3TextButton("关闭",()=>{SetQueue(false);}));
  var current=queue.Count>0?queue[Math.Clamp(queueIndex,0,queue.Count-1)]:null;
  Control currently=current is null?
    T("队列为空，先从本地曲库加入歌曲",13,true):
    H(Cover(current.SongId,54,54),
      V(T(Track(current.SongId).Title,14,false,true),
        T(current.Origin+(current.Account.Length==0?"":" · "+current.Account),11,true)));
  var display=queue.Select((entry,index)=>
    new QueueDisplay(index,entry.SongId,entry.Origin,entry.Account,
     Track(entry.SongId).Title,Track(entry.SongId).Artist,Track(entry.SongId).Duration)).ToArray();
  var list=new ListBox{ItemsSource=display,
    Height=narrow?280:390,Background=Brushes.Transparent,
    BorderThickness=new Thickness(0),SelectedIndex=Math.Clamp(queueIndex,0,Math.Max(0,queue.Count-1))};
  list.ItemTemplate=new FuncDataTemplate<QueueDisplay>((item,_)=>
  {
   if(item is null)return T("无效歌曲",12,true);
   var content=H(Cover(item.SongId,40,40),V(
    T(item.Title,12,false,true),
    T(item.Artist+" · "+item.Origin+(item.Account.Length>0?" / "+item.Account:""),10,true)));
   content.Spacing=8;content.Margin=new Thickness(1,3);
   return content;
  },true);
  list.Name="C31Queue";list.DoubleTapped+=(_,_)=>{
   if(list.SelectedItem is QueueDisplay item){
    queueIndex=item.Index;Play(item.SongId);
   }
  };
  var actions=H(C3TextButton("播放选中",()=>{
    if(list.SelectedItem is QueueDisplay item){queueIndex=item.Index;Play(item.SongId);}
   }),C3TextButton("移除选中",()=>{
     if(list.SelectedItem is QueueDisplay item&&item.Index<queue.Count){
      queue.RemoveAt(item.Index);queueIndex=Math.Clamp(queueIndex,0,Math.Max(0,queue.Count-1));
      Render();Notify("已从临时队列移除歌曲，不影响来源歌单");
     }
   }));
  var content=V(
    header,
    B("保存当前队列为本地歌单",SaveQueue,true),
    T("保存到本地歌单，不会写回任何音乐平台账号。",11,true),
    T("正在播放",12,true,true),
    currently,
    T("下一首及后续  ·  "+queue.Count+" 首",12,true,true),
    list,
    actions);
  content.Spacing=11;
  return new ScrollViewer{
    Content=content,
    VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
    HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled
  };
 }
}
