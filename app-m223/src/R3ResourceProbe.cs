using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using KugouAvaloniaPlayer.ViewModels;
using ManagedBass;
using SimpleAudio;
namespace HuayuanPreview;

// Diagnostic observer only: no collection, cache eviction, device changes or account access.
internal sealed class R3GcObserver : IDisposable {
 readonly CancellationTokenSource stop=new(); readonly Task loop; readonly Stopwatch time;
 internal R3GcObserver(Stopwatch time){this.time=time;loop=Observe();}
 static object Describe(GCMemoryInfo g)=>new {g.Index,g.Generation,g.Compacted,g.Concurrent,heapMiB=g.HeapSizeBytes/1048576d,fragmentMiB=g.FragmentedBytes/1048576d,liveEstimateMiB=(g.HeapSizeBytes-g.FragmentedBytes)/1048576d,committedMiB=g.TotalCommittedBytes/1048576d,g.PinnedObjectsCount,g.FinalizationPendingCount,g.PauseTimePercentage,generations=g.GenerationInfo.ToArray().Select((v,i)=>new {generation=i,beforeMiB=v.SizeBeforeBytes/1048576d,afterMiB=v.SizeAfterBytes/1048576d,fragmentMiB=v.FragmentationAfterBytes/1048576d}).ToArray()};
 internal static object Heap()=>new {any=Describe(GC.GetGCMemoryInfo()),fullBlocking=Describe(GC.GetGCMemoryInfo(GCKind.FullBlocking)),background=Describe(GC.GetGCMemoryInfo(GCKind.Background))};
 async Task Observe(){long full=0,bg=0;try{using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(250));while(await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false)){var f=GC.GetGCMemoryInfo(GCKind.FullBlocking);var b=GC.GetGCMemoryInfo(GCKind.Background);if(f.Index>full){full=f.Index;Program.Log("R3_NATURAL_FULL_GC",new {seconds=time.Elapsed.TotalSeconds,kind="FullBlocking",data=Describe(f)});}if(b.Index>bg){bg=b.Index;Program.Log("R3_NATURAL_FULL_GC",new {seconds=time.Elapsed.TotalSeconds,kind="Background",data=Describe(b)});}}}catch(OperationCanceledException){}}
 public void Dispose(){stop.Cancel();loop.GetAwaiter().GetResult();stop.Dispose();}
}
public sealed partial class PreviewWindow {
 [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="IsIconic")]
 [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]static extern bool R3IsIconic(IntPtr window);
 readonly HashSet<int> r3KnownNativeChannels=new();
 static object? Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(o);
 static int CountOf(object? o)=>o==null?0:Convert.ToInt32(o.GetType().GetProperty("Count")?.GetValue(o)??0);
 static int CapacityOf(object? o)=>o?.GetType().GetProperty("Capacity")?.GetValue(o) is int n?n:-1;
 object R3RendererState(){var source=typeof(TopLevel).GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).FirstOrDefault(p=>p.Name=="PresentationSource"&&p.PropertyType.FullName=="Avalonia.Controls.PresentationSource")?.GetValue(this);if(source==null)return new {available=false};var renderer=source.GetType().GetProperty("Renderer",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(source);if(renderer==null)return new {available=false};var target=renderer.GetType().GetProperty("CompositionTarget",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(renderer);return new {available=true,dirty=CountOf(Field(renderer,"_dirty")),recalculateChildren=CountOf(Field(renderer,"_recalculateChildren")),targetEnabled=target?.GetType().GetProperty("IsEnabled")?.GetValue(target)};}
 object R3Caches(OfflineWindowRoot root){
  var player=root.Player;var engine=root.Coordinator.Player;
  object Deck(string name){var deck=Field(engine,name)!;var state=Field(deck,"_state")!;var t=state.GetType();int Val(string p)=>Convert.ToInt32(t.GetProperty(p)!.GetValue(state));int stream=Val("Stream"),source=Val("SourceStream");if(stream!=0)r3KnownNativeChannels.Add(stream);if(source!=0)r3KnownNativeChannels.Add(source);var buffers=t.GetProperties().Where(p=>p.PropertyType.IsArray).ToDictionary(p=>p.Name,p=>(p.GetValue(state) as Array)?.Length??0);return new {stream,source,owner=t.GetProperty("StreamSourceOwner")!.GetValue(state)!=null,buffers};}
  var gain=typeof(TrackVolumeNormalizer).GetField("GainCache",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null);
  var transition=Field(player,"_transitionAnalysisService");
  var a=Deck("_deckA");var b=Deck("_deckB");
  return new {queue=player.PlaybackQueue.Count,displayQueue=player.DisplayPlaybackQueue.Count,queueCapacity=CapacityOf(player.PlaybackQueue),snapshotQueue=core.State.QueueEntries.Length,lyric=player.LyricLines.Count,renderLyric=player.RenderLyricLines.Count,history=core.State.History.Length,catalog=core.State.Tracks.Length,playlists=core.State.Lists.Length,artCache=artCache.Count,rowCache=rowCache.Count,queueRows=queueRows.Count,visibleQueue=nativeQueue?.GetVisualDescendants().OfType<ListBoxItem>().Count()??0,markers=playingMarkers.Count,tailTelemetry=CountOf(Field(player,"_tailTelemetry")),tailCapacity=CapacityOf(Field(player,"_tailTelemetry")),normalizationGain=CountOf(gain),transitionCache=CountOf(transition==null?null:Field(transition,"_analysisCache")),deckA=a,deckB=b,knownNativeChannelIds=r3KnownNativeChannels.Count,knownChannelsStillValid=r3KnownNativeChannels.Count(id=>Bass.ChannelGetInfo(id,out _))};
 }
 internal async Task R3ResourceTest(OfflineWindowRoot root,CorePlaybackAdapter adapter){
  if(!OfflineGuiApplication.SelfTest)throw new InvalidOperationException("Selftest authorization required");
  int seconds=int.TryParse(Environment.GetEnvironmentVariable("R3_SECONDS"),out var limit)?limit:2700;if(seconds is <120 or >3600)throw new ArgumentOutOfRangeException(nameof(seconds));
  bool diagnostic=Environment.GetEnvironmentVariable("R3_FORCED_DIAGNOSTIC")=="1";
  var watch=Stopwatch.StartNew();using var observer=new R3GcObserver(watch);using var proc=Process.GetCurrentProcess();using var allocations=Environment.GetEnvironmentVariable("R3_ALLOCATION_SAMPLE")=="1"?new R3AllocationObserver():null;
  var seen=new HashSet<Guid>();var weakDrawers=new List<WeakReference<ListBox>>();var trackedDrawers=new System.Runtime.CompilerServices.ConditionalWeakTable<ListBox,object>();int active=0,samples=0,checks=0;bool minimized=false,restored=false;bool cycle=Environment.GetEnvironmentVariable("R3_CYCLE")=="1";double minimizeAt=seconds>=2700?180:30,restoreAt=seconds>=2700?900:90;var stableLibrary=mainSongs;
  void TrackDrawer(){if(nativeQueue!=null&&!trackedDrawers.TryGetValue(nativeQueue,out _)){trackedDrawers.Add(nativeQueue,new());weakDrawers.Add(new(nativeQueue));}}
  object Snapshot(string phase){proc.Refresh();return new {seconds=watch.Elapsed.TotalSeconds,phase,windowState=WindowState.ToString(),IsVisible,deferredCoreStates,coreResumeCount,renderer=R3RendererState(),workingMiB=proc.WorkingSet64/1048576d,privateMiB=proc.PrivateMemorySize64/1048576d,managedMiB=GC.GetTotalMemory(false)/1048576d,totalAllocatedMiB=GC.GetTotalAllocatedBytes(false)/1048576d,threads=proc.Threads.Count,handles=proc.HandleCount,cpuSeconds=proc.TotalProcessorTime.TotalSeconds,gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2),timers=System.Threading.Timer.ActiveCount,heap=R3GcObserver.Heap(),caches=R3Caches(root),weakDrawersCreated=weakDrawers.Count,weakDrawersAlive=weakDrawers.Count(x=>x.TryGetTarget(out _)),device=Bass.CurrentDevice,root.Coordinator.Player.IsPlaying,root.Coordinator.Player.IsStopped,current=adapter.State.CurrentEntryId,adapter.State.Position};}
  await adapter.SetModeAsync(0);await adapter.VolumeAsync(0);
  var tracks=(await ((LocalMusicLibraryService)root.Library).GetAllLocalTracksAsync()).Take(2).ToArray();
  root.Player.SetControlledQueue(Enumerable.Range(0,1000).Select(i=>LocalMusicLibraryViewModel.ToSongItem(tracks[i%2]).NewQueueEntry()).ToArray());
  await adapter.QueueEntryAsync("play",root.Player.PlaybackQueue[0].QueueEntryId);Go("本地音乐");stableLibrary=mainSongs;SetQueue(true);TrackDrawer();
  Program.Log("R3_RESOURCE_BEGIN",new {seconds,noForcedGC=!diagnostic,diagnosticOnly=diagnostic,dpi=RenderScaling,queue=1000,baseSample=Snapshot("start")});
  while(watch.Elapsed.TotalSeconds<seconds){
   await Task.Delay(10000);var engine=root.Coordinator.Player;var recovery=Stopwatch.StartNew();while((!engine.IsPlaying||engine.GetActiveAnalysisSnapshot().Rms<=0)&&recovery.ElapsedMilliseconds<500)await Task.Delay(20);
   if(cycle&&!minimized&&watch.Elapsed.TotalSeconds>=minimizeAt){WindowState=WindowState.Minimized;await Task.Delay(250);if(TryGetPlatformHandle() is not {} h||!R3IsIconic(h.Handle))throw new InvalidOperationException("native minimization not confirmed");minimized=true;Program.Log("R3_MINIMIZED",Snapshot("minimized"));}
   if(cycle&&minimized&&!restored&&watch.Elapsed.TotalSeconds>=restoreAt){WindowState=WindowState.Normal;await Task.Delay(400);ResumeCorePresentation();if(!CanPresentCore||R3IsIconic(TryGetPlatformHandle()!.Handle)||!ReferenceEquals(mainSongs,stableLibrary)||displayed?.CurrentEntryId!=core.State.CurrentEntryId)throw new InvalidOperationException("restore reconciliation/library stability failed");if(queueOpen&&!queueRows.Select(x=>x.EntryId).SequenceEqual(core.State.QueueEntries.Select(x=>x.EntryId)))throw new InvalidOperationException("restored queue mismatch");restored=true;Program.Log("R3_RESTORED",Snapshot("restored"));}
   var rms=engine.GetActiveAnalysisSnapshot().Rms;bool pcm=engine.IsPlaying&&rms>0&&recovery.ElapsedMilliseconds<500;if(pcm)active++;samples++;seen.Add(adapter.State.CurrentEntryId);
   Program.Log("R3_RESOURCE_SAMPLE",new {sample=samples,pcm,rms,recoveryMs=recovery.Elapsed.TotalMilliseconds,metrics=Snapshot("playing")});if(allocations!=null&&samples%6==0)Program.Log("R3_SAMPLED_ALLOCATIONS",allocations.Snapshot());
   if(samples%6==0){await adapter.SeekAsync(Math.Min(2,adapter.State.Duration/2));await adapter.ToggleAsync(adapter.State.CurrentId);await Task.Delay(80);await adapter.ToggleAsync(adapter.State.CurrentId);root.Player.MoveControlledQueue(998,999);SetQueue(samples%12==0);TrackDrawer();}
  }
  await adapter.StopAsync();Program.Log("R3_STOP",Snapshot("justStopped"));
  for(int i=0;i<12;i++){await Task.Delay(5000);Program.Log("R3_STOP_SAMPLE",Snapshot("stopped"));}
  if(diagnostic){Program.Log("R3_FORCED_BEFORE",Snapshot("forcedBefore"));await Task.Run(()=>{GC.Collect(2,GCCollectionMode.Forced,true,true);GC.WaitForPendingFinalizers();GC.Collect(2,GCCollectionMode.Forced,true,true);});Program.Log("R3_FORCED_AFTER",Snapshot("forcedAfter"));}
  void Check(bool ok,string name){Program.Log("R3_RESOURCE_CHECK",new {ok,name});if(!ok)throw new InvalidOperationException(name);checks++;}
  Check(active==samples,"all real PCM windows recover within500ms");Check(seen.Count>=2,"natural EOF advances distinct entries");Check(root.Player.PlaybackQueue.Count==1000,"duplicate queue retained");Check(root.Coordinator.Player.IsStopped,"real engine stopped");Check(OfflineStartupNetwork.Requests==1,"terminal Mock has no additional requests");
  Check(r3KnownNativeChannels.All(id=>!Bass.ChannelGetInfo(id,out _)),"all observed old BASS channels invalid after stop");
  if(cycle){Check(minimized&&restored&&deferredCoreStates>0&&coreResumeCount>0,"native minimize playback and one-state restoration executed");Check(ReferenceEquals(mainSongs,stableLibrary),"library controls retained across minimize/restore");}
  Program.Log("R3_RESOURCE_COMPLETE",new {checks,seconds=watch.Elapsed.TotalSeconds,playingSeconds=seconds,samples,active,entries=seen.Count,diagnosticOnly=diagnostic,noForcedGC=!diagnostic,final=Snapshot("complete"),presentFrames="NOT_MEASURED",humanAudio="PENDING"});Close();
 }
}
