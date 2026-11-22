using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.VisualTree;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 Button LibraryActionsButton(){var b=NI("more","曲库操作",()=>{});b.Click+=(_,_)=>ShowLibraryActions(b);return b;}
 string librarySort="导入顺序";
 IEnumerable<Song> SortLibrary(IEnumerable<Song> rows)=>librarySort switch{
  "歌曲"=>rows.OrderBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Id),
  "歌手"=>rows.OrderBy(x=>x.Artist,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Id),
  "时长"=>rows.OrderBy(x=>x.Duration).ThenBy(x=>x.Id),_=>rows.OrderBy(x=>x.Id)};
 void SetLibrarySort(string order){if(!new[]{"导入顺序","歌曲","歌手","时长"}.Contains(order))throw new ArgumentException("Invalid sort");librarySort=order;RefreshRows();Notify("曲库排序："+order+"；不改变播放队列或歌单顺序");}
 void ShowLibraryActions(Control anchor){var menu=new MenuFlyout();var sort=new MenuItem{Header="曲库排序"};foreach(var text in new[]{"导入顺序","歌曲","歌手","时长"}){var mode=text;var item=new MenuItem{Header=(librarySort==mode?"✓ ":"")+mode};AutomationProperties.SetName(item,"按"+mode+"排序");item.Click+=(_,_)=>SetLibrarySort(mode);sort.Items.Add(item);}menu.Items.Add(sort);var actions=new MenuItem{Header="选中歌曲操作"};actions.Click+=(_,_)=>ShowSongFlyout(Track(selectedSong),anchor);menu.Items.Add(actions);menu.ShowAt(anchor);}
 internal async Task BodyLibraryChecks(){
  int count=0;void Check(bool ok,string name){Program.Log("BODY_UI_CHECK",new{ok,name});if(!ok)throw new InvalidOperationException(name);count++;}
  var list=mainSongs??throw new InvalidOperationException("Missing library");int selected=selectedSong;var actualQueue=core.State.Queue.ToArray();
  SetLibrarySort("歌曲");Check(visibleSongs.Select(x=>x.Track.Id).SequenceEqual(SortLibrary(songs).Select(x=>x.Id)),"title sort uses real metadata and stable IDs");
  SetLibrarySort("歌手");Check(visibleSongs.Select(x=>x.Track.Id).SequenceEqual(SortLibrary(songs).Select(x=>x.Id)),"artist sort uses metadata");
  SetLibrarySort("时长");Check(visibleSongs.Select(x=>x.Track.Id).SequenceEqual(SortLibrary(songs).Select(x=>x.Id)),"duration sort ordered");
  Check(ReferenceEquals(list,mainSongs)&&selectedSong==selected&&actualQueue.SequenceEqual(core.State.Queue),"sorting preserves selected track list instance and authoritative queue");
  list.ScrollIntoView(list.Items.Last()!);await Task.Delay(100);UpdateLayout();
  Check(list.GetVisualDescendants().OfType<ListBoxItem>().Count()<60,"sorted real500 library remains virtualized");
  Check(list.GetVisualDescendants().OfType<ListBoxItem>().Where(c=>c.Content is ListSongView).All(c=>c.GetVisualDescendants().OfType<Grid>().Any(g=>ReferenceEquals(g.Tag,c.Content))),"realized row template belongs to current content after scrolling");
  SetLibrarySort("导入顺序");Check(visibleSongs.Select(x=>x.Track.Id).SequenceEqual(songs.OrderBy(x=>x.Id).Select(x=>x.Id)),"original import order restored without database mutation");
  Program.Log("BODY_UI_COMPLETE",new{checks=count});
 }
}
