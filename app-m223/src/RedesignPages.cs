using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 LiquidSelector? categoryLens;Border? libraryHost;
 Control C3Home()=>gallery?GalleryHome():StudyHome();
 Control ArtTile(int id,double size){
  var s=Track(id);var body=V(Cover(id,size,size),T(s.Title,13,false,true),T(s.Artist,11,true));body.Spacing=4;body.Width=size;
  var b=B("",()=>Play(id));b.Content=body;b.Background=Brushes.Transparent;b.Padding=new(4);b.HorizontalContentAlignment=HorizontalAlignment.Left;AutomationProperties.SetName(b,"播放封面 "+s.Title);ToolTip.SetTip(b,s.Title+" · "+s.Artist);return b;
 }
 Control StudyHome(){
  var title=Section("你的音乐书房","SOUND, COLLECTED.   /   本地演示档案",()=>Go("本地音乐"));
  var coverSize=narrow?188:220;
  var now=V(T("接着听",11,true,true),Cover(nowSong,coverSize,coverSize),T(Track(nowSong).Title,23,false,true),T(Track(nowSong).Artist+" · "+Track(nowSong).Album,12,true));now.Spacing=9;
  var play=B("继续播放",()=>Play(nowSong),true);play.Content=H(C3Icon("play",true),TPrimary("继续播放"));now.Children.Add(H(play,IconButton("lyrics","查看歌词",()=>Go("正在播放"))));
  var archive=Panel(now,18);archive.HorizontalAlignment=HorizontalAlignment.Left;
  var recent=V(Section("最近播放","从熟悉的旋律，再出发",()=>Go("历史")));recent.Spacing=14;
  var grid=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,*,*"),RowDefinitions=RowDefinitions.Parse("Auto,Auto"),ColumnSpacing=12,RowSpacing=13};
  var sz=narrow?116:126;var ids=history.Concat(new[]{6,3,8,7,5}).Distinct().Take(6).ToArray();for(int i=0;i<ids.Length;i++){var item=ArtTile(ids[i],sz);Grid.SetColumn(item,i%3);Grid.SetRow(item,i/3);grid.Children.Add(item);}recent.Children.Add(grid);
  var lead=new Grid{ColumnDefinitions=ColumnDefinitions.Parse($"{coverSize+50},*"),ColumnSpacing=narrow?18:26};lead.Children.Add(archive);Grid.SetColumn(recent,1);lead.Children.Add(recent);
  return V(title,lead,Section("常用歌单","为每一种心情，留一格位置",()=>Go("本地歌单")),PlaylistShelf());
 }
 TextBlock TPrimary(string text){var t=T(text,13,false,true);t.Foreground=HighContrast?Brushes.Black:Brushes.White;return t;}
 Control PlaylistShelf(){
  var g=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,*,*"),ColumnSpacing=14};int i=0;
  foreach(var key in localLists.Keys.Take(3).ToArray()){var name=key;var cover=localLists[key].FirstOrDefault()?.SongId??6;var b=B("",()=>{localList=name;Go("本地歌单");});b.Content=H(Cover(cover,58,58),V(T(name,14,false,true),T(localLists[key].Count+" 首 · 本地演示",11,true)));b.Padding=new(10);b.Background=CeramicBrush();b.HorizontalContentAlignment=HorizontalAlignment.Left;b.HorizontalAlignment=HorizontalAlignment.Stretch;AutomationProperties.SetName(b,name+" 本地歌单");Grid.SetColumn(b,i++);g.Children.Add(b);}return g;
 }
 Control GalleryHome(){
  var w=narrow?234:320;var small=Math.Min(470,Math.Max(200,((Bounds.Width>0?Bounds.Width:Width)-(narrow?28:56)-w-44)/2));
  var header=V(T("THE PRIVATE SOUND GALLERY",11,true,true),T("让声音，成为风景。",narrow?30:38,false,true),T("原创封面与合成曲目 · 没有在线推荐",12,true));
  var feature=V(Cover(nowSong,w,w),T(Track(nowSong).Title,24,false,true),T(Track(nowSong).Artist,12,true),H(B("继续播放",()=>Play(nowSong),true),C3TextButton("走进歌词舞台",()=>Go("正在播放"))));feature.Spacing=10;
  var mosaic=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("Auto,*,*"),RowDefinitions=RowDefinitions.Parse("Auto,Auto"),ColumnSpacing=22,RowSpacing=18};Grid.SetRowSpan(feature,2);mosaic.Children.Add(feature);
  int[] ids=[4,6,3,8];for(int i=0;i<ids.Length;i++){var id=ids[i];var body=V(Cover(id,small,i<2?168:145),T(Track(id).Title,15,false,true),T(Track(id).Album,11,true));body.Spacing=6;var b=B("",()=>Play(id));b.Content=body;b.Padding=new(0);b.Background=Brushes.Transparent;b.HorizontalAlignment=HorizontalAlignment.Left;AutomationProperties.SetName(b,"画廊播放 "+Track(id).Title);Grid.SetColumn(b,1+i%2);Grid.SetRow(b,i/2);mosaic.Children.Add(b);}
  return V(header,mosaic,Section("收藏的片段","YOUR PLAYLISTS",()=>Go("本地歌单")),PlaylistShelf());
 }
 Control LocalLibrary(){
  var keys=new[]{"全部歌曲","专辑","歌手","歌单"};if(!keys.Contains(kind))kind="全部歌曲";
  libraryHost=new Border();categoryLens=new LiquidSelector(keys,Array.IndexOf(keys,kind),i=>{kind=keys[i];if(libraryHost!=null)libraryHost.Child=LibraryBody();},MotionEnabled,R("Surface2"),GlassBrush(),PearlBrush(),R("Text")){Name="LibraryLens"};
  libraryHost.Child=LibraryBody();return V(PageHeading("音乐资料库","YOUR LIBRARY  /  48首合成曲目，不读取磁盘"),categoryLens,libraryHost);
 }
 Control LibraryBody(){
  if(kind=="专辑")return AlbumGrid();if(kind=="歌手")return ArtistGrid();if(kind=="歌单")return V(Section("本地歌单","不会与平台歌单混合"),PlaylistShelf(),B("管理歌单",()=>Go("本地歌单")));
  var found=songs.Where(s=>string.IsNullOrWhiteSpace(query)||new[]{s.Title,s.Artist,s.Album}.Any(v=>v.Contains(query,StringComparison.OrdinalIgnoreCase))).ToArray();
  return V(C2Toolbar($"歌曲 / {found.Length:00}",true),found.Length==0?Panel(V(T("没有找到匹配的音乐",20,false,true),T("试试歌名、歌手或专辑中的其他文字。",13,true),B("清除搜索",()=>{query="";Render();}))):SongTable(found));
 }
 Control ArtistsAndAlbums(){kind=kind=="歌手"?"歌手":"专辑";return LocalLibrary();}
 Control AlbumGrid(){
  var grid=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"*,*,*":"*,*,*,*"),RowDefinitions=RowDefinitions.Parse("Auto,Auto"),ColumnSpacing=20,RowSpacing=22};int i=0;int cols=narrow?3:4;
  foreach(var group in songs.GroupBy(x=>x.Album)){var id=group.First().Id;var name=group.Key;var body=V(Cover(id,narrow?170:186,narrow?170:186),T(name,16,false,true),T(group.Count()+" 首 · "+group.First().Artist,11,true));var b=B("",()=>{query=name;kind="全部歌曲";Render();});b.Content=body;b.Padding=new(2);b.Background=Brushes.Transparent;AutomationProperties.SetName(b,"专辑 "+name);Grid.SetColumn(b,i%cols);Grid.SetRow(b,i/cols);grid.Children.Add(b);i++;}return grid;
 }
 Control ArtistGrid(){
  var content=V();foreach(var group in songs.GroupBy(s=>s.Artist)){var name=group.Key;var row=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("72,*,Auto"),ColumnSpacing=18};row.Children.Add(Cover(group.First().Id,62,62));var text=V(T(name,17,false,true),T(group.Count()+" 首演示作品",12,true));Grid.SetColumn(text,1);row.Children.Add(text);var b=C3TextButton("查看作品",()=>{query=name;kind="全部歌曲";Render();});Grid.SetColumn(b,2);row.Children.Add(b);content.Children.Add(Panel(row,12));}return content;
 }
 Control C2Toolbar(string title,bool import){
  var row=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,Auto"),ColumnSpacing=16};row.Children.Add(T(title,13,true,true));var actions=H(IconButton("play","播放选中",()=>Play(selectedSong),true),IconButton("plus","追加到队列",()=>{queue.Add(new(selectedSong,"本地",""));Notify("已追加到临时队列（模拟）");}),B("加入歌单",()=>{var selected=mainSongs?.SelectedItems?.OfType<Song>().ToArray()??[];if(selected.Length==0)AddLocalSelected();else foreach(var song in selected){selectedSong=song.Id;AddLocalSelected();}}));
  if(import)actions.Children.Add(B("导入演示",()=>Notify("候选不读取文件；仅使用48首内存合成曲目")));Grid.SetColumn(actions,1);row.Children.Add(actions);return row;
 }
 Control C2SongTable(Song[] list){
  var box=new ListBox{ItemsSource=list,Height=narrow?360:408,Background=Brushes.Transparent,BorderThickness=new(0),SelectedItem=list.FirstOrDefault(s=>s.Id==selectedSong),Name="SongTable",SelectionMode=SelectionMode.Multiple};
  box.ItemTemplate=new FuncDataTemplate<Song>((s,_)=>{
   if(s==null)return T("无效歌曲",13,true);
   var row=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"32,42,*,100,42,36":"32,42,*,180,50,36"),ColumnSpacing=12,MinHeight=48,Margin=new(2,2)};
   row.Children.Add(T(s.Id==nowSong&&playing?"▶":s.Id.ToString("00"),11,true));var cover=Cover(s.Id,36,36);Grid.SetColumn(cover,1);row.Children.Add(cover);var text=V(T(s.Title,13,false,true),T(s.Artist,11,true));text.Spacing=2;Grid.SetColumn(text,2);row.Children.Add(text);var album=T(s.Album,12,true);Grid.SetColumn(album,3);row.Children.Add(album);var duration=T(Time(s.Duration),11,true);Grid.SetColumn(duration,4);row.Children.Add(duration);
   Button more=null!;more=IconButton("more","歌曲操作 "+s.Title,()=>ShowSongFlyout(s,more));Grid.SetColumn(more,5);row.Children.Add(more);return row;
  },true);
  box.SelectionChanged+=(_,_)=>{if(box.SelectedItem is Song s)selectedSong=s.Id;};box.DoubleTapped+=(_,_)=>{if(box.SelectedItem is Song s)Play(s.Id);};box.KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&box.SelectedItem is Song s){Play(s.Id);e.Handled=true;}};mainSongs=box;
  var columns=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"32,42,*,100,42,36":"32,42,*,180,50,36"),ColumnSpacing=12,Margin=new(12,7)};foreach(var (label,col) in new[]{("#",0),("歌曲 / 歌手",2),("专辑",3),("时长",4)}){var t=T(label,10,true);Grid.SetColumn(t,col);columns.Children.Add(t);}return V(columns,new Border{Height=1,Background=R("Line")},box);
 }
 Control LocalPlaylists(){
  var entries=localLists.GetValueOrDefault(localList)??[];var id=entries.FirstOrDefault()?.SongId??5;
  var detail=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"150,*":"190,*"),ColumnSpacing=26};detail.Children.Add(Cover(id,narrow?144:180,narrow?144:180));
  var text=V(T("LOCAL PLAYLIST / 本地内存演示",10,true,true),T(localList,30,false,true),T(entries.Count+" 首 · 与平台账号独立 · 关闭不永久保留",12,true));var play=B("播放歌单",()=>{if(entries.Count>0)Play(entries[0].SongId);else Notify("歌单为空");},true);text.Children.Add(H(play,B("保存队列快照（演示）",SaveQueue)));Grid.SetColumn(text,1);detail.Children.Add(text);
  var lists=new WrapPanel{Orientation=Orientation.Horizontal};foreach(var key in localLists.Keys.ToArray()){var name=key;var b=C3TextButton(name,()=>{localList=name;Render();});b.Margin=new(0,0,6,0);if(name==localList)b.Classes.Add("selected");lists.Children.Add(b);}
  var field=new TextBox{PlaceholderText="新歌单名称",Width=170};AutomationProperties.SetName(field,"新歌单名称");var manage=new WrapPanel();
  foreach(var c in new Control[]{field,B("新建歌单",()=>{var name=(field.Text??"").Trim();if(name.Length==0||localLists.ContainsKey(name)){Notify("请输入未使用的名称");return;}localLists[name]=[];localList=name;Render();}),B("添加选中歌曲",AddLocalSelected),B("移除选中歌曲",()=>{entries.RemoveAll(x=>x.SongId==selectedSong);Render();Notify("仅修改内存演示歌单");}),B("批量操作说明",()=>Notify("列表支持Ctrl/Shift多选，曲库工具栏可批量加入内存歌单；没有永久保存或真实文件操作")),B("删除当前歌单",()=>{if(localLists.Count<=1){Notify("至少保留一个演示歌单");return;}localLists.Remove(localList);localList=localLists.Keys.First();Render();Notify("仅移除内存演示歌单，不删除音频");})}){c.Margin=new(0,0,8,6);manage.Children.Add(c);}
  return V(detail,lists,manage,T("队列快照保存仅在预览内存中有效，不代表数百首永久保存已实现。",11,true),entries.Count>0?SongTable(entries.Select(x=>Track(x.SongId)).ToArray()):Panel(V(T("这个歌单，等待第一首歌",19,false,true),B("前往曲库",()=>Go("本地音乐")))));
 }
 Control DesignSettings(){
  var strength=new Slider{Minimum=0,Maximum=1,Value=materialStrength,Width=260};AutomationProperties.SetName(strength,"材质强度");strength.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty)materialStrength=strength.Value;};
  var visual=Panel(V(T("外观调音台",20,false,true),T("材质强度 · 作用于局部表面，不是光学折射",12,true),strength,B("应用材质强度",Render),H(B("A · 唱片书房",()=>SetDesign(false)),B("B · 声音画廊",()=>SetDesign(true)))),18);
  return V(visual,SettingsPage(),Panel(V(T("扩展与安全",18,false,true),T("插件管理仅为未来入口；未实现安装、登录、同步或真实账号操作。",12,true),B("查看扩展管理",()=>Go("扩展管理")))));
 }
 Control ExtensionsPage(){
  var result=V(PageHeading("让来源各归其位","EXTENSIONS / 架构展示，未接入DLC"),Panel(V(T("本地音乐始终独立",19,false,true),T("本预览没有插件安装器。所有平台、账号、权限与同步时间均为合成数据。",13,true))));
  foreach(var p in platforms.Values){var key=p.Key;var row=V(T(p.Name,20,false,true),T(p.Accounts.Count+" 个演示账号 · 平台歌单按账号分区",12,true));foreach(var a in p.Accounts)row.Children.Add(H(T(a.Alias,14,false,true),StatusLabel(a.State)));row.Children.Add(H(B("查看空间",()=>OpenPlatform(key)),B(p.Enabled?"隐藏导航演示":"显示导航演示",()=>{p.Enabled=!p.Enabled;Render();})));var install=B("安装DLC（尚未实现）",()=>{});install.IsEnabled=false;row.Children.Add(install);result.Children.Add(Panel(row,18));}return result;
 }
}
