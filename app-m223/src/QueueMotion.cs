using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.ObjectModel;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 Border? queueDropIndicator;
 readonly ObservableCollection<QueueDisplay> queueRows=new();DateTime ignoreQueueDoubleTap;IPointer? queuePointer;Guid? dropQueueSong;bool queueDragging;Guid[] dragOrder=[];long queueAnimation;
 QueueDisplay[] QueueModels()=>core.State.QueueEntries.Select((e,i)=>new QueueDisplay(i,e.TrackId,"本地副本","",Track(e.TrackId).Title,Track(e.TrackId).Artist,Track(e.TrackId).Duration,e.EntryId)).ToArray();
 bool QueueMotion=>MotionEnabled&&!c31Low&&!HighContrast;
 IEnumerable<Grid> QueueVisualRows(ListBox list)=>list.GetVisualDescendants().OfType<Grid>().Where(x=>x.Tag is QueueDisplay);
 void UpdateStableQueueRows(ListBox list){
  if(dragQueueSong!=null&&!dragOrder.SequenceEqual(core.State.QueueEntries.Select(x=>x.EntryId)))CancelQueueDrag();
  queueAnimation++;var before=queueRows.ToDictionary(x=>x.EntryId,x=>x.Index);var models=QueueModels();var ids=models.Select(x=>x.EntryId).ToArray();var desired=ids.ToHashSet();
  for(int i=queueRows.Count-1;i>=0;i--)if(!desired.Contains(queueRows[i].EntryId))queueRows.RemoveAt(i);
  for(int i=0;i<ids.Length;i++){int at=-1;for(int j=i;j<queueRows.Count;j++)if(queueRows[j].EntryId==ids[i]){at=j;break;}if(at<0)queueRows.Insert(i,models[i]);else if(at!=i)queueRows.Move(at,i);}
  // Publish the final ordinals together before arranging. Never paint new ordinals at old FLIP positions.
  for(int i=0;i<queueRows.Count;i++)queueRows[i].Index=i;
  UpdateLayout();foreach(var row in QueueVisualRows(list)){var item=(QueueDisplay)row.Tag!;var label=row.Children.OfType<TextBlock>().FirstOrDefault();if(label!=null)label.Text=$"{item.Index+1}  {item.Title}";row.RenderTransform=null;if(row.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is {} container){container.Transitions=null;container.RenderTransform=null;container.Opacity=1;}}
  RefreshQueueSemantics(list);
  foreach(var row in QueueVisualRows(list)){var item=(QueueDisplay)row.Tag!;if(before.TryGetValue(item.EntryId,out var old)&&old!=item.Index)AnimateQueueRow(row);}
 }

 double ContainerTop(Grid row,ListBox list){var c=row.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();if(c==null)return 0;return (c.TranslatePoint(default,list)?.Y??0)-(c.RenderTransform is TranslateTransform t?t.Y:0);}
 void RefreshQueueSemantics(ListBox list){foreach(var c in list.GetVisualDescendants().OfType<ListBoxItem>()){int i=list.IndexFromContainer(c);if(i<0)continue;Avalonia.Automation.AutomationProperties.SetPositionInSet(c,i+1);Avalonia.Automation.AutomationProperties.SetSizeOfSet(c,queueRows.Count);if(i<queueRows.Count)Avalonia.Automation.AutomationProperties.SetName(c,$"{i+1} / {queueRows.Count} · {queueRows[i].Title}");}}
 void AnimateQueueRow(Grid row){
  // Deliberate stable fallback: no positional animation across virtualized containers.
  // The text is always at its final position and at least 88% opaque.
  if(row.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is not {} container)return;
  container.Transitions=null;container.RenderTransform=null;container.Opacity=QueueMotion ? .88 : 1;
  if(!QueueMotion)return;long generation=queueAnimation;
  container.Transitions=new Transitions{new DoubleTransition{Property=OpacityProperty,Duration=DesignTokens.MotionEffectsFast.Duration,Easing=DesignTokens.MotionEffectsFast.Curve}};
  Dispatcher.UIThread.Post(()=>{if(generation==queueAnimation&&TopLevel.GetTopLevel(row)!=null&&ReferenceEquals(container.Content,row.Tag))container.Opacity=1;},DispatcherPriority.Render);
 }
 void ApplyQueueDragFeedback(ListBox list){foreach(var row in QueueVisualRows(list)){var item=(QueueDisplay)row.Tag!;row.RenderTransform=null;row.Opacity=1;row.Background=item.EntryId==dragQueueSong?R("Surface2"):Brushes.Transparent;if(row.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is {} container){container.Transitions=null;container.RenderTransform=null;container.Opacity=1;}foreach(var line in row.Children.OfType<Border>())if(line.Tag?.ToString()=="DropLine")line.IsVisible=false;}}

 void StartQueueDrag(ListBox list,QueueDisplay row,PointerPressedEventArgs e){if(!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)return;CancelQueueDrag();dragQueueSong=row.EntryId;dragQueueIndex=row.Index;queueDragOrigin=e.GetPosition(list);dragOrder=core.State.QueueEntries.Select(x=>x.EntryId).ToArray();queuePointer=e.Pointer;list.Focus();}
 void MoveQueueDrag(ListBox list,PointerEventArgs e){if(dragQueueSong==null)return;var point=e.GetPosition(list);if(!queueDragging){if(Math.Abs(point.Y-queueDragOrigin.Y)<7)return;queueDragging=true;e.Pointer.Capture(list);ignoreQueueDoubleTap=DateTime.UtcNow.AddMilliseconds(350);}if(point.X<0||point.X>list.Bounds.Width||point.Y<0||point.Y>list.Bounds.Height){dropQueueSong=null;ClearQueueDragVisuals();return;}var scroll=list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();if(scroll!=null&&(point.Y<28||point.Y>list.Bounds.Height-28))scroll.Offset=new Vector(scroll.Offset.X,Math.Clamp(scroll.Offset.Y+(point.Y<28?-16:16),0,Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height)));dropQueueSong=null;if(queueDropIndicator!=null)queueDropIndicator.IsVisible=false;foreach(var row in QueueVisualRows(list)){var model=(QueueDisplay)row.Tag!;var at=new Point?(new Point(0,ContainerTop(row,list)));if(at.HasValue&&point.Y>=at.Value.Y&&point.Y<at.Value.Y+row.Bounds.Height){dropQueueSong=model.EntryId;if(queueDropIndicator!=null){bool down=model.Index>dragQueueIndex;queueDropIndicator.Margin=new Thickness(6,Math.Clamp(at.Value.Y+(down?row.Bounds.Height:0),0,Math.Max(0,list.Bounds.Height-4)),6,0);queueDropIndicator.IsVisible=model.EntryId!=dragQueueSong;}break;}}ApplyQueueDragFeedback(list);}

 void ClearQueueDragVisuals(){if(queueDropIndicator!=null)queueDropIndicator.IsVisible=false;if(nativeQueue==null)return;foreach(var row in QueueVisualRows(nativeQueue)){row.Opacity=1;row.Background=Brushes.Transparent;foreach(var line in row.Children.OfType<Border>())if(line.Tag?.ToString()=="DropLine")line.IsVisible=false;row.RenderTransform=null;if(row.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is {} container){container.Transitions=null;container.RenderTransform=null;container.Opacity=1;}}}
 void CancelQueueDrag(){dragQueueSong=null;dragQueueIndex=null;dropQueueSong=null;queueDragging=false;var pointer=queuePointer;queuePointer=null;pointer?.Capture(null);ClearQueueDragVisuals();queueAnimation++;}
 void EndQueueDrag(ListBox list,PointerReleasedEventArgs e){if(dragQueueSong is not Guid song)return;var target=dropQueueSong;bool commit=queueDragging&&target.HasValue&&dragOrder.SequenceEqual(core.State.QueueEntries.Select(x=>x.EntryId));bool moved=queueDragging;CancelQueueDrag();if(moved){e.Handled=true;ignoreQueueDoubleTap=DateTime.UtcNow.AddMilliseconds(350);}if(commit&&target!=song)RunCore(()=>core.QueueEntryAsync("before",song,target!.Value));}
}
