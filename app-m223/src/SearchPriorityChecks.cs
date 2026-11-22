using Avalonia.Controls;
using Avalonia.Threading;
using System.Diagnostics;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 async Task SearchPriorityChecks(TextBox input,Action<bool,string> check){
  var clock=Stopwatch.StartNew();double normalAt=-1,backgroundAt=-1;
  var normal=new DispatcherTimer(DispatcherPriority.Normal){Interval=TimeSpan.FromMilliseconds(220)};
  var background=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(220)};
  normal.Tick+=(_,_)=>{normalAt=clock.Elapsed.TotalMilliseconds;normal.Stop();};background.Tick+=(_,_)=>{backgroundAt=clock.Elapsed.TotalMilliseconds;background.Stop();};
  void Work(){var slice=Stopwatch.StartNew();while(slice.ElapsedMilliseconds<1)Thread.SpinWait(50);if(clock.ElapsedMilliseconds<500)Dispatcher.UIThread.Post(Work,DispatcherPriority.Normal);}
  input.Text="NO_MATCH_PRIORITY_R2";normal.Start();background.Start();Dispatcher.UIThread.Post(Work,DispatcherPriority.Normal);
  await Task.Delay(400);
  Program.Log("R2_SEARCH_PRIORITY",new{at=clock.Elapsed.TotalMilliseconds,normalAt,backgroundAt,mainSongs!.Items.Count,query,debounce=searchDebounce.IsEnabled});
  check(normalAt>=0&&(backgroundAt<0||normalAt<=backgroundAt),"normal timer runs before background under bounded normal-priority load");
  check(mainSongs.Items.Count==0&&emptyFeedback!.IsVisible&&!libraryPlay!.IsEnabled,"real empty search settles within unchanged400ms under bounded normal load");
  await Task.Delay(250);normal.Stop();background.Stop();input.Text="";await Task.Delay(400);
 }
}
