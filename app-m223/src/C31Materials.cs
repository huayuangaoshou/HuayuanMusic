using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using System.Runtime.InteropServices;

namespace HuayuanPreview;
public sealed partial class PreviewWindow
{
 bool c31MissingCover,c31LogoVariant; double materialStrength=.65;
 bool c31Materials=true,c31Low,c31Contrast,c31SystemReduced,c31SystemContrast,c31Mica,c31CoverOnly;
 bool HighContrast=>c31Contrast||c31SystemContrast;
 bool MotionEnabled=>!reduced&&!c31SystemReduced&&!c31Low&&!HighContrast;
 bool MaterialsEnabled=>c31Materials&&!c31Low&&!HighContrast&&!Program.Args.Contains("--software");
 ScrollViewer? c31Page;Control? c31QueueSurface;int c31QueueVersion;
 string c31Feedback="就绪";
 static TimeSpan PanelMotionDuration => DesignTokens.MotionSpatialDefault.Duration;
 [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")]static extern bool SpiBool(uint action,uint param,out int result,uint flags);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct HC {public uint Size,Flags;public IntPtr Scheme;}
 [DllImport("user32.dll",EntryPoint="SystemParametersInfoW",CharSet=CharSet.Unicode)]static extern bool SpiContrast(uint action,uint param,ref HC result,uint flags);
 void ReadAccessibility(){
  TransparencyLevelHint=new[]{WindowTransparencyLevel.None};
  if(OperatingSystem.IsWindows()){
   if(SpiBool(0x1042,0,out var enabled,0))c31SystemReduced=enabled==0;
   var hc=new HC{Size=(uint)Marshal.SizeOf<HC>()};if(SpiContrast(0x0042,hc.Size,ref hc,0))c31SystemContrast=(hc.Flags&1)!=0;
  }
  reduced=Program.Args.Contains("--reduced");c31Low=Program.Args.Contains("--low");dark=Program.Args.Contains("--dark");c31Contrast=Program.Args.Contains("--contrast");
  if(Program.Args.Contains("--narrow")){Width=980;Height=720;}
  Program.Log("ACCESSIBILITY",new{c31SystemReduced,c31SystemContrast,reduced,c31Low});
 }
 void ApplyWindowMaterial(){
  var request=c31Mica&&MaterialsEnabled&&OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)?WindowTransparencyLevel.Mica:WindowTransparencyLevel.None;
  if(TransparencyLevelHint.Count==0||!TransparencyLevelHint[0].Equals(request))TransparencyLevelHint=new[]{request,WindowTransparencyLevel.None};
 }
 bool NativeMica=>MaterialsEnabled&&c31Mica&&ActualTransparencyLevel.Equals(WindowTransparencyLevel.Mica);
 IBrush WindowFill()=>NativeMica?new SolidColorBrush(((ISolidColorBrush)R("Base")).Color,.86):R("Base");
 IBrush CeramicBrush()=>MaterialsEnabled?Gradient(dark?"#252E3B":"#FFFDF9",dark?"#1D2430":"#F0EEF2"):R("Surface");
 IBrush GlassBrush(){if(!MaterialsEnabled)return R("Surface");var a=(byte)(252-materialStrength*35);return Gradient(Color.FromArgb(a,dark?(byte)38:(byte)253,dark?(byte)47:(byte)252,dark?(byte)65:(byte)255).ToString(),dark?"#ED202A3B":"#EEF0EFF7");}
 IBrush PearlBrush()=>MaterialsEnabled?Gradient(dark?"#81BFE5":"#687DD1",dark?"#CC8CB9":"#B16AA2"):R("Primary");
 void ConfigureMotion(Control c){
  c.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);
  var spatial = c.Name == "LiquidPlay" ? DesignTokens.MotionSpatialFast : DesignTokens.MotionSpatialDefault;
  c.Transitions=MotionEnabled?new Transitions{
   new DoubleTransition{Property=Visual.OpacityProperty,Duration=DesignTokens.MotionEffectsFast.Duration,Easing=DesignTokens.MotionEffectsFast.Curve},
   new TransformOperationsTransition{Property=Visual.RenderTransformProperty,Duration=spatial.Duration,Easing=spatial.Curve}
  }:new Transitions();
  c.RenderTransform=TransformOperations.Parse("translate(0px, 0px) scale(1)");
 }
 void Move(Control c,double y,double scale){c.RenderTransform=TransformOperations.Parse($"translate(0px, {y.ToString(System.Globalization.CultureInfo.InvariantCulture)}px) scale({scale.ToString(System.Globalization.CultureInfo.InvariantCulture)})");}
 void Feedback(Control c,string state){
  if(!MotionEnabled){Move(c,0,1);return;}
  Move(c,state=="hover"?-1:0,state=="press"?.97:1);
  Event("C31_FEEDBACK",new{state,Name=AutomationProperties.GetName(c)});
 }
 void StyleMotion(Control c,bool primary=false){
  ConfigureMotion(c);
  if(primary&&c is Button b){b.Background=MaterialsEnabled?Gradient("#566AB4","#6555A1"):R("Primary");b.BorderBrush=MaterialsEnabled?Gradient("#A0C8F3","#756AAC"):R("Primary");b.BorderThickness=new Thickness(1);if(HighContrast)b.Foreground=Brushes.Black;}
  c.PointerEntered+=(_,_)=>Feedback(c,"hover");c.PointerExited+=(_,_)=>Feedback(c,"rest");
  c.PointerPressed+=(_,_)=>Feedback(c,"press");c.PointerReleased+=(_,_)=>Feedback(c,"hover");
  // Local gradient highlight only; no backdrop shader or full-window blur.
  if(primary&&c is Button primaryButton){var highlight=Gradient("#566AB4","#6555A1");primaryButton.Background=MaterialsEnabled?highlight:R("Primary");c.PointerMoved+=(_,e)=>{if(MaterialsEnabled&&c.Bounds.Width>0)highlight.StartPoint=new RelativePoint(Math.Clamp(e.GetPosition(c).X/c.Bounds.Width,0,1),0,RelativeUnit.Relative);};}
 }
 void Enter(Control c,double x=10){
  if(!MotionEnabled)return;
  c.Transitions=null;c.Opacity=0;c.RenderTransform=TransformOperations.Parse($"translate({x.ToString(System.Globalization.CultureInfo.InvariantCulture)}px, 0px)");
  ConfigureMotion(c);Dispatcher.UIThread.Post(()=>{c.Opacity=1;Move(c,0,1);},DispatcherPriority.Render);
 }
 void ComposePage(Grid owner,ScrollViewer next){
  var old=c31Page;c31Page=next;
  if(MotionEnabled&&old!=null&&!ReferenceEquals(old,next)&&!queueOpen&&page is not ("本地音乐" or "本地歌单" or "平台歌单")){
   if(old.Parent is Panel parent)parent.Children.Remove(old);
   old.IsHitTestVisible=false;owner.Children.Insert(0,old);Grid.SetColumn(old,0);old.Opacity=1;ConfigureMotion(old);
   old.Opacity=0;_ = RemoveOld(owner,old);
  }
  Enter(next,8);
 }
 async Task RemoveOld(Panel parent,Control old){await Task.Delay(PanelMotionDuration+TimeSpan.FromMilliseconds(30));parent.Children.Remove(old);}
 void ToggleQueue()=>SetQueue(!queueOpen);
 async void SetQueue(bool open){
  var generation=++c31QueueVersion;
  if(!CanPresentCore){queueOpen=open;coreUiDeferred=true;Event("C31_DRAWER","deferred while hidden/minimized");return;}
  if(open){queueOpen=true;UpdateQueueSurface();if(c31QueueSurface!=null){c31QueueSurface.Opacity=1;Move(c31QueueSurface,0,1);}Event("C31_DRAWER","open");return;}
  var surface=c31QueueSurface;
  if(MotionEnabled&&surface!=null){ConfigureMotion(surface);surface.Opacity=0;Move(surface,0,.98);await Task.Delay(PanelMotionDuration);}
  if(generation!=c31QueueVersion)return;queueOpen=false;UpdateQueueSurface();Event("C31_DRAWER","closed");
 }
 void CloseSongMenu(){c31SongFlyout?.Hide();songActionsOpen=false;}
 Control CoverPlaceholder(double width,double height){
  var g=new Grid{Width=width,Height=height};g.Children.Add(new Border{CornerRadius=new CornerRadius(9),Background=GlassBrush(),BorderBrush=PearlBrush(),BorderThickness=new Thickness(1)});var icon=C3Icon("logo");icon.Width=width*.44;icon.Height=height*.44;g.Children.Add(icon);return g;
 }
 void RefreshVisuals(){ReadSystemMotion();Render();Notify(c31Low?"轻量模式：复杂材质与动效已关闭":"视觉设置已更新");}
 void ReadSystemMotion(){
  if(!OperatingSystem.IsWindows())return;
  if(SpiBool(0x1042,0,out var enabled,0))c31SystemReduced=enabled==0;
  var hc=new HC{Size=(uint)Marshal.SizeOf<HC>()};if(SpiContrast(0x0042,hc.Size,ref hc,0))c31SystemContrast=(hc.Flags&1)!=0;
 }
 readonly DispatcherTimer c31AccessibilityWatch=new(){Interval=TimeSpan.FromSeconds(2)};
 void WatchAccessibility(){c31AccessibilityWatch.Tick+=(_,_)=>{var old=(c31SystemReduced,c31SystemContrast);ReadSystemMotion();if(old!=(c31SystemReduced,c31SystemContrast)){Render();Notify("已遵循系统辅助功能设置");}};c31AccessibilityWatch.Start();}

 CheckBox Option(string label,bool value,Action<bool> change){
  var b=new CheckBox{Content=label,IsChecked=value,Foreground=R("Text")};AutomationProperties.SetName(b,label);
  b.IsCheckedChanged+=(_,_)=>{change(b.IsChecked==true);RefreshVisuals();};return b;
 }
 Avalonia.Controls.TextBlock StatusLabel(string value){var text=T(value,13,true);if(!HighContrast)text.Foreground=R(value.Contains("失败")||value.Contains("过期")?"StatusError":value.Contains("尚无")?"StatusWarning":value=="就绪"?"StatusSuccess":"Sub");return text;}
 Control SettingsPage(){
  var settings=V(T("材质与动效",22,false,true),
   Option("深色模式",dark,x=>dark=x),Option("柔光陶瓷与局部玻璃高光",c31Materials,x=>c31Materials=x),
   Option("减少动画 / Reduced Motion",reduced,x=>reduced=x),Option("一键轻量模式",c31Low,x=>c31Low=x),
   Option("高对比度预览",c31Contrast,x=>c31Contrast=x),Option("缺失封面示例",c31MissingCover,x=>c31MissingCover=x),Option("星环 H 标志候选",c31LogoVariant,x=>c31LogoVariant=x),
   Option("尝试 Windows Mica（不支持时不透明）",c31Mica,x=>c31Mica=x),
   T("系统减少动态："+(c31SystemReduced?"已启用":"未启用")+" · 当前动效："+(MotionEnabled?"开启":"关闭"),13,true),
   T("窗口背景："+ActualTransparencyLevel+" · 不使用全窗口实时模糊",13,true));
  var feedback=V(T("加载与结果反馈",18,false,true),H(B("加载示例",()=>{c31Feedback="加载中";Render();}),B("空状态",()=>{c31Feedback="尚无歌曲，请添加演示内容";Render();}),B("错误状态",()=>{c31Feedback="演示同步失败；未连接网络";Render();}),B("恢复",()=>{c31Feedback="就绪";Render();})),StatusLabel(c31Feedback));
  if(c31Feedback=="加载中")feedback.Children.Add(new ProgressBar{IsIndeterminate=MotionEnabled,Value=35,Height=4});
  return V(Panel(settings),Panel(feedback),T("全部为合成数据。材质、Logo与动效仍等待你的视觉批准。",12,true));
 }
}
