using HuayuanMusic.Provider.Contracts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Animation;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
namespace HuayuanPreview.R2;
public sealed record R2Page(string DisplayName,Control View);
public sealed partial class WorkspaceState:ObservableObject {
 public ShellModel Host {get;}public bool Local {get;}public bool Allowed=>Local;public bool IsKugou=>!Local;public string Name=>Local?"本地音乐":"酷狗概念版";
 public List<R2Page> Pages {get;}=[];public Dictionary<R2Page,ListBox> Lists {get;}=[];private readonly Stack<BrowsePoint> back = new();
 private readonly record struct SongViewKey(R2Page Page, string? CollectionId);
 private readonly Dictionary<SongViewKey,InteractionController.ViewState> positions=[];
 private SongViewKey? onlineListKey;
 public R2Page CurrentPage {get;private set;}=null!;public R2Page DetailPage {get;}public R2Page SearchPage {get;}public R2Page QueuePage {get;}public R2Page CatalogPage {get;}
 public long SelectedPlaylistId {get;private set;}public int PageNumber {get;private set;}
 string search="";public string SearchTerm {get=>search;set=>SetProperty(ref search,value);}
 public Control Root {get;}
 public Control Navigation {get;}
 private readonly StackPanel playlistNav = new() { Spacing = 2 };
 private readonly Dictionary<Button, R2Page> navPages = [];
 private readonly Dictionary<Button, long> navPlaylists = [];
 private readonly Stack<BrowsePoint> forward = new();
 private sealed record BrowsePoint(R2Page Page, long Playlist, string Filter);
 private readonly ContentControl hero = new();
 private R2Page? lyricsPage, pluginPage;
 private R2Page? filterPage;
 public string FilterText { get; private set; } = "";
 readonly ContentControl content=new();readonly TextBlock heading=new(){FontSize=23,Margin=new Thickness(0,0,0,4)};readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};readonly WrapPanel gallery=new(){ItemWidth=188};
 readonly Dictionary<string,TrackRow> rowViews=[];PlaybackSnapshot? previous;readonly R2Page? favoritesPage,historyPage;TrackRow[] CatalogRows=>Local?Host.Player.State.Tracks.Where(t=>!t.Key.StartsWith("kugou-concept:",StringComparison.Ordinal)).Select(t=>Row(t)).ToArray():conceptRows;
 TrackRow[] conceptRows=[];
 PlatformPlaylistSnapshot? displayedSnapshot;
 string? providerPlaylistId;
 readonly R2Page galleryPage;
 readonly TextBlock galleryEmpty=new(){Text="还没有歌单",Classes={"muted"},Margin=new Thickness(6,12)};
 readonly Button prevPage=new(){Content="上一页"}, nextPage=new(){Content="下一页"};
 readonly StackPanel searchPanelHost;
 public WorkspaceState(ShellModel host,bool local){Host=host;Local=local;
  var nav=new StackPanel{Spacing=4,Margin=new Thickness(0)};
  Navigation = nav;
  heading.FontSize = DesignTokens.PageTitleMin; // R10 页面大标题 36px（36~42 区间）
  heading.FontWeight = FontWeight.SemiBold;
  heading.LineHeight = DesignTokens.PageTitleMin * DesignTokens.LineHeightTitle; // R11 大标题行高 1.08
  heading.Margin = new Thickness(0, 0, 0, 28); // R10 大标题下缘 margin 28px
  status.Classes.Add("muted");
  status.FontSize = DesignTokens.FontSm; // R10 副标题 14px
  status.IsVisible = !Program.Desktop;
  status.Margin = new Thickness(0,0,0,4);
  // R11: 进场编排——内容卡 12px 上移 + 淡入；退场更快（进场 60%）。
  content.Transitions = new()
  {
   new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(DesignTokens.MotionView), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
   new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(DesignTokens.MotionView), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }
  };
  content.RenderTransform = TransformOperations.Parse($"translateY({DesignTokens.ChoreoOffsetPx}px)");
  // R10 布局：史诗级留白——上 64 / 左右 64（≥5vw）/ 底部 96（给悬浮播放条让位）。
  var area=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,*"),Margin=new Thickness(DesignTokens.ContentPadSideMin,DesignTokens.ContentPadTop,DesignTokens.ContentPadSideMin,DesignTokens.ContentPadBottom)};
  area.Children.Add(heading);Grid.SetRow(status,1);area.Children.Add(status);Grid.SetRow(content,2);area.Children.Add(content);
  // R10 布局：内容最大宽 1336px 居中，超宽屏不无限拉伸。
  Root = new Border { MaxWidth = DesignTokens.ContentMaxWidth, HorizontalAlignment = HorizontalAlignment.Stretch, Child = area };
  if(Program.Desktop) AddDesktopNavigation(nav);
  if (Local && Program.Desktop)
  {
   var import = new Button { Content = "添加本地音乐", Classes = { "nav" } };
   import.Click += async (_, _) =>
   {
    if (TopLevel.GetTopLevel(import) is not { } top)
     return;
    var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
     Title = "选择音乐文件",
     AllowMultiple = true,
     FileTypeFilter = [new FilePickerFileType("音乐文件") { Patterns = ["*.mp3", "*.flac", "*.wav", "*.m4a", "*.ogg", "*.opus", "*.aac"] }]
    });
    var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
    if (paths.Length > 0)
     Host.Player.Import(paths);
   };
   nav.Children.Add(import);
  }
  CatalogPage=SongPage(Local?"本地曲库":"已同步曲目",CatalogRows,Local);AddNav(nav,CatalogPage);
  var cardBody=new StackPanel{Spacing=8};
  if(Local&&Program.Desktop)cardBody.Children.Add(NewPlaylistButton());
  cardBody.Children.Add(gallery);
  cardBody.Children.Add(galleryEmpty);
  galleryPage=new R2Page("歌单",new ScrollViewer{Content=cardBody});AddNav(nav,galleryPage);
  DetailPage=SongPage("歌单详情",[],Local);Pages.Add(DetailPage);var panel=(Grid)DetailPage.View;panel.RowDefinitions.Insert(0,new RowDefinition(GridLength.Auto));foreach(var child in panel.Children)Grid.SetRow(child,Grid.GetRow(child)+1);panel.Children.Add(hero);panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));var paging=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};prevPage.Click+=(_,_)=>SetPage(PageNumber-1);nextPage.Click+=(_,_)=>SetPage(PageNumber+1);paging.Children.Add(prevPage);paging.Children.Add(nextPage);
  if(P2AWorkspace.M212SnapshotPreview&&!Local){
   var jumpBox=new TextBox{PlaceholderText="歌单成员序号",Width=128};
   Avalonia.Automation.AutomationProperties.SetName(jumpBox,"歌单成员序号");
   var jumpButton=new Button{Content="定位记录"};
   jumpButton.Click+=(_,_)=>{
    var playlist=displayedSnapshot?.Playlists.FirstOrDefault(p=>p.ProviderPlaylistId==providerPlaylistId);
    if(playlist==null||!int.TryParse(jumpBox.Text,out int ordinal)||
       ordinal<1||ordinal>playlist.Tracks.Count){
      status.Text="请输入当前歌单内有效的成员序号";return;
    }
    SetPage((ordinal-1)/50);
    Dispatcher.UIThread.Post(()=>{
      var scroller=Lists[DetailPage].GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
      if(scroller!=null)scroller.Offset=new Vector(scroller.Offset.X,Math.Max(0,((ordinal-1)%50-1)*44));
    },DispatcherPriority.Loaded);
   };
   paging.Children.Add(jumpBox);paging.Children.Add(jumpButton);
  }
  Grid.SetRow(paging,3);panel.Children.Add(paging);
  SearchPage=SongPage("搜索结果",CatalogRows);Pages.Add(SearchPage);
  searchPanelHost=new StackPanel();
  if(Local){favoritesPage=SongPage("收藏",[]);historyPage=SongPage("历史",[]);AddNav(nav,favoritesPage);AddNav(nav,historyPage);}
  var lyrics=new StackPanel{Spacing=16,Margin=new Thickness(24)};var title=new TextBlock{FontSize=22};title.DataContext=host.Player;title.Bind(TextBlock.TextProperty,new Binding("DisplayedPlayingSong.Name"));lyrics.Children.Add(title);var lines=new ItemsControl();lines.DataContext=host.Player;lines.Bind(ItemsControl.ItemsSourceProperty,new Binding("State.Lyrics"));lines.ItemTemplate=new FuncDataTemplate<CoreLyric>((l,_)=>new TextBlock{Text=l!.Text,FontSize=17,TextWrapping=TextWrapping.Wrap});lyrics.Children.Add(lines);lyricsPage=new R2Page("歌词",new ScrollViewer{Content=lyrics});if(!Program.Desktop)AddNav(nav,lyricsPage);
  QueuePage=new R2Page("混合队列",new QueuePreview{DataContext=host});if(!Program.Desktop)AddNav(nav,QueuePage);
  if(!Local){AddNav(nav,new R2Page("我的账号",Host.Concept.AccountPage));Host.Concept.Changed+=RefreshConcept;RefreshConcept();}
  var playlistHeading = new StackPanel { Orientation=Orientation.Horizontal, Spacing=6, Margin=new Thickness(8,10,0,4) };
  playlistHeading.Children.Add(new TextBlock{Text="我的歌单",Classes={"muted"},FontSize=11,VerticalAlignment=VerticalAlignment.Center});
  if(Local&&Program.Desktop)playlistHeading.Children.Add(NewPlaylistButton());
  if(!Local&&Program.Desktop)onlineNavigation.Add(playlistHeading);
  nav.Children.Add(playlistHeading);
  nav.Children.Add(playlistNav);
  if(!Local&&Program.Desktop){onlineNavigation.Add(playlistNav);UpdateOnlineNavigation();}
  Navigate(Local?CatalogPage:Host.Concept.Snapshot!=null?galleryPage:Pages.Single(p=>p.DisplayName=="我的账号"),false);Refresh();
 }
 TrackRow Row(CoreTrack track,long member=0,long list=0){string key=member>0?$"m:{list}:{member}":"t:"+track.Id;if(!rowViews.TryGetValue(key,out var row)||row.Track!=track){row=new TrackRow(track,this,memberId:member,listId:list);rowViews[key]=row;}return row;}
 R2Page SongPage(string title,IEnumerable<TrackRow> rows,bool batchDeletable=false){var list=new ListBox{ItemTemplate=new FuncDataTemplate<TrackRow>((row,_)=>new SongRowView(),supportsRecycling:true),ItemsPanel=new FuncTemplate<Panel?>(()=>new VirtualizingStackPanel())};list.Classes.Add("songs");list.Styles.Add(new Style(s=>s.OfType<ListBoxItem>()){Setters={new Setter(ListBoxItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch)}});var grid=new Grid{RowDefinitions=new RowDefinitions("Auto,*")};var header=new SongTableHeader();grid.Children.Add(header);Grid.SetRow(list,1);grid.Children.Add(list);var page=new R2Page(title,grid);Lists.Add(page,list);var interactions=InteractionController.Attach(list,this,Host.Player,false);interactions.BatchDeletable=batchDeletable;header.DataContext=interactions;var empty = MakeSongEmptyState(title);
 grid.Children.Add(empty); Grid.SetRow(empty,1); emptySongPages.Add(page,(header,empty));
 StaggerListEntrance(list);
 Fill(page,rows);return page;}
 // R11: 列表行进场编排——逐行 +30ms 错峰，最多前 10 行，元素位移 12px + 淡入；余项直接显示。只动 Opacity/RenderTransform。
 void StaggerListEntrance(ListBox list){
  if(!Program.Desktop)return;
  var played=false;
  list.AttachedToVisualTree+=(_,_)=>{
   if(played)return;played=true;
   Dispatcher.UIThread.Post(()=>{
    var max=DesignTokens.ChoreoRowMax; // 前 10 行
    var count=Math.Min(list.ItemCount,max);
    for(var index=0;index<count;index++){
     if(list.ContainerFromIndex(index) is not ListBoxItem container)continue;
     container.Opacity=0;
     container.RenderTransform=TransformOperations.Parse($"translateY({DesignTokens.ChoreoOffsetPx}px)");
     container.Transitions=new Transitions{
      new DoubleTransition{Property=Visual.OpacityProperty,Duration=TimeSpan.FromMilliseconds(DesignTokens.MotionMicro),Easing=new Avalonia.Animation.Easings.CubicEaseOut()},
      new TransformOperationsTransition{Property=Visual.RenderTransformProperty,Duration=TimeSpan.FromMilliseconds(DesignTokens.MotionMicro),Easing=new Avalonia.Animation.Easings.CubicEaseOut()}
     };
     var i=index;
     _=Task.Delay(i*DesignTokens.ChoreoRowStagger).ContinueWith(_=>Dispatcher.UIThread.Post(()=>{
      if(!container.IsVisible)return;
      container.Opacity=1;
      container.RenderTransform=TransformOperations.Parse("translateY(0px)");
     }));
    }
   },DispatcherPriority.Loaded);
  };
 }
 void AddNav(StackPanel nav,R2Page page)
 {
  Pages.Add(page);
  var button=new Button{Content=page.DisplayName,Classes={"nav"}};
  button.Click+=(_,_)=>Navigate(page);
  navPages.Add(button,page);
  if(!Local&&Program.Desktop&&page.DisplayName!="我的账号")onlineNavigation.Add(button);
  nav.Children.Add(button);
 }
 private readonly List<Control> onlineNavigation=[];
 private readonly Dictionary<Button,string> discoveryNavigation=[];
 public bool IsWorkspaceFallbackSelected { get; private set; }
 public bool IsLocalFallbackSelected=>Local&&IsWorkspaceFallbackSelected;
 public bool IsKugouFallbackSelected=>IsKugou&&IsWorkspaceFallbackSelected;
 private void UpdateOnlineNavigation()
 {
  foreach(var item in onlineNavigation)item.IsVisible=Host.Concept.Connected;
  if(CurrentPage!=null)HighlightNavigation();
 }
 private readonly Dictionary<R2Page,(Control Header,Control Empty)> emptySongPages=[];
 void Fill(R2Page page,IEnumerable<TrackRow> rows)
 {
  var items=rows.ToArray();
  for(int index=0;index<items.Length;index++) items[index].Number=index+1;
  InteractionController.For(Lists[page]).SetRows(items);
  if(emptySongPages.TryGetValue(page,out var view)){view.Empty.IsVisible=items.Length==0;view.Header.IsVisible=items.Length>0;}
  InteractionController.For(Lists[page]).RefreshCurrentState();
 }
 private string? PlaylistCover(ProviderPlaylist list) =>
  list.CoverUrl ?? list.Tracks.Select(track=>track.CoverUrl).FirstOrDefault(cover=>cover!=null);
 private Control CoverFor(long id,double size)
 {
  if (!Local && displayedSnapshot is { } snapshot && id>=1 && id<=snapshot.Playlists.Count)
   return Host.Concept.Cover(snapshot.AccountKey,PlaylistCover(snapshot.Playlists[(int)id-1]),size);
  var row=Local?LocalMembers(id).FirstOrDefault(track=>track.Track.Cover!=null):null;
  return row==null?ArtworkPlaceholder.Create(size):new TrackArtwork{DisplaySize=size,DataContext=row};
 }
 private void AddPlaylist(long id,string title,int count,bool canPlay,string automationId="")
 {
  var open=new RelayCommand(()=>OpenPlaylist(id));
  var play=canPlay&&count>0?new RelayCommand(()=>PlayPlaylist(id)):null;
  gallery.Children.Add(new PlaylistCard{DataContext=new PlaylistCardModel(title,$"{count} 首",CoverFor(id,172),open,automationId,play)});
  var label=new TextBlock{Text=title,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
  var layout=new Grid{ColumnDefinitions=new ColumnDefinitions("28,*"),ColumnSpacing=8};
  layout.Children.Add(CoverFor(id,28));Grid.SetColumn(label,1);layout.Children.Add(label);
  var button=new Button{Classes={"nav"},Content=layout,Command=open,HorizontalContentAlignment=HorizontalAlignment.Stretch};
  Avalonia.Automation.AutomationProperties.SetName(button,title);
  if(Local&&Program.Desktop&&Host.Player.State.Lists.Any(playlist=>playlist.Id==id&&playlist.IsCustom))
  {
   var rename=new MenuItem{Header="重命名"};
   rename.Click+=async (_,_)=>await EditLocalPlaylistAsync(button,id);
   var delete=new MenuItem{Header="删除歌单"};
   delete.Click+=async (_,_)=>await EditLocalPlaylistAsync(button,id,true);
   button.ContextMenu=new ContextMenu{Items={rename,delete}};
  }
  navPlaylists.Add(button,id);
  playlistNav.Children.Add(button);
 }
 private TrackRow[] LocalMembers(long id)
 {
  var list=Host.Player.State.Lists.FirstOrDefault(item=>item.Id==id);
  if(list==null)return [];
  var tracks=Host.Player.State.Tracks.ToDictionary(track=>track.Id);
  return list.SongIds.Select((trackId,index)=>tracks.TryGetValue(trackId,out var track)
   ?Row(track,list.EntryIds?[index]??0,id):null).OfType<TrackRow>().ToArray();
 }
 private TrackRow[] ProviderMembers(long id)
 {
  if(displayedSnapshot is not { } snapshot || id<1 || id>snapshot.Playlists.Count)return [];
  var playlist=snapshot.Playlists[(int)id-1];
  return playlist.Tracks.Select((track,index)=>ConceptRow(track,
   $"kugou-concept:{snapshot.AccountKey}:{playlist.ProviderPlaylistId}:{index}:{track.ProviderTrackId}")).ToArray();
 }
 private void PlayPlaylist(long id) => Host.Player.PlayAll(Local?LocalMembers(id):ProviderMembers(id));
 private void UpdateHero(string title,int count)
 {
  var cover=new Border{CornerRadius=new CornerRadius(12),BoxShadow=BoxShadows.Parse("0 6 18 0 #25000000"),Child=CoverFor(SelectedPlaylistId,172)};
  var play=new Button{Classes={"primary"},Padding=new Thickness(18,8),HorizontalAlignment=HorizontalAlignment.Left,
   IsEnabled=count>0&&(Local||Host.Concept.Connected),Command=new RelayCommand(()=>PlayPlaylist(SelectedPlaylistId))};
  play.Content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7,VerticalAlignment=VerticalAlignment.Center,Children={
   new PathIcon{Width=13,Height=13,Data=Geometry.Parse("M4,2 L16,9 L4,16 Z"),Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center},
   new TextBlock{Text="播放全部",Foreground=Brushes.White,FontWeight=FontWeight.SemiBold,VerticalAlignment=VerticalAlignment.Center}}};
  var filter=new TextBox{PlaceholderText="筛选歌单内曲目",Text=FilterText,MaxWidth=265,HorizontalAlignment=HorizontalAlignment.Left};
  filter.TextChanged+=(_,_)=>ApplyFilter(filter.Text??"");
  var meta=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Spacing=9,Children={
   new SelectableTextBlock{Text=Local?"本地歌单":"在线歌单",Classes={"muted"},FontSize=11},
   new SelectableTextBlock{Text=title,FontSize=29,FontWeight=FontWeight.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis},
   new TextBlock{Text=$"{count} 首曲目",Classes={"muted"}},play,filter}};
  if(Local&&Program.Desktop&&Host.Player.State.Lists.FirstOrDefault(list=>list.Id==SelectedPlaylistId) is { IsCustom:true } customList)
  {
   var deletePlaylist=new Button{Content="删除歌单",Classes={"glass-pill"},HorizontalAlignment=HorizontalAlignment.Left};
   deletePlaylist.Click+=async (_,_)=>await EditLocalPlaylistAsync(deletePlaylist,customList.Id,true);
   meta.Children.Add(deletePlaylist);
  }
  var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("172,*"),ColumnSpacing=24,Margin=new Thickness(0,4,0,16)};
  grid.Children.Add(cover);Grid.SetColumn(meta,1);grid.Children.Add(meta);hero.Content=grid;
  playlistIntroRequest?.Cancel();
  if(Program.Desktop&&!Local&&Host.Concept.Connected&&displayedSnapshot is { } snapshot&&SelectedPlaylistId>0&&SelectedPlaylistId<=snapshot.Playlists.Count)
  {
   var intro = new SelectableTextBlock { Classes={"muted"}, FontSize=12, TextWrapping=TextWrapping.Wrap, MaxLines=3, IsVisible=false };
   meta.Children.Insert(3,intro);
   _=LoadPlaylistIntroAsync(snapshot.Playlists[(int)SelectedPlaylistId-1].ProviderPlaylistId,intro);
  }
 }
 CancellationTokenSource? playlistIntroRequest;
 async Task LoadPlaylistIntroAsync(string id,SelectableTextBlock label)
 {
  using var request=new CancellationTokenSource();
  playlistIntroRequest=request;
  try
  {
   var text=await Host.Concept.DesktopCall((g,ct)=>g.ReadDesktopPlaylistIntroAsync(id,ct),request.Token);
   if(!request.IsCancellationRequested&&!string.IsNullOrWhiteSpace(text)){label.Text=text;label.IsVisible=true;}
  }
  catch(OperationCanceledException){}
  catch(Exception error)
  { Program.Log("R2_PLAYLIST_INTRO_UNAVAILABLE",new{type=error.GetType().Name}); }
  finally{if(ReferenceEquals(playlistIntroRequest,request))playlistIntroRequest=null;}
 }
 TrackRow ConceptRow(ProviderTrackRef track,string key)=>new(
  new CoreTrack(-1,key,track.Title,track.Artist,track.Album,Host.Concept.CatalogDuration(track.ProviderTrackId),track.CoverUrl),this);
 void RefreshConcept(){
  UpdateOnlineNavigation();
  status.Text=Host.Concept.Summary;searchPanelHost.IsVisible=Host.Concept.Connected&&!P2AWorkspace.RealReadOnly&&!M219RStartup.Active;
  if(!Host.Concept.Connected)Fill(SearchPage,[]);
  var next=Host.Concept.Snapshot;
  if(ReferenceEquals(displayedSnapshot,next))
  {
   // Session consent can change without replacing the immutable playlist
   // snapshot. Refresh row access so Play/Add buttons update immediately.
   if(!Local&&(Program.Desktop||P2AWorkspace.M210ConsentQueueUi))
   {
    if(P2AWorkspace.M223Normal)
    {
     foreach(var page in new[]{CatalogPage,DetailPage,SearchPage})
      foreach(var row in Lists[page].Items.OfType<TrackRow>())row.RefreshAccess();
    }
    else { Fill(CatalogPage,conceptRows);SetPage(PageNumber); }
   }
   return;
  }
  var accountChanged=displayedSnapshot?.AccountKey!=next?.AccountKey;
  if(accountChanged){providerPlaylistId=null;SelectedPlaylistId=0;PageNumber=0;SearchTerm="";positions.Clear();onlineListKey=null;back.Clear();forward.Clear();Fill(SearchPage,[]);if(CurrentPage==DetailPage||CurrentPage==SearchPage)Navigate(galleryPage,false);}
  displayedSnapshot=next;
  gallery.Children.Clear();playlistNav.Children.Clear();navPlaylists.Clear();
  var playlists=next?.Playlists??[];
  conceptRows=playlists.SelectMany(p=>p.Tracks).DistinctBy(t=>t.ProviderTrackId)
    .Select(t=>ConceptRow(t,"kugou-concept:"+next!.AccountKey+":"+t.ProviderTrackId)).ToArray();
  Fill(CatalogPage,conceptRows);SetPage(PageNumber);
  for(int i=0;i<playlists.Count;i++){
   var list=playlists[i];var id=i+1;
   AddPlaylist(id,list.Name,list.Tracks.Count,Host.Concept.Connected);
  }
  galleryEmpty.IsVisible=gallery.Children.Count==0;
  if (next!=null && accountChanged && CurrentPage?.DisplayName=="我的账号")
   Navigate(galleryPage,false);
  if (CurrentPage!=null) HighlightNavigation();
 }
 public void Refresh()
 {
  if(!Local)return;
  var state=Host.Player.State;
  bool changed=previous==null||!ReferenceEquals(previous.Tracks,state.Tracks)||!ReferenceEquals(previous.Lists,state.Lists);
  if(changed)
  {
   Fill(CatalogPage,CatalogRows);Fill(SearchPage,CatalogRows.Where(Filter));SetPage(PageNumber);
   gallery.Children.Clear();playlistNav.Children.Clear();navPlaylists.Clear();
   foreach(var list in state.Lists) AddPlaylist(list.Id,list.Name,list.SongIds.Length,true);
   galleryEmpty.IsVisible=gallery.Children.Count==0;
   if(CurrentPage!=null) HighlightNavigation();
  }
  if(favoritesPage!=null&&(changed||previous==null||!previous.Favorites.SequenceEqual(state.Favorites)))Fill(favoritesPage,CatalogRows.Where(r=>state.Favorites.Contains(r.Track.Id)));
  if(historyPage!=null&&(changed||previous==null||!previous.History.SequenceEqual(state.History)))
  {
   var tracks=CatalogRows.ToDictionary(row=>row.Track.Id);
   Fill(historyPage,state.History.Where(tracks.ContainsKey).Select(id=>tracks[id]));
  }
  previous=state;
 }
 bool Filter(TrackRow r)=>r.Name.Contains(SearchTerm,StringComparison.OrdinalIgnoreCase)||r.Singer.Contains(SearchTerm,StringComparison.OrdinalIgnoreCase);
 public TrackRow[] Members()=>Local?LocalMembers(SelectedPlaylistId):ProviderMembers(SelectedPlaylistId);
 public void SetPage(int number)
 {
  var rows=Members();
  PageNumber=Math.Clamp(number,0,Math.Max(0,(rows.Length-1)/50));
  if(Program.Desktop)
  {
   prevPage.IsVisible=nextPage.IsVisible=false;
   Fill(DetailPage,filterPage==DetailPage?rows.Where(MatchesFilter):rows);
  }
  else
  {
   Fill(DetailPage,rows.Skip(PageNumber*50).Take(50));
   prevPage.IsVisible=nextPage.IsVisible=rows.Length>50;
  }
 }
 internal void ShowLocalPlaylists()=>Navigate(galleryPage);
 public void OpenPlaylist(long id)
 {
  if(!Local)
  {
   var lists=displayedSnapshot?.Playlists;
   if(lists==null||id<1||id>lists.Count)return;
  }
  if(CurrentPage!=null && (CurrentPage!=DetailPage || SelectedPlaylistId!=id)){back.Push(Here());forward.Clear();}
  PresentPlaylist(id,"");
 }
 private void PresentPlaylist(long id,string filter)
 {
  Save();
  if(!Local&&displayedSnapshot is { } current && id>=1&&id<=current.Playlists.Count)
   providerPlaylistId=current.Playlists[(int)id-1].ProviderPlaylistId;
  SelectedPlaylistId=id;
  FilterText=filter;
  filterPage=DetailPage;
  SetPage(0);
  var title=Local?Host.Player.State.Lists.FirstOrDefault(list=>list.Id==id)?.Name:
   displayedSnapshot?.Playlists[(int)id-1].Name;
  UpdateHero(title??"歌单",Members().Length);
  Navigate(DetailPage,false,saveCurrent:false);
 }
 private bool MatchesFilter(TrackRow row)=>
  row.Name.Contains(FilterText,StringComparison.OrdinalIgnoreCase)||
  row.Singer.Contains(FilterText,StringComparison.OrdinalIgnoreCase)||
  row.AlbumName.Contains(FilterText,StringComparison.OrdinalIgnoreCase);
 public void ApplyFilter(string value)
 {
  FilterText=value.Trim();
  if(CurrentPage==DetailPage){filterPage=DetailPage;SetPage(0);return;}
  if(CurrentPage==CatalogPage||CurrentPage==SearchPage)
  {
   filterPage=CurrentPage;
   Fill(CurrentPage,CatalogRows.Where(MatchesFilter));
  }
 }
 public void RefreshFavoriteRows()
 {
  foreach(var list in Lists.Values)
   foreach(var row in list.Items.OfType<TrackRow>())row.RefreshFavorite();
 }
 public void RefreshPlayingRows()
 {
  foreach(var list in Lists.Values)
   foreach(var row in list.Items.OfType<TrackRow>())row.Refresh();
  foreach(var list in Lists.Values)
   InteractionController.Find(list)?.RefreshCurrentState();
 }
 public async Task SearchAsync(){
  if(Local){Fill(SearchPage,CatalogRows.Where(Filter));Navigate(SearchPage);return;}
  if(string.IsNullOrWhiteSpace(SearchTerm))return;
  await Host.Concept.SearchAsync(SearchTerm,page=>{
   Fill(SearchPage,page.Tracks.Select(t=>new TrackRow(new CoreTrack(-1,"kugou-concept:"+Host.Concept.AccountKey+":"+t.Id,t.Title,t.Artist,t.Album,t.Seconds,t.Cover),this)));
   Navigate(SearchPage);
  });
 }
 private SongViewKey? CurrentSongView()
 {
  if(CurrentPage==null)return null;
  if(CurrentPage==onlinePage)return onlineListKey;
  if(!Lists.ContainsKey(CurrentPage))return null;
  if(CurrentPage!=DetailPage)return new(CurrentPage,null);
  if(SelectedPlaylistId==0)return null;
  return new(CurrentPage,Local?SelectedPlaylistId.ToString(System.Globalization.CultureInfo.InvariantCulture):providerPlaylistId);
 }
 public void Save()
 {
  if(CurrentSongView() is not { } key)return;
  var controller=InteractionController.For(Lists[key.Page]);
  positions[key]=controller.CaptureView();
  controller.CancelFollow();
 }
 public void Restore()
 {
  if(CurrentSongView() is not { } key)return;
  var controller=InteractionController.For(Lists[key.Page]);
  if(positions.TryGetValue(key,out var state))controller.RestoreView(state);
  else
  {
   controller.RestoreView(new(default,[]));
   controller.FollowCurrent(firstOpen:true);
  }
 }
 internal void FollowPlayback()
 {
  if(CurrentSongView() is { } key)
   InteractionController.For(Lists[key.Page]).FollowCurrent();
 }
 public void Navigate(R2Page page,bool push=true,bool saveCurrent=true)
 {
  if(saveCurrent)Save();
  if(push&&CurrentPage!=null&&CurrentPage!=page){back.Push(Here());forward.Clear();}
  CurrentPage=page;
  // R11 进场编排：标题先到（0ms）→ 内容卡 +60ms 后 12px 上移淡入；切走瞬时（比切来快，退场≈进场 60% 以内）。
  heading.Text=page.DisplayName;
  heading.IsVisible=page!=DetailPage&&page!=onlinePage;
  content.Opacity=0;
  content.RenderTransform=TransformOperations.Parse($"translateY({DesignTokens.ChoreoOffsetPx}px)");
  content.Content=page.View;
  Dispatcher.UIThread.Post(async()=>{
   await Task.Delay(DesignTokens.ChoreoCardDelay);
   if(CurrentPage!=page)return; // 期间又切了页，放弃本次进场
   content.Opacity=1;
   content.RenderTransform=TransformOperations.Parse("translateY(0px)");
  },DispatcherPriority.Render);
  status.IsVisible=!Local&&page!=DetailPage&&page!=onlinePage&&page.DisplayName!="我的账号";
  status.Text=Local?"":Host.Concept.Summary;
  HighlightNavigation();
  Restore();
 }
 private void HighlightNavigation()
 {
  foreach(var (button,page) in navPages)button.Classes.Set("selected",button.IsVisible&&page==CurrentPage);
  foreach(var (button,id) in navPlaylists)button.Classes.Set("selected",button.IsVisible&&CurrentPage==DetailPage&&id==SelectedPlaylistId);
  foreach(var (button,title) in discoveryNavigation)
   button.Classes.Set("selected",button.IsVisible&&CurrentPage==onlinePage&&onlineHeading?.Text==title);
  IsWorkspaceFallbackSelected=!navPages.Keys.Concat(navPlaylists.Keys).Concat(discoveryNavigation.Keys)
      .Any(button=>button.IsVisible&&button.Classes.Contains("selected"));
  OnPropertyChanged(nameof(IsLocalFallbackSelected));OnPropertyChanged(nameof(IsKugouFallbackSelected));
 }
 private BrowsePoint Here()=>new(CurrentPage,SelectedPlaylistId,FilterText);
 private void RestorePoint(BrowsePoint point)
 {
  if(point.Page==DetailPage){PresentPlaylist(point.Playlist,point.Filter);return;}
  Navigate(point.Page,false);
  ApplyFilter(point.Filter);
 }
 public void Back(){if(back.TryPop(out var point)){forward.Push(Here());RestorePoint(point);}}
 public void Forward(){if(forward.TryPop(out var point)){back.Push(Here());RestorePoint(point);}}
 public void ShowLyrics(){if(lyricsPage!=null)Navigate(lyricsPage);}
 public void ShowPlugins()
 {
  pluginPage??=new R2Page("DLC 插件中心",new PluginManager(Host));
  Navigate(pluginPage);
 }

}
public sealed partial class ShellModel:ObservableObject,IDisposable {
 internal P2AWorkspace Concept {get;}
 public CorePresentation Player {get;}public WorkspaceState LocalWorkspace {get;}public WorkspaceState KugouConceptWorkspace {get;}
 WorkspaceState workspace=null!;public WorkspaceState Workspace=>workspace;public Control WorkspaceRoot=>workspace.Root;
 public string SourceLabel{
  get{
   if(Player.State.CurrentId==0)return "当前没有播放歌曲";
   var track=Player.State.Tracks.FirstOrDefault(t=>t.Id==Player.State.CurrentId);
   if(track?.Key.StartsWith("kugou-concept:",StringComparison.Ordinal)==true)
    return P2AWorkspace.M210ConsentQueueUi?"歌曲来源：酷狗概念版（合成验收）":"歌曲来源：酷狗概念版";
   return "歌曲来源：本地音乐";
  }
 }
 public string AccountLabel => Concept.Connected ? Concept.Profile?.Name ?? "酷狗已登录" : "登录酷狗";
 public Control AccountAvatar => Concept.Cover(Concept.AccountKey,Concept.Profile?.Avatar,24);
 private Guid lastEntry;
 private bool lastPlaying;
 public void ShowAccount()
 {
  if(!KugouEnabled)return;
  Switch(KugouConceptWorkspace);
  KugouConceptWorkspace.Navigate(KugouConceptWorkspace.Pages.Single(page=>page.DisplayName=="我的账号"));
 }
 public ICommand SwitchLocalCommand {get;}public ICommand SwitchKugouCommand {get;}
 internal ShellModel(CorePlaybackAdapter core){Adapter=core;Concept=new P2AWorkspace(core);Player=new CorePresentation(core);LocalWorkspace=new(this,true);KugouConceptWorkspace=new(this,false);SwitchLocalCommand=new RelayCommand(()=>Switch(LocalWorkspace));SwitchKugouCommand=new RelayCommand(()=>Switch(KugouConceptWorkspace));Concept.Changed+=AccountChanged;Player.Updated+=OnSnapshot;Adapter.DesktopUnavailableChanged+=RefreshUnavailable;Switch(LocalWorkspace);Player.Attach(this);}
 void AccountChanged(){OnPropertyChanged(nameof(AccountLabel));OnPropertyChanged(nameof(AccountAvatar));}
 void RefreshUnavailable(string reason){ LocalWorkspace.RefreshPlayingRows();KugouConceptWorkspace.RefreshPlayingRows();foreach(var row in Player.DisplayPlaybackQueue)row.Refresh();if(reason.Length>0)Player.NotifyUnavailable(reason); }
 void OnSnapshot()
 {
  LocalWorkspace.Refresh();KugouConceptWorkspace.Refresh();
  if(lastEntry==Player.State.CurrentEntryId&&lastPlaying==Player.State.Playing)return;
  var entryChanged=lastEntry!=Player.State.CurrentEntryId;
  lastEntry=Player.State.CurrentEntryId;lastPlaying=Player.State.Playing;
  LocalWorkspace.RefreshPlayingRows();KugouConceptWorkspace.RefreshPlayingRows();
  if(entryChanged)workspace?.FollowPlayback();
  OnPropertyChanged(nameof(SourceLabel));
 }
 public void Switch(WorkspaceState next){if(next.IsKugou&&!KugouEnabled)return;workspace?.Save();workspace?.PauseOnlineWork();workspace=next;OnPropertyChanged(nameof(Workspace));OnPropertyChanged(nameof(WorkspaceRoot));next.Restore();}
 public void Dispose(){Concept.Changed-=AccountChanged;Player.Updated-=OnSnapshot;Adapter.DesktopUnavailableChanged-=RefreshUnavailable;Player.Dispose();Concept.Dispose();PreviewAssets.Dispose();}
}
