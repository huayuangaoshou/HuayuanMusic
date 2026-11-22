using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using System.Globalization;
namespace HuayuanPreview;

// Thin Avalonia adapter: pointer capture + existing render transforms. No physics/shader engine.
internal sealed class LiquidSelector:Border {
 readonly Border lens;readonly string[] labels;readonly Action<int> changed;readonly bool motion;readonly Grid labelsGrid;
 double targetX,startX,dragOrigin;bool dragging;int generation;public int Selection{get;private set;}public bool IsDragging=>dragging;
 public LiquidSelector(string[] names,int selected,Action<int> action,bool animate,IBrush surface,IBrush glass,IBrush pearl,IBrush ink){
  labels=names;Selection=selected;changed=action;motion=animate;CornerRadius=new(24);Padding=new(4);Height=46;MaxWidth=520;HorizontalAlignment=HorizontalAlignment.Left;MinWidth=Math.Min(520,names.Length*84);Background=surface;
  var root=new Grid();lens=new Border{Background=glass,BorderBrush=pearl,BorderThickness=new(1),CornerRadius=new(20),HorizontalAlignment=HorizontalAlignment.Left,IsHitTestVisible=false,RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative)};
  var sheen=new RadialGradientBrush{Center=new(.3,0,RelativeUnit.Relative),GradientOrigin=new(.3,0,RelativeUnit.Relative),RadiusX=new(.9,RelativeUnit.Relative),RadiusY=new(.9,RelativeUnit.Relative),GradientStops=new(){new(Color.Parse("#55ffffff"),0),new(Colors.Transparent,1)}};lens.Child=new Border{Background=sheen,CornerRadius=new(20)};
  DetachedFromVisualTree+=(_,_)=>{generation++;dragging=false;};
  KeyDown+=(_,e)=>{if(!IsEnabled)return;if(e.Key is Key.Left or Key.Right){Select((Selection+(e.Key==Key.Right?1:-1)+labels.Length)%labels.Length);e.Handled=true;}else if(e.Key is Key.Home or Key.End){Select(e.Key==Key.Home?0:labels.Length-1);e.Handled=true;}};
  root.Children.Add(lens);labelsGrid=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(string.Join(",",names.Select(_=>"*")))};
  for(int i=0;i<names.Length;i++){var index=i;var b=new Button{Content=names[i],Background=Brushes.Transparent,BorderThickness=new(0),Foreground=ink,CornerRadius=new(20),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center,Padding=new(12,7)};AutomationProperties.SetName(b,"分类 "+names[i]);b.Click+=(_,_)=>Select(index);Grid.SetColumn(b,i);labelsGrid.Children.Add(b);}
  root.Children.Add(labelsGrid);Child=root;SizeChanged+=(_,_)=>{lens.Width=Math.Max(1,(Bounds.Width-8)/names.Length);Settle(false);};
  AddHandler(PointerPressedEvent,(_,e)=>{if(!IsEnabled||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;generation++;startX=e.GetPosition(this).X;dragOrigin=Math.Clamp(startX-4-Math.Max(1,lens.Width)/2,0,(labels.Length-1)*Math.Max(1,lens.Width));targetX=dragOrigin;dragging=true;e.Pointer.Capture(this);e.Handled=true;var idx=Math.Clamp((int)((startX-4)/Math.Max(1,lens.Width)),0,labels.Length-1);labelsGrid.Children[idx].Focus();lens.Transitions=null;SetTransform(targetX,motion?1.04:1,motion?.97:1);},RoutingStrategies.Tunnel,true);
  PointerMoved+=(_,e)=>{if(!IsEnabled)return;var p=e.GetPosition(this);sheen.Center=new(Math.Clamp((p.X-targetX)/Math.Max(1,lens.Width),0,1),.1,RelativeUnit.Relative);if(!dragging)return;Pull(p.X-startX);};
  PointerReleased+=(_,e)=>{if(!dragging)return;var ix=Math.Clamp((int)Math.Round(targetX/Math.Max(1,lens.Width)),0,labels.Length-1);dragging=false;e.Pointer.Capture(null);Select(ix);e.Handled=true;};
  PointerCaptureLost+=(_,_)=>{if(dragging){dragging=false;Settle(true);}};
 }
 public void Pull(double delta){targetX=Math.Clamp(dragOrigin+delta,-6,(labels.Length-1)*lens.Width+6);var stretch=motion?Math.Min(.09,Math.Abs(delta)/1000):0;SetTransform(targetX,1+stretch,1-stretch*.45);Program.Log("LENS_DRAG",new{targetX,stretch,Selection});}
 public void Select(int index){if(!IsEnabled)return;Selection=Math.Clamp(index,0,labels.Length-1);for(int i=0;i<labelsGrid.Children.Count;i++){var button=(Button)labelsGrid.Children[i];button.FontWeight=i==Selection?FontWeight.SemiBold:FontWeight.Normal;AutomationProperties.SetHelpText(button,i==Selection?"已选中":"可切换");}Settle(true);Program.Log("LENS_SELECTED",new{Selection,label=labels[Selection],motion,refraction=false});var token=++generation;_ = Commit(token,Selection);}
 async Task Commit(int token,int selection){if(motion)await Task.Delay(340);if(token==generation&&this.VisualRoot!=null)changed(selection);}
 void Settle(bool animate){lens.Transitions=motion&&animate?new Transitions{new TransformOperationsTransition{Property=RenderTransformProperty,Duration=TimeSpan.FromMilliseconds(300),Easing=new SpringEasing{Mass=1,Stiffness=250,Damping=22}}}:new();targetX=Selection*lens.Width;SetTransform(targetX,1,1);}
 void SetTransform(double x,double sx,double sy)=>lens.RenderTransform=TransformOperations.Parse(string.Format(CultureInfo.InvariantCulture,"translate({0}px, 0px) scale({1}, {2})",x,sx,sy));
 public double Position=>targetX;
 internal Control LensForTest=>lens;
}
