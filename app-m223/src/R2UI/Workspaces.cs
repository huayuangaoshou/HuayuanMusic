using HuayuanMusic.Provider.Contracts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Animation;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
namespace HuayuanPreview.R2;
public sealed record R2Page(string DisplayName,Control View);
public sealed class WorkspaceState:ObservableObject {
 public ShellModel Host {get;}public bool Local {get;}public bool Allowed=>Local;public bool IsKugou=>!Local;public string Name=>Local?"本地音乐":"酷狗概念版";
 public List<R2Page> Pages {get;}=[];public Dictionary<R2Page,ListBox> Lists {get;}=[];private readonly Stack<BrowsePoint> back = new();
 public Dictionary<R2Page,(Vector Offset,string[] Selected)> Positions {get;}=[];
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
 readonly ContentControl content=new();readonly TextBlock heading=new(){FontSize=23,Margin=new Thickness(0,8)};readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};readonly WrapPanel gallery=new(){ItemWidth=188};
 readonly Dictionary<string,TrackRow> rowViews=[];PlaybackSnapshot? previous;readonly R2Page? favoritesPage,historyPage;TrackRow[] CatalogRows=>Local?Host.Player.State.Tracks.Where(t=>!t.Key.StartsWith("kugou-concept:",StringComparison.Ordinal)).Select(t=>Row(t)).ToArray():conceptRows;
 TrackRow[] conceptRows=[];
 PlatformPlaylistSnapshot? displayedSnapshot;
 string? providerPlaylistId;
 readonly R2Page galleryPage;
 readonly Button prevPage=new(){Content="上一页"}, nextPage=new(){Content="下一页"};
 readonly StackPanel searchPanelHost;
 public WorkspaceState(ShellModel host,bool local){Host=host;Local=local;
  var nav=new StackPanel{Spacing=4,Margin=new Thickness(0)};
  Navigation = nav;
  heading.FontSize = 27;
  heading.FontWeight = FontWeight.SemiBold;
  status.Classes.Add("muted");
  status.Margin = new Thickness(0,0,0,12);
  content.Transitions = new() { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) } };
  var area=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,*"),Margin=new Thickness(25,15,25,12)};
  area.Children.Add(heading);Grid.SetRow(status,1);area.Children.Add(status);Grid.SetRow(content,2);area.Children.Add(content);
  Root=area;
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
  CatalogPage=SongPage(Local?"本地曲库":"已同步曲目",CatalogRows);AddNav(nav,CatalogPage);
  var cardBody=new StackPanel{Spacing=10};
  cardBody.Children.Add(gallery);
  galleryPage=new R2Page("歌单",new ScrollViewer{Content=cardBody});AddNav(nav,galleryPage);
  DetailPage=SongPage("歌单详情",[]);Pages.Add(DetailPage);var panel=(Grid)DetailPage.View;panel.RowDefinitions.Insert(0,new RowDefinition(GridLength.Auto));foreach(var child in panel.Children)Grid.SetRow(child,Grid.GetRow(child)+1);panel.Children.Add(hero);panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));var paging=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};prevPage.Click+=(_,_)=>SetPage(PageNumber-1);nextPage.Click+=(_,_)=>SetPage(PageNumber+1);paging.Children.Add(prevPage);paging.Children.Add(nextPage);
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
  var lyrics=new StackPanel{Spacing=16,Margin=new Thickness(24)};var title=new TextBlock{FontSize=22};title.DataContext=host.Player;title.Bind(TextBlock.TextProperty,new Binding("DisplayedPlayingSong.Name"));lyrics.Children.Add(title);var lines=new ItemsControl();lines.DataContext=host.Player;lines.Bind(ItemsControl.ItemsSourceProperty,new Binding("State.Lyrics"));lines.ItemTemplate=new FuncDataTemplate<CoreLyric>((l,_)=>new TextBlock{Text=l!.Text,FontSize=17,TextWrapping=TextWrapping.Wrap});lyrics.Children.Add(lines);lyricsPage=new R2Page("歌词",new ScrollViewer{Content=lyrics});AddNav(nav,lyricsPage);
  QueuePage=new R2Page("混合队列",new QueuePreview{DataContext=host});AddNav(nav,QueuePage);
  if(!Local){AddNav(nav,new R2Page("账号状态",Host.Concept.AccountPage));Host.Concept.Changed+=RefreshConcept;RefreshConcept();}
  nav.Children.Add(new TextBlock{Text="我的歌单",Classes={"muted"},FontSize=11,Margin=new Thickness(10,14,0,4)});
  nav.Children.Add(playlistNav);
  Navigate(Local?CatalogPage:Host.Concept.Snapshot!=null?galleryPage:Pages.Single(p=>p.DisplayName=="账号状态"),false);Refresh();
 }
 TrackRow Row(CoreTrack track,long member=0,long list=0){string key=member>0?$"m:{list}:{member}":"t:"+track.Id;if(!rowViews.TryGetValue(key,out var row)||row.Track!=track){row=new TrackRow(track,this,memberId:member,listId:list);rowViews[key]=row;}return row;}
 R2Page SongPage(string title,IEnumerable<TrackRow> rows){var list=new ListBox{ItemTemplate=new FuncDataTemplate<TrackRow>((row,_)=>new SongRowView(),supportsRecycling:true),ItemsPanel=new FuncTemplate<Panel?>(()=>new VirtualizingStackPanel())};list.Classes.Add("songs");list.Styles.Add(new Style(s=>s.OfType<ListBoxItem>()){Setters={new Setter(ListBoxItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch)}});var grid=new Grid{RowDefinitions=new RowDefinitions("Auto,*")};var header=new SongTableHeader(!Local&&!Program.Desktop);grid.Children.Add(header);Grid.SetRow(list,1);grid.Children.Add(list);var page=new R2Page(title,grid);Lists.Add(page,list);var interactions=InteractionController.Attach(list,this,Host.Player,false);header.DataContext=interactions;interactions.SetRows(rows.ToArray());return page;}
 void AddNav(StackPanel nav,R2Page page)
 {
  Pages.Add(page);
  var button=new Button{Content=page.DisplayName,Classes={"nav"}};
  button.Click+=(_,_)=>Navigate(page);
  navPages.Add(button,page);
  nav.Children.Add(button);
 }
 void Fill(R2Page page,IEnumerable<TrackRow> rows)
 {
  var items=rows.ToArray();
  for(int index=0;index<items.Length;index++) items[index].Number=index+1;
  InteractionController.For(Lists[page]).SetRows(items);
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
  var play=new Button{Classes={"primary"},Content="▶  播放全部",Padding=new Thickness(18,8),HorizontalAlignment=HorizontalAlignment.Left,
   IsEnabled=count>0&&(Local||Host.Concept.Connected),Command=new RelayCommand(()=>PlayPlaylist(SelectedPlaylistId))};
  var filter=new TextBox{PlaceholderText="筛选歌单内曲目",Text=FilterText,MaxWidth=265,HorizontalAlignment=HorizontalAlignment.Left};
  filter.TextChanged+=(_,_)=>ApplyFilter(filter.Text??"");
  var meta=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Spacing=9,Children={
   new TextBlock{Text=Local?"本地歌单":"酷狗概念版 · 只读歌单",Classes={"muted"},FontSize=11},
   new TextBlock{Text=title,FontSize=29,FontWeight=FontWeight.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis},
   new TextBlock{Text=$"{count} 首曲目",Classes={"muted"}},play,filter}};
  var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("172,*"),ColumnSpacing=25,Margin=new Thickness(0,2,0,18)};
  grid.Children.Add(cover);Grid.SetColumn(meta,1);grid.Children.Add(meta);hero.Content=grid;
 }
 TrackRow ConceptRow(ProviderTrackRef track,string key)=>new(
  new CoreTrack(-1,key,track.Title,track.Artist,track.Album,Host.Concept.CatalogDuration(track.ProviderTrackId),track.CoverUrl),this);
 void RefreshConcept(){
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
  if(accountChanged){providerPlaylistId=null;SelectedPlaylistId=0;PageNumber=0;SearchTerm="";Positions.Clear();back.Clear();forward.Clear();Fill(SearchPage,[]);if(CurrentPage==DetailPage||CurrentPage==SearchPage)Navigate(galleryPage,false);}
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
  if (next!=null && accountChanged && CurrentPage?.DisplayName=="账号状态")
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
 public void OpenPlaylist(long id)
 {
  if(!Local)
  {
   var lists=displayedSnapshot?.Playlists;
   if(lists==null||id<1||id>lists.Count)return;
   providerPlaylistId=lists[(int)id-1].ProviderPlaylistId;
  }
  if(CurrentPage!=null && (CurrentPage!=DetailPage || SelectedPlaylistId!=id)){back.Push(Here());forward.Clear();}
  PresentPlaylist(id,"");
 }
 private void PresentPlaylist(long id,string filter)
 {
  if(!Local&&displayedSnapshot is { } current && id>=1&&id<=current.Playlists.Count)
   providerPlaylistId=current.Playlists[(int)id-1].ProviderPlaylistId;
  SelectedPlaylistId=id;
  FilterText=filter;
  filterPage=DetailPage;
  SetPage(0);
  var title=Local?Host.Player.State.Lists.FirstOrDefault(list=>list.Id==id)?.Name:
   displayedSnapshot?.Playlists[(int)id-1].Name;
  UpdateHero(title??"歌单",Members().Length);
  Navigate(DetailPage,false);
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
 public void RefreshPlayingRows()
 {
  foreach(var list in Lists.Values)
   foreach(var row in list.Items.OfType<TrackRow>())row.Refresh();
 }
 public async Task SearchAsync(){
  if(Local){Fill(SearchPage,CatalogRows.Where(Filter));Navigate(SearchPage);return;}
  if(string.IsNullOrWhiteSpace(SearchTerm))return;
  await Host.Concept.SearchAsync(SearchTerm,page=>{
   Fill(SearchPage,page.Tracks.Select(t=>new TrackRow(new CoreTrack(-1,"kugou-concept:"+Host.Concept.AccountKey+":"+t.Id,t.Title,t.Artist,t.Album,t.Seconds,t.Cover),this)));
   Navigate(SearchPage);
  });
 }
 public void Save(){if(CurrentPage!=null&&Lists.TryGetValue(CurrentPage,out var list)){var scroll=list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();Positions[CurrentPage]=(scroll?.Offset??default,list.SelectedItems?.Cast<TrackRow>().Select(r=>r.Identity).ToArray()??[]);}}
 public void Restore(){if(CurrentPage!=null&&Positions.TryGetValue(CurrentPage,out var value)&&Lists.TryGetValue(CurrentPage,out var list)){list.SelectedItems?.Clear();foreach(var row in list.Items.OfType<TrackRow>())if(value.Selected.Contains(row.Identity))list.SelectedItems?.Add(row);Dispatcher.UIThread.Post(()=>{var scroll=list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();if(scroll!=null)scroll.Offset=value.Offset;},DispatcherPriority.Loaded);}}
 public void Navigate(R2Page page,bool push=true)
 {
  Save();
  if(push&&CurrentPage!=null&&CurrentPage!=page){back.Push(Here());forward.Clear();}
  CurrentPage=page;
  content.Opacity=0;
  content.Content=page.View;
  Dispatcher.UIThread.Post(()=>content.Opacity=1,DispatcherPriority.Render);
  heading.Text=page.DisplayName;
  heading.IsVisible=page!=DetailPage;
  status.IsVisible=page!=DetailPage&&page.DisplayName!="账号状态";
  status.Text=Local?"":Host.Concept.Summary;
  HighlightNavigation();
  Restore();
 }
 private void HighlightNavigation()
 {
  foreach(var (button,page) in navPages)button.Classes.Set("selected",page==CurrentPage);
  foreach(var (button,id) in navPlaylists)button.Classes.Set("selected",CurrentPage==DetailPage&&id==SelectedPlaylistId);
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
  pluginPage??=new R2Page("DLC 插件中心",new StackPanel{Spacing=12,Children={
   new TextBlock{Text="酷狗概念版",FontSize=20,FontWeight=FontWeight.SemiBold},
   new TextBlock{Text="已内置 · 歌单、曲目及账号工作空间",Classes={"muted"}},
   new Button{Content="打开酷狗概念版账号",Command=new RelayCommand(Host.ShowAccount)}
  }});
  Navigate(pluginPage);
 }

}
public sealed class ShellModel:ObservableObject,IDisposable {
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
 public string AccountLabel => Concept.Connected ? "酷狗已登录" : "登录酷狗";
 private Guid lastEntry;
 private bool lastPlaying;
 public void ShowAccount()
 {
  Switch(KugouConceptWorkspace);
  KugouConceptWorkspace.Navigate(KugouConceptWorkspace.Pages.Single(page=>page.DisplayName=="账号状态"));
 }
 public ICommand SwitchLocalCommand {get;}public ICommand SwitchKugouCommand {get;}
 internal ShellModel(CorePlaybackAdapter core){Concept=new P2AWorkspace(core);Player=new CorePresentation(core);LocalWorkspace=new(this,true);KugouConceptWorkspace=new(this,false);SwitchLocalCommand=new RelayCommand(()=>Switch(LocalWorkspace));SwitchKugouCommand=new RelayCommand(()=>Switch(KugouConceptWorkspace));Concept.Changed+=AccountChanged;Player.Updated+=OnSnapshot;Switch(LocalWorkspace);Player.Attach(this);}
 void AccountChanged()=>OnPropertyChanged(nameof(AccountLabel));
 void OnSnapshot()
 {
  LocalWorkspace.Refresh();KugouConceptWorkspace.Refresh();
  if(lastEntry==Player.State.CurrentEntryId&&lastPlaying==Player.State.Playing)return;
  lastEntry=Player.State.CurrentEntryId;lastPlaying=Player.State.Playing;
  LocalWorkspace.RefreshPlayingRows();KugouConceptWorkspace.RefreshPlayingRows();
  OnPropertyChanged(nameof(SourceLabel));
 }
 public void Switch(WorkspaceState next){workspace?.Save();workspace=next;OnPropertyChanged(nameof(Workspace));OnPropertyChanged(nameof(WorkspaceRoot));next.Restore();}
 public void Dispose(){Concept.Changed-=AccountChanged;Player.Updated-=OnSnapshot;Player.Dispose();Concept.Dispose();PreviewAssets.Dispose();}
}
