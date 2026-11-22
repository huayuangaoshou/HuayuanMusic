using HuayuanMusic.Provider.Contracts;

namespace HuayuanMusic.Provider.Kugou;

public enum ClaimState { NotRequested, AlreadyReceived, ClaimSuccess, Rejected, OutcomeUnknown }
public sealed record Entitlement(bool Active, string Product, DateTimeOffset? Until, string Evidence);
public sealed record DailyOffer(DateOnly ServerDay, bool Received, bool Eligible, bool RulesConfirmed, bool RequiresOfficialClientOrAd);
public sealed record ClaimReceipt(ClaimState State, Entitlement Entitlement);
public sealed record CatalogTrack(string Id,string Title,string Artist,string Album,int Seconds,long AlbumId=0,long AlbumAudioId=0,string? Cover=null);
public sealed record SearchPage(int Page,IReadOnlyList<CatalogTrack> Tracks,bool More);
public sealed record MediaPermission(bool Allowed,string Quality,string Reason,string? EffectiveQuality=null);
// Grant identity is account/session scoped. Never persist a bearer URL in tracks or queue cache.
public sealed record MediaGrant(string Account,long Generation,string TrackId,string Quality,Guid Id,DateTimeOffset Expires,string? EffectiveQuality=null);
public sealed record GrantSource(MediaGrant Grant,Uri Address);

public interface IKugouGateway : IDisposable {
 bool IsFixture {get;}
 Task<Entitlement> EntitlementAsync(CancellationToken ct);
 Task<DailyOffer> OfferAsync(CancellationToken ct);
 Task<bool> ClaimAsync(CancellationToken ct);
 Task<SearchPage> SearchAsync(string keyword,int page,int size,CancellationToken ct);
 Task<MediaPermission> PermissionAsync(string track,string quality,CancellationToken ct);
 Task<Uri> ResolveAsync(string track,string quality,CancellationToken ct);
 Task<string> LyricsAsync(string track,CancellationToken ct);
 Task<IReadOnlyList<ProviderPlaylist>> PlaylistsAsync(string account,CancellationToken ct);
}

