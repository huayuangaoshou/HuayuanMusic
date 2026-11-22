using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
namespace HuayuanPreview;

public sealed partial class PreviewWindow {
 bool gallery; readonly Dictionary<string,Bitmap> artCache=new();
 readonly Dictionary<string,Geometry> iconCache=new();
 void DonorPalette(){
  var app=Application.Current!;
  var keys=new[]{"Base","Surface","Surface2","Text","Sub","Line","Primary","Selected","CardBlue"};
  var vals=dark?new[]{"#141820","#1E2530","#2A3240","#F1EFE9","#B0B8C6","#3A4556","#ADB8FF","#343D59","#2B3851"}
   :new[]{"#F5F3EF","#FFFDF9","#EBEBF3","#252B3C","#656B7A","#DEDFE6","#5363AD","#E5E8F6","#E4E7F2"};
  for(int i=0;i<keys.Length;i++)app.Resources[keys[i]]=BrushFrom(vals[i]);
  if(HighContrast){foreach(var k in new[]{"Base","Surface","Surface2"})app.Resources[k]=Brushes.Black;foreach(var k in new[]{"Text","Sub","Line"})app.Resources[k]=Brushes.White;app.Resources["Primary"]=Brushes.Yellow;app.Resources["Selected"]=Brush.Parse("#333333");}
  app.RequestedThemeVariant=HighContrast||dark?ThemeVariant.Dark:ThemeVariant.Light;
  app.Resources["Ink"]=R("Text");app.Resources["Hover"]=R("Surface2");app.Resources["Edge"]=R("Line");app.Resources["Accent"]=R("Primary");app.Resources["PrimaryInk"]=HighContrast?Brushes.Black:Brushes.White;app.Resources["PrimaryHover"]=HighContrast?Brushes.Yellow:BrushFrom("#5666AE");
  foreach(var k in new[]{"ButtonForeground","ButtonForegroundPointerOver","ButtonForegroundPressed","TextControlForeground","ComboBoxForeground","MenuItemForeground"})app.Resources[k]=R("Text");
  foreach(var k in new[]{"ButtonBackgroundPointerOver","ButtonBackgroundPressed","ComboBoxBackground","MenuFlyoutPresenterBackground"})app.Resources[k]=R("Surface2");
  app.Resources["StatusError"]=BrushFrom(HighContrast?"#FFFF00":dark?"#FFBDC6":"#9B2B44");app.Resources["StatusSuccess"]=BrushFrom(HighContrast?"#FFFFFF":dark?"#99DBC7":"#28634F");app.Resources["StatusWarning"]=BrushFrom(HighContrast?"#FFFF00":dark?"#E9CEA2":"#79541F");
  app.Resources["TextControlPlaceholderOpacity"]=1d;ApplyWindowMaterial();Background=NativeMica?Brushes.Transparent:R("Base");
 }
 void SetDesign(bool b){gallery=b;c31CoverOnly=false;Render();Notify(b?"B · 声音画廊 / 可运行构图对照":"A · 唱片书房 / 推荐全页面方案");Event("DESIGN_SWITCH",gallery?"B":"A");}
 void DonorRender(){
  Palette();c3LyricViewport=null;mainSongs=null;c31QueueSurface=null;
  var rail=narrow||page=="正在播放";var side=gallery?0:rail?72:196;
  root=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(gallery?"*":$"{side},*"),RowDefinitions=RowDefinitions.Parse("Auto,*,Auto,Auto"),Background=WindowFill()};
  var top=C3Top();Grid.SetColumnSpan(top,gallery?1:2);root.Children.Add(top);
  if(!gallery){var nav=C2Sidebar();Grid.SetRow(nav,1);root.Children.Add(nav);}
  var body=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(queueOpen&&!narrow?"*,316":"*"),ColumnSpacing=18,Margin=new Thickness(narrow?14:24,16,narrow?14:28,16)};
  var scroller=new ScrollViewer{Content=BuildPage(),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
  body.Children.Add(scroller);
  if(queueOpen){var q=Panel(C3QueuePanel(),16);c31QueueSurface=q;if(narrow){q.Width=332;q.HorizontalAlignment=HorizontalAlignment.Right;q.VerticalAlignment=VerticalAlignment.Stretch;q.BoxShadow=MaterialsEnabled?BoxShadows.Parse("-8 0 24 0 #18202B24"):new();}else Grid.SetColumn(q,1);body.Children.Add(q);}
  Grid.SetColumn(body,gallery?0:1);Grid.SetRow(body,1);root.Children.Add(body);
  var player=C3PlayerBar();Grid.SetRow(player,2);Grid.SetColumnSpan(player,gallery?1:2);root.Children.Add(player);
  statusText=T(status,10,true);statusText.HorizontalAlignment=HorizontalAlignment.Right;
  var footer=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("Auto,*"),Margin=new Thickness(18,3)};footer.Children.Add(T("C3.1 REDESIGN  /  离线 · 无声 · 内存模拟",10,true));Grid.SetColumn(statusText,1);footer.Children.Add(statusText);Grid.SetRow(footer,3);Grid.SetColumnSpan(footer,gallery?1:2);root.Children.Add(footer);
  Content=root;ComposePage(body,scroller);if(c31QueueSurface!=null)Enter(c31QueueSurface,14);UpdatePlayback();
 }
 Control C3Top(){
  var bar=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"64,*,Auto":"196,*,Auto"),ColumnSpacing=16,Margin=new Thickness(20,14,22,6)};
  var brand=H(new Border{Width=34,Height=34,CornerRadius=new(10),Background=R("Primary"),Child=C3Icon("logo",true)});
  if(!narrow)brand.Children.Add(V(T("huayuan",19,false,true),T("MUSIC  /  私人声音档案",9,true)));
  bar.Children.Add(brand);
  var search=new TextBox{PlaceholderText="搜索你的音乐、歌手、专辑",Text=query,MaxWidth=360,MinWidth=180,HorizontalAlignment=HorizontalAlignment.Left,FontSize=13,CornerRadius=new(18),Padding=new(15,8)};
  AutomationProperties.SetName(search,"搜索本地演示曲库");search.TextChanged+=(_,_)=>query=search.Text??"";search.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){kind="全部歌曲";Go("本地音乐");}};
  Grid.SetColumn(search,1);bar.Children.Add(search);
  var a=B(narrow?"A":"A · 唱片书房",()=>SetDesign(false));var b=B(narrow?"B":"B · 声音画廊",()=>SetDesign(true));
  a.Classes.Add(!gallery?"selected":"quiet");b.Classes.Add(gallery?"selected":"quiet");AutomationProperties.SetName(a,"设计 A");AutomationProperties.SetName(b,"设计 B");
  var actions=H(a,b,IconButton("settings","设置",()=>Go("设置")));actions.Spacing=4;Grid.SetColumn(actions,2);bar.Children.Add(actions);
  if(!gallery)return bar;
  var tabs=H();foreach(var (title,target) in new[]{("音乐画廊","首页"),("资料库","本地音乐"),("歌单","本地歌单"),("我喜欢的","收藏"),("播放历史","历史"),("平台演示","平台"),("歌词舞台","正在播放"),("扩展管理","扩展管理")}){var p=target;var button=C3TextButton(title,()=>Go(p));if(page==p)button.Classes.Add("selected");tabs.Children.Add(button);}
  return V(bar,new ScrollViewer{Content=tabs,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new(24,0,24,0)});
 }
 Control C2Sidebar(){
  var rail=narrow||page=="正在播放";var nav=V();nav.Spacing=5;
  if(!rail)nav.Children.Add(T("音乐资料库",10,true,true));
  foreach(var (name,key,icon) in new[]{("此刻","首页","home"),("本地曲库","本地音乐","music"),("专辑与歌手","歌手专辑","album"),("我喜欢的","收藏","heart"),("播放历史","历史","history")})nav.Children.Add(RDNav(name,key,icon,rail));
  nav.Children.Add(new Border{Height=12});if(!rail)nav.Children.Add(H(T("本地歌单",10,true,true),IconButton("plus","本地歌单",()=>Go("本地歌单"))));
  foreach(var n in localLists.Keys.Take(6).ToArray()){var name=n;var button=RDNav(name,"本地歌单","playlist",rail,()=>{localList=name;Go("本地歌单");},page=="本地歌单"&&localList==name);nav.Children.Add(button);}
  nav.Children.Add(new Border{Height=12});if(!rail)nav.Children.Add(T("平台空间 · 全部演示",10,true,true));
  foreach(var p in platforms.Values.Where(x=>x.Enabled)){var key=p.Key;nav.Children.Add(RDNav(p.Name,"平台","provider",rail,()=>OpenPlatform(key),page.StartsWith("平台")&&provider==key));}
  nav.Children.Add(new Border{Height=14});nav.Children.Add(RDNav("外观与设置","设置","settings",rail));nav.Children.Add(RDNav("扩展管理","扩展管理","grid",rail));
  return new ScrollViewer{Content=nav,Margin=new Thickness(rail?9:16,20,8,12),VerticalScrollBarVisibility=ScrollBarVisibility.Hidden};
 }
 Button RDNav(string label,string dest,string icon,bool compact,Action? action=null,bool? selected=null){
  var active=selected??page==dest;var button=B("",action??(()=>Go(dest)));var glyph=C3Icon(icon);glyph.Width=18;glyph.Height=18;
  var row=H(glyph);if(!compact)row.Children.Add(T(label,13,!active,active));row.Spacing=12;
  button.Content=row;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.HorizontalAlignment=HorizontalAlignment.Stretch;button.Padding=new(11,10);button.Background=active?R("Selected"):Brushes.Transparent;button.Classes.Add("nav");if(active)button.Classes.Add("selected");ToolTip.SetTip(button,label);AutomationProperties.SetName(button,label);return button;
 }
 Button IconButton(string icon,string label,Action action,bool primary=false){var b=B("",action,primary);b.Content=C3Icon(icon,primary);b.Width=36;b.Height=36;b.Padding=new(7);b.CornerRadius=new(18);ToolTip.SetTip(b,label);AutomationProperties.SetName(b,label);return b;}
 Control DonorIcon(string name,bool primary=false){
  var path=name switch{
   "play"=>"M8 4L20 12L8 20Z","pause"=>"M8 5V19 M16 5V19","stop"=>"M6 6H18V18H6Z",
   "prev"=>"M5 5V19 M19 5L8 12L19 19Z","next"=>"M19 5V19 M5 5L16 12L5 19Z",
   "logo"=>"M5 5V19 M19 5V19 M5 12Q12 4 19 12 M5 12Q12 20 19 12",
   "home"=>"M3 11L12 3L21 11 M5 10V21H10V15H14V21H19V10",
   "music"=>"M10 18V5L20 3V16 M10 18C10 23 2 23 2 18C2 14 10 14 10 18 M20 16C20 21 12 21 12 17C12 13 20 12 20 16",
   "album"=>"M4 4H20V20H4Z M12 8A4 4 0 1 0 12 16A4 4 0 1 0 12 8 M12 11V13",
   "heart"=>"M12 21C8 18 2 13 2 8C2 3 8 2 12 6C16 2 22 3 22 8C22 13 16 18 12 21Z",
   "history"=>"M3 10A9 9 0 1 1 5 19 M3 4V10H9 M12 7V12L16 15",
   "playlist"=>"M3 5H21 M3 11H21 M3 17H14","plus"=>"M12 4V20 M4 12H20",
   "lyrics"=>"M4 4H20V17H12L7 21V17H4Z M8 8H16 M8 12H14",
   "queue"=>"M3 5H21 M3 11H21 M3 17H12 M17 15L22 18L17 21Z",
   "settings"=>"M12 3L15 5L19 5L19 9L21 12L19 15V19H15L12 21L9 19H5V15L3 12L5 9V5H9Z M12 8A4 4 0 1 0 12 16A4 4 0 1 0 12 8",
   "close"=>"M6 6L18 18 M18 6L6 18","more"=>"M4 12H5 M11 12H12 M18 12H19",
   "volume"=>"M3 9H7L12 5V19L7 15H3Z M16 8Q21 12 16 16 M19 5Q26 12 19 19",
   "mute"=>"M3 9H7L12 5V19L7 15H3Z M16 9L22 15 M22 9L16 15",
   "arrow"=>"M5 12H19 M13 6L19 12L13 18","folder"=>"M3 6H10L12 8H21V20H3Z",
   _=>"M4 4H10V10H4Z M14 4H20V10H14Z M4 14H10V20H4Z M14 14H20V20H14Z"};
  if(name=="logo"&&c31LogoVariant)path="M5 7V19 M19 7V19 M5 13H19 M12 2L15 6L12 10L9 6Z";
  if(!iconCache.TryGetValue(path,out var geometry))iconCache[path]=geometry=Geometry.Parse(path);
  return new Avalonia.Controls.Shapes.Path{Data=geometry,Stroke=primary?(HighContrast?Brushes.Black:Brushes.White):R("Text"),Fill=name=="play"?(primary?(HighContrast?Brushes.Black:Brushes.White):R("Text")):Brushes.Transparent,StrokeThickness=1.6,StrokeLineCap=PenLineCap.Round,StrokeJoin=PenLineJoin.Round,Width=21,Height=21,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
 }
 Control DonorCover(int id,double width=54,double height=54,bool subtle=false){
  if(c31MissingCover||id<=0)return CoverPlaceholder(width,height);
  string key=(Math.Abs(width-height)>10?"landscape-":"")+((id-1)%8);if(!artCache.TryGetValue(key,out var bitmap))artCache[key]=bitmap=new Bitmap(AssetLoader.Open(new Uri($"avares://HuayuanMusic.P17.Nightflight/Assets/Art/{key}.png")));
  return new Border{Width=width,Height=height,CornerRadius=new(width>=200?12:7),ClipToBounds=true,Child=new Image{Source=bitmap,Stretch=Stretch.UniformToFill}};
 }
 Control BuildPage()=>page switch{
  "首页"=>C3Home(),"本地音乐"=>LocalLibrary(),"歌手专辑"=>ArtistsAndAlbums(),"本地歌单"=>LocalPlaylists(),"收藏"=>V(PageHeading("喜欢，值得再听一次","LOCAL FAVORITES / 独立本地收藏"),FavoritesPage()),"历史"=>V(PageHeading("最近听过","LISTENING HISTORY / 仅内存模拟"),HistoryPage()),
  "平台"=>V(PageHeading(CurrentPlatform.Name,"PLATFORM SPACE / 演示账号，不联网"),C2Platform()),"平台歌单"=>PlatformPlaylistPage(),"正在播放"=>C3Lyrics(),"设置"=>V(PageHeading("让音乐，以你的方式呈现","APPEARANCE & EXPERIENCE"),DesignSettings()),"扩展管理"=>ExtensionsPage(),_=>C3Home()};
 Control PageHeading(string title,string eyebrow)=>V(T(eyebrow,10,true,true),T(title,narrow?25:29,false,true));
 Control Section(string title,string sub,Action? more=null){var row=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,Auto")};row.Children.Add(V(T(title,21,false,true),T(sub,11,true)));if(more!=null){var b=C3TextButton("查看全部",more,12);Grid.SetColumn(b,1);row.Children.Add(b);}return row;}
 Control C3PlayerBar(){
  var grid=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"174,*,178":"246,*,256"),ColumnSpacing=16,Margin=new Thickness(narrow?16:24,12,24,10)};
  var track=Track(nowSong);var metadata=V(T(track.Title,14,false,true),T(track.Artist,11,true));metadata.Width=narrow?95:164;metadata.Name="PlayerMetadata";var info=B("",()=>Go("正在播放"));info.Content=H(Cover(nowSong,56,56),metadata);info.Background=Brushes.Transparent;info.Padding=new(0);info.HorizontalContentAlignment=HorizontalAlignment.Left;AutomationProperties.SetName(info,"打开沉浸歌词");grid.Children.Add(info);
  playButton=IconButton(playing?"pause":"play",playing?"暂停":"播放",TogglePlay,true);playButton.Width=48;playButton.Height=48;playButton.CornerRadius=new(24);c3ShownPlay=null;playButton.Name="LiquidPlay";ConfigureMotion(playButton);var liquid=playButton;liquid.PointerMoved+=(_,e)=>{if(MotionEnabled){var at=e.GetPosition(liquid);var x=(at.X/liquid.Bounds.Width-.5)*2;var y=(at.Y/liquid.Bounds.Height-.5)*2;liquid.RenderTransform=TransformOperations.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture,"translate({0}px, {1}px) scale(1.015, .99)",x,y));}};
  var controls=H(IconButton("prev","上一首",()=>Skip(-1)),playButton,IconButton("next","下一首",()=>Skip(1)),IconButton("stop","停止",()=>{playing=false;seconds=0;UpdatePlayback();}));controls.HorizontalAlignment=HorizontalAlignment.Center;controls.Spacing=12;
  seek=new Slider{Minimum=0,Maximum=track.Duration,Value=seconds,MinWidth=90,Height=24};AutomationProperties.SetName(seek,"播放进度");seek.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty&&!seekUpdating){seconds=seek.Value;UpdatePlaybackText();C3RefreshLyricHighlight();}};
  nowTime=T(Time((int)seconds),10,true);var progress=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("36,*,36"),ColumnSpacing=8};progress.Children.Add(nowTime);Grid.SetColumn(seek,1);progress.Children.Add(seek);var end=T(Time(track.Duration),10,true);Grid.SetColumn(end,2);progress.Children.Add(end);
  var middle=V(controls,progress);middle.Spacing=1;Grid.SetColumn(middle,1);grid.Children.Add(middle);
  var like=IconButton("heart",favorites.Contains(nowSong)?"取消收藏":"收藏",()=>Like(nowSong));if(favorites.Contains(nowSong))like.Background=R("Selected");
  var volumeButton=IconButton(volume==0?"mute":"volume","静音",()=>{volume=volume==0?.6:0;Render();});
  Slider VolumeSlider(double width){var slider=new Slider{Minimum=0,Maximum=1,Value=volume,Width=width,Height=22};AutomationProperties.SetName(slider,"音量");slider.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty){volume=slider.Value;volumeButton.Content=C3Icon(volume==0?"mute":"volume");}};return slider;}
  var right=H(like,volumeButton);if(!narrow)right.Children.Add(VolumeSlider(60));
  right.Children.Add(IconButton("lyrics","歌词",()=>Go("正在播放")));var queueButton=IconButton("queue",queueOpen?"关闭队列":"队列",ToggleQueue);if(queueOpen)queueButton.Classes.Add("selected");right.Children.Add(queueButton);right.HorizontalAlignment=HorizontalAlignment.Right;
  Control rightContent=right;if(narrow){var slider=VolumeSlider(86);slider.HorizontalAlignment=HorizontalAlignment.Right;rightContent=V(right,slider);}
  Grid.SetColumn(rightContent,2);grid.Children.Add(rightContent);
  return new Border{Background=GlassBrush(),BorderBrush=R("Line"),BorderThickness=new(0,1,0,0),Child=grid};
 }
}
