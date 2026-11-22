using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
namespace HuayuanPreview;
// Native Slider semantics with a transient user preview. The production snapshot owns the value.
public sealed class CommitSlider:Slider {
 protected override Type StyleKeyOverride=>typeof(Slider);
 public bool IsEditing{get;private set;}public bool IsCommitPending{get;private set;}public double ModelValue{get;private set;}
 public Func<double,Task>? Submit{get;set;}public event Action? PreviewChanged;public event Action? EditStarted;
 public Task PendingCommit{get;private set;}=Task.CompletedTask;public long CommitCount{get;private set;}
 public CommitSlider(){Height=32;MinHeight=32;Focusable=true;SmallChange=1;LargeChange=5;Classes.Add("player-slider");
  Resources["SliderHorizontalHeight"]=32d;Resources["SliderHorizontalThumbWidth"]=12d;Resources["SliderHorizontalThumbHeight"]=12d;Resources["SliderTrackThemeHeight"]=3d;
  Resources["SliderPreContentMargin"]=new GridLength(1,GridUnitType.Star);Resources["SliderPostContentMargin"]=new GridLength(1,GridUnitType.Star);
  AddHandler(PointerPressedEvent,(_,e)=>{if(IsEnabled&&e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)BeginPreview();},RoutingStrategies.Tunnel,true);
  AddHandler(PointerReleasedEvent,(_,_)=>{if(IsEditing)PendingCommit=CommitPreviewAsync();},RoutingStrategies.Tunnel,true);
  PointerCaptureLost+=(_,_)=>{if(IsEditing)CancelPreview();};LostFocus+=(_,_)=>{if(IsEditing)CancelPreview();};DetachedFromVisualTree+=(_,_)=>CancelPreview();
  PropertyChanged+=(_,e)=>{if(e.Property==ValueProperty&&IsEditing)PreviewChanged?.Invoke();};
 }
 public void Sync(double model){ModelValue=Math.Clamp(model,Minimum,Maximum);if(!IsEditing&&!IsCommitPending)Value=ModelValue;}
 public void BeginPreview(){if(!IsEnabled||IsCommitPending||IsEditing)return;IsEditing=true;Classes.Add("editing");EditStarted?.Invoke();}
 public void Preview(double value){if(IsEditing)Value=Math.Clamp(value,Minimum,Maximum);}
 public void CancelPreview(){IsEditing=false;Classes.Remove("editing");if(!IsCommitPending)Value=ModelValue;PreviewChanged?.Invoke();}
 public async Task CommitPreviewAsync(){if(!IsEditing)return;IsEditing=false;Classes.Remove("editing");IsCommitPending=true;var target=Value;
  try{if(Math.Abs(target-ModelValue)>.0001&&Submit!=null){CommitCount++;await Submit(target);}}
  finally{IsCommitPending=false;Value=ModelValue;PreviewChanged?.Invoke();}
 }
 static bool Navigation(Key k)=>k is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;
 protected override void OnKeyDown(KeyEventArgs e){if(e.Key==Key.Escape&&IsEditing){CancelPreview();e.Handled=true;return;}if(IsEnabled&&Navigation(e.Key)){BeginPreview();if(IsEditing)Preview(e.Key switch{Key.Home=>Minimum,Key.End=>Maximum,Key.Left or Key.Down=>Value-SmallChange,Key.Right or Key.Up=>Value+SmallChange,Key.PageDown=>Value-LargeChange,_=>Value+LargeChange});e.Handled=true;return;}base.OnKeyDown(e);}
 protected override void OnKeyUp(KeyEventArgs e){if(Navigation(e.Key)&&IsEditing){PendingCommit=CommitPreviewAsync();e.Handled=true;return;}base.OnKeyUp(e);}
}