/// <summary>Independent orchestration. No player, queue, database, filesystem or network ownership.</summary>
public sealed class KugouProvider : IProviderModule, IProviderPlaylistReader {
 public const string Identity="kugou-concept";
 public string ProviderId=>Identity;
 public ProviderCapability Capabilities=>ProviderCapability.PlaylistRead|ProviderCapability.MediaResolve;
 public IProviderPlaylistReader PlaylistReader=>this;
 readonly Func<string,IKugouGateway>? fixtureFactory;
 readonly SemaphoreSlim serial=new(1,1);
 readonly Dictionary<string,IReadOnlyList<ProviderPlaylist>> cache=new(StringComparer.Ordinal);
 readonly System.Collections.Concurrent.ConcurrentDictionary<Guid,GrantSource> grants=new();
 IKugouGateway? gateway;string? account;long generation;bool enabled,disposed;
 ClaimState claim;DateOnly? claimDay;
 public string? Account=>account;
 public long Generation=>generation;
 public bool Enabled=>enabled&&!disposed;
 public bool AutoClaimEnabled=>false; // No confirmed current rules permitting automation.
 public bool FixtureOnly=>gateway is not IRealSdkGateway;
 public bool LoggedIn=>gateway is IRealSdkGateway sdk?sdk.LoggedIn:account!=null;
 public bool SdkConnected=>gateway is IRealSdkGateway;
 public bool IsDirectSdk=>gateway is DirectSdkGateway;
 // Uses the mounted R5 account, existing UserClient and the same account
 // partition as saved playlists. Preview reads history and durable ledger only;
 // no claim transport is constructed or dispatched by this method.
 public Task<ConceptVipClaimPreview> PreviewConceptClaimAsync(HuayuanMusic.Kugou.Gateway.ActivityApproval? reviewed,
     string isolatedLedgerRoot,CancellationToken ct=default)=>
     Run(g=>g is DirectSdkGateway direct
         ?direct.PreviewClaimAsync(reviewed,isolatedLedgerRoot,ct)
         :throw new NotSupportedException("当前 SDK 未提供单会话领取预检"),ct);
 public long RealSongSourceRequestCount=>gateway is DirectSdkGateway sdk?sdk.SourceRequestCount:0;
 public void CancelPlaylistDetails(){if(gateway is DirectSdkGateway direct)direct.CancelPlaylistDetails();}
 public string? DirectAccountCacheKey=>gateway is DirectSdkGateway sdk?sdk.AccountCacheKey:null;
 public CatalogTrack? CatalogTrackForPlaylist(string id)=>
     gateway is DirectSdkGateway sdk?sdk.CatalogTrackForPlaylist(id):null;
 public void SaveDirectLogin(){if(gateway is not DirectSdkGateway direct)throw new UnauthorizedAccessException("请先连接成熟 SDK 并登录");direct.SaveCurrentSession();}
 public void DeleteStoredDirectLogin(){DirectSdkCredentialStore.Delete();}
 public bool ImportVerifiedProbeLogin(){if(gateway is DirectSdkGateway connected&&connected.LoggedIn)throw new InvalidOperationException("集成版当前已登录，不需要导入；如需更换账号先退出");bool imported=DirectSdkCredentialStore.ImportFromVerifiedProbeOnce();if(imported&&gateway is DirectSdkGateway direct)direct.AdoptImportedSession();return imported;}
 public event Action? Revoked;
 public KugouProvider(Func<string,IKugouGateway>? fixtureFactory=null){this.fixtureFactory=fixtureFactory;}
 void Mounted(){ObjectDisposedException.ThrowIf(disposed,this);if(!enabled)throw new InvalidOperationException("Module disabled");}
 IKugouGateway Demand(){Mounted();return gateway??throw new UnauthorizedAccessException("Login required");}
 void Revoke(){generation++;grants.Clear();Revoked?.Invoke();}
 public void Enable(){ObjectDisposedException.ThrowIf(disposed,this);enabled=true;}
 // Synchronous lifecycle transitions are serialized against active operations, never bypassing cancellation.
 public async Task LoginFixtureAsync(string alias,CancellationToken ct=default){
  await serial.WaitAsync(ct);try{Mounted();if(alias is not ("demo-a" or "demo-b"))throw new UnauthorizedAccessException("Unknown fixture account");
   var next=(fixtureFactory??throw new InvalidOperationException("Fixture login is unavailable"))(alias);if(!next.IsFixture){next.Dispose();throw new UnauthorizedAccessException("Real platform transport is not authorized in this candidate");}
   Revoke();DisposeGateway();gateway=next;account=alias;claim=ClaimState.NotRequested;claimDay=null;
  }finally{serial.Release();}
 }
 public async Task ConnectSdkAsync(IRealSdkGateway next,CancellationToken ct=default){
  await serial.WaitAsync(ct);try{Mounted();if(gateway!=null)throw new InvalidOperationException("Logout before attaching another account");Revoke();gateway=next;account=next.Alias;next.Revoked+=Revoke;}finally{serial.Release();}
 }
 public Task CancelQrAsync(CancellationToken ct=default)=>Run(async g=>{if(g is not IRealSdkGateway sdk)throw new UnauthorizedAccessException("SDK connection absent");await sdk.CancelLoginAsync(ct);return 0;},ct);
 public Task<HuayuanMusic.Kugou.Gateway.QrChallenge> BeginQrAsync(CancellationToken ct=default)=>Run(g=>g is IRealSdkGateway sdk?sdk.BeginQrAsync(ct):throw new UnauthorizedAccessException("Approved SDK/Auth gateway not configured"),ct);
 public Task<KuGou.Net.Abstractions.Models.QrLoginStatus> PollQrAsync(CancellationToken ct=default)=>Run(g=>g is IRealSdkGateway sdk?sdk.PollQrAsync(ct):throw new UnauthorizedAccessException("Approved SDK/Auth gateway not configured"),ct);
 void DisposeGateway(){if(gateway is IRealSdkGateway sdk)sdk.Revoked-=Revoke;gateway?.Dispose();gateway=null;}
 public async Task LogoutAsync(CancellationToken ct=default){await serial.WaitAsync(ct);try{Revoke();DisposeGateway();account=null;claim=ClaimState.NotRequested;claimDay=null;}finally{serial.Release();}}
 public async Task DisableAsync(CancellationToken ct=default){await serial.WaitAsync(ct);try{enabled=false;Revoke();DisposeGateway();account=null;claim=ClaimState.NotRequested;claimDay=null;}finally{serial.Release();}}
 async Task<T> Run<T>(Func<IKugouGateway,Task<T>> action,CancellationToken ct){await serial.WaitAsync(ct);try{ct.ThrowIfCancellationRequested();return await action(Demand());}finally{serial.Release();}}
 public Task<DirectSdkGateway.TargetOccurrenceProof> ReadTargetOccurrenceAsync(CancellationToken ct=default)=>
     Run(g=>g is DirectSdkGateway direct?direct.ReadTargetOccurrenceAsync(Generation,ct):
         throw new UnauthorizedAccessException("Target SDK absent"),ct);
 public bool MatchesTarget(DirectSdkGateway.TargetOccurrenceProof proof)=>
     Enabled&&gateway is DirectSdkGateway direct&&direct.MatchesTarget(proof,Generation);
 public Task<bool> ConsumeTargetAsync(DirectSdkGateway.TargetOccurrenceProof proof,CancellationToken ct)=>
     Run(g=>Task.FromResult(g is DirectSdkGateway direct&&direct.ConsumeTarget(proof,Generation)),ct);
 public Task<Entitlement> RefreshEntitlementAsync(CancellationToken ct=default)=>Run(g=>g.EntitlementAsync(ct),ct);
 public Task<KuGou.Net.Abstractions.Models.VipReceiveHistoryResponse> ReadClaimHistoryAsync(CancellationToken ct=default)=>Run(g=>g is IRealSdkGateway sdk?sdk.ReadClaimHistoryAsync(ct):throw new UnauthorizedAccessException("SDK connection absent"),ct);
 public Task<ClaimReceipt> ClaimTodayAsync(bool explicitUserIntent,CancellationToken ct=default)=>Run(async g=>{
  if(g is IRealSdkGateway integrated)return await integrated.ClaimTodayAsync(explicitUserIntent,ct);
  if(!explicitUserIntent)throw new UnauthorizedAccessException("Manual confirmation required");
  var offer=await g.OfferAsync(ct);var before=await g.EntitlementAsync(ct);
  if(claimDay!=offer.ServerDay){claim=ClaimState.NotRequested;claimDay=offer.ServerDay;}
  if(offer.Received)return new ClaimReceipt(ClaimState.AlreadyReceived,before);
  if(claim is ClaimState.OutcomeUnknown or ClaimState.ClaimSuccess)return new ClaimReceipt(claim,before);
  if(!offer.Eligible||!offer.RulesConfirmed||offer.RequiresOfficialClientOrAd)return new ClaimReceipt(ClaimState.Rejected,before);
  // Once dispatched, interruption is UNKNOWN, not a safe retry. No blind POST retries or upgrade/ad fabrication.
  claim=ClaimState.OutcomeUnknown;
  try{claim=await g.ClaimAsync(ct)?ClaimState.ClaimSuccess:ClaimState.Rejected;}
  catch(OperationCanceledException){return new ClaimReceipt(claim,before);}
  catch(TimeoutException){return new ClaimReceipt(claim,before);}
  var after=await g.EntitlementAsync(ct);return new ClaimReceipt(claim,after);
 },ct);
 public Task<SearchPage> SearchAsync(string keyword,int page=1,int size=20,CancellationToken ct=default){
  if(string.IsNullOrWhiteSpace(keyword)||keyword.Length>120||page<1||page>1000||size<1||size>50)throw new ArgumentException("Invalid search bounds");
  return Run(g=>g.SearchAsync(keyword.Trim(),page,size,ct),ct);
 }
 public Task<MediaPermission> PermissionAsync(string track,string quality,CancellationToken ct=default)=>Run(g=>g.PermissionAsync(track,quality,ct),ct);
 public Task<MediaGrant> ResolveAsync(string track,string quality,CancellationToken ct=default)=>Run(async g=>{
  if(grants.Count>=32)throw new InvalidOperationException("Grant capacity reached; release unused media");
  var p=await g.PermissionAsync(track,quality,ct);if(!p.Allowed||p.Quality!=quality)throw new UnauthorizedAccessException("Playback not authorized for requested quality");
  var address=await g.ResolveAsync(track,quality,ct);
  // This shipping research candidate has zero real sockets. No permissive CDN allowlist is invented.
  if(g is IRealSdkGateway sdk){if(!sdk.Allows(address))throw new UnauthorizedAccessException("Unapproved gateway handle");}
  else if(address.Scheme!="https"||address.Host!="audio.fixture.invalid"||address.Port!=443||!string.IsNullOrEmpty(address.UserInfo)||!string.IsNullOrEmpty(address.Fragment))throw new UnauthorizedAccessException("Unapproved media origin");
  if(grants.Count>=32)throw new InvalidOperationException("Grant capacity reached; revoke or release unused media");
  var grant=new MediaGrant(account!,generation,track,quality,Guid.NewGuid(),DateTimeOffset.UtcNow.AddMinutes(2),p.EffectiveQuality);if(!grants.TryAdd(grant.Id,new(grant,address)))throw new InvalidOperationException("Grant collision");return grant;
 },ct);
 public GrantSource DemandGrant(MediaGrant grant){Mounted();if(account!=grant.Account||generation!=grant.Generation||grant.Expires<=DateTimeOffset.UtcNow||!grants.TryGetValue(grant.Id,out var source)||source.Grant!=grant||(gateway is IRealSdkGateway sdk&&!sdk.Allows(source.Address)))throw new UnauthorizedAccessException("Expired, foreign or revoked grant");return source;}
 public void ReleaseGrant(MediaGrant grant){if(grants.TryRemove(grant.Id,out var source)&&gateway is IRealSdkGateway sdk)sdk.Release(source.Address);}
 public Task<HuayuanMusic.Kugou.Gateway.OpenedAudio> OpenSdkAudioAsync(MediaGrant grant,CancellationToken ct=default){var source=DemandGrant(grant);return gateway is IRealSdkGateway sdk?sdk.OpenAsync(source.Address,ct):throw new UnauthorizedAccessException("Not an SDK audio grant");}
 public Task<string> LyricsAsync(string track,CancellationToken ct=default)=>Run(g=>g.LyricsAsync(track,ct),ct);
 public Task<IReadOnlyList<ProviderPlaylist>> ReadAsync(string alias,CancellationToken cancellationToken=default)=>Run(async g=>{
  if(alias!=account)throw new UnauthorizedAccessException("Account mismatch");
  if(!cache.ContainsKey(alias)&&cache.Count>=8)throw new InvalidOperationException("Account metadata cache capacity reached");
  var lists=await g.PlaylistsAsync(alias,cancellationToken);
  if(lists.Count>200||lists.Any(p=>p.ProviderId!=Identity||p.Tracks.Count>5000||p.Tracks.Any(t=>t.ProviderId!=Identity)))throw new InvalidOperationException("Invalid provider metadata");
  var copy=lists.Select(p=>p with{Tracks=Array.AsReadOnly(p.Tracks.ToArray())}).ToArray();cache[alias]=Array.AsReadOnly(copy);return cache[alias];
 },cancellationToken);
 public IReadOnlyList<ProviderPlaylist> Cached(string alias)=>cache.TryGetValue(alias,out var value)?value:Array.Empty<ProviderPlaylist>();
 public void Dispose(){if(disposed)return;disposed=true;enabled=false;Revoke();DisposeGateway();account=null;}
}
