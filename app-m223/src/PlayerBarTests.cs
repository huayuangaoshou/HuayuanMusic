using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using Microsoft.Data.Sqlite;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 internal async Task PlayerBarTests(OfflineWindowRoot root,CorePlaybackAdapter adapter,Func<string,Task> shot,Action<bool,string> check){
  var engine=root.Coordinator.Player;await adapter.StopAsync();await adapter.VolumeAsync(0);await adapter.PlayAsync(1);await adapter.ToggleAsync(1);await Task.Delay(120);Go("本地音乐");await Task.Delay(150);
  async Task Click(Button b){b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await PendingCoreOperations;await Task.Delay(100);}
  bool Filled(Button b)=>((Avalonia.Controls.Shapes.Path)b.Content!).Fill is ISolidColorBrush c&&c.Color.A>0;
  Button Heart()=>favoriteControls.First().Button;
  if(!adapter.State.Favorites.Contains(1))await adapter.FavoriteAsync(1);await Task.Delay(100);
  check(Filled(Heart()),"P171 solid heart comes from true stored favorite");
  check(((ISolidColorBrush)((Avalonia.Controls.Shapes.Path)Heart().Content!).Fill!).Color.R>190,"P171 favorite soft red fill");await shot("favorite-solid");
  await Click(Heart());check(!Filled(Heart())&&!adapter.State.Favorites.Contains(1),"P171 cancel changes same DB and outlined glyph");await shot("favorite-outline");
  Heart().Focus();Heart().RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Space});Heart().RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyUpEvent,Key=Key.Space});await PendingCoreOperations;await Task.Delay(100);check(Filled(Heart())&&adapter.State.Favorites.Contains(1),"P171 native Button keyboard Space toggles true favorite");await Click(Heart());
  for(int i=0;i<6;i++)Heart().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await PendingCoreOperations;await Task.Delay(120);
  check(!Filled(Heart())&&!adapter.State.Favorites.Contains(1),"P171 six rapid favorite clicks serialize correctly");
  await Click(Heart());Go("正在播放");await Task.Delay(100);check(favoriteControls.Count>=2&&favoriteControls.All(x=>Filled(x.Button)),"P171 lyrics and player favorite share real snapshot");
  await adapter.NextAsync();await Task.Delay(150);check(adapter.State.Lyrics.Length>0&&c3LyricButtons.Select(x=>x.Content?.ToString()?.TrimStart('·',' ')).SequenceEqual(adapter.State.Lyrics.Select(x=>x.Text)),"P172 live lyric page updates when next-track lyrics finish loading");await adapter.PreviousAsync();await Task.Delay(150);if(engine.IsPlaying)await adapter.ToggleAsync(1);
  // Only this newly provisioned synthetic profile: fail its SQLite write, then remove the owned trigger.
  using(var db=new SqliteConnection("Data Source="+Path.Combine(OfflineStartupProfile.Current.Roaming,"huayuan-music-p1.db"))){db.Open();using var cmd=db.CreateCommand();
   cmd.CommandText="CREATE TRIGGER p171_owned_failure BEFORE INSERT ON kv_store WHEN NEW.scope='huayuan_local_music_records_v1' AND NEW.key='favorites' BEGIN SELECT RAISE(ABORT,'P171_OWNED_WRITE_FAILURE'); END;";cmd.ExecuteNonQuery();
   try{await adapter.FavoriteAsync(1);await Task.Delay(80);check(adapter.State.Error.Length>0&&adapter.State.Favorites.Contains(1)&&favoriteControls.All(x=>Filled(x.Button)),"P171 real SQLite failure preserves old favorite and reports error");}
   finally{cmd.CommandText="DROP TRIGGER p171_owned_failure;";cmd.ExecuteNonQuery();}
  }
  await shot("favorite-failure");await adapter.SeekAsync(3);Go("本地音乐");await Task.Delay(120);
  var slider=(CommitSlider)seek!;int seeks=adapter.SeekSubmissions;slider.Sync(3);slider.Value=4;check(adapter.SeekSubmissions==seeks,"P171 programmatic slider value never submits seek");slider.Sync(adapter.State.Position);
  slider.BeginPreview();for(int i=0;i<20;i++)slider.Preview(2+i*.1);slider.Sync(3);check(slider.IsEditing&&Math.Abs(slider.Value-3.9)<.01&&adapter.SeekSubmissions==seeks,"P171 preview is not stolen by clock and emits zero seeks");
  await slider.CommitPreviewAsync();check(adapter.SeekSubmissions==seeks+1&&Math.Abs(engine.GetPosition().TotalSeconds-3.9)<.1,"P171 twenty previews release submits exactly once to real engine");
  seeks=adapter.SeekSubmissions;slider.BeginPreview();slider.Preview(7);slider.CancelPreview();check(adapter.SeekSubmissions==seeks&&Math.Abs(slider.Value-slider.ModelValue)<.01,"P171 cancellation restores production position without seek");
  void SendKey(Control c,Key key,bool up=false)=>c.RaiseEvent(new KeyEventArgs{RoutedEvent=up?InputElement.KeyUpEvent:InputElement.KeyDownEvent,Key=key});
  slider.Focus();SendKey(slider,Key.Right);SendKey(slider,Key.Right);check(adapter.SeekSubmissions==seeks,"P171 keyboard autorepeat is preview only");SendKey(slider,Key.Right,true);await slider.PendingCommit;await PendingCoreOperations;check(adapter.SeekSubmissions==seeks+1,"P171 keyboard release single seek");
  await adapter.SeekAsync(4);await adapter.SeekAsync(4.1);check(Math.Abs(engine.GetPosition().TotalSeconds-4.1)<.04&&engine.IsPaused,"P171 paused sub-half-second seek reaches native clock");
  slider.BeginPreview();slider.Preview(8);await adapter.NextAsync();await Task.Delay(120);int afterSwitch=adapter.SeekSubmissions;await slider.CommitPreviewAsync();check(adapter.SeekSubmissions==afterSwitch&&!slider.IsEditing,"P171 track change cancels stale seek preview");
  await adapter.StopAsync();await adapter.PlayAsync(1);await adapter.ToggleAsync(1);await adapter.VolumeAsync(0);Go("本地音乐");await Task.Delay(100);
  var volumeSlider=await WaitForVolumeControlAsync(true);check(volumeSlider.Bounds.Width>=100&&volumeSlider.Bounds.Height>=32,"P171 usable volume width and hit area");int changes=adapter.VolumeSubmissions;
  volumeSlider.Sync(0);volumeSlider.Value=.9;check(adapter.VolumeSubmissions==changes&&root.Player.MusicVolume==0,"P171 production volume refresh cannot feedback into commands");
  volumeSlider.BeginPreview();for(int i=1;i<=8;i++)volumeSlider.Preview(i*.05);check(adapter.VolumeSubmissions==changes&&root.Player.MusicVolume==0,"P171 volume drag previews without changing real gain");await volumeSlider.CommitPreviewAsync();
  check(adapter.VolumeSubmissions==changes+1&&Math.Abs(root.Player.MusicVolume-.4)<.001&&Math.Abs(engine.GetVolume()-.16)<.003,"P171 release applies volume once to original engine gain");await shot("volume-visible");if(narrow)await shot("volume-popup");
  await Click(CurrentMuteControl());check(root.Player.IsMuted&&adapter.State.Volume==0,"P171 mute source agrees with zero volume");await Click(CurrentMuteControl());check(Math.Abs(adapter.State.Volume-.4)<.001&&!root.Player.IsMuted,"P171 unmute restores original VM remembered volume");
  await adapter.VolumeAsync(0);await Click(CurrentMuteControl());check(Math.Abs(adapter.State.Volume-.4)<.001,"P171 slider zero then restore uses same remembered volume");await adapter.VolumeAsync(0);if(volumeFlyout?.IsOpen==true)volumeFlyout.Hide();
  Go("本地音乐");await Task.Delay(100);mainSongs!.SelectedIndex=1;check(playingMarkers[1].Text=="Ⅱ"&&playingMarkers[2].Text=="02","P171 current paused marker differs from selected next row");
  var search=this.GetVisualDescendants().OfType<TextBox>().First(x=>x.Name=="LibrarySearch");search.Text="HY Test B";SendKey(search,Key.Enter);await Task.Delay(120);check(mainSongs!.Items.Count==1&&selectedSong==2,"P171 Enter search filters real library metadata");
  search=this.GetVisualDescendants().OfType<TextBox>().First(x=>x.Name=="LibrarySearch");search.Text="NO_MATCH_P171";SendKey(search,Key.Enter);await Task.Delay(100);check(this.GetVisualDescendants().Any(x=>x.Name=="LibraryEmpty")&&!this.GetVisualDescendants().OfType<Button>().First(x=>x.Name=="CorePlaySelected").IsEnabled,"P171 no results gives feedback and disables empty play");await shot("search-empty");
  query="";dark=true;reduced=true;Render();await Task.Delay(120);check(favoriteControls.All(x=>Filled(x.Button))&&reduced,"P171 dark reduced mode keeps real favorite feedback");await shot("dark-reduced");
  dark=false;reduced=false;Go("本地音乐");await Task.Delay(100);await adapter.VolumeAsync(0);await adapter.PlayAsync(1);check(adapter.State.Favorites.Contains(1)&&adapter.State.History.Contains(1)&&adapter.State.Volume==0,"P171 persistent favorite history and muted real playback retained");
 }
}
