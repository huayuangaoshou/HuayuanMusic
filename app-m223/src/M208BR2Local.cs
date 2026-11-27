using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using KugouAvaloniaPlayer.Services.Startup;
using HuayuanPreview.R2;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 bool R2Enabled=>Program.Desktop||Environment.GetEnvironmentVariable("M208B_R2_UI")=="1";
 internal bool M219RPassed=>R2Model?.Concept.LocalTrialPassed==true;
 internal ShellModel? R2Model;internal R2ShellView? R2View;
 public async Task WaitForR2CloseAsync(){
  if(R2Model==null)return;
  M219RStartup.Lease?.Stop();
  if(R2Model.Concept.CancelTrial())await core.StopAsync();
  if(P2AWorkspace.M223Normal)
  {
   await R2Model.Player.Pending;
   await R2Model.Concept.Pending;
  }
  await R2Model.Concept.RevokeAsync();
  await R2Model.Concept.Pending;
  if(M219RStartup.Active)await R2Model.Concept.RevokeAsync();
  await R2Model.Player.Pending;
  if(P2AWorkspace.M223Normal&&core is CorePlaybackAdapter adapter)
   Program.Log("M223_CLOSE",new {
    terminalMockRequests=OfflineStartupNetwork.Requests,
    streams=adapter.ProviderStreamCount,registry=KugouAvaloniaPlayer.Services.ProviderStreamRegistry.Count,
    connected=adapter.Kugou.LoggedIn||adapter.Kugou.Enabled||adapter.Kugou.Account!=null,
    localEntries=adapter.State.QueueEntries.Length
   });
 }
 internal void MountDesktopR2(CorePlaybackAdapter adapter)
 {
  R2Model = new ShellModel(adapter);
  R2View = new R2ShellView { DataContext = R2Model };
  RequestedThemeVariant = R2.DesktopPreferences.Behavior.Dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
  ExtendClientAreaToDecorationsHint = true;
  ExtendClientAreaTitleBarHeightHint = 0;
  WindowDecorations = WindowDecorations.None;
  // R6: 窗口底启用真实材质——Win11 22000+ 用 Mica，其余回退不透明 Base。
  EnableDesktopBackdrop();
  Content = R2View;
  WindowFrameMeter.Attach(this, "main");
  Title = "HuayuanMusic";
  MinWidth = 1000; MinHeight = 700; Width = 1280; Height = 840;
  R2View.InstallResizeHandles(this);
  Closed += (_, _) => R2Model.Dispose();
 }
 // R6: 窗口底真实材质。Win11 22000+ 用 Mica；其余回退不透明 Base（由 R2ShellView 的 GlassBase 平涂兜底）。
 internal bool DesktopBackdropActive { get; private set; }
 void EnableDesktopBackdrop()
 {
  var request = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
   ? new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.None }
   : new[] { WindowTransparencyLevel.None };
  TransparencyLevelHint = request;
  // Mica 需窗口背景透明才能透出；不透明兜底时保留 R2ShellView 的 GlassBase 平涂。
  DesktopBackdropActive = false;
  PropertyChanged += OnBackdropLevelChanged;
  // 初始按当前实际材质等级应用一次，避免等待后续属性变更事件。
  ApplyBackdropState();
 }
 void OnBackdropLevelChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
 {
  if (args.Property != ActualTransparencyLevelProperty) return;
  ApplyBackdropState();
 }
 void ApplyBackdropState()
 {
  var mica = ActualTransparencyLevel == WindowTransparencyLevel.Mica;
  if (mica == DesktopBackdropActive) return;
  DesktopBackdropActive = mica;
  Background = mica ? Avalonia.Media.Brushes.Transparent : null;
  R2View?.ApplyBackdrop(mica);
 }
 public async Task AttachR2LocalAsync(){
  if(!R2Enabled||!Path.GetFileName(OfflineStartupProfile.Current.Root).StartsWith("p17-body-r3-manual-test-r2-",StringComparison.Ordinal))throw new UnauthorizedAccessException("R2 requires a new synthetic-only admitted profile");
  if(core.State.Tracks.Length==0){
   var dir=Path.Combine(Program.Root,"fixtures","r2-local");var hashes=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(dir,"manifest.json")))!;
   foreach(var kv in hashes){if(Path.GetFileName(kv.Key)!=kv.Key)throw new IOException("Fixture boundary");var path=Path.Combine(dir,kv.Key);OfflineStartupProfile.RejectLinks(path);if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))!=kv.Value)throw new IOException("Fixture integrity");}
   await core.ImportFilesAsync(hashes.Keys.Select(n=>Path.Combine(dir,n)).ToArray(),CancellationToken.None);if(core.State.Error.Length>0||core.State.Tracks.Length!=61)throw new IOException("Fixture import failed");
   foreach(var name in new[]{"本地合成歌单 A","本地合成歌单 B"}){await core.PlaylistAsync("create",name);if(core.State.Error.Length>0)throw new IOException(core.State.Error);var list=core.State.Lists.Single(l=>l.Name==name);foreach(var track in core.State.Tracks.Take(name.EndsWith("A")?61:5)){await core.PlaylistAsync("add",listId:list.Id,songId:track.Id);if(core.State.Error.Length>0)throw new IOException(core.State.Error);}}
  }
  if(Environment.GetEnvironmentVariable("M208B_R2_AUTOMATION")=="1")await core.VolumeAsync(0);
  R2Model=new ShellModel((CorePlaybackAdapter)core);R2View=new R2ShellView{DataContext=R2Model};Content=R2View;Title=P2AWorkspace.M223Normal?"HuayuanMusic · M2-23 离线预览":M219RStartup.Real?"HuayuanMusic · M222 本机短试听":P2AWorkspace.RealEntitlementOnly?"HuayuanMusic · P2BP-E · VIP 权益只读":P2AWorkspace.RealCombinedVipAudio?"HuayuanMusic · P2BP-F · VIP+单曲受控验证":P2AWorkspace.RealSingleAudio?"HuayuanMusic · P2B-P · 单曲受控验证":P2AWorkspace.RealReadOnly?"HuayuanMusic · P2B-R · 酷狗概念版真实只读":"HuayuanMusic · R2 P2A · TEST DATA";MinWidth=1000;MinHeight=700;Width=1280;Height=840;KeyDown-=Keyboard;
  core.Changed+=WriteR2Probe;Closed+=(_,_)=>{core.Changed-=WriteR2Probe;R2Model.Dispose();};WriteR2Probe(core.State);
  Program.Log("R2_REAL_UI_READY",new{singleCore=true,tracks=core.State.Tracks.Length,core.State.Playing,initialIds=core.State.QueueEntries.Select(e=>e.EntryId),lists=core.State.Lists});
 }
 // Test-only observer. Snapshot readers can temporarily hold the target file open
 // on Windows; never let that diagnostic race terminate the real audio/UI host.
 void WriteR2Probe(PlaybackSnapshot state){
  if(Environment.GetEnvironmentVariable("M208B_R2_PROBE")!="1")return;
  var path=Path.Combine(OfflineStartupProfile.Current.Root,"r2-snapshot.json");
  var temp=path+".tmp";
  try{
   File.WriteAllText(temp,JsonSerializer.Serialize(new{pid=Environment.ProcessId,state,rms=((CorePlaybackAdapter)core).TrialDecodedRms,uiError=R2Model?.Player.UiError,uiQueue=R2Model?.Player.DisplayPlaybackQueue.Select(r=>r.QueueId)}));
   for(int attempt=1;attempt<=5;attempt++){
    try{
     File.Move(temp,path,true);
     if(attempt>1)Program.Log("R2_PROBE_COLLISION_RECOVERED",new{state.Revision,attempt});
     return;
    }
    catch(UnauthorizedAccessException ex){
     if(attempt==5){Program.Log("R2_PROBE_SNAPSHOT_DROPPED",new{state.Revision,attempt,exception=ex.GetType().Name});return;}
    }
    catch(IOException ex) when ((ex.HResult&0xffff)==32||(ex.HResult&0xffff)==33){
     if(attempt==5){Program.Log("R2_PROBE_SNAPSHOT_DROPPED",new{state.Revision,attempt,exception=ex.GetType().Name});return;}
    }
    System.Threading.Thread.Sleep(12);
   }
  }catch(IOException ex){
   Program.Log("R2_PROBE_IO_FAILURE",new{state.Revision,type=ex.GetType().Name,code=ex.HResult});
  }catch(UnauthorizedAccessException ex){
   Program.Log("R2_PROBE_IO_FAILURE",new{state.Revision,type=ex.GetType().Name,code=ex.HResult});
  }
 }
}
