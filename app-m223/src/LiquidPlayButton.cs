using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
namespace HuayuanPreview;
// Native Button owns activation/focus/disabled behavior. Material layers have independent motion.
internal sealed class LiquidPlayButton:Button {
 readonly Border skin,highlight;readonly Grid face;readonly bool animate;readonly Func<Control> glyph;bool held;
 public LiquidPlayButton(bool motion,bool material,Action action,Func<Control> icon,bool contrast=false){animate=motion;glyph=icon;Width=44;Height=44;Padding=new(0);CornerRadius=new(22);BorderThickness=new(0);Background=Brushes.Transparent;face=new Grid{Width=44,Height=44};skin=new Border{CornerRadius=new(22),Background=contrast?Brushes.Yellow:new LinearGradientBrush{StartPoint=new(0,0,RelativeUnit.Relative),EndPoint=new(1,1,RelativeUnit.Relative),GradientStops=new(){new(Color.Parse("#7791ea"),0),new(Color.Parse("#7065b3"),1)}},BorderBrush=Brush.Parse("#99ffffff"),BorderThickness=new(1),RenderTransformOrigin=new(.5,.5,RelativeUnit.Relative)};highlight=new Border{CornerRadius=new(22),IsHitTestVisible=false,Opacity=material?.7:0,Background=new RadialGradientBrush{Center=new(.3,.12,RelativeUnit.Relative),GradientOrigin=new(.3,.12,RelativeUnit.Relative),RadiusX=new(.9,RelativeUnit.Relative),RadiusY=new(.8,RelativeUnit.Relative),GradientStops=new(){new(Color.Parse("#aaffffff"),0),new(Colors.Transparent,1)}},RenderTransformOrigin=new(.5,.5,RelativeUnit.Relative)};face.Children.Add(skin);face.Children.Add(highlight);var symbol=icon();symbol.HorizontalAlignment=HorizontalAlignment.Center;symbol.VerticalAlignment=VerticalAlignment.Center;face.Children.Add(symbol);Content=face;
  if(motion){skin.Transitions=new Transitions{new TransformOperationsTransition{Property=RenderTransformProperty,Duration=DesignTokens.MotionSpatialFast.Duration,Easing=DesignTokens.MotionSpatialFast.Curve}};highlight.Transitions=new Transitions{new DoubleTransition{Property=OpacityProperty,Duration=DesignTokens.MotionEffectsFast.Duration,Easing=DesignTokens.MotionEffectsFast.Curve},new TransformOperationsTransition{Property=RenderTransformProperty,Duration=DesignTokens.MotionSpatialFast.Duration,Easing=DesignTokens.MotionSpatialFast.Curve}};}
  PointerMoved+=(_,e)=>{if(!material||!IsEnabled)return;var p=e.GetPosition(this);((RadialGradientBrush)highlight.Background!).Center=new(Math.Clamp(p.X/44,0,1),Math.Clamp(p.Y/44,0,1),RelativeUnit.Relative);highlight.Opacity=held?1:.85;};
  AddHandler(PointerPressedEvent,(_,_)=>{if(!IsEnabled)return;held=true;Deform(true);},Avalonia.Interactivity.RoutingStrategies.Tunnel);
  AddHandler(PointerReleasedEvent,(_,_)=>{held=false;Deform(false);},Avalonia.Interactivity.RoutingStrategies.Tunnel);
  PointerCaptureLost+=(_,_)=>{held=false;Deform(false);};PointerExited+=(_,_)=>{if(!held){Deform(false);highlight.Opacity=material?.5:0;}};
  Click+=(_,_)=>{action();var old=face.Children.Last();face.Children.Remove(old);var next=glyph();next.HorizontalAlignment=HorizontalAlignment.Center;next.VerticalAlignment=VerticalAlignment.Center;face.Children.Add(next);if(animate)_=new Animation{Duration=DesignTokens.MotionEffectsFast.Duration,Easing=DesignTokens.EmphasizedDecelerate,Children={new KeyFrame{Cue=new Cue(0),Setters={new Avalonia.Styling.Setter(OpacityProperty,.15)}},new KeyFrame{Cue=new Cue(1),Setters={new Avalonia.Styling.Setter(OpacityProperty,1d)}}}}.RunAsync(next);};
 }
 void Deform(bool down){if(!animate)return;skin.RenderTransform=TransformOperations.Parse(down?"scale(1.09, .9)":"scale(1, 1)");highlight.RenderTransform=TransformOperations.Parse(down?"translate(0px, 3px) scale(.84, 1.06)":"translate(0px, 0px) scale(1, 1)");highlight.Opacity=down?1:.65;Program.Log("NIGHT_PLAY_MATERIAL",new{down,skin=down?"1.09,.90":"1,1",highlightIndependent=true});}
 public void SetPressedForTest(bool down)=>Deform(down);
 public void RefreshGlyph(){var old=face.Children.Last();face.Children.Remove(old);var next=glyph();next.HorizontalAlignment=HorizontalAlignment.Center;next.VerticalAlignment=VerticalAlignment.Center;face.Children.Add(next);}
}
