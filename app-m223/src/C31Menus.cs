using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Layout;
namespace HuayuanPreview;
public sealed partial class PreviewWindow{
 MenuFlyout? c31SongFlyout;Control? menuAnchor;
 void ShowSongFlyout(Song song,Control? anchor=null){
  c31SongFlyout?.Hide();selectedSong=song.Id;songActionsOpen=true;menuAnchor=anchor??mainSongs??(Control)root;
  var menu=new MenuFlyout{Placement=PlacementMode.BottomEdgeAlignedLeft};
  void Item(string label,string icon,Action action){var item=new MenuItem{Header=label,Icon=C3Icon(icon)};item.Click+=(_,_)=>{action();menu.Hide();};menu.Items.Add(item);}
  Item("播放选中歌曲","play",()=>{if(song.PlaylistEntryId>0)PlayPlaylistEntry(song);else Play(song.Id);});Item("下一首播放","queue",()=>RunCore(async()=>{await core.QueueAsync("next",song.Id);if(core.State.Error.Length==0)Notify(core.State.CurrentId==song.Id?"当前歌曲已保持原状":"已放到当前曲目后面；连续插队按最近一次优先");}));Item("追加到队列末尾","queue",()=>RunCore(async()=>{await core.QueueAsync("append",song.Id);if(core.State.Error.Length==0)Notify("已追加到队列末尾");}));
  menu.Items.Add(new MenuItem{Header="-"});Item(favorites.Contains(song.Id)?"取消收藏":"收藏歌曲","heart",()=>Like(song.Id));var add=new MenuItem{Header="加入本地歌单"};foreach(var list in core.State.Lists){var captured=list;var entry=new MenuItem{Header=list.Name};entry.Click+=(_,_)=>{RunCore(()=>core.PlaylistAsync("add",listId:captured.Id,songId:song.Id));menu.Hide();};add.Items.Add(entry);}menu.Items.Add(add);Item("新建本地歌单","plus",()=>PlaylistNameDialog("create"));if(page=="本地歌单"){Item("从当前歌单移除","close",()=>RunCore(()=>core.PlaylistEntryAsync("remove",ActiveListId,song.PlaylistEntryId)));var at=Array.IndexOf(core.State.Lists.FirstOrDefault(x=>x.Id==ActiveListId)?.EntryIds??[],song.PlaylistEntryId);Item("歌单内上移","queue",()=>RunCore(()=>core.PlaylistEntryAsync("move",ActiveListId,song.PlaylistEntryId,Math.Max(0,at-1))));Item("歌单内下移","queue",()=>RunCore(()=>core.PlaylistEntryAsync("move",ActiveListId,song.PlaylistEntryId,Math.Min((core.State.Lists.FirstOrDefault(x=>x.Id==ActiveListId)?.SongIds.Length??1)-1,at+1))));}Item("关闭菜单","close",()=>{});
  menu.Closed+=(_,_)=>{songActionsOpen=false;if(menuAnchor?.IsVisible==true)menuAnchor.Focus();Event("C31_MENU_CLOSE","native menu closed");};
  menu.Opened+=(_,_)=>Event("C31_MENU_OPEN",new{native=true,HighContrast});c31SongFlyout=menu;menu.ShowAt(menuAnchor);Notify("本地歌曲操作 / 隔离生产核心");
 }
}
