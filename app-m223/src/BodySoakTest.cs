using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using KugouAvaloniaPlayer.ViewModels;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 internal async Task BodySoakTest(OfflineWindowRoot root,CorePlaybackAdapter adapter){
  await adapter.SetModeAsync(0);await adapter.VolumeAsync(0);
  var tracks=(await ((LocalMusicLibraryService)root.Library).GetAllLocalTracksAsync()).Take(2).ToArray();
  var rows=Enumerable.Range(0,1000).Select(i=>LocalMusicLibraryViewModel.ToSongItem(tracks[i%2]).NewQueueEntry()).ToArray();root.Player.SetControlledQueue(rows);
  await adapter.QueueEntryAsync("play",rows[0].QueueEntryId);Go("本地音乐");SetQueue(true);
  var proc=System.Diagnostics.Process.GetCurrentProcess();var samples=new List<object>();var memory=new List<double>();var ids=new HashSet<Guid>();int active=0;var watch=System.Diagnostics.Stopwatch.StartNew();
  for(int i=0;i<180;i++){
   await Task.Delay(10000);var engine=root.Coordinator.Player;bool initialPlaying=engine.IsPlaying;double initialRms=engine.GetActiveAnalysisSnapshot().Rms;var recovery=System.Diagnostics.Stopwatch.StartNew();
   // A point may coincide with natural EOF. Observe actual PCM recovery, never infer it from UI state.
   while((!engine.IsPlaying||engine.GetActiveAnalysisSnapshot().Rms<=0)&&recovery.ElapsedMilliseconds<500)await Task.Delay(20);
   double settledRms=engine.GetActiveAnalysisSnapshot().Rms;bool recovered=engine.IsPlaying&&settledRms>0&&recovery.ElapsedMilliseconds<500;
   proc.Refresh();double working=proc.WorkingSet64/1048576d;memory.Add(working);ids.Add(adapter.State.CurrentEntryId);if(recovered)active++;
   var item=new{seconds=watch.Elapsed.TotalSeconds,initialPlaying,initialRms,settledRms,recoveryMs=recovery.Elapsed.TotalMilliseconds,recovered,workingMiB=working,managedMiB=GC.GetTotalMemory(false)/1048576d,privateMiB=proc.PrivateMemorySize64/1048576d,threads=proc.Threads.Count,handles=proc.HandleCount,cpuSeconds=proc.TotalProcessorTime.TotalSeconds,gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2),adapter.State.CurrentId,adapter.State.CurrentEntryId,adapter.State.Position,engine.IsPlaying};samples.Add(item);Program.Log("BODY_SOAK_SAMPLE",item);
   if(i%6==5){await adapter.SeekAsync(Math.Min(2,adapter.State.Duration/2));await adapter.ToggleAsync(adapter.State.CurrentId);await Task.Delay(80);await adapter.ToggleAsync(adapter.State.CurrentId);root.Player.MoveControlledQueue(998,999);SetQueue(i%12==11);}
  }
  await adapter.StopAsync();int checks=0;void Check(bool ok,string name){Program.Log("BODY_SOAK_CHECK",new{ok,name});if(!ok)throw new InvalidOperationException(name);checks++;}
  Check(active==180,"actual decoded PCM present or recovers within500ms in all180 observation windows");Check(ids.Count>=2,"natural EOF occurrence advances");Check(memory.Max()<900,"working set stays below resource budget");Check(root.Player.PlaybackQueue.Count==1000,"long duplicate queue retained");Check(root.Coordinator.Player.IsStopped,"explicit stop after soak");Check(OfflineStartupNetwork.Requests==1,"no extra network transports during loop");
  Program.Log("BODY_SOAK_COMPLETE",new{checks,seconds=watch.Elapsed.TotalSeconds,active,samples,warmupSeconds=180,noForcedGC=true,presentationFrames="NOT_MEASURED",humanAudio="PENDING"});Close();
 }
}
