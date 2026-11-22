using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HuayuanMusic.Provider.Contracts;
using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 internal async Task M206AExperienceTest(OfflineWindowRoot root,CorePlaybackAdapter adapter,CatalogTrack source,Action<bool> deny,Func<int> requests){
  int count=0;void Check(bool ok,string label){Program.Log(ok?"M206A_PASS":"M206A_FAIL",new{label});if(!ok)throw new Exception(label);count++;}
  async Task Click(Button b){Check(b.IsEnabled,"Action is enabled");b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await PendingCoreOperations;await Task.Delay(80);}
  Button ButtonIn(Control c,string text)=>c.GetVisualDescendants().OfType<Button>().First(b=>b.Content?.ToString()==text);
  async Task Shot(string name){var path=Path.Combine(Program.Session,name+".ready");Program.Log("CAPTURE",new{name,hwnd=TryGetPlatformHandle()!.Handle.ToInt64(),RenderScaling});File.WriteAllText(path,"ready");for(int i=0;i<250&&!File.Exists(path+".done");i++)await Task.Delay(20);Check(File.Exists(path+".done"),"Screenshot acknowledged "+name);}
  await Task.Delay(150);Go("平台");await Task.Delay(100);
  int before=requests();bool rejected=await kugouConsent.RequestAsync(()=>Task.FromResult(false));
  Check(!rejected&&!kugouConsent.Approved&&requests()==before,"Rejected permission leaves transport untouched");
  int prompts=0;await kugouConsent.RequestAsync(()=>{prompts++;return Task.FromResult(true);});
  Check(kugouConsent.Reconcile(true,adapter.Kugou.Account,null),"Approved connection binds current SDK alias");
  for(int i=0;i<5;i++)await kugouConsent.RequestAsync(()=>{prompts++;return Task.FromResult(true);});
  Check(prompts==1,"Repeated normal operations require one session prompt");
  var model=new KugouSessionConsent();await model.RequestAsync(()=>Task.FromResult(true));Check(model.Reconcile(true,"a",null)&&model.Reconcile(true,"a","key-a"),"Login establishment retains initial consent");
  Check(!model.Reconcile(true,"a","key-b")&&!model.Approved,"Changed real account partition revokes old consent");
  await model.RequestAsync(()=>Task.FromResult(true));model.Reconcile(true,"a","key-a");Check(!model.Reconcile(true,"b","key-a"),"Changed SDK session alias revokes old consent");
  var pending=new TaskCompletionSource<bool>();var approval=model.RequestAsync(()=>pending.Task);model.Revoke();pending.SetResult(true);Check(!await approval,"Late approval cannot revive revoked session");
  var tracks=Enumerable.Range(1,61).Select(i=>new ProviderTrackRef(KugouProvider.Identity,source.Id,"Synthetic song "+i,"Artist","Album")).ToArray();
  var lists=new[]{new ProviderPlaylist(KugouProvider.Identity,"official-a","Official synthetic A",tracks),new ProviderPlaylist(KugouProvider.Identity,"official-b","Official synthetic B",tracks.Take(3).ToArray())};
  playlistCatalogSelfTest=_=>source;ShowKugouOfficialPlaylists(adapter,lists);
  var section=kugouOfficialPlaylists!;var expanders=section.Children.OfType<Expander>().ToArray();Check(expanders.Length==2,"Two official playlists displayed together");
  foreach(var e in expanders)e.IsExpanded=true;await Task.Delay(100);var first=(StackPanel)expanders[0].Content!;
  await Click(ButtonIn((StackPanel)expanders[1].Content!,"查看曲目（每页 50 首）"));
  await Click(ButtonIn(first,"查看曲目（每页 50 首）"));Check(first.GetVisualDescendants().OfType<Button>().Count(b=>b.Content?.ToString()=="播放此曲")==50,"First page has exactly fifty playable rows");
  await Click(ButtonIn(first,"下一页"));Check(first.GetVisualDescendants().OfType<Button>().Count(b=>b.Content?.ToString()=="播放此曲")==11,"Second page preserves remaining eleven rows");
  await Task.Delay(100);kugouPageScroll!.Offset=new Vector(0,section.TranslatePoint(new Point(0,0),kugouPageCache!)!.Value.Y);await Task.Delay(100);var offset=kugouPageScroll.Offset;
  int[] CurrentList()=>section.Children.OfType<Expander>().Select(e=>((StackPanel)e.Content!).Children.Count).ToArray();var pageCounts=CurrentList();
  void Retained(string label){Check(ReferenceEquals(kugouOfficialPlaylists,section)&&ReferenceEquals(kugouPageScroll!.Content,kugouPageCache)&&expanders.All(e=>e.IsExpanded)&&pageCounts.SequenceEqual(CurrentList()),label);}
  await Shot("experience-before");
  await Click(ButtonIn(first,"播放此曲"));Check(adapter.State.Playing,"Actual shared BASS plays injected synthetic audio");Retained("Click play keeps same page, two lists, expansion and page number");Check(Equals(first.Tag,50),"Current row selection retained");
  Check(kugouPageScroll.Offset==offset,"Playing preserves platform scroll offset");
  await Click(ButtonIn(first,"加入本体队列"));Retained("Queue admission publish does not replace official lists");
  deny(true);await Click(ButtonIn(first,"播放此曲"));deny(false);Retained("Denied song playback leaves official list visible");Check(kugouStatus!.Text!.Contains("操作未完成"),"Playback failure gives error feedback");
  await Click(ButtonIn(first,"播放此曲"));
  // Playback state publication can precede stable native stream progress after replacement.
  // Wait for real BASS progress; never retry the command or weaken the pause assertion.
  for(int wait=0;wait<100&&(!root.Coordinator.Player.IsPlaying||adapter.State.Position<.15);wait++)await Task.Delay(20);
  if(!root.Coordinator.Player.IsPlaying||adapter.State.Position<.15)throw new InvalidOperationException("BASS failed to settle before pause");
  await adapter.ToggleAsync(adapter.State.CurrentId);Check(root.Coordinator.Player.IsPaused,"Pause uses same core");await adapter.ToggleAsync(adapter.State.CurrentId);Retained("Pause resume preserve official page");
  await adapter.NextAsync();Retained("Next preserves official page");await Click(ButtonIn(first,"播放此曲"));Retained("Replay preserves official page");
  Go("本地音乐");await Task.Delay(100);Go("平台");await Task.Delay(100);Retained("Return to platform reuses same expanded paged controls");Check(kugouPageScroll.Offset==offset,"Navigation restores same scrolling viewport");
  System.Threading.Volatile.Write(ref kugouHttpSessionAllowed,1);
  KugouUiRevoked();await Task.Delay(100);Retained("Temporary grant notification does not clear metadata");Check(IsKugouHttpSessionApproved,"Approved HTTP session is retained for same account");Check(kugouConsent.Approved&&prompts==1,"Same account transient notification retains session consent");
  await Shot("experience-after");PlatformPlaylistSnapshotStore.Save(new string('A',64),lists);
  RevokeKugouConsent();await adapter.Kugou.LogoutAsync();await Task.Delay(100);Retained("Logout retains metadata and expansion");Check(!kugouConsent.Approved&&!IsKugouHttpSessionApproved&&!adapter.Kugou.LoggedIn&&kugouPlaylistPlayButtons.All(b=>!b.IsEnabled),"Logout revokes session and disables old-account playback");
  await adapter.DisableKugouAsync();await Task.Delay(100);Check(!kugouConsent.Approved&&PlatformPlaylistSnapshotStore.LoadLatest()?.Playlists.Count==2,"DLC disable retains persisted official metadata");
  var local=adapter.State.Tracks.First(t=>t.Id<1000000).Id;await adapter.PlayAsync(local);Check(root.Coordinator.Player.IsPlaying,"Local BASS remains independent after DLC disable");await adapter.StopAsync();
  await Shot("experience-revoked");Program.Log("M206A_COMPLETE",new{checks=count,prompts,realRequests=0});
 }
}
