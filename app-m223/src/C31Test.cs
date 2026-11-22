using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
namespace HuayuanPreview;
public sealed partial class PreviewWindow{
 async Task C31Test(){
  int count=0;void Check(bool ok,string label){Program.Log(ok?"C31_PASS":"C31_FAIL",new{label});if(!ok)throw new Exception(label);count++;}
  async Task Click(string name){var b=this.GetVisualDescendants().OfType<Button>().FirstOrDefault(x=>AutomationProperties.GetName(x)==name)??throw new Exception("Missing button "+name);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);}
  try{
   await Task.Delay(600);Check(IsVisible&&TryGetPlatformHandle()?.Handle is {} handle&&handle!=IntPtr.Zero,"actual native window created");Check(Math.Abs(RenderScaling-1.5)<.01,"150 percent actual DPI");
   if(MotionEnabled){Enter(c31Page!,8);await Task.Delay(60);var middle=c31Page!.Opacity;await Task.Delay(220);Check(middle>0&&middle<1&&Math.Abs(c31Page.Opacity-1)<.001,"attached page has real intermediate and completed animation values");}
   else Check(playButton!.Transitions!.Count==0,"startup reduced/low mode removes core animation transitions");
   Check(queue.Count==200,"200 synthetic queue entries");Check(platforms.Count==3&&platforms.Values.Sum(p=>p.Accounts.Count)==5,"three mock platforms five independent accounts");
   Program.Log("CAPTURE",new{label="initial",RenderScaling,Bounds,MotionEnabled,MaterialsEnabled,HighContrast,transparency=ActualTransparencyLevel.ToString()});await Task.Delay(650);
   var b=playButton!;Feedback(b,"hover");await Task.Delay(180);Feedback(b,"press");await Task.Delay(160);Check(b.RenderTransform!=null,"hover press render transform feedback configured");Feedback(b,"rest");
   await Click("播放");Check(playing,"routed play button mock state");await Click("暂停");Check(!playing,"routed pause button mock state");
   SetQueue(true);await Task.Delay(400);var list=this.GetVisualDescendants().OfType<ListBox>().First(x=>x.Name=="C31Queue");
   Check(list.Items.Count==200,"queue presents all 200 items");var realized=list.GetVisualDescendants().OfType<ListBoxItem>().Count();Check(realized>0&&realized<60,"queue virtualization bounded realized containers");
   list.ScrollIntoView(list.Items[199]!);await Task.Delay(250);Check(list.GetVisualDescendants().OfType<ListBoxItem>().Count()<60,"virtualization remains bounded after last item scroll");
   Program.Log("CAPTURE",new{label="queue",realized});await Task.Delay(650);SetQueue(false);await Task.Delay(40);SetQueue(true);await Task.Delay(300);Check(queueOpen,"drawer close can be interrupted by reopen");SetQueue(false);await Task.Delay(400);Check(!queueOpen,"drawer exit completes");
   Go("本地音乐");await Task.Delay(300);OpenSongMenu(Track(1));await Task.Delay(350);Check(c31SongFlyout?.IsOpen==true,"actual song action flyout opened");
   var menuHwnd=TopLevel.GetTopLevel(c31SongFlyout!.Items.OfType<MenuItem>().First())?.TryGetPlatformHandle()?.Handle.ToInt64()??0;Check(menuHwnd!=0,"song menu has actual native popup HWND");Program.Log("CAPTURE",new{label="menu",hwnd=menuHwnd});await Task.Delay(650);CloseSongMenu();await Task.Delay(350);Check(c31SongFlyout?.IsOpen==false,"song menu exits");
   OpenPlatform("qq");await Task.Delay(300);var combo=this.GetVisualDescendants().OfType<ComboBox>().First();combo.IsDropDownOpen=true;await Task.Delay(250);Check(combo.IsDropDownOpen,"account selector popup opens");combo.IsDropDownOpen=false;combo.SelectedItem="QQ 小号";await Task.Delay(350);Check(chosenAccount["qq"]=="alt","QQ alternative mock account selected");
   OpenPlatform("kugou");await Task.Delay(250);Check(chosenAccount["qq"]=="alt","other platform cannot overwrite QQ account");OpenPlatform("qq");await Task.Delay(250);Check(CurrentAccount?.Alias=="QQ 小号","account selection restored");
   foreach(var pair in platforms){OpenPlatform(pair.Key);await Task.Delay(250);foreach(var identity in pair.Value.Accounts){var picker=this.GetVisualDescendants().OfType<ComboBox>().First();picker.SelectedItem=identity.Alias;await Task.Delay(250);Check(CurrentAccount?.Key==identity.Key,"mock platform/account selector "+pair.Key+"/"+identity.Key);}}
   Go("正在播放");await Task.Delay(350);seconds=96;UpdatePlayback();await Task.Delay(400);Check(c3ActiveLyric==3,"lyric current line follows simulated seek");Check(c3LyricViewport!.Offset.Y>0,"lyric viewport smoothly reaches target");
   Program.Log("CAPTURE",new{label="lyrics"});await Task.Delay(650);await Click("专注封面");Check(c31CoverOnly,"cover focus mode");await Click("显示歌词");Check(!c31CoverOnly,"return to lyric view");
   Go("设置");await Task.Delay(250);await Click("加载示例");Check(c31Feedback=="加载中","loading feedback");await Click("空状态");Check(c31Feedback.Contains("尚无"),"empty feedback");await Click("错误状态");Check(c31Feedback.Contains("失败"),"error feedback without HTTP");await Click("恢复");
   var mica=this.GetVisualDescendants().OfType<CheckBox>().First(x=>AutomationProperties.GetName(x)?.StartsWith("尝试 Windows Mica")==true);mica.IsChecked=true;await Task.Delay(300);Check(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)||ActualTransparencyLevel.Equals(WindowTransparencyLevel.None),"Windows 10 Mica request safely falls back to opaque");
   reduced=true;Render();await Task.Delay(250);Check(!MotionEnabled&&playButton!.Transitions!.Count==0,"reduced motion disables core transitions");reduced=false;c31Low=true;Render();await Task.Delay(250);Check(!MaterialsEnabled&&!MotionEnabled,"one-click lightweight fallback");
   Program.Log("CAPTURE",new{label="low"});await Task.Delay(650);c31Low=false;c31Contrast=true;Render();await Task.Delay(250);Check(HighContrast&&!MotionEnabled&&!MaterialsEnabled,"high contrast opaque fallback");Check(this.GetVisualDescendants().OfType<CheckBox>().All(x=>Equals(x.Foreground,R("Text"))),"settings labels retain high contrast foreground");
   Program.Log("CAPTURE",new{label="contrast"});await Task.Delay(650);Go("首页");await Task.Delay(300);Program.Log("CAPTURE",new{label="contrast-home"});await Task.Delay(650);Check(Gradient("#FFFFFF","#FFFFFF").GradientStops.All(x=>x.Color==Avalonia.Media.Colors.Black),"high contrast removes pastel ambient backgrounds");c31Contrast=false;dark=!dark;Render();await Task.Delay(250);Program.Log("CAPTURE",new{label="opposite-theme"});await Task.Delay(650);
   Check(ActualTransparencyLevel.Equals(WindowTransparencyLevel.None),"opaque default fallback");Check(queue.Count==200,"interaction tests do not mutate queue core");
   Program.Log("C31_TEST_COMPLETE",new{checks=count,hardwarePresentationFPS="not inferred",nativePointer="pending"});Close();
  }catch(Exception error){Program.Log("C31_TEST_FAILED",error.ToString());Close();}
 }
}
