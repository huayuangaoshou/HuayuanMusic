using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;

namespace HuayuanPreview;

public sealed partial class PreviewWindow {
 internal async Task KugouIntegrationTest(OfflineWindowRoot root,CorePlaybackAdapter adapter){
  int count=0;void Check(bool pass,string label){Program.Log(pass?"KUGOU_PASS":"KUGOU_FAIL",new{label});if(!pass)throw new InvalidOperationException(label);count++;}
  async Task Reject<T>(Func<Task> work,string label)where T:Exception{try{await work();}catch(T){Check(true,label);return;}throw new InvalidOperationException("Expected refusal: "+label);}
  var dbBefore=SnapshotSyntheticDbRows();var net=OfflineStartupNetwork.Requests;
  try{
   Check(!adapter.State.Playing,"Default no autoplay");
   TrustedModuleDescriptor.Current.Validate(1);Check(true,"Fixed module descriptor compatible with host");
   await Reject<NotSupportedException>(()=>Task.Run(()=>TrustedModuleDescriptor.Current.Validate(2)),"Contract mismatch rejected");
   await Reject<NotSupportedException>(()=>Task.Run(()=>(TrustedModuleDescriptor.Current with{Version="999.0.0"}).Validate(1)),"Unknown module version rejected");
   await Reject<NotSupportedException>(()=>Task.Run(()=>(TrustedModuleDescriptor.Current with{FixtureOnly=false}).Validate(1)),"Descriptor cannot opt into live transport");
   var transport=new FixtureKgTransport();using var p=new KugouProvider(_=>new FixtureSdkGateway(transport));
   await Reject<InvalidOperationException>(()=>p.LoginFixtureAsync("demo-a"),"Disabled module refuses login");
   p.Enable();await Reject<UnauthorizedAccessException>(()=>p.LoginFixtureAsync("actual-account"),"Unknown account refused");
   await p.LoginFixtureAsync("demo-a");Check(!p.AutoClaimEnabled&&p.FixtureOnly,"Auto claim disabled; real transport absent");
   Check(!(await p.RefreshEntitlementAsync()).Active,"Initial mock entitlement inactive");
   await Reject<UnauthorizedAccessException>(()=>p.ClaimTodayAsync(false),"No unconfirmed claim");
   transport.ActivateAfterClaim=false;var claim=await p.ClaimTodayAsync(true);
   Check(claim.State==ClaimState.ClaimSuccess&&!claim.Entitlement.Active,"Claim success does not imply active entitlement");
   var again=await p.ClaimTodayAsync(true);Check(again.State==ClaimState.AlreadyReceived&&transport.ClaimCalls==1,"No duplicate daily claim");
   await Reject<UnauthorizedAccessException>(()=>p.ResolveAsync("fixture-member","standard"),"Claim is not playback permission");
   var results=await p.SearchAsync("测试");Check(results.Tracks.Count==2,"Inherited SDK parses Chinese search");
   var first=await p.SearchAsync("测试",1,1);var second=await p.SearchAsync("测试",2,1);Check(first.More&&first.Tracks[0].Id!=second.Tracks[0].Id,"Search pagination");
   Check((await p.SearchAsync("不存在")).Tracks.Count==0,"Empty search distinct from error");
   await Reject<ArgumentException>(()=>p.SearchAsync("",0),"Invalid search bounds");
   await Reject<UnauthorizedAccessException>(()=>p.ResolveAsync("fixture-free","lossless"),"Quality permission enforced");
   var grant=await p.ResolveAsync("fixture-free","standard");Check(p.DemandGrant(grant).Address.Host=="audio.fixture.invalid","Only terminal fixture media address");
   await Reject<UnauthorizedAccessException>(()=>Task.Run(()=>p.DemandGrant(grant with{Account="demo-b"})),"Forged account grant refused");
   await Reject<UnauthorizedAccessException>(()=>Task.Run(()=>p.DemandGrant(grant with{Expires=DateTimeOffset.UtcNow.AddMinutes(-1)})),"Expired grant refused");
   Check((await p.LyricsAsync("fixture-free")).Contains("[00:"),"LRC payload available");
   var lists=await p.ReadAsync("demo-a");Check(lists.Count==1&&lists[0].Name.Contains("demo-a"),"Account A readonly playlists");
   await Reject<UnauthorizedAccessException>(()=>p.ReadAsync("demo-b"),"Cross-account playlist read refused");
   transport.FailRead=true;await Reject<IOException>(()=>p.RefreshEntitlementAsync(),"Transport outage explicit");transport.FailRead=false;
   Check(p.Cached("demo-a").Count==1&&p.Cached("demo-b").Count==0,"Cache account separation");
   await p.LoginFixtureAsync("demo-b");await Reject<UnauthorizedAccessException>(()=>Task.Run(()=>p.DemandGrant(grant)),"Account switch revokes grant");
   await p.LogoutAsync();await Reject<UnauthorizedAccessException>(()=>p.SearchAsync("测试"),"Logout removes session access");
   Check(p.Cached("demo-a").Count==1,"Non-secret stale metadata retained separately");
   using(var uncertain=new KugouProvider(_=>new FixtureSdkGateway(new FixtureKgTransport{TimeoutClaim=true}))){uncertain.Enable();await uncertain.LoginFixtureAsync("demo-a");Check((await uncertain.ClaimTodayAsync(true)).State==ClaimState.OutcomeUnknown,"Timeout does not report success");Check((await uncertain.ClaimTodayAsync(true)).State==ClaimState.OutcomeUnknown,"Unknown mutation not blindly retried");}
   var constrained=new FixtureKgTransport{OfficialOnly=true};using(var blocked=new KugouProvider(_=>new FixtureSdkGateway(constrained))){blocked.Enable();await blocked.LoginFixtureAsync("demo-a");Check((await blocked.ClaimTodayAsync(true)).State==ClaimState.Rejected&&constrained.ClaimCalls==0,"Official/ad requirement not bypassed");}
   using(var canceled=new CancellationTokenSource()){canceled.Cancel();await Reject<OperationCanceledException>(()=>p.LoginFixtureAsync("demo-a",canceled.Token),"Cancellation before operation");}
   Check(SnapshotSyntheticDbRows().SequenceEqual(dbBefore),"Provider business operations leave SQLite unchanged");
   Check(!ProviderStreamRegistry.Allows("provider-stream://forged"),"Metadata cannot manufacture source admission");
   Check(!await root.Coordinator.LoadAsync("https://audio.fixture.invalid/forged.wav","forged",1,TimeSpan.FromSeconds(2),default),"Direct HTTP still refused by production coordinator");
   var kg=adapter.Kugou;adapter.EnableKugou();await kg.LoginFixtureAsync("demo-a");int id=await adapter.AdmitKugouAsync(results.Tracks[0],"standard");
   Check(!adapter.State.Playing&&adapter.State.Tracks.Any(x=>x.Id==id),"Admission updates metadata without autoplay");
   await adapter.QueueAsync("append",id);Check(adapter.State.Queue.Contains(id),"Provider added to unique production queue");
   await adapter.VolumeAsync(0);await adapter.PlayAsync(id);await Task.Delay(400);
   Check(root.Coordinator.Player.IsPlaying&&adapter.State.CurrentId==id,"Actual production BASS stream playback");
   var position=root.Coordinator.Player.GetPosition().TotalSeconds;await Task.Delay(350);Check(root.Coordinator.Player.GetPosition().TotalSeconds>position+.1,"Actual BASS audio clock advances");
   var analysis=root.Coordinator.Player.GetActiveAnalysisSnapshot();Check(analysis.Rms>0,"Nonzero decoded synthetic PCM, not a state timer");
   await adapter.ToggleAsync(id);Check(root.Coordinator.Player.IsPaused,"Pause real stream");
   await adapter.SeekAsync(.8);Check(Math.Abs(root.Coordinator.Player.GetPosition().TotalSeconds-.8)<.2,"Seek real stream paused");
   await adapter.ToggleAsync(id);Check(root.Coordinator.Player.IsPlaying,"Resume real stream");
   await adapter.VolumeAsync(.2);Check(Math.Abs(adapter.State.Volume-.2)<.01,"Production volume readback");await adapter.VolumeAsync(0);
   Check(adapter.State.Lyrics.Length>0,"Existing lyric parser receives admitted LRC");
   var local=adapter.State.Tracks.First(x=>x.Id<1_000_000).Id;await adapter.QueueAsync("append",local);Check(adapter.State.Queue.Contains(id)&&adapter.State.Queue.Contains(local),"Local and provider share one queue");
   await adapter.PlaylistAsync("save","MIXED_MUST_REFUSE");Check(adapter.State.Error.Contains("暂不能保存"),"Mixed permanent save refuses without data loss");
   await adapter.DisableKugouAsync();await Task.Delay(100);Check(!root.Coordinator.Player.IsPlaying&&ProviderStreamRegistry.Count==0,"Disable stops provider output and revokes streams");
   await adapter.PlayAsync(local);Check(root.Coordinator.Player.IsPlaying,"Local playback survives provider disable");
   await adapter.StopAsync();Check(root.Coordinator.Player.IsStopped,"Stop releases active audio stream");
   Check(OfflineStartupNetwork.Requests==net,"No terminal network factory calls beyond original preflight");
   Check((await root.Library.GetPlaylistsAsync()).All(x=>x.Name!="MIXED_MUST_REFUSE"),"Rejected save created no false playlist");
   adapter.RemoveKugou();Check(!adapter.Providers.IsEnabled(KugouProvider.Identity),"Trusted module removal disables registration");
   Go("平台");await Task.Delay(120);Program.Log("KUGOU_COMPLETE",new{checks=count,realPlatform="REAL_PENDING",audio="MOCK_TRANSPORT_REAL_BASS_PCM",human="PENDING"});
  }catch(Exception e){Program.Result=2;Program.Log("KUGOU_FAILED",e.ToString());}
  finally{Close();}
 }
}
