using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 void C3RefreshLyricHighlight(){if(page!="正在播放"||c3LyricButtons.Count!=LyricLines.Length)return;var idx=core.State.LyricIndex;if(idx<0||idx>=c3LyricButtons.Count){c3ActiveLyric=-1;return;}if(idx==c3ActiveLyric)return;for(int i=0;i<c3LyricButtons.Count;i++){var b=c3LyricButtons[i];var current=i==idx;b.Content=(current?"·  ":"   ")+LyricLines[i];b.FontWeight=current?FontWeight.SemiBold:FontWeight.Normal;b.Foreground=R(current?"Text":"Sub");b.BorderThickness=new(0);b.Background=Brushes.Transparent;b.Opacity=current?1:.72;AutomationProperties.SetName(b,(current?"当前歌词 ":"歌词 ")+(i+1));if(MotionEnabled)b.Transitions=new Transitions{new Avalonia.Animation.TransformOperationsTransition{Property=RenderTransformProperty,Duration=TimeSpan.FromMilliseconds(450)}};b.RenderTransformOrigin=new(0,.5,RelativeUnit.Relative);b.RenderTransform=TransformOperations.Parse(current?"scale(1.09, 1.09)":"scale(1, 1)");}c3ActiveLyric=idx;var viewport=c3LyricViewport;Dispatcher.UIThread.Post(()=>{if(page!="正在播放"||viewport==null||viewport!=c3LyricViewport||idx!=c3ActiveLyric||idx<0||idx>=c3LyricButtons.Count)return;var line=c3LyricButtons[idx];var point=line.TranslatePoint(default,(Control)viewport.Content!);if(point!=null)viewport.Offset=new(0,Math.Max(0,point.Value.Y-viewport.Bounds.Height/2+line.Bounds.Height/2));},DispatcherPriority.Loaded);}
}
