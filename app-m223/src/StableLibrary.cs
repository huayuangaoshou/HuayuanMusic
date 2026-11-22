using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.ObjectModel;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 readonly DispatcherTimer searchDebounce=new(DispatcherPriority.Normal){Interval=TimeSpan.FromMilliseconds(220)};
 readonly ObservableCollection<ListSongView> visibleSongs=new();readonly Dictionary<(int,long),ListSongView> rowCache=new();
 TextBox? librarySearch;
 bool IsComposing(TextBox input)=>input.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Any(x=>!string.IsNullOrEmpty(x.PreeditText));
 Control? emptyFeedback;Button? libraryPlay;TextBlock? libraryCount;Grid? nightPageHost;Control? playerSurface;long fullRenders,localUpdates;
 Button? playerInfo;TextBlock? playerTitle,playerArtist,playerTotal;ContentControl? playerArtwork;
 TextBlock? queueCurrentTitle,queueCurrentArtist;ContentControl? queueCurrentArtwork;ListBox? nativeQueue;
 void RefreshQueueCurrent(bool select=true){if(queueCurrentTitle!=null)queueCurrentTitle.Text=Track(nowSong).Title;if(queueCurrentArtist!=null)queueCurrentArtist.Text=Track(nowSong).Artist;if(queueCurrentArtwork!=null)queueCurrentArtwork.Content=Cover(nowSong,40,40);if(select&&nativeQueue!=null)nativeQueue.SelectedIndex=queueIndex;}
 long selectedPlaylistEntryId;
 readonly Stack<string> navigationHistory=new();bool goingBack;
 IEnumerable<Song> FilteredSongs()=>page=="收藏"?songs.Where(x=>favorites.Contains(x.Id)):page=="历史"?history.Select(Track):page=="专辑"?songs.Where(x=>x.Album==Track(nightAlbum).Album):page=="本地歌单"?PlaylistSongs():SortLibrary(songs.Where(x=>string.IsNullOrWhiteSpace(query)||(x.Title+" "+x.Artist+" "+x.Album).Contains(query.Trim(),StringComparison.OrdinalIgnoreCase)));
 void RefreshRows(){if(mainSongs==null)return;var rows=FilteredSongs().ToArray();var selected=selectedSong;var wanted=new List<ListSongView>();foreach(var s in rows){if(!rowCache.TryGetValue((s.Id,s.PlaylistEntryId),out var row)||row.Track!=s)rowCache[(s.Id,s.PlaylistEntryId)]=row=new(s);wanted.Add(row);}
  for(int i=0;i<wanted.Count;i++){if(i<visibleSongs.Count&&ReferenceEquals(visibleSongs[i],wanted[i]))continue;int old=visibleSongs.IndexOf(wanted[i]);if(old>=0)visibleSongs.Move(old,i);else visibleSongs.Insert(i,wanted[i]);}while(visibleSongs.Count>wanted.Count)visibleSongs.RemoveAt(visibleSongs.Count-1);
  int index=rows.ToList().FindIndex(x=>selectedPlaylistEntryId>0&&page=="本地歌单"?x.PlaylistEntryId==selectedPlaylistEntryId:x.Id==selected);if(index<0&&rows.Length>0)index=0;mainSongs.SelectedIndex=index;if(index>=0)selectedSong=rows[index].Id;
  mainSongs.IsVisible=rows.Length>0;if(libraryCount!=null)libraryCount.Text=rows.Length+" 项";if(libraryPlay!=null)libraryPlay.IsEnabled=rows.Length>0;if(emptyFeedback!=null)emptyFeedback.IsVisible=rows.Length==0;localUpdates++;
 }
 void QueueSearch(TextBox input){query=input.Text??"";searchDebounce.Stop();searchDebounce.Tick-=ApplySearch;searchDebounce.Tick+=ApplySearch;searchDebounce.Start();}
 void ApplySearch(object? s,EventArgs e){searchDebounce.Stop();if(librarySearch!=null&&IsComposing(librarySearch)){searchDebounce.Start();return;}if(page=="本地音乐")RefreshRows();}
 void Back(){if(navigationHistory.Count==0){Notify("已经是起始页面");return;}goingBack=true;try{Go(navigationHistory.Pop());}finally{goingBack=false;}}
 void RefreshPlayerMetadata(){var s=Track(nowSong);if(playerInfo!=null)ToolTip.SetTip(playerInfo,s.Title+" · "+s.Artist);if(playerTitle!=null)playerTitle.Text=s.Title;if(playerArtist!=null)playerArtist.Text=s.Artist;if(playerTotal!=null)playerTotal.Text=Time(s.Duration);if(playerArtwork!=null)playerArtwork.Content=Cover(s.Id,49,49);favoriteControls.Clear();foreach(var b in this.GetVisualDescendants().OfType<Button>().Where(x=>x.Name=="CoreFavorite").ToArray()){int id=core.State.CurrentId>0?core.State.CurrentId:selectedSong;favoriteControls.Add((b,id));b.Tag=null;}RefreshBarState();}
 void UpdateQueueSurface(){
  if(nightPageHost==null)return;
  if(queueOpen&&c31QueueSurface!=null&&nightPageHost.Children.Contains(c31QueueSurface)&&nativeQueue is {} list){
   bool hadFocus=list.IsKeyboardFocusWithin;var scroll=list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();var offset=scroll?.Offset??default;Guid? selected=(list.SelectedItem as QueueDisplay)?.EntryId;
   UpdateStableQueueRows(list);var wanted=queueRows.FirstOrDefault(x=>x.EntryId==selected);list.SelectedItem=wanted??queueRows.FirstOrDefault(x=>x.EntryId==core.State.CurrentEntryId);RefreshQueueCurrent(false);if(queueCountText!=null)queueCountText.Text=$"{queueRows.Count} 首 · 本地受控副本 · 上限1000";UpdateLayout();if(hadFocus&&!list.IsKeyboardFocusWithin)list.Focus();if(scroll!=null)scroll.Offset=offset;
   Dispatcher.UIThread.Post(()=>{if(ReferenceEquals(nativeQueue,list)&&scroll!=null)scroll.Offset=offset;},DispatcherPriority.Loaded);return;
  }
  if(c31QueueSurface!=null)nightPageHost.Children.Remove(c31QueueSurface);c31QueueSurface=null;if(queueOpen){c31QueueSurface=NightQueue();nightPageHost.Children.Add(c31QueueSurface);Enter(c31QueueSurface,18);}
 }

 void RefreshCurrentPage(){if(page=="正在播放"&&nightBody!=null){nightBody.Content=NightLyrics();}RefreshPlayerMetadata();UpdatePlayback();}
}
