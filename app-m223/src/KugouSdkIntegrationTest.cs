using System.Net;
using Avalonia.VisualTree;
using System.Text;
using System.Text.Json;
using HuayuanMusic.Kugou.Gateway;
using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;

namespace HuayuanPreview;

public sealed partial class PreviewWindow {
 // Focused HTTP-handler injection for the NEW connection only. No socket, alternate provider or fake player.
 internal async Task KugouSdkIntegrationTest(OfflineWindowRoot root,CorePlaybackAdapter adapter){
  bool mature=Environment.GetEnvironmentVariable("KUGOU_MATURE_SUITE")=="1";
  int checks=0;void Check(bool good,string label){Program.Log(good?"SDK_PASS":"SDK_FAIL",new{label});if(!good)throw new InvalidOperationException(label);checks++;}
  async Task Refuse<T>(Func<Task> f,string label)where T:Exception{try{await f();}catch(T){Check(true,label);return;}throw new InvalidOperationException("Expected refusal: "+label);}
  void ClickAccount(string text){var b=this.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(x=>x.Content?.ToString()==text);b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));}
  var dbBefore=SnapshotSyntheticDbRows();var networkBefore=OfflineStartupNetwork.Requests;
  try{
   if(Environment.GetEnvironmentVariable("M205_GATE_SUITE")=="1"){
    await Refuse<UnauthorizedAccessException>(()=>Task.Run(()=>DemandM205Connection()),"M205 unapproved host refuses before credential access");
    await Refuse<UnauthorizedAccessException>(()=>Task.Run(()=>adapter.DirectKugouConnection!()),"M205 direct factory refuses offline account access");
    await Refuse<UnauthorizedAccessException>(()=>Task.Run(()=>adapter.ApprovedKugouConnection!()),"M205 configured factory refuses offline account access");
   }
   const string hash="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
   var identity=new AuthorizedIdentity{AppId="9000000000",QrIssuerAppId="9000000002",SourceAppId="9000000001",ClientVersion="1",PlatformPermissionReference="OFFLINE_INJECTION_NOT_A_PLATFORM_APPROVAL",Dfid="OFFLINE_TEST_DEVICE",Mid="OFFLINE_TEST_MID",Uuid="OFFLINE_TEST_UUID"};
   var permit=new GatewayPermit{HumanApprovalReference="TERMINAL_TEST_ONLY",NotAfter=DateTimeOffset.UtcNow.AddMinutes(5),Operations=Enum.GetValues<GatewayOperation>().Where(op=>!mature||op is not(GatewayOperation.UserVerify or GatewayOperation.SongAuthorize or GatewayOperation.ResolveAudio)).ToHashSet(),MediaHosts=new HashSet<string>{"media.example.test"},AllowAudioTransport=true};
   using var signing=new CredentialBoundSigner("TEST_SIGNING_ONLY","TEST_QR_ONLY","TEST_MEDIA_ONLY");
   using var handler=new SdkConnectionHandler{Mature=mature};var wire=new AuthorizedHttpWire(new HttpClient(handler),identity,permit,signing);
   var concept=new ConceptGateway("sdk-test-a",identity,wire);
   var media=Path.Combine(OfflineStartupProfile.Current.Media,"scan","a.wav");OfflineStartupProfile.Current.DemandMedia(media);
   using var audioHandler=new SdkConnectionAudioHandler(media);
   var mediaTransport=new AuthorizedAudioTransport(new HttpClient(audioHandler),permit);
   var sdk=mature?new IntegratedSdkGateway(concept,mediaTransport):new IntegratedSdkGateway(concept,mediaTransport,KugouPlaybackPath.ConceptAuth);
   Go("平台");await Task.Delay(120);ClickAccount("离线二维码演练");Check(kugouQrImage?.Source is Avalonia.Media.Imaging.Bitmap&&!adapter.Kugou.LoggedIn&&handler.Paths.Count==0,"Interactive offline QR preview is not an account login or HTTP request");ClickAccount("取消登录");Check(kugouQrImage?.Source==null,"Offline QR preview cancellation releases image");ClickAccount("连接获准配置");await PendingCoreOperations;
   Check(!adapter.Kugou.SdkConnected&&handler.Paths.Count==0,"Unconfigured UI refuses connection before any request");
   var configured=new ApprovedKugouConnectionProvider(OfflineStartupProfile.Current.Root);
   var configuredData=new ApprovedKugouConfiguration{Schema=1,ProfileRoot=OfflineStartupProfile.Current.Root,Application=new(){AppId=identity.AppId,QrIssuerAppId=identity.QrIssuerAppId,SourceAppId=identity.SourceAppId,ClientVersion=identity.ClientVersion,PlatformPermissionReference=identity.PlatformPermissionReference},Device=new(){Dfid=identity.Dfid,Mid=identity.Mid,Uuid=identity.Uuid},Permit=new GatewayPermit{HumanApprovalReference="TERMINAL_TEST_ONLY",NotAfter=DateTimeOffset.UtcNow.AddMinutes(5),Operations=new HashSet<GatewayOperation>{GatewayOperation.QrCreate,GatewayOperation.QrPoll},MediaHosts=new HashSet<string>(),LiveNetworkApproved=false},Signing=new(){Android="TEST_SIGNING_ONLY",Web="TEST_QR_ONLY",Media="TEST_MEDIA_ONLY"}};
   void StoreConfiguration(){var b=JsonSerializer.SerializeToUtf8Bytes(configuredData,ApprovedKugouConnectionProvider.JsonOptions());try{CurrentUserKugouCredential.WriteTest(configured.Target,b);}finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(b);}}
   StoreConfiguration();try{
    await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect()),"Terminal-only credential cannot create live transport");
    configuredData.Schema=2;StoreConfiguration();await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect(_=>sdk)),"Unknown configuration schema rejected");configuredData.Schema=1;
    configuredData.ProfileRoot+="-other";StoreConfiguration();await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect(_=>sdk)),"Cross-profile credential content rejected");configuredData.ProfileRoot=OfflineStartupProfile.Current.Root;
    var validPermit=configuredData.Permit;configuredData.Permit=new GatewayPermit{HumanApprovalReference="EXPIRED",NotAfter=DateTimeOffset.UtcNow.AddMinutes(-1),Operations=new HashSet<GatewayOperation>{GatewayOperation.QrCreate},MediaHosts=new HashSet<string>()};StoreConfiguration();await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect(_=>sdk)),"Expired approval rejected before connection");configuredData.Permit=validPermit;StoreConfiguration();
    var liveConstructionPermit=new GatewayPermit{LiveNetworkApproved=true,HumanApprovalReference="OFFLINE_CONSTRUCTION_ONLY_NO_REQUEST",NotAfter=DateTimeOffset.UtcNow.AddMinutes(5),Operations=new HashSet<GatewayOperation>{GatewayOperation.QrCreate,GatewayOperation.QrPoll},MediaHosts=new HashSet<string>()};
    configuredData.Permit=liveConstructionPermit;StoreConfiguration();using(var constructed=configured.Connect()){Check(!constructed.LoggedIn&&constructed.PlaybackPath==KugouPlaybackPath.MatureSdk&&handler.Paths.Count==0,"Production credential factory constructs mature connection without account or audio request, then disposes");}
    configuredData.Permit=new GatewayPermit{HumanApprovalReference="TEST_ONLY",NotAfter=DateTimeOffset.UtcNow.AddMinutes(5),Operations=new HashSet<GatewayOperation>{GatewayOperation.ClaimDaily},MediaHosts=new HashSet<string>()};StoreConfiguration();await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect(_=>sdk)),"Connection credential cannot implicitly approve VIP claiming");
    configuredData.Permit=validPermit;StoreConfiguration();
    adapter.ApprovedKugouConnection=()=>configured.Connect(c=>{Check(c.Signing.Android=="TEST_SIGNING_ONLY"&&c.Identity.Dfid==identity.Dfid&&c.Permit.Operations.Contains(GatewayOperation.QrCreate),"Current-user credential reaches existing connection factory with separate identity/signing/permit");return sdk;});
    ClickAccount("连接获准配置");await PendingCoreOperations;
   Check(adapter.Kugou.SdkConnected&&!adapter.Kugou.FixtureOnly&&!adapter.State.Playing,"Same installed DLC selects SDK/Auth; no autoplay");
   }finally{CurrentUserKugouCredential.DeleteTest(configured.Target);}
   await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect()),"Deleted credential no longer connects; existing session stays memory-only");
   Check(handler.Paths.Count==0,"Attach has no automatic login or HTTP request");
   bool mockExecuted=false;KugouFixtureAction(()=>{mockExecuted=true;return Task.CompletedTask;});await PendingCoreOperations;Check(!mockExecuted&&handler.Paths.Count==0,"Mock UI cannot dispatch actions to an attached SDK account");
   using(var canceled=new CancellationTokenSource()){canceled.Cancel();await Refuse<OperationCanceledException>(()=>adapter.Kugou.BeginQrAsync(canceled.Token),"Canceled QR never dispatched");}
   Check(handler.Paths.Count==0,"Cancellation leaves zero requests");
   await Refuse<UnauthorizedAccessException>(()=>adapter.Kugou.SearchAsync("测试"),"No search before QR session");
   Go("平台");await Task.Delay(150);Program.Log("QR_UI_ENV",new{RenderScaling,ClientSize.Width,ClientSize.Height});handler.LoginState=1;
   ClickAccount("二维码登录");await Task.Delay(150);Check(kugouQrImage?.Source is Avalonia.Media.Imaging.Bitmap&&kugouQrImage.Bounds.Width>0,"Native window contains decoded offline QR image in actual account UI");
   ClickAccount("取消登录");await PendingCoreOperations;Check(!concept.State.LoggedIn&&kugouQrImage?.Source==null&&!kugouLoginRunning,"UI cancel clears QR and memory authorization");
   KugouQrDeadline=TimeSpan.FromMilliseconds(100);ClickAccount("二维码登录");await PendingCoreOperations;Check(!concept.State.LoggedIn&&!kugouLoginRunning&&kugouAccountState!.Text!.Contains("超时"),"UI timeout stops polling and clears transient authorization");
   KugouQrDeadline=TimeSpan.FromMinutes(2);handler.LoginState=4;ClickAccount("二维码登录");await PendingCoreOperations;
   Check(concept.State.LoggedIn&&kugouAccountState!.Text!.Contains("登录成功"),"UI QR login directly uses SDK LoginClient and same session");
   Check(!handler.Paths.Any(p=>p=="/v1/get_union_vip"||p.Contains("receive_vip")),"Login UI does not implicitly read rights or claim");
   ClickAccount("查询权益");await PendingCoreOperations;Check(kugouVipState!.Text!.Contains("ENTITLEMENT_ACTIVE=False"),"UI entitlement query uses SDK independently of login and song grant");
   ClickAccount("手动领取免费福利");await PendingCoreOperations;Check(!handler.Paths.Any(p=>p.Contains("receive_vip")),"Unchecked UI claim never sends a mutation");
   await Refuse<UnauthorizedAccessException>(()=>adapter.Kugou.ClaimTodayAsync(true),"Missing activity permission refuses before claim");
   Check(!handler.Paths.Any(x=>x.Contains("receive_vip")),"No automatic or unapproved VIP claim");
   ClickAccount("查询领取状态");await PendingCoreOperations;Check(handler.Paths.Any(p=>p.Contains("get_month_vip_record"))&&!handler.Paths.Any(p=>p.Contains("receive_vip")),"User-initiated claim status reads SDK history only, never claims");
   var claimPermit=new GatewayPermit{HumanApprovalReference="CONNECTION_TEST_ONLY",NotAfter=DateTimeOffset.UtcNow.AddMinutes(4),Operations=new HashSet<GatewayOperation>{GatewayOperation.QrCreate,GatewayOperation.QrPoll,GatewayOperation.ClaimHistory,GatewayOperation.Entitlement,GatewayOperation.ClaimDaily},MediaHosts=new HashSet<string>()};
   var rules=new ActivityApproval(DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime),"12345","OFFLINE_ACTIVITY_RULES",true,false);
   configuredData.Permit=claimPermit;configuredData.ManualClaim=new(){HumanApprovalReference="SEPARATE_CLAIM_TEST_ONLY",NotAfter=DateTimeOffset.UtcNow.AddMinutes(5),Activity=rules};
   StoreConfiguration();try{
    configuredData.ManualClaim.HumanApprovalReference=claimPermit.HumanApprovalReference;StoreConfiguration();await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect(_=>sdk)),"General connection approval cannot substitute independent claim approval");configuredData.ManualClaim.HumanApprovalReference="SEPARATE_CLAIM_TEST_ONLY";
    configuredData.ManualClaim.Activity=rules with{RequiresOfficialClientOrAd=true};StoreConfiguration();await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect(_=>sdk)),"Configuration cannot authorize invented official-client or ad completion");
    configuredData.ManualClaim.Activity=rules with{EligibilityConfirmed=false};StoreConfiguration();await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect(_=>sdk)),"Unknown activity eligibility fails before attaching claim service");
    configuredData.ManualClaim.Activity=rules;configuredData.ManualClaim.NotAfter=DateTimeOffset.UtcNow.AddMinutes(1);StoreConfiguration();await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect(_=>sdk)),"Independent claim approval must cover the full permitted connection lifetime");configuredData.ManualClaim.NotAfter=DateTimeOffset.UtcNow.AddMinutes(5);
    configuredData.Permit=new GatewayPermit{HumanApprovalReference="CONNECTION_TEST_ONLY",NotAfter=claimPermit.NotAfter,Operations=new HashSet<GatewayOperation>{GatewayOperation.ClaimDaily,GatewayOperation.Entitlement},MediaHosts=new HashSet<string>()};StoreConfiguration();await Refuse<UnauthorizedAccessException>(()=>Task.FromResult(configured.Connect(_=>sdk)),"Manual claim cannot omit required read-only history operation scope");configuredData.Permit=claimPermit;
    StoreConfiguration();configured.Connect(_=>sdk);Check(sdk.ApprovedActivity==rules,"Reviewed activity from Windows credential is attached to original IntegratedSdkGateway");
   }finally{CurrentUserKugouCredential.DeleteTest(configured.Target);}
   handler.ServerTimeOffset=-86400;await Refuse<GatewayFailure>(()=>adapter.Kugou.ClaimTodayAsync(true),"Existing server-day/clock check rejects stale history before mutation");handler.ServerTimeOffset=0;
   ClickAccount("手动领取免费福利");await PendingCoreOperations;Check(!handler.Paths.Any(p=>p.Contains("receive_vip")),"Attached approval still requires explicit UI confirmation on every click");
   var confirmation=this.GetVisualDescendants().OfType<Avalonia.Controls.CheckBox>().First(x=>x.Content?.ToString()?.Contains("我确认本次手动领取")==true);confirmation.IsChecked=true;
   ClickAccount("手动领取免费福利");await PendingCoreOperations;Check(handler.Paths.Count(p=>p.Contains("receive_vip"))==1&&handler.LastClaimSource==rules.SourceId&&kugouStatus!.Text!.Contains("CLAIM=ClaimSuccess")&&kugouStatus.Text.Contains("ENTITLEMENT_ACTIVE=False"),"UI manual claim reuses SDK, reports server confirmation separately from actual entitlement");
   confirmation.IsChecked=true;ClickAccount("手动领取免费福利");await PendingCoreOperations;Check(handler.Paths.Count(p=>p.Contains("receive_vip"))==1,"Existing outcome ledger prevents duplicate claim when server history lags");
   int approvedClaimCount=handler.Paths.Count(p=>p.Contains("receive_vip"));
   var search=await adapter.Kugou.SearchAsync("测试");Check(search.Tracks.Count==1&&search.Tracks[0].Title=="SDK 测试曲","Existing SearchClient builder and SongInfo parser used");
   Check(handler.LastSearchOwnIdentity&&!handler.LastSearchHasSession,"Own identity plus original SDK search filter; search gets no account token");
   var lists=await adapter.Kugou.ReadAsync("sdk-test-a");Check(lists.Count==1&&lists[0].Tracks.Single().ProviderTrackId==hash,"UserClient + PlaylistClient paged read shares QR account");
   Check(handler.PlaylistBodySharedSession&&handler.PlaylistSongsSharedSession,"SDK playlist index and song pages use the same QR session");
   Check(SnapshotSyntheticDbRows().SequenceEqual(dbBefore),"QR, SDK business calls and playlist sync leave SQLite unchanged");
   await Refuse<UnauthorizedAccessException>(()=>adapter.Kugou.ReadAsync("sdk-test-b"),"Cross-account read rejected");
   if(Environment.GetEnvironmentVariable("M207B_ART_SUITE")=="1"){await M207BArtworkTest(root,adapter,search.Tracks[0],lists);return;}
   if(Environment.GetEnvironmentVariable("M206B_RISK_SUITE")=="1"){await M206BRiskTest(adapter,()=>handler.Paths.Count);return;}
   if(Environment.GetEnvironmentVariable("M206A_EXPERIENCE_SUITE")=="1"){
    await M206AExperienceTest(root,adapter,search.Tracks[0],value=>handler.DenySong=value,()=>handler.Paths.Count);return;
   }
   handler.DenySong=true;var before=adapter.State.Queue.ToArray();
   await Refuse<GatewayFailure>(()=>adapter.AdmitKugouAsync(search.Tracks[0],"standard"),"Denied song source never admitted");
   Check(adapter.State.Queue.SequenceEqual(before)&&ProviderStreamRegistry.Count==0,"Denied permission leaves unique queue and Registry unchanged");handler.DenySong=false;
   if(mature){
    var low=await adapter.Kugou.ResolveAsync(hash,"flac");Check(low.Quality=="flac"&&low.EffectiveQuality=="320"&&handler.LastLegacyQuality=="320","KA quality downgrade reaches the module grant as effective 320, preserving requested flac");adapter.Kugou.ReleaseGrant(low);
    handler.Preview=true;await Refuse<GatewayFailure>(()=>adapter.AdmitKugouAsync(search.Tracks[0],"standard"),"Legacy preview cannot become full-song grant");handler.Preview=false;
    handler.OmitPermission=true;await Refuse<GatewayFailure>(()=>adapter.AdmitKugouAsync(search.Tracks[0],"standard"),"Legacy unknown permission cannot default to zero through SDK model");handler.OmitPermission=false;
    handler.LegacyHttpFailure=true;await Refuse<GatewayFailure>(()=>adapter.AdmitKugouAsync(search.Tracks[0],"standard"),"Legacy HTTP failure does not retry Auth or mutate account");handler.LegacyHttpFailure=false;
    Check(handler.Paths.Count(x=>x.Contains("receive_vip"))==approvedClaimCount&&!handler.Paths.Any(x=>x.Contains("upgrade_vip")||x.Contains("refresh")),"Playback and failure recovery never claim or refresh account");
    using(var cancel=new CancellationTokenSource()){cancel.Cancel();int previous=handler.Paths.Count;await Refuse<OperationCanceledException>(()=>concept.ResolveMatureSdkAsync(hash,ct:cancel.Token),"Canceled SDK resolution has no request");Check(handler.Paths.Count==previous,"Canceled resolution leaves transport untouched");}
    var absentIdentity=new AuthorizedIdentity{AppId="9000000000",QrIssuerAppId="9000000002",SourceAppId="9000000001",ClientVersion="1",PlatformPermissionReference="OFFLINE_ONLY",Dfid="-",Mid="TEST_MID",Uuid="TEST_UUID"};
    using var absentHandler=new SdkConnectionHandler();using var absent=new ConceptGateway("missing-device",absentIdentity,new AuthorizedHttpWire(new HttpClient(absentHandler),absentIdentity,permit,signing));await absent.BeginQrAsync();await absent.PollQrAsync();
    await Refuse<UnauthorizedAccessException>(()=>absent.ResolveMatureSdkAsync(hash),"Missing device identity refuses mature route before SDK random fallback");Check(absentHandler.Paths.Count==2,"Missing device makes zero privilege or URL requests");
   }
   int id=await adapter.AdmitKugouAsync(search.Tracks[0],"standard");
   Check(!adapter.State.Playing&&adapter.State.Tracks.Single(t=>t.Id==id).Title=="SDK 测试曲","Admitted metadata is not mislabeled fixture and does not autoplay");
   if(mature){
    Check(handler.Paths.Contains("/v2/get_res_privilege/lite")&&handler.Paths.Contains("/v5/url")&&!handler.Paths.Contains("/v1/user_verify")&&!handler.Paths.Contains("/v1/authorization")&&!handler.Paths.Contains("/tracker/v5/url"),"Mature SDK privilege and URL used, with all new Auth scopes absent");
    Check(handler.LegacyOwnIdentity&&handler.PrivilegeOwnIdentity,"SDK versions retained; only owned identity/device/key and approved product context");
    var capture=new OriginalSdkRequestCapture();var sdkSession=new KuGou.Net.Protocol.Session.KgSessionManager(new CookieContainer());sdkSession.Session.Dfid=identity.Dfid;
    var rawSdk=new KuGou.Net.Protocol.Raw.RawSongApi(capture,sdkSession);await rawSdk.GetUrlAsync(hash,"128","0","0",false);
    var original=capture.Last!;var excluded=new HashSet<string>{"clientver","pid","page_id","ppage_id"};
    Check(original.Params.Where(x=>!excluded.Contains(x.Key)).All(x=>handler.LastLegacyParameters!.GetValueOrDefault(x.Key)==x.Value),"Every non-identity/product V5 business parameter equals original SDK request");
    await rawSdk.GetPrivilegeLiteAsync(hash,"0");var originalBody=System.Text.Json.Nodes.JsonNode.Parse(KuGou.Net.util.SdkRequestSerialization.Serialize(capture.Last!.Body!,capture.Last.BodyTypeInfo))!.AsObject();
    var actualBody=System.Text.Json.Nodes.JsonNode.Parse(handler.LastPrivilegeBody)!.AsObject();foreach(var field in new[]{"appid","clientver"}){originalBody.Remove(field);actualBody.Remove(field);}
    Check(System.Text.Json.Nodes.JsonNode.DeepEquals(originalBody,actualBody),"Privilege body structure equals original SDK except declared owned app identity");
   }else{
    Check(handler.Paths.Contains("/v1/user_verify")&&handler.Paths.Contains("/v1/authorization")&&handler.Paths.Contains("/tracker/v5/url")&&!handler.Paths.Contains("/v5/url"),"Explicit Auth path retained, no fallback");
    Check(handler.CorrectAuthChain,"Account auth, song auth and open_time passed through unchanged");
   }
   await adapter.QueueAsync("append",id);await adapter.VolumeAsync(0);await adapter.PlayAsync(id);await Task.Delay(350);
   Check(root.Coordinator.Player.IsPlaying&&adapter.State.CurrentId==id,"SDK/Auth -> DLC -> Registry -> production BASS plays admitted synthetic WAV");
   var pos=root.Coordinator.Player.GetPosition().TotalSeconds;await Task.Delay(300);
   Check(root.Coordinator.Player.GetPosition().TotalSeconds>pos+.1&&root.Coordinator.Player.GetActiveAnalysisSnapshot().Rms>0,"Real decoder PCM and BASS audio clock, not a UI timer");
   Check(audioHandler.Calls==1&&audioHandler.NoAccountHeaders,"Single owned response stream; no account credentials sent to media host");
   await adapter.ToggleAsync(id);Check(root.Coordinator.Player.IsPaused,"Production pause");await adapter.SeekAsync(.7);Check(Math.Abs(root.Coordinator.Player.GetPosition().TotalSeconds-.7)<.2,"Production stream Seek while paused");await adapter.ToggleAsync(id);Check(root.Coordinator.Player.IsPlaying,"Production resume");
   Check(adapter.State.Lyrics.Any(l=>l.Text.Contains("SDK")),"Direct LyricClient LRC reaches existing lyric parser");
   handler.ExpireAccount=true;await Refuse<GatewayFailure>(()=>adapter.Kugou.RefreshEntitlementAsync(),"HTTP 401 invalidates account without retry");
   Check(!root.Coordinator.Player.IsPlaying&&ProviderStreamRegistry.Count==0&&!audioHandler.OpenStream!.CanRead,"Session expiry revokes Registry, stops BASS and disposes response");
   Check(adapter.Kugou.Cached("sdk-test-a").Count==1,"Non-secret account metadata survives revocation");
   ClickAccount("退出账号");await PendingCoreOperations;Check(!adapter.Kugou.LoggedIn,"UI logout clears SDK session");
   ClickAccount("停用 DLC");await PendingCoreOperations;Check(!adapter.Providers.IsEnabled(KugouProvider.Identity),"Existing lifecycle disables integrated module");
   var local=adapter.State.Tracks.First(t=>t.Id<1_000_000).Id;await adapter.PlayAsync(local);Check(root.Coordinator.Player.IsPlaying,"Local BASS playback independent of SDK transport failure");await adapter.StopAsync();
   var protectedBefore=dbBefore.Where(x=>!x.StartsWith("kv:")||x.StartsWith("kv:session\u001f")||x.StartsWith("kv:favorite_like_cache\u001f")).ToArray();
   var protectedAfter=SnapshotSyntheticDbRows().Where(x=>!x.StartsWith("kv:")||x.StartsWith("kv:session\u001f")||x.StartsWith("kv:favorite_like_cache\u001f")).ToArray();
   Check(protectedAfter.SequenceEqual(protectedBefore),"Playback leaves library/playlists and legacy account/favorite sentinels unchanged; local history/settings may update");
   adapter.RemoveKugou();Check(!adapter.Providers.IsEnabled(KugouProvider.Identity),"Integrated module removal works without local data deletion");
   Check(OfflineStartupNetwork.Requests==networkBefore,"No production network transport reached; injected handlers only");
   Go("平台");await Task.Delay(100);Program.Log("SDK_COMPLETE",new{checks,path=mature?"MatureSdk":"ConceptAuth",realPlatform="NOT_EXECUTED",audio="TERMINAL_RESPONSE_SYNTHETIC_WAV_REAL_BASS",human="PENDING"});
  }catch(Exception e){Program.Result=2;Program.Log("SDK_FAILED",e.ToString());}finally{Close();}
 }

 sealed class OriginalSdkRequestCapture:KuGou.Net.Infrastructure.Http.IKgTransport {
  public KuGou.Net.Protocol.Transport.KgRequest? Last;
  public Task<JsonElement> SendAsync(KuGou.Net.Protocol.Transport.KgRequest request){Last=request;return Task.FromResult(JsonSerializer.SerializeToElement(new{status=1}));}
 }
 sealed class SdkConnectionHandler:HttpMessageHandler {
  public readonly List<string> Paths=new();public bool DenySong,ExpireAccount,LastSearchOwnIdentity,LastSearchHasSession,PlaylistBodySharedSession,PlaylistSongsSharedSession,CorrectAuthChain,Mature,LegacyOwnIdentity,PrivilegeOwnIdentity,Preview,OmitPermission,LegacyHttpFailure;public string LastClaimSource="";public int ServerTimeOffset;public string LastLegacyQuality="";public int LoginState=4;public Dictionary<string,string>? LastLegacyParameters;public string LastPrivilegeBody="";
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct){
   ct.ThrowIfCancellationRequested();string path=req.RequestUri!.AbsolutePath;Paths.Add(path);string query=Uri.UnescapeDataString(req.RequestUri.Query);
   var parameters=req.RequestUri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Split('=',2)).ToDictionary(x=>Uri.UnescapeDataString(x[0]),x=>Uri.UnescapeDataString(x.Length>1?x[1]:""));
   if(ExpireAccount&&path=="/v1/get_union_vip")return new(HttpStatusCode.Unauthorized);
   if(LegacyHttpFailure&&path=="/v5/url")return new(HttpStatusCode.Forbidden);
   object data=path switch {
    "/v2/qrcode"=>new {status=1,data=new{qrcode="SDK_TEST_QR",qrcode_img="data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(Path.Combine(Program.Root,"src","Assets","offline-test-qr.png")))}},
    "/v2/get_userinfo_qrcode"=>new{status=1,data=new{status=LoginState,userid=7,token=LoginState==4?"SDK_TEST_NOT_A_CREDENTIAL":""}},
    "/youth/v1/activity/get_month_vip_record"=>new{status=1,data=new{server_time=DateTimeOffset.UtcNow.AddSeconds(ServerTimeOffset).ToUnixTimeSeconds(),list=Array.Empty<object>()}},
    "/youth/v1/recharge/receive_vip_listen_song"=>new{status=1,data=new{ad_vip_num=1}},
    "/v1/get_union_vip"=>new{status=1,data=new{is_vip=0,busi_vip=Array.Empty<object>()}},
    "/v3/search/song"=>new{status=1,data=new{total=1,lists=new[]{new{FileHash=new string('a',32),FileName="SDK 测试曲",SingerName="自产测试",AlbumName="隔离媒体",AlbumID=Environment.GetEnvironmentVariable("M207B_ART_SUITE")=="1"?"17":"0",Image=Environment.GetEnvironmentVariable("M207B_ART_SUITE")=="1"?"https://imgessl.kugou.com/song/{size}/a.png":null,Duration=12}}}},
    "/v7/get_all_list"=>new{status=1,data=new{userid=7,list_count=1,info=new[]{new{name="我喜欢 · 测试",global_collection_id="test-list",count=1,is_def=2,pic=Environment.GetEnvironmentVariable("M207B_ART_SUITE")=="1"?"https://imgessl.kugou.com/list/{size}/a.png":null}}}},
    "/pubsongs/v2/get_other_list_file_nofilt"=>new{status=1,data=new{count=1,songs=new[]{new{name="SDK 测试曲",hash=new string('a',32),timelen=12000,album_id=Environment.GetEnvironmentVariable("M207B_ART_SUITE")=="1"?"17":"0",cover=Environment.GetEnvironmentVariable("M207B_ART_SUITE")=="1"?"https://imgessl.kugou.com/song/{size}/a.png":null,albuminfo=new{name="隔离媒体"}}}}},
    "/v2/get_res_privilege/lite"=>new{status=1,data=new[]{new{hash=new string('a',32),quality="128",relate_goods=new[]{new{quality="320"}}}}},
    "/v5/url"=>OmitPermission?(object)new{status=1,hash=new string('a',32),url=new[]{"https://media.example.test/test.wav"}}:new{status=1,hash=new string('a',32),priv_status=DenySong?1:0,is_free_part=Preview?1:0,url=new[]{"https://media.example.test/test.wav"}},
    "/v1/user_verify"=>new{status=1,data=new{auth="SDK_USER_AUTH"}},
    "/v1/authorization"=>new{status=1,data=new{auth="SDK_SONG_AUTH",open_time=1234567}},
    "/tracker/v5/url"=>new{status=1,data=new{hash=new string('a',32),priv_status=DenySong?1:0,url=new[]{"https://media.example.test/test.wav"}}},
    "/v1/search"=>new{status=200,candidates=new[]{new{id="test-lrc",accesskey="TEST_LRC_KEY"}}},
    "/download"=>new{status=200,content=Convert.ToBase64String(Encoding.UTF8.GetBytes("[00:00.00]SDK 复用歌词\n[00:02.00]真实生产解析器")),contenttype=1},
    _=>throw new UnauthorizedAccessException("Unexpected request in finite connection test")
   };
   if(path.Contains("receive_vip"))LastClaimSource=parameters.GetValueOrDefault("source_id","");
   if(path=="/v5/url"){LastLegacyParameters=parameters;LastLegacyQuality=query.Contains("quality=320")?"320":query.Contains("quality=flac")?"flac":"128";LegacyOwnIdentity=query.Contains("appid=9000000000")&&query.Contains("dfid=OFFLINE_TEST_DEVICE")&&query.Contains("userid=7")&&query.Contains("token=SDK_TEST_NOT_A_CREDENTIAL")&&parameters.GetValueOrDefault("version")=="11430"&&parameters.GetValueOrDefault("pidversion")=="3001"&&!parameters.ContainsKey("srcappid")&&!parameters.ContainsKey("pid")&&!parameters.ContainsKey("page_id")&&!parameters.ContainsKey("ppage_id")&&query.Contains("key="+KuGou.Net.util.KgUtils.Md5(new string('a',32)+"TEST_MEDIA_ONLY"+"9000000000"+"OFFLINE_TEST_MID"+"7"));}
   if(path=="/v2/get_res_privilege/lite"){var body=await req.Content!.ReadAsStringAsync(ct);LastPrivilegeBody=body;PrivilegeOwnIdentity=body.Contains("9000000000")&&body.Contains("\"clientver\":\"1\"");}
   if(path=="/v3/search/song"){LastSearchOwnIdentity=query.Contains("appid=9000000000")&&query.Contains("platform=AndroidFilter");LastSearchHasSession=query.Contains("token=")||query.Contains("userid=");}
   if(path=="/pubsongs/v2/get_other_list_file_nofilt")PlaylistSongsSharedSession=query.Contains("userid=7")&&query.Contains("token=SDK_TEST_NOT_A_CREDENTIAL");
   if(path=="/v7/get_all_list"){var body=await req.Content!.ReadAsStringAsync(ct);PlaylistBodySharedSession=body.Contains("SDK_TEST_NOT_A_CREDENTIAL")&&body.Contains("userid");}
   if(path=="/tracker/v5/url")CorrectAuthChain=query.Contains("auth=SDK_SONG_AUTH")&&query.Contains("open_time=1234567");
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
  }
 }
 sealed class SdkConnectionAudioHandler(string path):HttpMessageHandler {
  public int Calls;public bool NoAccountHeaders;public Stream? OpenStream;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
   ct.ThrowIfCancellationRequested();if(request.RequestUri?.Host!="media.example.test")throw new UnauthorizedAccessException("Unexpected test origin");
   OfflineStartupProfile.Current.DemandMedia(path);Calls++;NoAccountHeaders=!request.Headers.Contains("Cookie")&&!request.Headers.Contains("Authorization")&&!request.RequestUri.Query.Contains("token");
   var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);long length=file.Length;
   OpenStream=new NonSeekingInput(file);var content=new StreamContent(OpenStream);content.Headers.ContentType=new("audio/wav");content.Headers.ContentLength=length;
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
  }
 }
 // Real HttpContent response streams generally cannot seek. Exercise that shape, not a convenient FileStream.
 sealed class NonSeekingInput(Stream inner):Stream {
  public override bool CanRead=>inner.CanRead;public override bool CanSeek=>false;public override bool CanWrite=>false;
  public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
  public override int Read(byte[] b,int o,int c)=>inner.Read(b,o,c);
  public override int Read(Span<byte> b)=>inner.Read(b);
  public override ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken ct=default)=>inner.ReadAsync(b,ct);
  public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
  public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush(){}
  protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
 }
}
