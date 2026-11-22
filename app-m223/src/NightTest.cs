using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Threading;
using System.Diagnostics;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 async Task NightTest(){
  var checks=0;var timings=new List<double>();
  void Check(bool ok,string label){Program.Log(ok?"NIGHT_PASS":"NIGHT_FAIL",new{label});if(!ok)throw new Exception(label);checks++;}
  async Task Page(string name){var sw=Stopwatch.StartNew();Go(name);await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Loaded);sw.Stop();timings.Add(sw.Elapsed.TotalMilliseconds);await Task.Delay(260);}
  async Task Shot(string label){Program.Log("NIGHT_CAPTURE",new{label,hwnd=TryGetPlatformHandle()?.Handle.ToInt64(),RenderScaling,Width,Height,MotionEnabled,MaterialsEnabled});var marker=Path.Combine(Program.Session,"capture-"+label+".ready");File.WriteAllText(marker,"ready");for(int n=0;n<100&&!File.Exists(marker+".done");n++)await Task.Delay(30);}
  try{
   await Task.Delay(650);Go("首页");await Task.Delay(300);Check(IsVisible&&TryGetPlatformHandle()?.Handle!=IntPtr.Zero,"actual native top-level HWND");Check(Math.Abs(RenderScaling-1.5)<.01,"actual Windows RenderScaling 150 percent");Check(!playing,"no autoplay; simulation only");Check(queue.Count==200,"existing 200-entry queue reused");Check(platforms.Count==3&&platforms.Values.Sum(x=>x.Accounts.Count)==5,"existing three provider five-account fixtures");Check(Track(1).Title=="月光备忘录","approved design fixture identity");await Shot("home");
   if(MotionEnabled){Enter(nightBody!,8);await Task.Delay(60);Check(nightBody!.Opacity>0&&nightBody.Opacity<1,"page animation actual intermediate opacity");await Task.Delay(250);Check(nightBody.Opacity>.99,"page animation completes");}
   playButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(playing,"native Button routed play command");playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!playing,"native Button routed pause command");Check(playButton.Content is Grid,"playback refresh retains independent material layers");
   seek!.Value=100;Check(Math.Abs(seconds-100)<.01,"native slider controls simulation position");var before=nowSong;Skip(1);Check(nowSong!=before,"next track changes view state");Skip(-1);Check(nowSong==before,"previous track restores state");playing=false;var fav=favorites.Contains(nowSong);Like(nowSong);Check(favorites.Contains(nowSong)!=fav,"favorite state toggles");Like(nowSong);Check(favorites.Contains(nowSong)==fav,"favorite cancel restores");
   await Page("本地音乐");Check(mainSongs?.Items.Count==48,"library presents 48 synthetic fixtures");await Shot("library");
   TabControl Categories()=>this.GetVisualDescendants().OfType<TabControl>().First(x=>x.Name=="LibraryCategories");
   Check(Categories().Items.Count==4,"four native categories retained");
   Check(!this.GetVisualDescendants().OfType<LiquidSelector>().Any(),"library has no drag lens or liquid selector");
   Check(GetType().Assembly.GetType("HuayuanPreview.GpuBackdropLens")==null&&GetType().Assembly.GetType("HuayuanPreview.RefractionProbe")==null,"no refraction renderer compiled into this candidate");
   var expected=new[]{"歌曲","专辑","歌手","歌单"};
   for(int i=1;i<4;i++){
    Categories().SelectedIndex=i;await Task.Delay(280);Check(nightTab==expected[i],"native category change "+expected[i]);
    Check(Categories().SelectedIndex==i,"selected state retained after render "+expected[i]);
    var tiles=this.GetVisualDescendants().OfType<UniformGrid>().First(x=>x.Name=="LibraryTiles");
    Check(tiles.Children.Count==(i==3?3:6),"category content remains available "+expected[i]);
    var top=tiles.TranslatePoint(default,this)!.Value.Y;var cat=Categories();var bottom=cat.TranslatePoint(default,this)!.Value.Y+cat.Bounds.Height;
    Check(top>=bottom-1,"category toolbar does not overlap content "+expected[i]);
    Check(cat.Bounds.Width>200&&cat.Bounds.Height>=32,"category controls have visible hit area "+expected[i]);
    await Shot(i==1?"albums":i==2?"artists":"library-playlists");
   }
   Categories().SelectedIndex=0;await Task.Delay(250);var first=(TabItem)Categories().SelectedItem!;first.Focus();
   first.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Right});await Task.Delay(280);
   Check(nightTab=="专辑","native category arrow-key navigation");
   Categories().SelectedIndex=0;await Task.Delay(250);Check(mainSongs?.Items.Count==48,"songs view restored after category changes");
   for(int i=0;i<12;i++){Categories().SelectedIndex=(i+1)%4;await Task.Delay(15);}Categories().SelectedIndex=0;await Task.Delay(280);
   Check(nightTab=="歌曲"&&Categories().SelectedIndex==0,"rapid switching has no delayed spring commit");
   ShowSongFlyout(Track(1),mainSongs);await Task.Delay(300);Check(c31SongFlyout?.IsOpen==true,"native song MenuFlyout opens");Check(TopLevel.GetTopLevel(c31SongFlyout!.Items.OfType<MenuItem>().First())?.TryGetPlatformHandle()?.Handle!=IntPtr.Zero,"native menu has popup HWND");await Shot("menu");c31SongFlyout.Hide();await Task.Delay(120);Check(!c31SongFlyout.IsOpen,"native menu closes");
   SetQueue(true);await Task.Delay(360);var list=this.GetVisualDescendants().OfType<ListBox>().First(x=>x.Name=="C31Queue");Check(list.Items.Count==200,"native queue list has 200 entries");var realized=list.GetVisualDescendants().OfType<ListBoxItem>().Count();Check(realized>0&&realized<60,"virtualized initial queue containers bounded");list.ScrollIntoView(list.Items[199]!);await Task.Delay(220);Check(list.GetVisualDescendants().OfType<ListBoxItem>().Count()<60,"queue last-item scroll remains virtualized");await Shot("queue");SetQueue(false);await Task.Delay(40);SetQueue(true);await Task.Delay(320);Check(queueOpen,"queue close/reopen interrupts safely");SetQueue(false);await Task.Delay(350);Check(!queueOpen,"queue closes after transition");
   foreach(var pair in platforms){provider=pair.Key;await Page("平台");foreach(var account in pair.Value.Accounts){var picker=this.GetVisualDescendants().OfType<ComboBox>().First();picker.SelectedIndex=pair.Value.Accounts.IndexOf(account);await Task.Delay(260);Check(CurrentAccount?.Key==account.Key,"provider/account selection "+pair.Key+"/"+account.Key);}}await Shot("platform");
   await Page("本地歌单");Check(mainSongs?.Items.Count>0,"existing local playlist renders entries");await Shot("playlist");nightAlbum=1;await Page("专辑");await Shot("album");
   await Page("正在播放");seconds=96;UpdatePlayback();await Task.Delay(550);Check(c3ActiveLyric==3,"lyrics follow simulation seek");Check(c3LyricViewport!.Offset.Y>0,"lyrics smoothly locate actual laid-out line");await Shot("lyrics");c31CoverOnly=true;Render();await Task.Delay(250);Check(c3LyricViewport==null,"cover-only hides lyric viewport");c31CoverOnly=false;Render();await Task.Delay(250);Check(c3LyricViewport!=null,"lyric mode restores viewport");
   await Page("设置");await Shot("settings");nightSettingsGroup="性能与辅助";Render();await Task.Delay(220);var motionBefore=reduced;var reduceToggle=this.GetVisualDescendants().OfType<ToggleSwitch>().First(x=>AutomationProperties.GetName(x)=="减少动态");reduceToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(reduced!=motionBefore,"native settings routed reduced-motion toggle");await Task.Delay(220);reduceToggle=this.GetVisualDescendants().OfType<ToggleSwitch>().First(x=>AutomationProperties.GetName(x)=="减少动态");reduceToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(reduced==motionBefore,"native settings toggle restores preference");nightSettingsGroup="外观";await Page("扩展管理");Check(this.GetVisualDescendants().OfType<Button>().Count(x=>!x.IsEnabled)>=3,"DLC install actions disabled");await Shot("extensions");
   await Page("设置");nightSettingsGroup="性能与辅助";Render();await Task.Delay(200);
   Check(!this.GetVisualDescendants().OfType<TextBlock>().Any(x=>(x.Text??"").Contains("折射样本")),"material demo removed from real settings page");
   reduced=true;Render();Check(!MotionEnabled,"reduced motion disables native transitions");c31Low=true;Render();Check(!MaterialsEnabled,"lightweight mode disables complex material");await Shot("fallback");c31Low=false;reduced=false;c31Contrast=true;Render();Check(HighContrast&&!MotionEnabled&&!MaterialsEnabled,"high contrast disables color decoration");c31Contrast=false;dark=!dark;await Page("首页");await Shot("opposite-theme");Check(queue.Count==200,"controls did not mutate queue source");
   timings.Sort();Program.Log("NIGHT_TEST_COMPLETE",new{checks,layoutOperationMedianMs=timings[timings.Count/2],layoutOperationP95Ms=timings[(int)((timings.Count-1)*.95)],actualPresentationFPS="NOT_MEASURED",pointerAutomation="NOT_CLAIMED",memory=Process.GetCurrentProcess().WorkingSet64});Close();
  }catch(Exception e){Program.Log("NIGHT_TEST_FAILED",e.ToString());Close();}
 }
}
