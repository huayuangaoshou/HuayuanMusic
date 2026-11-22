using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 async Task NightMotionEvidence(){
  async Task CaptureMotion(string name,Control target,Action action){var p=target.TranslatePoint(default,this)??default;Program.Log("MOTION_RECORD",new{name,hwnd=TryGetPlatformHandle()?.Handle.ToInt64(),scale=RenderScaling,rect=new[]{Math.Max(0,(p.X-12)*RenderScaling),Math.Max(0,(p.Y-12)*RenderScaling),(target.Bounds.Width+24)*RenderScaling,(target.Bounds.Height+24)*RenderScaling}});var ready=Path.Combine(Program.Session,"motion-"+name+".ready");File.WriteAllText(ready,"ready");for(int n=0;n<100&&!File.Exists(ready+".start");n++)await Task.Delay(20);action();for(int n=0;n<120&&!File.Exists(ready+".done");n++)await Task.Delay(20);}
  try{await Task.Delay(500);Go("本地音乐");await Task.Delay(500);var tabs=this.GetVisualDescendants().OfType<TabControl>().First(x=>x.Name=="LibraryCategories");await CaptureMotion("category-switch",tabs,()=>tabs.SelectedIndex=1);await Task.Delay(500);var play=(LiquidPlayButton)playButton!;await CaptureMotion("circle-press-release",play,()=>{play.SetPressedForTest(true);DispatcherTimer.RunOnce(()=>play.SetPressedForTest(false),TimeSpan.FromMilliseconds(300));});SetQueue(true);await Task.Delay(400);await CaptureMotion("queue-close",c31QueueSurface!,()=>SetQueue(false));Go("正在播放");seconds=0;C3RefreshLyricHighlight();await Task.Delay(600);await CaptureMotion("lyric-follow",c3LyricViewport!,()=>{seconds=96;C3RefreshLyricHighlight();});Program.Log("MOTION_RECORD_COMPLETE",new{count=4,presentationFPS="NOT_MEASURED",method="test-driven native transformations + timestamped PrintWindow crops"});Close();}catch(Exception e){Program.Log("MOTION_RECORD_FAILED",e.ToString());Close();}
 }
}
