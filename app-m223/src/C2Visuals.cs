using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HuayuanPreview;

public sealed partial class PreviewWindow {
 readonly List<Button> c2LyricButtons = new();
 int c2LastLyricIndex = -1;
 void C2RefreshLyricHighlight(){
  if(page!="正在播放"||c2LyricButtons.Count!=LyricLines.Length)return;
  var index=(int)(seconds/32)%LyricLines.Length;
  if(index==c2LastLyricIndex)return;
  for(int i=0;i<c2LyricButtons.Count;i++){
   var active=i==index;
   var button=c2LyricButtons[i];
   button.Content=(active?"♫   ":"     ")+LyricLines[i];
   AutomationProperties.SetName(button,(string)button.Content!);
   button.FontWeight=active?FontWeight.Bold:FontWeight.Normal;
   button.FontSize=active?22:19;
   button.Foreground=active?R("Text"):R("Sub");
  }
  c2LastLyricIndex=index;
 }

 static readonly string[] C2A={"#304B79","#395985","#6C4D87","#314B61","#55619A","#5D577F","#384A6D","#7C577B"};
 static readonly string[] C2B={"#778FE0","#55B8C1","#D795C8","#77B6B3","#AD8CEB","#CCABCE","#82A0DE","#F4B7A9"};
 string[] LyricLines=[];
 LinearGradientBrush Gradient(string top,string bottom){
  if(HighContrast){top="#000000";bottom="#000000";}
  return new LinearGradientBrush{
   StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),
   EndPoint=new RelativePoint(1,1,RelativeUnit.Relative),
   GradientStops=new GradientStops{new GradientStop(Color.Parse(top),0),new GradientStop(Color.Parse(bottom),1)}
  };
 }
 Control LegacyCover(int songId,double width=54,double height=54,bool subtle=false){
  if(c31MissingCover||songId<=0)return CoverPlaceholder(width,height);
  var ix=(songId-1)%C2A.Length;
  var g=new Grid{Width=width,Height=height,ClipToBounds=true};
  var back=new Border{Background=Gradient(C2A[ix],C2B[ix]),CornerRadius=new CornerRadius(width>=100?14:8)};
  g.Children.Add(back);
  g.Children.Add(new Ellipse{Width=width*.76,Height=height*.76,Stroke=new SolidColorBrush(Colors.White,subtle?.16:.30),
   StrokeThickness=Math.Max(1,width*.025),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,
   Margin=new Thickness(0,-height*.12,-width*.18,0)});
  g.Children.Add(new Ellipse{Width=width*.40,Height=height*.40,Stroke=new SolidColorBrush(Colors.White,.24),
   StrokeThickness=Math.Max(1,width*.015),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,
   Margin=new Thickness(-width*.12,0,0,-height*.06)});
  var center=new Border{Width=width*.46,Height=height*.46,CornerRadius=new CornerRadius(width*.24),
   Background=new SolidColorBrush(Colors.White,.17),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
   Child=new TextBlock{Text=Track(songId).Album.Substring(0,Math.Min(2,Track(songId).Album.Length)),FontSize=width*.17,FontWeight=FontWeight.Bold,Foreground=Brushes.White,
   HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
  g.Children.Add(center);
  return g;
 }
 Border Plain(Control child,int padding=0)=>new(){Child=child,Padding=new Thickness(padding),Background=Brushes.Transparent};
 Button Quiet(string label,Action work){
  var b=B(label,work);
  b.Background=Brushes.Transparent;b.BorderThickness=new Thickness(0);
  b.Padding=new Thickness(10,7);
  return b;
 }
 Control C2Top(){
  var g=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"76,*,Auto":"208,*,Auto"),
    ColumnSpacing=12,Margin=new Thickness(18,12,22,8)};
  var logo=H(T("◉",22,false,true),T("HUAYUAN",18,false,true));
  if(narrow)logo=H(T("◉",24,false,true));
  Grid.SetColumn(logo,0);g.Children.Add(logo);
  var search=new TextBox{PlaceholderText="搜索本地音乐、歌手、专辑",Text=query,
    Width=narrow?220:330,FontSize=13,CornerRadius=new CornerRadius(12)};
  search.TextChanged+=(_,_)=>query=search.Text??"";
  search.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){page="本地音乐";Render();Notify("仅搜索本地曲库："+query);}};
  var nav=H(Quiet("‹",()=>Notify("当前预览未实现页面历史")),
    Quiet("›",()=>Notify("当前预览未实现页面历史")),search);
  Grid.SetColumn(nav,1);g.Children.Add(nav);
  var other=H(T("C3 · PREVIEW",11,true),Quiet("⚙",()=>Go("设置")));
  Grid.SetColumn(other,2);g.Children.Add(other);
  return Plain(g);
 }
 Control LegacyC2Sidebar(){
  var items=V();
  items.Spacing=4;
  if(narrow){
   items.Children.Add(C2SideButton("⌂","首页",()=>Go("首页"),page=="首页"));
   items.Children.Add(C2SideButton("♫","曲库",()=>Go("本地音乐"),page=="本地音乐"));
   items.Children.Add(C2SideButton("▤","歌单",()=>Go("本地歌单"),page=="本地歌单"));
   items.Children.Add(C2SideButton("♥","收藏",()=>Go("收藏"),page=="收藏"));
   foreach(var p in platforms.Values.Where(x=>x.Enabled)){
    var k=p.Key;
    items.Children.Add(C2SideButton(p.Name.Substring(0,1),p.Name,()=>OpenPlatform(k),page.StartsWith("平台")&&provider==k));
   }
  }else{
   items.Children.Add(T("你的音乐",11,true,true));
   items.Children.Add(C2SideButton("⌂","首页",()=>Go("首页"),page=="首页"));
   items.Children.Add(C2SideButton("♫","本地歌曲",()=>Go("本地音乐"),page=="本地音乐"));
   items.Children.Add(C2SideButton("▧","歌手与专辑",()=>Go("歌手专辑"),page=="歌手专辑"));
   items.Children.Add(C2SideButton("♥","我喜欢的",()=>Go("收藏"),page=="收藏"));
   items.Children.Add(C2SideButton("◷","播放历史",()=>Go("历史"),page=="历史"));
   items.Children.Add(new Border{Height=13});
   items.Children.Add(H(T("本地歌单",11,true,true),Quiet("＋",()=>Go("本地歌单"))));
   foreach(var name in localLists.Keys.Take(6).ToArray()){
    var copy=name;
    items.Children.Add(C2SideButton("▤",name,()=>{localList=copy;Go("本地歌单");},
       page=="本地歌单"&&localList==name));
   }
   items.Children.Add(new Border{Height=13});
   items.Children.Add(H(T("音乐平台",11,true,true),Quiet("＋",()=>Go("扩展管理"))));
   foreach(var p in platforms.Values.Where(x=>x.Enabled)){
    var key=p.Key;
    items.Children.Add(C2SideButton("◉",p.Name,()=>OpenPlatform(key),page.StartsWith("平台")&&provider==key,
      p.Accounts.Count.ToString()));
   }
  }
  var scroll=new ScrollViewer{Content=items,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  var aside=Plain(scroll,8);aside.Margin=new Thickness(10,4,0,8);return aside;
 }
 Control C2SideButton(string glyph,string text,Action handler,bool active=false,string? count=null){
  var kind=glyph switch{
   "⌂"=>"home","♫"=>"music","▧"=>"album","♥"=>"heart",
   "◷"=>"history","▤"=>"playlist",_=>"provider"
  };
  var b=B("",handler);
  var icon=C3Icon(kind);
  icon.Width=17;icon.Height=17;
  var row=narrow?H(icon):H(icon,T(text+(count is null?"":"  · "+count),13,!active,active));
  row.Spacing=12;
  b.Content=row;
  b.HorizontalContentAlignment=HorizontalAlignment.Left;
  b.HorizontalAlignment=HorizontalAlignment.Stretch;
  b.Background=active?R("Selected"):Brushes.Transparent;
  b.Foreground=active?R("Text"):R("Sub");
  b.Classes.Add("nav");if(active){b.Classes.Add("selected");b.BorderBrush=PearlBrush();b.BorderThickness=new Thickness(2,0,0,0);}
  b.Padding=new Thickness(13,8);
  b.CornerRadius=new CornerRadius(9);
  AutomationProperties.SetName(b,text);
  return b;
 }
 Control C2Hero(){
  var grid=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,190"),ColumnSpacing=25,MinHeight=210};
  var copy=V(
   T("在你的音乐里，继续漫游",26,false,true),
   T("已收藏 "+favorites.Count+" 首  ·  本地曲库 "+songs.Length+" 首  ·  "+localLists.Count+" 个常用歌单",13,true),
   T("让每一段熟悉的旋律，都有自己的位置。",13,true),
   H(B("▶  继续播放",()=>Play(nowSong),true),Quiet("浏览曲库  ›",()=>Go("本地音乐"))));
  copy.Spacing=14;copy.VerticalAlignment=VerticalAlignment.Center;copy.Margin=new Thickness(6,0,0,0);
  Grid.SetColumn(copy,0);grid.Children.Add(copy);
  var art=Cover(nowSong,168,168);art.HorizontalAlignment=HorizontalAlignment.Center;
  Grid.SetColumn(art,1);grid.Children.Add(art);
  return new Border{Child=grid,Background=Gradient(dark?"#2A3352":"#E7EDFF",dark?"#242D49":"#F5E8F5"),
   CornerRadius=new CornerRadius(19),Padding=new Thickness(22,22)};
 }
 Control C2Home(){
  var recent=V();recent.Spacing=8;
  var hdr=H(T("最近播放",21,false,true),Quiet("查看全部  ›",()=>Go("历史")));
  recent.Children.Add(hdr);
  var tiles=new StackPanel{Orientation=Orientation.Horizontal,Spacing=14};
  foreach(var id in history.Take(4).ToArray()){
   var num=id;var item=V(Cover(id,106,106),T(Track(id).Title,13,false,true),T(Track(id).Artist,11,true));
   item.Width=130;item.Spacing=5;
   var go=B("播放",()=>Play(num),true);go.MinHeight=28;go.Padding=new Thickness(8,3);
   item.Children.Add(go);tiles.Children.Add(item);
  }
  recent.Children.Add(tiles);
  var lists=V(T("你的歌单",21,false,true));
  lists.Spacing=10;
  var grid=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,*"),RowDefinitions=RowDefinitions.Parse("Auto,Auto"),ColumnSpacing=14,RowSpacing=12};
  var i=0;
  foreach(var k in localLists.Keys.Take(4).ToArray()){
   var key=k;int coverId=localLists[key].FirstOrDefault()?.SongId??1;
   var unit=H(Cover(coverId,53,53),V(T(key,14,false,true),T(localLists[key].Count+" 首 · 本地歌单",12,true)));
   unit.Spacing=12;var btn=new Button{Content=unit,Background=R("Surface"),BorderBrush=R("Line"),
      BorderThickness=new Thickness(.6),CornerRadius=new CornerRadius(12),Padding=new Thickness(10)};
   AutomationProperties.SetName(btn,key+" 本地歌单");
   btn.Click+=(_,_)=>{localList=key;Go("本地歌单");};
   Grid.SetRow(btn,i/2);Grid.SetColumn(btn,i%2);grid.Children.Add(btn);i++;
  }
  lists.Children.Add(grid);
  return V(C2Hero(),new Border{Height=10},recent,new Border{Height=6},lists);
 }
 Control LegacyC2Toolbar(string title,bool import){
  var r=H(T(title,16,false,true),
   B("▶ 播放选中",()=>Play(selectedSong),true),
   Quiet("＋ 队列",()=>{queue.Add(new(selectedSong,"本地",""));Notify("已添加到临时队列（模拟）");}),
   Quiet("加入歌单",AddLocalSelected));
  if(import)r.Children.Add(Quiet("导入音乐",()=>Notify("预览不读取本机文件；真实文件选择框仍属P1.6待验收")));
  return Plain(r,5);
 }
 Control LegacyC2SongTable(Song[] list){
  var box=new ListBox{ItemsSource=list,Height=480,Background=Brushes.Transparent,
    BorderThickness=new Thickness(0),SelectedItem=list.FirstOrDefault(x=>x.Id==selectedSong)};
  box.ItemTemplate=new FuncDataTemplate<Song>((song,_)=>
  {
   if(song is null)return T("未知歌曲",13,true);
   var row=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("50,*,140,54,48"),
      ColumnSpacing=10,Margin=new Thickness(3,5),MinHeight=52};
   Grid.SetColumn(row,0);
   var cover=Cover(song.Id,44,44);Grid.SetColumn(cover,0);row.Children.Add(cover);
   var title=V(T(song.Title,13,false,true),T(song.Artist,11,true));title.Spacing=1;
   Grid.SetColumn(title,1);row.Children.Add(title);
   var album=T(song.Album,12,true);Grid.SetColumn(album,2);row.Children.Add(album);
   var duration=T(Time(song.Duration),12,true);Grid.SetColumn(duration,3);row.Children.Add(duration);
   var more=Quiet("···",()=>OpenSongMenu(song));more.MinHeight=32;
   Grid.SetColumn(more,4);row.Children.Add(more);
   return row;
  },true);
  box.SelectionChanged+=(_,_)=>{if(box.SelectedItem is Song s)selectedSong=s.Id;};
  box.DoubleTapped+=(_,_)=>{if(box.SelectedItem is Song s)Play(s.Id);};
  mainSongs=box;
  var cols=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("50,*,140,54,48"),ColumnSpacing=10,Margin=new Thickness(14,5)};
  foreach(var (name,index) in new[]{(" ",0),("歌名 / 艺术家",1),("专辑",2),("时长",3),(" ",4)}){
   var t=T(name,11,true);Grid.SetColumn(t,index);cols.Children.Add(t);
  }
  return V(cols,new Border{Height=1,Background=R("Line")},box);
 }
 Control C2Platform(){
  var p=CurrentPlatform;
  var acts=p.Accounts.ToArray();
  if(acts.Length==0)return Plain(V(T("尚无已连接账号",20,false,true),
     T("连接状态演示。此处没有真实账号和官方歌单。",13,true),B("恢复演示账号",()=>ResetDemoAccounts(p))),16);
  var act=CurrentAccount??acts[0];chosenAccount.TryAdd(provider,act.Key);
  var combo=new ComboBox{ItemsSource=acts.Select(a=>a.Alias).ToArray(),SelectedItem=act.Alias,Width=165};
  AutomationProperties.SetName(combo,"平台账号切换");
  StyleMotion(combo);combo.SelectionChanged+=(_,_)=>{
   var next=acts.FirstOrDefault(a=>a.Alias==combo.SelectedItem?.ToString());
   if(next is null||next.Key==chosenAccount.GetValueOrDefault(provider))return;
   chosenAccount[provider]=next.Key;selectedRemotePlaylist="";Render();
   Event("ACCOUNT_SWITCH",new{Provider=provider,Account=next.Key,Lists=next.Lists.Select(x=>x.RemoteId).ToArray()});
   Notify(p.Name+"已切换账号："+next.Alias+"（模拟）");
  };
  var statusLine=H(T("账号",12,true),combo,StatusLabel(act.State),Quiet("管理账号",()=>Go("扩展管理")));
  var head=V(statusLine,T("上次成功同步（演示） "+act.LastSync,11,true));
  var lists=V(T("你的歌单",21,false,true),T("仅显示当前 "+act.Alias+" 的歌单，不混入其他账号",12,true));lists.Spacing=10;
  var layout=new StackPanel{Spacing=12};
  foreach(var entry in act.Lists){
   var id=entry.RemoteId;
   var idx=entry.SongIds.FirstOrDefault();
   var row=H(Cover(Math.Max(1,idx),64,64),
       V(T(entry.Name,16,false,true),T(entry.Count+" 首歌曲  ·  "+act.Alias,12,true)));
   row.Spacing=13;
   var btn=new Button{Content=row,Padding=new Thickness(12),HorizontalContentAlignment=HorizontalAlignment.Left,
      BorderThickness=new Thickness(.8),CornerRadius=new CornerRadius(12),
      Background=R("Surface"),BorderBrush=R("Line")};
   AutomationProperties.SetName(btn,entry.Name+" "+entry.Count+" 首");
   btn.Click+=(_,_)=>{selectedRemotePlaylist=id;Go("平台歌单");};
   StyleMotion(btn);layout.Children.Add(btn);
  }
  lists.Children.Add(layout);
  var state=act.State.Contains("过期")||act.State.Contains("失败")?
    T("此账号已失效或同步失败。列表仅为缓存演示，不会继续请求平台。",12,true):
    T("平台歌单为演示数据，不代表官方读取或写入授权已落实。",12,true);
  lists.Children.Add(state);
  return V(Plain(head,4),new Border{Height=1,Background=R("Line")},lists);
 }
 Control C2Lyrics(){
  c2LyricButtons.Clear();
  c2LastLyricIndex=-1;
  var track=Track(nowSong);
  var grid=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("290,*"),ColumnSpacing=34,Margin=new Thickness(18,12)};
  var art=V(Cover(nowSong,255,255),T(track.Title,21,false,true),T(track.Artist+"  ·  "+track.Album,13,true));
  art.Spacing=14;Grid.SetColumn(art,0);grid.Children.Add(art);
  var lyric=V(T("时间同步歌词 · 模拟预览",12,true),new Border{Height=18});
  var active=(int)(seconds/32)%LyricLines.Length;
  for(int i=0;i<LyricLines.Length;i++){
   var idx=i;
   var b=B((i==active?"♫   ":"     ")+LyricLines[i],()=>{
    seconds=idx*32;Render();Notify("已模拟定位到 "+Time((int)seconds));
   });
   b.FontSize=i==active?22:19;b.FontWeight=i==active?FontWeight.Bold:FontWeight.Normal;
   b.Foreground=i==active?R("Text"):R("Sub");
   b.Background=Brushes.Transparent;b.BorderThickness=new Thickness(0);
   b.HorizontalContentAlignment=HorizontalAlignment.Left;
   b.Padding=new Thickness(4,12);
   lyric.Children.Add(b);
   c2LyricButtons.Add(b);
  }
  Grid.SetColumn(lyric,1);grid.Children.Add(lyric);
  C2RefreshLyricHighlight();
  return V(Plain(grid,5),H(Quiet("← 返回曲库",()=>Go("本地音乐")),
    Quiet("打开队列",()=>{queueOpen=true;Render();})));
 }
 Control C2PlayerBar(){
  var bar=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"190,*,150":"260,*,225"),
    ColumnSpacing=12,Margin=new Thickness(18,8)};
  var left=H(Cover(nowSong,47,47),V(T(Track(nowSong).Title,14,false,true),T(Track(nowSong).Artist,11,true)));
  Grid.SetColumn(left,0);bar.Children.Add(left);
  playButton=B(playing?"❚❚":"▶",TogglePlay,true,36);
  var previous=Quiet("⏮",()=>Skip(-1));var next=Quiet("⏭",()=>Skip(1));
  nowTime=T(Time((int)seconds)+" / "+Time(Track(nowSong).Duration),11,true);
  seek=new Slider{Minimum=0,Maximum=Track(nowSong).Duration,Value=seconds,Width=narrow?95:280};
  seek.PropertyChanged+=(_,e)=>{
   if(e.Property==RangeBase.ValueProperty&&!seekUpdating&&seek!=null&&Math.Abs(seconds-seek.Value)>.7){
    seconds=seek.Value;UpdatePlaybackText();C2RefreshLyricHighlight();
   }
  };
  var center=H(previous,playButton,next,seek,nowTime);
  Grid.SetColumn(center,1);bar.Children.Add(center);
  var right=H(Quiet("歌词",()=>Go("正在播放")),
      Quiet(queueOpen?"关闭队列":"队列",()=>{ToggleQueue();}));
  if(!narrow){
   var volumeSlider=new Slider{Minimum=0,Maximum=1,Value=volume,Width=60};
   volumeSlider.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty)volume=volumeSlider.Value;};
   right.Children.Insert(0,volumeSlider);
   right.Children.Insert(0,Quiet(favorites.Contains(nowSong)?"♥":"♡",()=>Like(nowSong)));
  }
  Grid.SetColumn(right,2);bar.Children.Add(right);
  var stack=V(bar);
  stack.Spacing=2;
  statusText=T(status,11,true);statusText.Margin=new Thickness(20,0,10,0);
  stack.Children.Add(statusText);
  return new Border{Child=stack,Background=R("Surface"),BorderBrush=R("Line"),
    BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,5)};
 }
}
