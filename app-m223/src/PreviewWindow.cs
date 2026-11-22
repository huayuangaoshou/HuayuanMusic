using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace HuayuanPreview;

// All identities, accounts, playlist names and songs are synthetic. No music platform integration.
internal sealed record Song(int Id,string Title,string Artist,string Album,int Duration,string Origin="本地",string Account="",long PlaylistEntryId=0);
internal sealed record Account(string Key,string Alias,string State,string LastSync,List<PlatformPlaylist> Lists);
internal sealed record PlatformPlaylist(string RemoteId,string Name,int Count,List<int> SongIds);
internal sealed class Platform {
 public string Key {get;}
 public string Name {get;}
 public List<Account> Accounts {get;}
 public bool Enabled {get;set;} = true;
 public Platform(string key,string name,List<Account> accounts){Key=key;Name=name;Accounts=accounts;}
}
internal sealed record QueueEntry(int SongId,string Origin,string Account);
internal sealed class ListSongView {public Song Track {get;}public ListSongView(Song t){Track=t;}}

public sealed partial class PreviewWindow:Window {
 Song[] songs=[];
 readonly Dictionary<string,List<QueueEntry>> localLists=new();
 readonly HashSet<int> favorites=[];
 readonly List<int> history=[];
 readonly List<QueueEntry> queue=[]; // display projection ONLY; production queue owns all mutations
 readonly Dictionary<string,Platform> platforms;
 readonly Dictionary<string,string> chosenAccount = new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(500)};
 string page="本地音乐",provider="qq",selectedRemotePlaylist="",localList="深夜循环",query="",kind="全部歌曲";
 string status="M1 本地音乐测试版：请选择导入文件或文件夹；平台与账号页面均为演示，不联网。";
 int selectedSong=1,nowSong=1,queueIndex=0;
 bool playing,queueOpen,dark,reduced,songActionsOpen;
 double seconds=35,volume=.60;
 Grid root=null!;
 TextBlock? nowTime,nowLyric,statusText;
 Button? playButton;
 Slider? seek;
 ListBox? mainSongs;
 bool seekUpdating;
 bool narrow;
 bool VeryNarrow=>Bounds.Width>0?Bounds.Width<1050:Width<1050;
 Song Track(int id)=>songs.FirstOrDefault(x=>x.Id==id)??new Song(0,"请选择隔离测试歌曲","本地合成音频","原创占位封面",0);
 Platform CurrentPlatform=>platforms[provider];
 Account? CurrentAccount=>CurrentPlatform.Accounts.FirstOrDefault(a=>a.Key==chosenAccount.GetValueOrDefault(provider));
 PlatformPlaylist? CurrentRemotePlaylist=>CurrentAccount?.Lists.FirstOrDefault(l=>l.RemoteId==selectedRemotePlaylist);

 public PreviewWindow(INightflightPlayback core){
  this.core=core;core.Changed+=ApplyCoreState;
  platforms=Program.Desktop ? new() : new Dictionary<string,Platform>{
   ["kugou"]=new("kugou","酷狗概念版",[
    new("main","概念主号","已连接 · 仅可读取","昨天 20:12",[
      new("list-a","天天听歌",76,[1,4,6,8]),new("list-b","收藏的旋律",24,[2,10,12])]),
    new("alt","概念小号","模拟同步失败 · 请检查权限","前天 09:30",[
      new("list-a","我的收藏",13,[3,9,11]),new("list-c","夜色漫步",19,[2,5,7])])
   ]),
   ["qq"]=new("qq","QQ 音乐",[
    new("main","QQ 主号","已连接 · 只读歌单","今天 08:16",[
      new("list-1","我喜欢的音乐",328,[1,2,3,8]),new("list-2","学习时光",47,[4,5,6])]),
    new("alt","QQ 小号","授权已过期 · 无法同步","上周 16:42",[
      new("list-1","游戏 BGM",120,[7,10,12]),new("list-3","轻音乐收藏",26,[4,13,15])])
   ]),
   ["netease"]=new("netease","网易云音乐",[
    new("main","网易号","已连接 · 只读歌单","昨天 18:32",[
      new("list-1","晚安歌单",65,[1,4,8]),new("list-2","爵士时刻",48,[5,7,9])])
   ])
  };
  foreach(var p in platforms.Values) if(p.Accounts.Count>0)chosenAccount[p.Key]=p.Accounts[0].Key;
  Title="Huayuan Music · 夜航 · M1 本地音乐测试版（隔离）";
  Width=1440;Height=960;MinWidth=980;MinHeight=720;
  Icon=new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://HuayuanMusic.P17.Nightflight/Assets/Huayuan-C31.ico")));
  WindowStartupLocation=WindowStartupLocation.CenterScreen;
  ReadAccessibility();gallery=Program.Args.Contains("--gallery");WatchAccessibility();narrow=VeryNarrow;Palette();if(!Program.Desktop)Render();
  SizeChanged+=(_,_)=>{var updated=VeryNarrow;if(updated!=narrow){narrow=updated;Render();}};
  if(!R2Enabled)KeyDown+=Keyboard;
  PropertyChanged+=(_,e)=>{if(e.Property==ActualTransparencyLevelProperty&&IsVisible)Dispatcher.UIThread.Post(Render);};
  PropertyChanged+=(_,e)=>{if((e.Property==WindowStateProperty||e.Property==IsVisibleProperty)&&CanPresentCore&&coreUiDeferred)Dispatcher.UIThread.Post(ResumeCorePresentation,DispatcherPriority.Loaded);};
  Opened+=(_,_)=>Program.Log("C_WINDOW_OPEN",new{RenderScaling,Width,Height,Platforms=platforms.Count,Accounts=platforms.Values.Sum(p=>p.Accounts.Count),Queue=queue.Count,Simulation=false,MockPlatformPages=!Program.Desktop});
  Closed+=(_,_)=>{core.Changed-=ApplyCoreState;foreach(var image in artCache.Values)image.Dispose();artCache.Clear();c31AccessibilityWatch.Stop();searchDebounce.Stop();timer.Stop();Program.Log("C_WINDOW_CLOSE",new{reason="normal",timer=timer.IsEnabled,accessibilityTimer=c31AccessibilityWatch.IsEnabled,artCache=artCache.Count});};
 }
 IBrush BrushFrom(string hex)=>Brush.Parse(hex);
 IBrush R(string name)=>(IBrush)Application.Current!.Resources[name]!;
 void LegacyPalette(){
  var keys=new[]{"Base","Surface","Surface2","Text","Sub","Line","Primary","Selected","CardBlue"};
  var vals=dark?new[]{"#111624","#192133","#242F44","#EEF3FD","#ABBAD0","#34445A","#9BAFFA","#293A57","#36466B"}
               :new[]{"#F6F7FB","#FFFFFF","#EDEFFD","#222A42","#626C83","#E5E8F2","#596DCC","#E9EDFF","#E2E9FC"};
  var app=Application.Current!;
  for(int i=0;i<keys.Length;i++)app.Resources[keys[i]]=BrushFrom(vals[i]);
  app.RequestedThemeVariant=HighContrast?ThemeVariant.Dark:dark?ThemeVariant.Dark:ThemeVariant.Light;
  if(HighContrast){app.Resources["Base"]=Brushes.Black;app.Resources["Surface"]=Brushes.Black;app.Resources["Surface2"]=Brushes.Black;app.Resources["Text"]=Brushes.White;app.Resources["Sub"]=Brushes.White;app.Resources["Line"]=Brushes.White;app.Resources["Primary"]=Brushes.Yellow;app.Resources["Selected"]=Brush.Parse("#313131");}
  app.Resources["TextControlPlaceholderOpacity"]=1d;
  app.Resources["StatusError"]=BrushFrom(dark?"#FFB4C0":"#9B2745");app.Resources["StatusSuccess"]=BrushFrom(dark?"#84DBC4":"#176449");app.Resources["StatusWarning"]=BrushFrom(dark?"#F1D090":"#7A5100");
  app.Resources["Ink"]=R("Text");app.Resources["Hover"]=R("Surface2");app.Resources["Edge"]=R("Line");app.Resources["Accent"]=R("Primary");
  ApplyWindowMaterial();Background=NativeMica?Brushes.Transparent:R("Base");
 }
 TextBlock T(string text,int size=14,bool secondary=false,bool strong=false)=>new(){
  Text=text,Foreground=R(secondary?"Sub":"Text"),FontSize=size,
  FontWeight=strong?FontWeight.SemiBold:FontWeight.Normal,
  TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
 StackPanel V(params Control[] parts){var p=new StackPanel{Spacing=12};foreach(var v in parts)p.Children.Add(v);return p;}
 StackPanel H(params Control[] parts){var p=new StackPanel{Orientation=Orientation.Horizontal,Spacing=9,VerticalAlignment=VerticalAlignment.Center};foreach(var v in parts)p.Children.Add(v);return p;}
 Border Panel(Control c,int pad=16,bool selected=false)=>new(){Child=c,Padding=new Thickness(pad),Background=CeramicBrush(),BorderBrush=selected?PearlBrush():R("Line"),
  BorderThickness=selected?new Thickness(1.5):new Thickness(1),CornerRadius=new CornerRadius(9)};
 Button B(string text,Action click,bool primary=false,int min=30){
  var b=new Button{Content=text,Padding=new Thickness(primary?16:12,7),MinHeight=min,
    CornerRadius=new CornerRadius(9),Foreground=primary?Brushes.White:R("Text"),
    Background=primary?R("Primary"):R("Surface2"),
    HorizontalContentAlignment=HorizontalAlignment.Center};
  AutomationProperties.SetName(b,text);
  b.Click+=(_,_)=>{Program.Log("C_ACTION",new{page,action=text,provider,account=chosenAccount.GetValueOrDefault(provider)});click();};
  if(primary&&text.Length>0)b.Content=new TextBlock{Text=text,Foreground=HighContrast?Brushes.Black:Brushes.White,FontSize=13,VerticalAlignment=VerticalAlignment.Center};
  if(primary)b.Classes.Add("primary");StyleMotion(b,primary);return b;
 }
 Button Nav(string title,string target,int pad=0){
  var focused=page==target||(target=="平台"&&page.StartsWith("平台"));
  var b=B(new string(' ',pad)+(focused?"● ":"")+title,()=>Go(target));
  b.HorizontalAlignment=HorizontalAlignment.Stretch;
  b.HorizontalContentAlignment=HorizontalAlignment.Left;
  b.Background=focused?R("Selected"):Brushes.Transparent;
  b.BorderThickness=new Thickness(0);b.Padding=new Thickness(10,5);
  return b;
 }
 void Event(string eventName,object detail)=>Program.Log(eventName,detail);
 void Notify(string text){status=text;if(statusText!=null)statusText.Text=text;Event("C_STATUS",text);}
 void Go(string target){c31SongFlyout?.Hide();if(!goingBack&&page!=target)navigationHistory.Push(page);page=target;songActionsOpen=false;Render();Notify("已打开："+target);}
 void OpenPlatform(string key){provider=key;page="平台";selectedRemotePlaylist="";Render();Notify("已打开："+CurrentPlatform.Name+"（演示账号，不联网）");}
 void LegacyRender(){
  Palette();
  root=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"76,*":"220,*"),RowDefinitions=RowDefinitions.Parse("Auto,*,Auto,Auto"),Background=WindowFill()};
  var top=C3Top();Grid.SetColumnSpan(top,2);Grid.SetRow(top,0);root.Children.Add(top);
  var side=C2Sidebar();Grid.SetColumn(side,0);Grid.SetRow(side,1);root.Children.Add(side);
  var contents=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(queueOpen&&!narrow?"*,318":"*"),ColumnSpacing=10,Margin=new Thickness(10,12,20,8)};
  var pageContents=(page=="首页"||page=="正在播放")?V(BuildPage()):V(Header(),BuildPage());pageContents.Spacing=15;
  if(queueOpen&&narrow){c31QueueSurface=Panel(C3QueuePanel(),10);pageContents.Children.Add(c31QueueSurface);}
  var contentScroll=new ScrollViewer{Content=pageContents,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  Grid.SetColumn(contentScroll,0);contents.Children.Add(contentScroll);
  if(queueOpen&&!narrow){var q=Panel(C3QueuePanel(),12);c31QueueSurface=q;Grid.SetColumn(q,1);contents.Children.Add(q);}
  Grid.SetColumn(contents,1);Grid.SetRow(contents,1);root.Children.Add(contents);
  var player=C3PlayerBar();Grid.SetColumnSpan(player,2);Grid.SetRow(player,2);root.Children.Add(player);
  var foot=new Border{Background=R("Surface"),Padding=new Thickness(16,4),BorderBrush=R("Line"),BorderThickness=new Thickness(0,1,0,0)};
  var footer=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("Auto,*"),ColumnSpacing=16};
  var previewLabel=T("C3.1 · 离线模拟预览 · 不联网不发声",10,true);
  Grid.SetColumn(previewLabel,0);footer.Children.Add(previewLabel);
  statusText=T(status,10,true);
  statusText.HorizontalAlignment=HorizontalAlignment.Right;
  Grid.SetColumn(statusText,1);footer.Children.Add(statusText);
  foot.Child=footer;
  Grid.SetColumnSpan(foot,2);Grid.SetRow(foot,3);root.Children.Add(foot);
  Content=root;ComposePage(contents,contentScroll);if(c31QueueSurface!=null&&queueOpen)Enter(c31QueueSurface,18);UpdatePlayback();
 }
 Control BuildTop(){
  var g=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("215,*,Auto"),Margin=new Thickness(12,10,16,2)};
  var brand=H(T("◈",26,false,true),T("Huayuan Music",19,false,true));
  Grid.SetColumn(brand,0);g.Children.Add(brand);
  var search=new TextBox{PlaceholderText="搜索本地歌曲 / 歌手 / 专辑",Text=query,MinWidth=185,MaxWidth=470,FontSize=14};
  search.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){query=search.Text??"";page="本地音乐";Render();Notify("仅搜索本地资料库："+query);}};
  search.TextChanged+=(_,_)=>query=search.Text??"";
  var bar=H(B("‹",()=>Notify("预览仅模拟当前导航，暂无历史返回栈")),B("›",()=>Notify("预览仅模拟当前导航，暂无历史前进栈")),search,
    B("搜索",()=>{page="本地音乐";Render();Notify("已按部分关键词搜索本地曲库");}));
  Grid.SetColumn(bar,1);g.Children.Add(bar);
  var right=H(T("模拟预览",11,true),B("设置",()=>Go("设置")));Grid.SetColumn(right,2);g.Children.Add(right);
  return Panel(g,8);
 }
 Control BuildSidebar(){
  var c=V(T(narrow?"◈":"我的音乐",narrow?17:16,false,true));
  if(narrow){
   c.Children.Add(B("⌂",()=>Go("首页")));c.Children.Add(B("♫",()=>Go("本地音乐")));
   c.Children.Add(B("☷",()=>Go("本地歌单")));c.Children.Add(B("♥",()=>Go("收藏")));
   c.Children.Add(B("⌛",()=>Go("历史")));
   c.Children.Add(T("平台",11,true));
   foreach(var p in platforms.Values.Where(p=>p.Enabled)){var k=p.Key;c.Children.Add(B(p.Name.Substring(0,1),()=>OpenPlatform(k)));}
  } else {
   c.Children.Add(Nav("首页","首页"));c.Children.Add(T("本地音乐",12,true,true));
   c.Children.Add(Nav("全部歌曲","本地音乐",1));c.Children.Add(Nav("歌手 / 专辑","歌手专辑",1));
   c.Children.Add(T("我的音乐",12,true,true));c.Children.Add(Nav("我喜欢的音乐","收藏",1));c.Children.Add(Nav("最近播放","历史",1));
   c.Children.Add(Nav("本地歌单","本地歌单",1));
   foreach(var k in localLists.Keys.Take(5).ToArray()){var name=k;c.Children.Add(B("  ♫  "+name,()=>{localList=name;Go("本地歌单");}));}
   c.Children.Add(T("音乐平台 · UI演示",12,true,true));
   foreach(var p in platforms.Values.Where(p=>p.Enabled)){var k=p.Key;var count=p.Accounts.Count;c.Children.Add(B("  "+p.Name+"    "+count+"个账号",()=>OpenPlatform(k)));}
   c.Children.Add(B("＋ 扩展管理",()=>Go("扩展管理")));
  }
  var v=new ScrollViewer{Content=c,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  var panel=Panel(v,12);
  panel.Margin=new Thickness(12,9,0,8);
  return panel;
 }
 Control Header(){
  var title=page switch{
   "首页"=>"上午好，来听点音乐",
   "本地音乐"=>"全部歌曲",
   "歌手专辑"=>"歌手与专辑",
   "本地歌单"=>"本地歌单  /  "+localList,
   "收藏"=>"我喜欢的音乐","历史"=>"最近播放",
   "平台"=>CurrentPlatform.Name,"平台歌单"=>CurrentPlatform.Name+"  /  "+(CurrentRemotePlaylist?.Name??"官方歌单"),
   "正在播放"=>"正在播放","扩展管理"=>"音乐扩展管理","设置"=>"设置",_=>page
  };
  return H(T(title,22,false,true),T(page.StartsWith("平台")?"多账号独立空间 · UI模拟":"仅本地演示",11,true));
 }
 Control LegacyBuildPage()=>page switch{
  "首页"=>C3Home(),"本地音乐"=>LocalLibrary(),"歌手专辑"=>ArtistsAndAlbums(),
  "本地歌单"=>LocalPlaylists(),"收藏"=>FavoritesPage(),"历史"=>HistoryPage(),
  "平台"=>C2Platform(),"平台歌单"=>PlatformPlaylistPage(),"正在播放"=>C3Lyrics(),
  "设置"=>SettingsPage(),"扩展管理"=>ExtensionsPage(),_=>Home()
 };
 Control Home(){
  var welcome=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,Auto"),ColumnSpacing=12};
  var body=V(T("继续你的音乐旅程",22,false,true),
    T("本地收藏、近期播放与歌单都能在这里找到。在线音乐平台由独立扩展管理。",13,true),
    H(B("▶  继续播放",()=>Play(nowSong),true),B("本地曲库",()=>Go("本地音乐")),
      B("我的歌单",()=>Go("本地歌单"))));
  Grid.SetColumn(body,0);welcome.Children.Add(body);
  var art=new Border{Width=160,Height=140,CornerRadius=new CornerRadius(14),Background=R("CardBlue"),
   Child=new TextBlock{Text="♫",FontSize=84,Foreground=R("Primary"),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center}};
  Grid.SetColumn(art,1);welcome.Children.Add(art);
  var recent=V(T("最近播放",19,false,true));
  foreach(var id in history.Take(4).ToArray()){int n=id;recent.Children.Add(B("♫  "+Track(n).Title+"  ·  "+Track(n).Artist,()=>Play(n)));}
  var lists=V(T("常用本地歌单",19,false,true));
  foreach(var k in localLists.Keys.ToArray()){var name=k;lists.Children.Add(B("▤  "+name+"    "+localLists[k].Count+"首",()=>{localList=name;Go("本地歌单");}));}
  return V(Panel(welcome,24),Panel(recent),Panel(lists));
 }
 Control LegacyLocalLibrary(){
  var keys=new[]{"全部歌曲","歌手","专辑","文件夹"};
  var segment=H(keys.Select(k=>B((kind==k?"● ":"")+k,()=>{kind=k;Render();})).Cast<Control>().ToArray());
  if(kind=="歌手"||kind=="专辑")return ArtistsAndAlbums();
  if(kind=="文件夹")return V(Panel(V(T("导入来源与文件夹",18,false,true),
   T("实际导入功能由 P1.6 单独验收；这里不会读取用户磁盘。",13,true),B("导入本地音乐（预览）",()=>Notify("仅为布局入口，未打开真实文件对话框。")))));
  var found=songs.Where(s=>string.IsNullOrWhiteSpace(query)||new[]{s.Title,s.Artist,s.Album}.Any(v=>v.Contains(query,StringComparison.OrdinalIgnoreCase))).ToArray();
  return V(segment,Toolbar("歌曲 "+found.Length+" 首",true),songActionsOpen?SongActionsPanel():new Border{Height=1},SongTable(found));
 }
 Control LegacyArtistsAndAlbums(){
  var artist=songs.GroupBy(x=>x.Artist).Select(x=>(Name:x.Key,Count:x.Count())).ToArray();
  var album=songs.GroupBy(x=>x.Album).Select(x=>(Name:x.Key,Count:x.Count())).ToArray();
  var a=V(T("歌手",19,false,true));foreach(var x in artist)a.Children.Add(B("♫  "+x.Name+"  ·  "+x.Count+" 首",()=>{query=x.Name;Go("本地音乐");}));
  var b=V(T("专辑",19,false,true));foreach(var x in album)b.Children.Add(B("▧  "+x.Name+"  ·  "+x.Count+" 首",()=>{query=x.Name;Go("本地音乐");}));
  return V(Panel(a),Panel(b));
 }
 Control Toolbar(string title,bool import)=>C2Toolbar(title,import);
 Control SongTable(Song[] list)=>C2SongTable(list);
 void OpenSongMenu(Song song){ShowSongFlyout(song);}
 Control SongActionsPanel(){
  var song=Track(selectedSong);
  return Panel(V(H(T("歌曲操作："+song.Title,16,false,true),B("收起",()=>{
   CloseSongMenu();
  })),
  H(B("播放下一首",()=>{queue.Insert(Math.Min(queueIndex+1,queue.Count),new(song.Id,"本地",""));Notify("已放入下一首（模拟）");}),
    B("追加到队列",()=>{queue.Add(new(song.Id,"本地",""));Notify("已追加至队列（模拟）");}),
    B("加入本地歌单",()=>AddLocalSelected()),
    B("收藏 / 取消",()=>Like(song.Id)),
    B("复制歌名",()=>_=CopySongTitle(song.Id)))),10,true);
 }
 Control LegacyLocalPlaylists(){
  var selector=H(T("我的歌单",18,false,true));
  foreach(var k in localLists.Keys.Take(5).ToArray()){
   var n=k;selector.Children.Add(B((localList==k?"● ":"")+k,()=>{localList=n;Render();}));
  }
  var field=new TextBox{PlaceholderText="新建歌单名称",Width=190};
  var edit=H(field,B("新建歌单",()=>{
   var n=(field.Text??"").Trim();
   if(n.Length==0||localLists.ContainsKey(n)){Notify("请输入未使用的歌单名称");return;}
   localLists[n]=[];localList=n;Render();Notify("新建成功（仅预览内存）");
  }),B("保存当前队列为歌单",SaveQueue),B("删除当前歌单",()=>{
   if(!localLists.ContainsKey(localList)){Notify("请先选歌单");return;}
   if(localLists.Count==1){Notify("至少保留一个演示歌单");return;}
   localLists.Remove(localList);localList=localLists.Keys.First();Render();Notify("仅删除歌单记录，原始歌曲未删除（模拟）");
  }));
  var items=localLists.GetValueOrDefault(localList)??[];
  var listing=items.Select(x=>Track(x.SongId)).ToArray();
  return V(Plain(V(selector,T("本地歌单与音乐平台官方歌单始终分开。",12,true),edit),6),
   Toolbar(localList+"   ·   "+items.Count+" 首",false),
   listing.Length>0?SongTable(listing):Panel(T("歌单为空，可从本地曲库添加歌曲或保存播放队列。",14,true)));
 }
 Control FavoritesPage()=>V(T("这里展示本体自己的收藏，不等于平台账号的官方收藏。",13,true),
   Toolbar("收藏歌曲 · "+favorites.Count,false),SongTable(songs.Where(s=>favorites.Contains(s.Id)).ToArray()));
 Control HistoryPage()=>V(T("最近播放记录保存在本体（此处仅为内存演示）。",13,true),
   Toolbar("播放历史 · "+history.Count,false),SongTable(history.Select(Track).ToArray()),
   B("清除历史（模拟）",()=>{history.Clear();Render();Notify("仅清除本体播放历史（内存模拟）");}));
 Control PlatformPage(){
  var p=CurrentPlatform;
  var accounts=p.Accounts.ToArray();
  if(accounts.Length==0)return Panel(V(T("尚无已连接账号",19,false,true),T("这是账号空态演示。不能读取任何官方歌单。",13,true),
      B("恢复演示账号",()=>ResetDemoAccounts(p))));
  var act=CurrentAccount??accounts[0];
  if(!chosenAccount.ContainsKey(provider))chosenAccount[provider]=act.Key;
  var combo=new ComboBox{ItemsSource=accounts.Select(a=>a.Alias).ToArray(),SelectedItem=act.Alias,Width=185};
  AutomationProperties.SetName(combo,"平台账号切换");
  combo.SelectionChanged+=(_,_)=>{
   var chosen=accounts.FirstOrDefault(a=>a.Alias==combo.SelectedItem?.ToString());
   if(chosen==null||chosen.Key==chosenAccount.GetValueOrDefault(provider))return;
   chosenAccount[provider]=chosen.Key;selectedRemotePlaylist="";Render();
   Event("ACCOUNT_SWITCH",new{Provider=provider,Account=chosen.Key,Lists=chosen.Lists.Select(x=>x.RemoteId).ToArray()});
   Notify(p.Name+"已切换账号："+chosen.Alias+"（模拟）");
  };
  var hdr=H(T(p.Name+" · 平台专属空间",19,false,true),T("当前账号",12,true),combo,
    B("账号管理",()=>Go("扩展管理")));
  var info=V(hdr,H(T("◉  "+act.Alias,15,false,true),T(act.State,13,true)),
    T("上次同步（模拟）："+act.LastSync,12,true),
    T("官方歌单只属于当前平台和账号，切换不会混入其他账号。",12,true));
  var cards=V(T(act.Alias+" 的官方歌单",18,false,true));
  foreach(var list in act.Lists){
   var id=list.RemoteId;
   cards.Children.Add(B("▣   "+list.Name+"             "+list.Count+" 首     ›",()=>{
    selectedRemotePlaylist=id;Go("平台歌单");
   }));
  }
  var access=act.State.Contains("过期")||act.State.Contains("失败")?
    T("该账号当前不可正常同步。已缓存的歌单名称只用于演示；实际授权失效时不得继续请求。",13,true):
    T("只读示例：不代表官方平台已提供歌单读取或写回权限。",13,true);
  return V(Panel(info,18,true),Panel(V(cards,access)));
 }
 Control PlatformPlaylistPage(){
  var act=CurrentAccount;
  var list=CurrentRemotePlaylist;
  if(act==null||list==null)return Panel(V(T("账号或歌单已不可用",18,false,true),B("返回平台",()=>Go("平台"))));
  return V(Panel(V(H(B("← 返回 "+CurrentPlatform.Name,()=>Go("平台")),
     T(act.Alias+" / "+list.Name,21,false,true)),
     T(list.Count+" 首 · "+act.State+" · 同名歌单不跨账号合并",13,true),
     H(B("播放歌单（模拟）",()=>Play(list.SongIds[0]),true),
       B("加入临时队列（模拟）",()=>{
        foreach(var id in list.SongIds)queue.Add(new(id,CurrentPlatform.Name,act.Alias));
        Notify("已添加 "+list.SongIds.Count+" 首到本体临时队列（模拟）");
       }))),18,true),
   SongTable(list.SongIds.Select(Track).ToArray()));
 }
 Control QueuePanel(){
  var rows=queue.Select((item,i)=>(item,i)).ToArray();
  var entries=rows.Select(x=>(queueIndex==x.i?"▶ ":"")+(x.i+1).ToString("000")+" "+Track(x.item.SongId).Title+"  · "+x.item.Origin+
   (string.IsNullOrWhiteSpace(x.item.Account)?"":" / "+x.item.Account)).ToArray();
  var list=new ListBox{ItemsSource=entries,Height=380,SelectedIndex=Math.Clamp(queueIndex,0,Math.Max(0,entries.Length-1)),
    Background=R("Surface"),BorderThickness=new Thickness(0)};
  list.DoubleTapped+=(_,_)=>{if(list.SelectedIndex>=0&&list.SelectedIndex<queue.Count){queueIndex=list.SelectedIndex;Play(queue[queueIndex].SongId);}};
  return V(H(T("播放队列",19,false,true),B("关闭",()=>{queueOpen=false;Render();})),
   T(queue.Count+" 首 · 本体统一队列，来源/账号各自保留",12,true),
   list,
   H(B("播放选中",()=>{if(list.SelectedIndex>=0&&list.SelectedIndex<queue.Count){queueIndex=list.SelectedIndex;Play(queue[queueIndex].SongId);}}),
     B("移除选中",()=>{if(list.SelectedIndex>=0&&list.SelectedIndex<queue.Count){queue.RemoveAt(list.SelectedIndex);queueIndex=Math.Min(queueIndex,Math.Max(0,queue.Count-1));Render();Notify("已从队列移除，不会删除歌单");}})),
   B("保存为本地歌单",SaveQueue,true),
   T("保存只进入本地歌单，不会自动写回任意官方平台账号。",12,true));
 }
 Control LyricsPage(){
  var track=Track(nowSong);
  var art=new Border{Width=210,Height=210,Background=R("CardBlue"),CornerRadius=new CornerRadius(20),
    Child=new TextBlock{Text="♫",Foreground=R("Primary"),FontSize=115,
    HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
  var lyrics=V(T(track.Title,26,false,true),T(track.Artist+"  /  "+track.Album,15,true),T("同步歌词 · 模拟预览",12,true));
  for(int i=0;i<6;i++){
   var n=i;var line=new[]{"夜空翻开未寄出的信","风吹过城市微亮的街道","旋律慢慢落进耳边","每一个瞬间都值得珍藏","沿着光寻找归来的方向","音乐继续，故事继续"}[i];
   lyrics.Children.Add(B((i==(int)(seconds/32)%6?"♪ ":"  ")+line,()=>{seconds=n*32;UpdatePlayback();Notify("已模拟定位歌词至 "+Time((int)seconds));},i==(int)(seconds/32)%6));
  }
  var main=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("225,*"),ColumnSpacing=30,Margin=new Thickness(12)};
  Grid.SetColumn(art,0);main.Children.Add(art);Grid.SetColumn(lyrics,1);main.Children.Add(lyrics);
  nowLyric=T("♪ "+("夜空翻开未寄出的信"),13,true);
  return V(Panel(main,24,true),H(B("返回曲库",()=>Go("本地音乐")),B("打开播放队列",()=>{SetQueue(true);})));
 }
 Control OldSettingsPage(){
  var theme=new CheckBox{Content="深色模式",IsChecked=dark};
  theme.IsCheckedChanged+=(_,_)=>{dark=theme.IsChecked==true;Render();Notify("主题已变更（仅预览）");};
  var rm=new CheckBox{Content="减少动画 / Reduced Motion",IsChecked=reduced};
  rm.IsCheckedChanged+=(_,_)=>{reduced=rm.IsChecked==true;Notify("已设置减少动画（仅演示）");};
  return V(Panel(V(T("外观与交互",19,false,true),theme,rm,T("原定液态玻璃/柔光陶瓷/虹彩珠光在布局获批后做适量集成。",13,true))),
   Panel(V(T("本地功能",19,false,true),T("真正的 MP3/FLAC、系统文件选择和数据恢复仍由 P1.6 本体验收。",13,true))),
   Panel(V(T("账户与扩展",19,false,true),B("音乐平台DLC管理（模拟）",()=>Go("扩展管理")),
    T("当前界面仅设计多账号信息架构，不连接真实授权或同步。",13,true))));
 }
 Control LegacyExtensionsPage(){
  var outp=V(T("平台与账号管理 · 全部模拟",19,false,true),T("这里不安装插件、不访问服务器，也不执行平台登录。",13,true));
  foreach(var p in platforms.Values){
   var key=p.Key;
   var row=V(H(T(p.Name,17,false,true),T(p.Enabled?"已启用展示":"已关闭展示",12,true),
    B(p.Enabled?"关闭导航演示":"启用导航演示",()=>{p.Enabled=!p.Enabled;Render();Notify(p.Name+"导航演示状态已变更");})),
    T("账号 "+p.Accounts.Count+" 个。每个账号的授权、同步和歌单分开。",12,true),
    H(B("查看该平台",()=>OpenPlatform(key)),
      B("新增模拟账号",()=>{
       var idx=p.Accounts.Count+1;
       p.Accounts.Add(new("mock-"+idx,p.Name+" 演示号 "+idx,"演示只读 · 无真实授权","从未同步",
          [new("mock-list-"+idx,"第 "+idx+" 个账号的歌单",idx*3,[1,2,3])]));
       if(!chosenAccount.ContainsKey(key))chosenAccount[key]=p.Accounts[0].Key;
       Render();Notify("已增加一个模拟账号，仅内存");
      }),
      B("清空该平台演示账号",()=>{p.Accounts.Clear();chosenAccount.Remove(key);Render();Notify("平台当前零账号，其他平台未受影响");}),
      B("恢复默认演示账号",()=>ResetDemoAccounts(p))));
   outp.Children.Add(Panel(row));
  }
  return outp;
 }
 void ResetDemoAccounts(Platform p){
  if(p.Accounts.Count>0){chosenAccount[p.Key]=p.Accounts[0].Key;Render();Notify("此平台已经有账号，未覆盖现有演示账号");return;}
  p.Accounts.Add(new("restored",p.Name+" 新演示号","模拟只读","从未同步",
   [new("restored-list","独立恢复的歌单",5,[1,2,3])]));
  chosenAccount[p.Key]="restored";Render();Notify(p.Name+"已恢复一个演示账号");
 }
 Control PlayerBar(){
  var g=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"180,*,148":"255,*,255"),ColumnSpacing=12,Margin=new Thickness(16,10)};
  var songInfo=H(new Border{Width=50,Height=50,Background=R("CardBlue"),CornerRadius=new CornerRadius(10),
    Child=new TextBlock{Text="♫",FontSize=26,Foreground=R("Primary"),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center}},
    V(T(Track(nowSong).Title,14,false,true),T(Track(nowSong).Artist,11,true)));
  Grid.SetColumn(songInfo,0);g.Children.Add(songInfo);
  playButton=B(playing?"❚❚":"▶",TogglePlay,true,35);
  nowTime=T(Time((int)seconds)+" / "+Time(Track(nowSong).Duration),11,true);
  seek=new Slider{Minimum=0,Maximum=Track(nowSong).Duration,Value=seconds,Width=narrow?110:290};
  seek.PropertyChanged+=(_,e)=>{
   if(e.Property==RangeBase.ValueProperty&&!seekUpdating&&seek!=null&&Math.Abs(seconds-seek.Value)>.7){
    seconds=seek.Value;UpdatePlaybackText();
   }
  };
  var player=V(H(B("⏮",()=>Skip(-1)),playButton,B("⏭",()=>Skip(1)),seek,nowTime));
  player.Spacing=2;Grid.SetColumn(player,1);g.Children.Add(player);
  var right=H(B("歌词",()=>Go("正在播放")),B(queueOpen?"关闭队列":"队列",()=>{ToggleQueue();}));
  if(!narrow){
   var slider=new Slider{Minimum=0,Maximum=1,Value=volume,Width=72};
   slider.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty)volume=slider.Value;};
   right.Children.Insert(0,slider);
   right.Children.Insert(0,B(favorites.Contains(nowSong)?"♥":"♡",()=>Like(nowSong)));
  }
  Grid.SetColumn(right,2);g.Children.Add(right);
  var wrapper=V(g);
  statusText=T(status,11,true);wrapper.Children.Add(statusText);
  return Panel(wrapper,4,true);
 }
 void UpdatePlaybackText(){
  if(nowTime!=null)nowTime.Text=Time((int)(seek is CommitSlider preview&&(preview.IsEditing||preview.IsCommitPending)?preview.Value:seconds));

 }
 void UpdatePlayback(){
  seekUpdating=true;
  try{
   UpdatePlaybackText();
   C3RefreshLyricHighlight();
   if(seek is CommitSlider committed){committed.Maximum=Math.Max(1,core.State.Duration>0?core.State.Duration:Track(nowSong).Duration);committed.Sync(seconds);}else if(seek!=null){seek.Value=seconds;}RefreshBarState();
   if(playButton!=null){if(c3ShownPlay!=playing){Enter(playButton,0);if(playButton is LiquidPlayButton liquid)liquid.RefreshGlyph();else playButton.Content=C3Icon(playing?"pause":"play",true);c3ShownPlay=playing;}AutomationProperties.SetName(playButton,playing?"暂停":"播放");}
   if(nowLyric!=null)nowLyric.Text=core.State.Lyrics.ElementAtOrDefault(core.State.LyricIndex)?.Text??"";
  }finally{seekUpdating=false;}
 }
 void Play(int id)=>RunCore(()=>page=="本地歌单"?core.PlaylistAsync("play",listId:ActiveListId,songId:id):core.PlayAsync(id));
 void Skip(int delta)=>RunCore(delta<0?core.PreviousAsync:core.NextAsync);
 void TogglePlay()=>RunCore(()=>core.ToggleAsync(selectedSong));
 void Like(int id)=>RunCore(()=>core.FavoriteAsync(id));
 void AddLocalSelected()=>ShowSongFlyout(Track(selectedSong));
 void SaveQueue()=>PlaylistNameDialog("save");
 async Task CopySongTitle(int id){
  try{if(Clipboard!=null)await Clipboard.SetTextAsync(Track(id).Title);Notify("已复制歌名："+Track(id).Title);}
  catch(Exception ex){Notify("复制失败："+ex.GetType().Name);}
 }
 static string Time(int duration)=>TimeSpan.FromSeconds(Math.Max(0,duration)).ToString(@"m\:ss");
 void Keyboard(object? sender,KeyEventArgs e){
  if(e.Key==Key.Escape){if(queueOpen)SetQueue(false);else if(songActionsOpen)CloseSongMenu();e.Handled=true;}
  if(e.Key==Key.O&&e.KeyModifiers.HasFlag(KeyModifiers.Control)){SelectImport(e.KeyModifiers.HasFlag(KeyModifiers.Shift));e.Handled=true;}
  if(e.Key==Key.K&&e.KeyModifiers.HasFlag(KeyModifiers.Control)){Go("本地音乐");e.Handled=true;}
  if(e.Key==Key.Space&&!IsInteractiveKeySource(e.Source)){TogglePlay();e.Handled=true;}
 }
}
