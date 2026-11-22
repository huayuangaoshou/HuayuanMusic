using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using ManagedBass;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 async Task<CommitSlider> WaitForVolumeControlAsync(bool openIfClosed){
  if(narrow&&volumeFlyout?.IsOpen!=true){if(!openIfClosed)throw new InvalidOperationException("Volume popup unexpectedly closed before capture");OpenVolume();}
  for(int i=0;i<50;i++){
   UpdateLayout();
   var control=volumeControls.LastOrDefault(v=>v.IsVisible&&v.Bounds.Width>=100&&v.Bounds.Height>=32&&TopLevel.GetTopLevel(v) is {IsVisible:true} top&&(!narrow||top!=this));
   if(control!=null&&(!narrow||volumeFlyout?.IsOpen==true))return control;
   await Task.Delay(20);
  }
  throw new TimeoutException("Visible attached volume control did not stabilize within 1 second");
 }
 internal async Task IntegrationTest(OfflineWindowRoot root,CorePlaybackAdapter adapter){
  var process=System.Diagnostics.Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;var elapsed=System.Diagnostics.Stopwatch.StartNew();int checks=0;void Check(bool value,string name){Program.Log(value?"TEST_PASS":"TEST_FAIL",new{name});if(!value)throw new Exception(name);checks++;}
  async Task Click(Button b){b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await PendingCoreOperations;await Task.Delay(130);}
  Button FindButton(string name)=>name=="静音"?CurrentMuteControl():this.GetVisualDescendants().OfType<Button>().First(b=>b.Name==name||AutomationProperties.GetName(b)==name);
  Slider FindSlider(string name){if(name=="音量"){throw new InvalidOperationException("Volume must be resolved asynchronously");}return this.GetVisualDescendants().OfType<Slider>().First(b=>AutomationProperties.GetName(b)==name);}
  async Task SetUserSlider(string name,double value){var c=name=="音量"?await WaitForVolumeControlAsync(true):(CommitSlider)FindSlider(name);c.BeginPreview();c.Preview(value);await c.CommitPreviewAsync();await PendingCoreOperations;}
  async Task Shot(string name){
   if(WindowState==WindowState.Minimized){Program.Log("CAPTURE_RESTORE_OWN_WINDOW",name);WindowState=WindowState.Normal;}
   // Activating the owner light-dismisses its Flyout. Preserve the active popup during both volume shots.
   if(volumeFlyout?.IsOpen!=true)Activate();
   await Task.Delay(200);UpdateLayout();
   var volume=name=="volume-popup"?await WaitForVolumeControlAsync(false):null;
   var target=volume==null?this:TopLevel.GetTopLevel(volume);
   var handle=target?.TryGetPlatformHandle()?.Handle??IntPtr.Zero;
   if(handle==IntPtr.Zero||target?.IsVisible!=true)throw new InvalidOperationException("Capture target is not a visible native window");
   Program.Log("CAPTURE",new{name,hwnd=handle.ToInt64(),RenderScaling,popupOpen=volumeFlyout?.IsOpen==true,volumeCount=volumeControls.Count});
   var path=Path.Combine(Program.Session,name+".ready");File.WriteAllText(path,"ready");
   for(int i=0;i<250&&!File.Exists(path+".done");i++)await Task.Delay(20);
   if(!File.Exists(path+".done"))throw new TimeoutException("Owned window capture did not acknowledge within 5 seconds: "+name);
   if(volume!=null&&(!volumeControls.Contains(volume)||volumeFlyout?.IsOpen!=true))throw new InvalidOperationException("Volume popup closed during screenshot");
  }

  try{
   await Task.Delay(650);UpdateLayout();var engine=root.Coordinator.Player;
   Check(IsVisible&&TryGetPlatformHandle()?.Handle!=IntPtr.Zero,"actual Nightflight native HWND");Check(Math.Abs(RenderScaling-1.5)<.01,"actual 150 percent DPI");
   Check(adapter.State.Tracks.Length>=2&&mainSongs!.Items.Count==adapter.State.Tracks.Length,"real library rows from production SQLite");
   Check(!playing&&engine.IsStopped&&Bass.CurrentDevice==-1&&queue.Count==0,"no autoplay device or restored old queue");Check(!timer.IsEnabled,"UI synthetic playback timer not started");
   Check(OfflineStartupNetwork.Requests==1,"terminal Mock preflight only before UI");
   if(Environment.GetEnvironmentVariable("P17_RESTART")=="1"){Check(adapter.State.Favorites.Contains(1),"favorite persisted across process restart");Check(adapter.State.History.Contains(1),"history persisted across process restart");Check(adapter.State.Lists.Length>0,"library and playlist persisted across restart");}
   await Shot("library-ready");
   var tabs=this.GetVisualDescendants().OfType<TabControl>().First(x=>x.Name=="LibraryCategories");Check(tabs.Items.Count==4&&!this.GetVisualDescendants().OfType<LiquidSelector>().Any(),"four simplified native library categories without drag lens");
   for(int i=1;i<4;i++){tabs=this.GetVisualDescendants().OfType<TabControl>().First(x=>x.Name=="LibraryCategories");tabs.SelectedIndex=i;await Task.Delay(80);Check(nightTab==new[]{"歌曲","专辑","歌手","歌单"}[i],"category selection "+i);}
   this.GetVisualDescendants().OfType<TabControl>().First(x=>x.Name=="LibraryCategories").SelectedIndex=0;await Task.Delay(100);mainSongs!.SelectedIndex=0;
   await Click(FindButton("CorePlaySelected"));await Task.Delay(500);var p0=engine.GetPosition().TotalSeconds;await Task.Delay(400);
   Check(engine.IsPlaying&&root.Player.IsPlayingAudio&&engine.GetPosition().TotalSeconds>p0+.15,"real BASS production audio clock advances");
   Check(engine.GetActiveAnalysisSnapshot().Rms>0,"nonzero PCM decoded by original engine");Check(adapter.State.Playing&&playing,"real playing state snapshot reaches Nightflight");Check(Math.Abs(seconds-engine.GetPosition().TotalSeconds)<.35,"UI seconds read back native audio clock");
   Check(root.Player.CurrentPlayingSong!.LocalFilePath!.StartsWith(OfflineStartupProfile.Current.Media,StringComparison.OrdinalIgnoreCase),"source confined to admitted profile");Check(adapter.State.Volume==0&&engine.GetVolume()==0,"automated playback is muted");Check(queue.Count==root.Player.PlaybackQueue.Count&&queue.Count==adapter.State.Tracks.Length,"queue is production projection without mock entries");
   await Shot("playing-native");await Click(playButton!);p0=engine.GetPosition().TotalSeconds;await Task.Delay(350);Check(engine.IsPaused&&!playing&&Math.Abs(engine.GetPosition().TotalSeconds-p0)<.06,"routed pause freezes real audio and state");
   await SetUserSlider("播放进度",5);await Task.Delay(150);Check(Math.Abs(engine.GetPosition().TotalSeconds-5)<.16&&Math.Abs(adapter.State.Position-5)<.16,"native seek slider reaches production engine and snapshot");
   await SetUserSlider("音量",.2);Check(Math.Abs(root.Player.MusicVolume-.2)<.001&&Math.Abs(engine.GetVolume()-.04)<.003,"volume slider uses original production gain");await Click(FindButton("静音"));Check(root.Player.IsMuted&&adapter.State.Volume==0&&engine.GetVolume()==0,"mute uses production state");await Click(FindButton("静音"));Check(!root.Player.IsMuted,"unmute restores volume");await SetUserSlider("音量",0);if(volumeFlyout?.IsOpen==true)volumeFlyout.Hide();
   Go("正在播放");await Task.Delay(450);Check(adapter.State.Lyrics.Length>0&&c3LyricButtons.Count==root.Player.LyricLines.Count,"real LyricsService lines presented by Nightflight");var line=Math.Min(1,c3LyricButtons.Count-1);await Click(c3LyricButtons[line]);await Task.Delay(250);Check(Math.Abs(engine.GetPosition().TotalMilliseconds-root.Player.LyricLines[line].StartTime)<180,"lyric click seeks original LRC timestamp");Check(c3ActiveLyric==root.Player.CurrentLyricIndex,"current lyric visual uses production index");await Shot("lyrics-real-clock");
   await Click(playButton!);await Task.Delay(200);Check(engine.IsPlaying&&playing,"resume via original toggle command");var first=root.Player.CurrentPlayingSong!.LocalFilePath;await Click(FindButton("下一首"));Check(root.Player.CurrentPlayingSong!.LocalFilePath!=first&&adapter.State.CurrentId==nowSong,"next track and metadata readback");await Click(FindButton("上一首"));Check(root.Player.CurrentPlayingSong!.LocalFilePath==first,"previous track uses production queue");
   await Click(FindButton("CoreStop"));Check(engine.IsStopped&&!playing,"stop command reaches original engine");
   if(!adapter.State.Favorites.Contains(1))await adapter.FavoriteAsync(1);await Task.Delay(100);Check(LocalMusicRecordStore.LoadFavorites().Any(x=>x.LocalFilePath==first),"favorite stored in original local SQLite namespace");await adapter.FavoriteAsync(1);Check(!LocalMusicRecordStore.IsFavorite(root.ViewModel.Library.SelectedPlaylistSongs[0]),"favorite cancellation real storage");await adapter.FavoriteAsync(1);
   Go("收藏");await Task.Delay(200);Check(mainSongs!.Items.Count==adapter.State.Favorites.Length&&mainSongs.Items.Count>0,"favorites page shows persistent core rows");Go("历史");await Task.Delay(150);await Click(FindButton("清除本地历史"));Check(LocalMusicRecordStore.LoadHistory().Count==0&&adapter.State.Favorites.Contains(1),"clear history preserves favorites");
   Go("本地音乐");await Task.Delay(200);mainSongs!.SelectedIndex=0;await Click(FindButton("CorePlaySelected"));Check(adapter.State.History.Contains(1)&&LocalMusicRecordStore.LoadHistory().Count>0,"successful real playback records persistent history");
   SetQueue(true);await Task.Delay(300);var q=this.GetVisualDescendants().OfType<ListBox>().First(x=>x.Name=="C31Queue");Check(q.Items.Count==root.Player.PlaybackQueue.Count,"native drawer matches sole production queue");Check(this.GetVisualDescendants().OfType<Button>().Any(x=>x.IsEnabled&&AutomationProperties.GetName(x)=="保存队列为歌单"),"real queue save entry available (P173 replaces unavailable assertion)");SetQueue(false);await Task.Delay(250);
   await adapter.PlayAsync(-1);Check(adapter.State.Error.Length>0,"unadmitted selection safely reported");await Click(FindButton("CorePlaySelected"));Check(engine.IsPlaying&&adapter.State.Error.Length==0,"valid playback recovers after rejected input");Check(!status.Contains("仅允许当前隔离测试曲目"),"recovery clears stale UI error feedback");Check(OfflineStartupNetwork.Requests==1,"all playback lyrics records and queue operations use no real HTTP");
   int baseChecks=checks;await PlayerBarTests(root,adapter,Shot,Check);int playerBarChecks=checks-baseChecks;await Shot("final-library");process.Refresh();Program.Log("TEST_COMPLETE",new{checks,baseChecks,playerBarChecks,elapsedSeconds=elapsed.Elapsed.TotalSeconds,cpuSeconds=(process.TotalProcessorTime-cpu).TotalSeconds,logicalCPUs=Environment.ProcessorCount,workingSetMiB=process.WorkingSet64/1048576d,peakWorkingSetMiB=process.PeakWorkingSet64/1048576d,presentationFrames="NOT_MEASURED",pcm=engine.GetActiveAnalysisSnapshot().Rms,clock=engine.GetPosition().TotalSeconds,device=Bass.CurrentDevice,queue=adapter.State.Queue.Length,source=Path.GetFileName(root.Player.CurrentPlayingSong!.LocalFilePath),realSound="HUMAN_AUDIO_PENDING"});Close();
  }catch(Exception e){Program.Result=2;Program.Log("TEST_FAILED",e.ToString());Close();}
 }
}
