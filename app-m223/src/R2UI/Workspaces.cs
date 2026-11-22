using HuayuanMusic.Provider.Contracts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
 public ShellModel Host {get;}public bool Local {get;}public bool Allowed=>Local;public string Name=>Local?"本地音乐":"酷狗概念版";
 public List<R2Page> Pages {get;}=[];public Dictionary<R2Page,ListBox> Lists {get;}=[];public Stack<R2Page> BackStack {get;}=[];
 public Dictionary<R2Page,(Vector Offset,string[] Selected)> Positions {get;}=[];
 public R2Page CurrentPage {get;private set;}=null!;public R2Page DetailPage {get;}public R2Page SearchPage {get;}public R2Page QueuePage {get;}public R2Page CatalogPage {get;}
 public long SelectedPlaylistId {get;private set;}public int PageNumber {get;private set;}
 string search="";public string SearchTerm {get=>search;set=>SetProperty(ref search,value);}
 public Control Root {get;}
 readonly ContentControl content=new();readonly TextBlock heading=new(){FontSize=23,Margin=new Thickness(0,8)};readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};readonly WrapPanel gallery=new(){ItemWidth=190,ItemHeight=154};
 readonly Dictionary<string,TrackRow> rowViews=[];PlaybackSnapshot? previous;readonly R2Page? favoritesPage,historyPage;TrackRow[] CatalogRows=>Local?Host.Player.State.Tracks.Where(t=>!t.Key.StartsWith("kugou-concept:",StringComparison.Ordinal)).Select(t=>Row(t)).ToArray():conceptRows;
 TrackRow[] conceptRows=[];
 PlatformPlaylistSnapshot? displayedSnapshot;
 string? providerPlaylistId;
 readonly R2Page galleryPage;
 readonly Button prevPage=new(){Content="上一页"}, nextPage=new(){Content="下一页"};
 readonly StackPanel searchPanelHost;
 public WorkspaceState(ShellModel host,bool local){Host=host;Local=local;
  var nav=new StackPanel{Spacing=5,Margin=new Thickness(12)};var area=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,*"),Margin=new Thickness(16,8)};area.Children.Add(heading);Grid.SetRow(status,1);area.Children.Add(status);Grid.SetRow(content,2);area.Children.Add(content);
  var root=new Grid{Background=Local?Brushes.White:SolidColorBrush.Parse("#F6FAFF")};
  if(Local){root.ColumnDefinitions=new ColumnDefinitions("190,*");root.Children.Add(new Border{Background=SolidColorBrush.Parse("#ECEEF1"),Child=new ScrollViewer{Content=nav}});Grid.SetColumn(area,1);root.Children.Add(area);}else{root.RowDefinitions=new RowDefinitions("Auto,*");nav.Orientation=Orientation.Horizontal;root.Children.Add(new ScrollViewer{Content=nav,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});Grid.SetRow(area,1);root.Children.Add(area);}Root=root;
  if (Local && Program.Desktop)
  {
   var import = new Button { Content = "添加本地音乐" };
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
  cardBody.Children.Add(new TextBlock{Text=Local?"本地歌单 · SQLite 持久保存":
    P2AWorkspace.M212SnapshotPreview?"历史真实歌单快照 · 离线只读 · 未登录，不可播放或入队":
    (Program.Desktop||P2AWorkspace.RealReadOnly)?"已同步的官方歌单 · 只读":"TEST DATA · 只读歌单快照",TextWrapping=TextWrapping.Wrap});
  cardBody.Children.Add(gallery);
  galleryPage=new R2Page("歌单",new ScrollViewer{Content=cardBody});AddNav(nav,galleryPage);
  DetailPage=SongPage("歌单详情",[]);Pages.Add(DetailPage);var panel=(Grid)DetailPage.View;panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));var paging=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};prevPage.Click+=(_,_)=>SetPage(PageNumber-1);nextPage.Click+=(_,_)=>SetPage(PageNumber+1);paging.Children.Add(prevPage);paging.Children.Add(nextPage);
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
  Grid.SetRow(paging,2);panel.Children.Add(paging);
  SearchPage=SongPage("搜索结果",CatalogRows);Pages.Add(SearchPage);var box=new TextBox{PlaceholderText=Local?"搜索本地资料":"搜索酷狗歌曲",MinWidth=60};box.DataContext=this;box.Bind(TextBox.TextProperty,new Binding(nameof(SearchTerm)){Mode=BindingMode.TwoWay});var searchButton=new Button{Content="搜索"};searchButton.Click+=async(_,_)=>await SearchAsync();box.KeyDown+=async(_,e)=>{if(e.Key==Avalonia.Input.Key.Enter)await SearchAsync();};var searchPanel=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};searchPanel.Children.Add(box);Grid.SetColumn(searchButton,1);searchPanel.Children.Add(searchButton);var top=new StackPanel{Spacing=8};searchPanelHost=new StackPanel{Children={searchPanel}};top.Children.Add(searchPanelHost);var back=new Button{Content="返回"};back.Click+=(_,_)=>Back();top.Children.Add(back);nav.Children.Insert(0,top);
  if(Local){favoritesPage=SongPage("收藏",[]);historyPage=SongPage("历史",[]);AddNav(nav,favoritesPage);AddNav(nav,historyPage);}
  var lyrics=new StackPanel{Spacing=16,Margin=new Thickness(24)};var title=new TextBlock{FontSize=22};title.DataContext=host.Player;title.Bind(TextBlock.TextProperty,new Binding("DisplayedPlayingSong.Name"));lyrics.Children.Add(title);var lines=new ItemsControl();lines.DataContext=host.Player;lines.Bind(ItemsControl.ItemsSourceProperty,new Binding("State.Lyrics"));lines.ItemTemplate=new FuncDataTemplate<CoreLyric>((l,_)=>new TextBlock{Text=l!.Text,FontSize=17,TextWrapping=TextWrapping.Wrap});lyrics.Children.Add(lines);lyrics.Children.Add(new TextBlock{Text="显示当前曲目的歌词；缺失时不联网补齐"});AddNav(nav,new R2Page("歌词",new ScrollViewer{Content=lyrics}));
  QueuePage=new R2Page("混合队列",new QueuePreview{DataContext=host});AddNav(nav,QueuePage);
  if(!Local){AddNav(nav,new R2Page("账号状态",Host.Concept.AccountPage));Host.Concept.Changed+=RefreshConcept;RefreshConcept();}
  Navigate(Local?CatalogPage:Host.Concept.Snapshot!=null?galleryPage:Pages.Single(p=>p.DisplayName=="账号状态"),false);Refresh();
 }
 TrackRow Row(CoreTrack track,long member=0,long list=0){string key=member>0?$"m:{list}:{member}":"t:"+track.Id;if(!rowViews.TryGetValue(key,out var row)||row.Track!=track){row=new TrackRow(track,this,memberId:member,listId:list);rowViews[key]=row;}return row;}
 R2Page SongPage(string title,IEnumerable<TrackRow> rows){var list=new ListBox{ItemTemplate=new FuncDataTemplate<TrackRow>((row,_)=>Local?new SongRowView{DataContext=row}:row==null?null:ConceptSong(row),supportsRecycling:Local),ItemsPanel=new FuncTemplate<Panel?>(()=>new VirtualizingStackPanel())};list.Styles.Add(new Style(s=>s.OfType<ListBoxItem>()){Setters={new Setter(ListBoxItem.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch)}});var grid=new Grid{RowDefinitions=new RowDefinitions("Auto,*")};var header=new SongTableHeader(!Local);grid.Children.Add(header);Grid.SetRow(list,1);grid.Children.Add(list);var page=new R2Page(title,grid);Lists.Add(page,list);var interactions=InteractionController.Attach(list,this,Host.Player,false);header.DataContext=interactions;interactions.SetRows(rows.ToArray());return page;}
 void AddNav(StackPanel nav,R2Page page){Pages.Add(page);var b=new Button{Content=page.DisplayName,HorizontalContentAlignment=HorizontalAlignment.Left};b.Click+=(_,_)=>Navigate(page);nav.Children.Add(b);}
 void Fill(R2Page page,IEnumerable<TrackRow> rows)=>InteractionController.For(Lists[page]).SetRows(rows.ToArray());
 Control ConceptSong(TrackRow row){
  var cover=Host.Concept.Cover(displayedSnapshot?.AccountKey??Host.Concept.AccountKey,row.Track.Cover,32);
  var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("42,*")};
  grid.Children.Add(cover);var song=new SongRowView{DataContext=row};Grid.SetColumn(song,1);grid.Children.Add(song);
  return grid;
 }
 TrackRow ConceptRow(ProviderTrackRef track,string key)=>new(
  new CoreTrack(-1,key,track.Title,track.Artist,track.Album,0,track.CoverUrl),this);
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
  if(accountChanged){providerPlaylistId=null;SelectedPlaylistId=0;PageNumber=0;SearchTerm="";Positions.Clear();BackStack.Clear();Fill(SearchPage,[]);if(CurrentPage==DetailPage||CurrentPage==SearchPage)Navigate(galleryPage,false);}
  displayedSnapshot=next;
  gallery.Children.Clear();
  var playlists=next?.Playlists??[];
  conceptRows=playlists.SelectMany(p=>p.Tracks).DistinctBy(t=>t.ProviderTrackId)
    .Select(t=>ConceptRow(t,"kugou-concept:"+next!.AccountKey+":"+t.ProviderTrackId)).ToArray();
  Fill(CatalogPage,conceptRows);SetPage(PageNumber);
  for(int i=0;i<playlists.Count;i++){
   var list=playlists[i];var id=i+1;
   gallery.Children.Add(new PlaylistCard{DataContext=new PlaylistCardModel(list.Name,$"{list.Tracks.Count} 首 · 只读",
    Host.Concept.Cover(next!.AccountKey,list.CoverUrl,56),new RelayCommand(()=>OpenPlaylist(id)),P2AWorkspace.M218BTarget&&id==2?"M222.Target.Playlist":"")});
  }
 }
 public void Refresh(){if(!Local)return;var state=Host.Player.State;bool changed=previous==null||!ReferenceEquals(previous.Tracks,state.Tracks)||!ReferenceEquals(previous.Lists,state.Lists);if(changed){Fill(CatalogPage,CatalogRows);Fill(SearchPage,CatalogRows.Where(Filter));SetPage(PageNumber);gallery.Children.Clear();foreach(var l in state.Lists){var id=l.Id;gallery.Children.Add(new PlaylistCard{DataContext=new PlaylistCardModel(l.Name,l.SongIds.Length+" 首 · 本地歌单",new Image{Source=PreviewAssets.Get(),Width=56,Height=56,HorizontalAlignment=HorizontalAlignment.Left},new RelayCommand(()=>OpenPlaylist(id)))});}}
  if(favoritesPage!=null&&(changed||previous==null||!previous.Favorites.SequenceEqual(state.Favorites)))Fill(favoritesPage,CatalogRows.Where(r=>state.Favorites.Contains(r.Track.Id)));
  if(historyPage!=null&&(changed||previous==null||!previous.History.SequenceEqual(state.History)))Fill(historyPage,state.History.Select(id=>CatalogRows.FirstOrDefault(r=>r.Track.Id==id)).OfType<TrackRow>());previous=state;
 }
 bool Filter(TrackRow r)=>r.Name.Contains(SearchTerm,StringComparison.OrdinalIgnoreCase)||r.Singer.Contains(SearchTerm,StringComparison.OrdinalIgnoreCase);
 public TrackRow[] Members(){if(!Local){var playlist=displayedSnapshot?.Playlists.FirstOrDefault(p=>p.ProviderPlaylistId==providerPlaylistId);
 if(playlist==null)return [];
 return playlist.Tracks.Select((t,i)=>{
  var row=ConceptRow(t,$"kugou-concept:{displayedSnapshot!.AccountKey}:{playlist.ProviderPlaylistId}:{i}:{t.ProviderTrackId}");
  return P2AWorkspace.M212SnapshotPreview||P2AWorkspace.M213RowMode
   ?new TrackRow(row.Track,this,memberId:i+1,listId:SelectedPlaylistId):row;
 }).ToArray();}var l=Host.Player.State.Lists.FirstOrDefault(l=>l.Id==SelectedPlaylistId);if(l==null)return [];return l.SongIds.Select((id,i)=>{var t=Host.Player.State.Tracks.First(x=>x.Id==id);return Row(t,l.EntryIds![i],l.Id);}).ToArray();}
 public void SetPage(int n){var rows=Members();PageNumber=Math.Clamp(n,0,Math.Max(0,(rows.Length-1)/50));Fill(DetailPage,rows.Skip(PageNumber*50).Take(50));if(!Local){prevPage.IsVisible=nextPage.IsVisible=rows.Length>50;prevPage.IsEnabled=PageNumber>0;nextPage.IsEnabled=(PageNumber+1)*50<rows.Length;}}
 public void OpenPlaylist(long id){
  if(!Local){var lists=displayedSnapshot?.Playlists;if(lists==null||id<1||id>lists.Count)return;providerPlaylistId=lists[(int)id-1].ProviderPlaylistId;}
  SelectedPlaylistId=id;SetPage(0);Navigate(DetailPage);
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
 public void Navigate(R2Page page,bool push=true){Save();if(push&&CurrentPage!=null&&CurrentPage!=page)BackStack.Push(CurrentPage);CurrentPage=page;content.Content=page.View;heading.Text=Name+" · "+(page==DetailPage?(Local?Host.Player.State.Lists.FirstOrDefault(l=>l.Id==SelectedPlaylistId)?.Name:displayedSnapshot?.Playlists.FirstOrDefault(l=>l.ProviderPlaylistId==providerPlaylistId)?.Name)??page.DisplayName:page.DisplayName);status.Text=Local?"本地曲库":Host.Concept.Summary;Restore();}
 public void Back(){if(BackStack.TryPop(out var p))Navigate(p,false);}
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
 public ICommand SwitchLocalCommand {get;}public ICommand SwitchKugouCommand {get;}
 internal ShellModel(CorePlaybackAdapter core){Concept=new P2AWorkspace(core);Player=new CorePresentation(core);LocalWorkspace=new(this,true);KugouConceptWorkspace=new(this,false);SwitchLocalCommand=new RelayCommand(()=>Switch(LocalWorkspace));SwitchKugouCommand=new RelayCommand(()=>Switch(KugouConceptWorkspace));Player.Updated+=OnSnapshot;Switch(LocalWorkspace);Player.Attach(this);}
 void OnSnapshot(){LocalWorkspace.Refresh();KugouConceptWorkspace.Refresh();OnPropertyChanged(nameof(SourceLabel));}
 public void Switch(WorkspaceState next){workspace?.Save();workspace=next;OnPropertyChanged(nameof(Workspace));OnPropertyChanged(nameof(WorkspaceRoot));next.Restore();}
 public void Dispose(){Player.Updated-=OnSnapshot;Player.Dispose();Concept.Dispose();PreviewAssets.Dispose();}
}
