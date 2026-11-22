using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 async Task RedesignTest(){
  int checks=0;void Check(bool ok,string label){Program.Log(ok?"DESIGN_PASS":"DESIGN_FAIL",new{label});if(!ok)throw new Exception(label);checks++;}
  async Task Capture(string label){Program.Log("CAPTURE",new{label});await Task.Delay(700);}
  async Task Click(string name){var b=this.GetVisualDescendants().OfType<Button>().FirstOrDefault(x=>AutomationProperties.GetName(x)==name)??throw new Exception("Missing "+name);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(320);}
  try{
   await Task.Delay(650);Check(IsVisible&&TryGetPlatformHandle()?.Handle!=IntPtr.Zero,"native candidate window visible");Check(Math.Abs(RenderScaling-1.5)<.01,"150 percent DPI");Check(!playing,"no automatic playback even mock");Check(queue.Count==200&&songs.Length==48,"original synthetic media identity preserved");
   await Capture(gallery?"B-home":"A-home");var initial=gallery;await Click(initial?"设计 A":"设计 B");Check(gallery!=initial,"real variant button switches composition");await Capture(gallery?"B-home":"A-home");
   Go("正在播放");await Task.Delay(350);seconds=96;UpdatePlayback();await Task.Delay(400);Check(c3ActiveLyric==3,"current line uses inherited timeline");Check(c3LyricViewport!.Offset.Y>0,"actual layout-based line positioning");await Capture(gallery?"B-lyrics":"A-lyrics");
   await Click("专注封面");Check(c31CoverOnly,"cover-only view");await Capture(gallery?"B-cover":"A-cover");await Click("显示歌词");Check(!c31CoverOnly,"lyrics mode returns");
   SetDesign(!gallery);Go("正在播放");await Task.Delay(350);seconds=128;UpdatePlayback();await Task.Delay(400);Check(c3ActiveLyric==4,"other composition shares lyric state");await Capture(gallery?"B-lyrics":"A-lyrics");
   SetDesign(false);kind="全部歌曲";Go("本地音乐");await Task.Delay(350);Check(mainSongs?.Items.Count==48,"dense native song list");await Capture("A-library");
   categoryLens!.Select(1);await Task.Delay(350);Check(kind=="专辑"&&libraryHost!.GetVisualDescendants().OfType<Image>().Any(),"capsule opens album grid");await Capture("A-albums");
   var lens=categoryLens!;var before=lens.Position;lens.Pull(80);Check(lens.Position!=before,"capsule adapter supports constrained drag geometry, not pointer proof");lens.Select(2);await Task.Delay(350);Check(kind=="歌手","capsule opens artists");lens.Select(3);await Task.Delay(300);Check(kind=="歌单","capsule opens playlists");lens.Select(0);await Task.Delay(250);Check(mainSongs?.Items.Count==48,"capsule restores songs");
   query="没有这样的歌曲";Render();await Task.Delay(250);Check(this.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="没有找到匹配的音乐"),"real empty search state");query="";Render();await Task.Delay(250);
   var trigger=this.GetVisualDescendants().OfType<Button>().First(x=>(AutomationProperties.GetName(x)??"").StartsWith("歌曲操作 "));ShowSongFlyout(Track(1),trigger);await Task.Delay(250);Check(c31SongFlyout!.IsOpen&&ReferenceEquals(menuAnchor,trigger),"native menu is anchored at song operation trigger");CloseSongMenu();await Task.Delay(250);Check(!c31SongFlyout.IsOpen,"native menu closes");
   localList=localLists.Keys.First();Go("本地歌单");await Task.Delay(350);Check(mainSongs!.Items.Count==localLists[localList].Count,"playlist detail presents original queue entries");await Capture("A-playlist");
   var oldCount=localLists.Count;SaveQueue();await Task.Delay(350);Check(localLists.Count==oldCount+1&&localLists[localList].Count==200,"queue snapshot retains 200 entries in memory only");Check(localLists[localList].Select(x=>x.Origin).Distinct().Count()==4,"snapshot retains four source labels");
   var favorite=favorites.Contains(4);Like(4);Check(favorites.Contains(4)!=favorite,"favorite action preserved");Like(4);Check(favorites.Contains(4)==favorite,"favorite restored");Play(6);Check(playing&&history[0]==6&&nowSong==6,"mock play shares history and visual state");TogglePlay();Check(!playing,"mock pause");Skip(1);Check(playing,"mock next song");TogglePlay();
   OpenPlatform("qq");await Task.Delay(300);Check(CurrentAccount!=null,"platform account space uses original identities");await Capture("A-platform");Go("扩展管理");await Task.Delay(300);var installs=this.GetVisualDescendants().OfType<Button>().Where(b=>(AutomationProperties.GetName(b)??"").StartsWith("安装DLC")).ToArray();Check(installs.Length==3&&installs.All(b=>!b.IsEnabled),"unimplemented plugin installs actually disabled");await Capture("A-extensions");
   Go("设置");await Task.Delay(300);Check(this.GetVisualDescendants().OfType<Slider>().Any(s=>AutomationProperties.GetName(s)=="材质强度"),"material strength is a real setting");await Capture("A-settings");
   reduced=true;Render();Check(!MotionEnabled,"reduced mode");c31Low=true;Render();Check(!MotionEnabled&&!MaterialsEnabled,"lightweight fallback");c31Low=false;reduced=false;dark=!dark;Go("首页");await Task.Delay(350);await Capture("opposite-home");
   var old=WindowState;WindowState=WindowState.Maximized;await Task.Delay(450);Check(WindowState==WindowState.Maximized,"native maximization");WindowState=old;await Task.Delay(450);Check(WindowState==old,"native normal restoration");
   Width=980;Height=720;await Task.Delay(500);Check(narrow,"responsive 980 x 720 layout");Check(this.GetVisualDescendants().OfType<Slider>().Any(s=>AutomationProperties.GetName(s)=="音量"),"narrow layout retains volume adjustment");await Capture("narrow-home");
   var original=songs[0];var originalNow=nowSong;songs[0]=original with{Title="超长中文歌名｜夜空翻开未寄出的信 · Unicode Café 星河与海岸的声音档案 / A very long original demo title"};nowSong=1;kind="全部歌曲";Go("本地音乐");await Task.Delay(350);var meta=this.GetVisualDescendants().OfType<StackPanel>().First(x=>x.Name=="PlayerMetadata");Check(meta.Bounds.Width<=96&&meta.GetVisualDescendants().OfType<TextBlock>().All(x=>x.TextTrimming==Avalonia.Media.TextTrimming.CharacterEllipsis),"long Chinese Unicode player title remains bounded and ellipsized");await Capture("long-title");songs[0]=original;nowSong=originalNow;Go("首页");SetDesign(true);await Task.Delay(350);await Capture("B-narrow-home");Go("正在播放");await Task.Delay(350);await Capture("B-narrow-lyrics");
   c31Contrast=true;Render();Check(!MaterialsEnabled&&!MotionEnabled,"high contrast bypasses materials and motion");Check(Equals(R("Text"),Avalonia.Media.Brushes.White),"high contrast text");await Capture("contrast-lyrics");
   Program.Log("DESIGN_TEST_COMPLETE",new{checks,actualPointer="requires separate external input",presentationFrames="not inferred"});Close();
  }catch(Exception error){Program.Log("DESIGN_TEST_FAILED",error.ToString());Close();}
 }
}
