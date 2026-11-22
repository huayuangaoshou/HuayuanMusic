using Avalonia.Controls;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 internal async Task R2HeapCheck(OfflineWindowRoot root){
  var tracks=(await ((LocalMusicLibraryService)root.Library).GetAllLocalTracksAsync()).Take(2).ToArray();root.Player.SetControlledQueue(Enumerable.Range(0,1000).Select(i=>LocalMusicLibraryViewModel.ToSongItem(tracks[i%2]).NewQueueEntry()).ToArray());Go("本地音乐");await Task.Delay(100);
  var weak=new List<WeakReference<ListBox>>();var rows=new List<object>();
  for(int batch=0;batch<3;batch++){
   for(int i=0;i<20;i++){SetQueue(true);await Task.Delay(25);weak.Add(new(nativeQueue!));SetQueue(false);await Task.Delay(25);}
   await Task.Delay(150);await Task.Run(()=>{GC.Collect(2,GCCollectionMode.Forced,true,true);GC.WaitForPendingFinalizers();GC.Collect(2,GCCollectionMode.Forced,true,true);});await Task.Delay(150);
   var info=GC.GetGCMemoryInfo();var item=new{batch,created=weak.Count,alive=weak.Count(x=>x.TryGetTarget(out _)),managedMiB=GC.GetTotalMemory(false)/1048576d,heapAfterGCMiB=info.HeapSizeBytes/1048576d,fragmentedMiB=info.FragmentedBytes/1048576d};rows.Add(item);Program.Log("R2_DRAWER_HEAP",item);
  }
  Program.Log("R2_HEAP_COMPLETE",new{diagnosticOnly=true,forcedGC=true,rows});Close();
 }
}
