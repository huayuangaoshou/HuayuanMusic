using System.Net;
using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;

namespace HuayuanMusic.Kugou.Gateway;

public sealed record AccountState(string Alias,bool LoggedIn,long Generation);
public sealed class QrChallenge(Uri displayUrl,byte[]? imageBytes=null){public byte[]? ImageBytes=>imageBytes;public Uri DisplayUrl=>displayUrl;public override string ToString()=>"QrChallenge [transient; render to user only]";}
public enum ClaimAcknowledgement { Confirmed, Rejected, AlreadyReceived, Unknown }
public sealed record ActivityApproval(DateOnly Day,string SourceId,string RulesReference,bool EligibilityConfirmed,bool RequiresOfficialClientOrAd);
public sealed record ClaimResult(ClaimAcknowledgement Claim,UserVipResponse? Entitlement);
public sealed class PlaybackGrant {
 internal readonly Guid Id=Guid.NewGuid();internal readonly string AccountId,Hash;internal readonly long Generation;internal readonly Uri Address;internal readonly DateTimeOffset NotAfter;internal readonly GatewayOperation SourceOperation;
 readonly CancellationTokenSource lifetime=new();int revoked;
 internal CancellationToken Revocation=>lifetime.Token;
 internal void Revoke(){if(Interlocked.Exchange(ref revoked,1)!=0)return;lifetime.Cancel();lifetime.Dispose();}
 public string Quality{get;}
 internal PlaybackGrant(string account,long generation,string hash,string quality,Uri address,DateTimeOffset deadline,GatewayOperation sourceOperation=GatewayOperation.ResolveAudio){SourceOperation=sourceOperation;AccountId=account;Generation=generation;Hash=hash;Quality=quality;Address=address;NotAfter=deadline;}
 public override string ToString()=>"PlaybackGrant [account/track scoped; signed URL redacted]";
}

/// <summary>Real protocol adapter. No mock service, UI, audio engine, filesystem or platform private-session reader.</summary>
public sealed partial class ConceptGateway : IDisposable,IAsyncDisposable {
 readonly IConceptWire wire;readonly AuthorizedIdentity identity;readonly SemaphoreSlim gate=new(1,1);KgSessionManager session=new(new CookieContainer());readonly TimeProvider clock;
 readonly string alias;long generation;volatile bool disposed;string? pendingQr;DateTimeOffset lastPoll;readonly Dictionary<(string,DateOnly),ClaimAcknowledgement> claims=new();
 readonly System.Collections.Concurrent.ConcurrentDictionary<Guid,PlaybackGrant> grants=new();
 public AccountState State=>new(alias,!string.IsNullOrEmpty(session.Session.Token),generation);
 public ConceptGateway(string accountAlias,AuthorizedIdentity identity,IConceptWire wire,TimeProvider? clock=null){if(string.IsNullOrWhiteSpace(accountAlias)||accountAlias.Length>80)throw new ArgumentException("Account alias required");identity.Validate();this.identity=identity;this.wire=wire;alias=accountAlias;this.clock=clock??TimeProvider.System;}
 public event Action? AuthorizationRevoked;public int RevocationNotificationFailures{get;private set;}
 void NotifyRevocation(){foreach(var callback in AuthorizationRevoked?.GetInvocationList()??[])try{((Action)callback)();}catch{RevocationNotificationFailures++;}}
 void ClearSession(){foreach(var grant in grants.Values)grant.Revoke();session=new KgSessionManager(new CookieContainer());pendingQr=null;generation++;grants.Clear();NotifyRevocation();}
 void DemandLogin(){if(!State.LoggedIn)throw new UnauthorizedAccessException("Account authorization required");}
 async Task<T> Run<T>(Func<Task<T>> action,CancellationToken ct){await gate.WaitAsync(ct);try{ObjectDisposedException.ThrowIf(disposed,this);ct.ThrowIfCancellationRequested();return await action();}catch(GatewayFailure e)when(e.Code=="ACCOUNT_EXPIRED"){ClearSession();throw;}finally{gate.Release();}}
 async Task<JsonElement> Send(GatewayOperation op,KgRequest request,CancellationToken ct){var raw=await wire.SendAsync(op,request,session.Session.UserId,session.Session.Token,ct);CheckSuccess(raw);return raw;}
 public static void CheckSuccess(JsonElement raw){if(!raw.TryGetProperty("status",out var s)||s.ValueKind!=JsonValueKind.Number||s.GetInt32()!=1)throw new GatewayFailure("API_REJECTED");foreach(var name in new[]{"error_code","errcode","err_code"})if(raw.TryGetProperty(name,out var e)&&e.ValueKind==JsonValueKind.Number&&e.GetInt32()!=0)throw new GatewayFailure("API_"+e.GetInt32());}
 public static JsonElement Data(JsonElement raw)=>raw.TryGetProperty("data",out var d)&&d.ValueKind==JsonValueKind.Object?d:raw;
 static string Required(JsonElement raw,string name){if(!raw.TryGetProperty(name,out var v)||v.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(v.GetString()))throw new GatewayFailure("MISSING_"+name.ToUpperInvariant());return v.GetString()!;}
 public Task<QrChallenge> BeginQrAsync(CancellationToken ct=default)=>Run(async()=>{
  if(State.LoggedIn)throw new InvalidOperationException("Logout explicitly before changing accounts");
  var qr=await SdkLogin(ct).GetQrCodeAsync()??throw new GatewayFailure("INVALID_QR_RESPONSE");var key=qr.Qrcode;
  if(string.IsNullOrWhiteSpace(key)||key.Length>512||key.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))throw new GatewayFailure("INVALID_QR_KEY");
  var image=await ReadQrImage(qr.QrcodeImg,ct);pendingQr=key;lastPoll=DateTimeOffset.MinValue;return new QrChallenge(new Uri("https://h5.kugou.com/apps/loginQRCode/html/index.html?appid="+Uri.EscapeDataString(identity.AppId)+"&qrcode="+Uri.EscapeDataString(key)),image);
 },ct);
 public Task<QrLoginStatus> PollQrAsync(CancellationToken ct=default)=>Run(async()=>{
  if(pendingQr==null)throw new InvalidOperationException("Start a QR authorization first");
  if(clock.GetUtcNow()-lastPoll<TimeSpan.FromSeconds(2))throw new InvalidOperationException("QR polling rate limited");lastPoll=clock.GetUtcNow();
  var parsed=await SdkLogin(ct).CheckQrStatusAsync(pendingQr)??throw new GatewayFailure("INVALID_QR_RESPONSE");
  if(ct.IsCancellationRequested){ClearSession();ct.ThrowIfCancellationRequested();}
  if(parsed.Status==null||!Enum.IsDefined((QrLoginStatus)parsed.Status.Value))throw new GatewayFailure("UNKNOWN_QR_STATE");
  var state=(QrLoginStatus)parsed.Status.Value;
  if(state==QrLoginStatus.Success){if(parsed.UserId is not >0||string.IsNullOrWhiteSpace(parsed.Token)){ClearSession();throw new GatewayFailure("INVALID_ACCOUNT_RESPONSE");}generation++;grants.Clear();pendingQr=null;}
  else if(state==QrLoginStatus.Expired)pendingQr=null;
  return state;
 },ct);
 public Task LogoutAsync(CancellationToken ct=default)=>Run(()=>{ClearSession();return Task.FromResult(0);},ct);
 async Task<UserVipResponse> Entitlement(CancellationToken ct){DemandLogin();return await new KuGou.Net.Clients.UserClient(new KuGou.Net.Protocol.Raw.RawUserApi(Sdk(ct)),session).GetVipInfoAsync()??throw new GatewayFailure("INVALID_ENTITLEMENT");}
 public Task<UserVipResponse> ReadEntitlementAsync(CancellationToken ct=default)=>Run(()=>Entitlement(ct),ct);
 async Task<VipReceiveHistoryResponse> History(CancellationToken ct){DemandLogin();return await new KuGou.Net.Clients.UserClient(new KuGou.Net.Protocol.Raw.RawUserApi(Sdk(ct)),session).GetVipRecordAsync()??throw new GatewayFailure("INVALID_HISTORY");}
 public Task<VipReceiveHistoryResponse> ReadClaimHistoryAsync(CancellationToken ct=default)=>Run(()=>History(ct),ct);
 public Task<ClaimResult> ClaimTodayAsync(ActivityApproval rules,bool explicitUserClick,CancellationToken ct=default)=>Run(async()=>{
  DemandLogin();if(!explicitUserClick||!rules.EligibilityConfirmed||rules.RequiresOfficialClientOrAd||string.IsNullOrWhiteSpace(rules.RulesReference)||!rules.SourceId.All(char.IsAsciiDigit)||rules.SourceId.Length==0)throw new UnauthorizedAccessException("Confirmed activity rules, eligibility and manual claim required");
  var history=await History(ct);if(history.ServerTime<=0)throw new GatewayFailure("SERVER_DAY_UNAVAILABLE");
  var serverNow=DateTimeOffset.FromUnixTimeSeconds(history.ServerTime);if(Math.Abs((clock.GetUtcNow()-serverNow).TotalMinutes)>10)throw new GatewayFailure("SERVER_CLOCK_MISMATCH");
  var day=DateOnly.FromDateTime(serverNow.ToOffset(TimeSpan.FromHours(8)).DateTime);if(day!=rules.Day)throw new UnauthorizedAccessException("Activity day differs from server day");
  var key=(session.Session.UserId,day);
  if(history.Items.Any(x=>x.Day==day.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)&&x.IsReceived==1))return new ClaimResult(ClaimAcknowledgement.AlreadyReceived,await Entitlement(ct));
  if(claims.TryGetValue(key,out var old))return new ClaimResult(old,await Entitlement(ct));
  if(claims.Count>=128)throw new InvalidOperationException("Claim ledger capacity reached; review before continuing");
  
  claims[key]=ClaimAcknowledgement.Unknown;
  try{var result=await new KuGou.Net.Clients.UserClient(new KuGou.Net.Protocol.Raw.RawUserApi(new SdkTransport(this,ct,rules)),session).ReceiveOneDayVipAsync();claims[key]=result?.AdVipNum>0?ClaimAcknowledgement.Confirmed:ClaimAcknowledgement.Unknown;}
  catch(GatewayFailure e)when(e.Code.StartsWith("API_",StringComparison.Ordinal)){claims[key]=ClaimAcknowledgement.Rejected;return new ClaimResult(ClaimAcknowledgement.Rejected,null);}
  catch(Exception e)when(e is GatewayFailure or OperationCanceledException or HttpRequestException){return new ClaimResult(ClaimAcknowledgement.Unknown,null);}
  // A confirmed claim must survive a failing entitlement refresh; the four outcomes stay distinct.
  UserVipResponse? entitlement=null;try{entitlement=await Entitlement(ct);}catch(Exception e)when(e is GatewayFailure or OperationCanceledException){ }
  return new ClaimResult(claims[key],entitlement);
 },ct);
 public Task<JsonElement> FreeListenCatalogAsync(int page=1,int size=30,CancellationToken ct=default)=>Run(async()=>{
  DemandLogin();if(page<1||page>1000||size<1||size>50)throw new ArgumentOutOfRangeException(nameof(page));
  var r=ConceptRequests.Create(GatewayOperation.FreeListen);r.Body=new{apiver=2,source=2,pagesize=size,page};return await Send(GatewayOperation.FreeListen,r,ct);
 },ct);
 public Task<PlaybackGrant> ResolveAuthorizedAsync(string hash,long albumId=0,long albumAudioId=0,string quality="128",CancellationToken ct=default)=>Run(async()=>{
  DemandLogin();hash=ConceptRequests.Hash(hash);if(quality is not("128" or "320" or "flac")||albumId<0||albumAudioId<0)throw new ArgumentException("Unsupported quality or invalid album identity");
  if(grants.Count>=32)throw new InvalidOperationException("Release old playback grants first");
  var verify=ConceptRequests.Create(GatewayOperation.UserVerify);verify.Params["module_id"]="51";
  var userAuth=Required(Data(await Send(GatewayOperation.UserVerify,verify,ct)),"auth");
  var authorization=Data(await Send(GatewayOperation.SongAuthorize,ConceptRequests.Authorize(hash,albumAudioId,userAuth),ct));
  var trackAuth=Required(authorization,"auth");if(!authorization.TryGetProperty("open_time",out var time)||!time.TryGetInt64(out var openTime)||openTime<=0)throw new GatewayFailure("MISSING_OPEN_TIME");
  var response=await Send(GatewayOperation.ResolveAudio,ConceptRequests.Resolve(hash,albumId,albumAudioId,trackAuth,openTime,quality),ct);return IssuePlaybackGrant(hash,quality,response);
 },ct);
 static Uri ValidateAuthorizedSource(string hash,JsonElement response){
  var payload=Data(response);
  var play=payload.Deserialize<PlayUrlData>()??throw new GatewayFailure("INVALID_PLAY_RESPONSE");
  // A URL alone is insufficient. Preserve explicit rights failures; do not reinterpret unknown layouts as success.
  if(!payload.TryGetProperty("priv_status",out var permission)||!permission.TryGetInt32(out var permitted)||permitted!=0||play.ErrCode!=0||play.ErrorCode is not null and not 0||play.Status is not null and not 1||!string.Equals(play.Hash,hash,StringComparison.OrdinalIgnoreCase)||play.Urls?.Count is not >0)throw new GatewayFailure("PLAYBACK_NOT_AUTHORIZED_OR_UNKNOWN");
  if(payload.TryGetProperty("is_free_part",out var part)&&part.GetInt32()!=0)throw new GatewayFailure("PREVIEW_ONLY");
  if(!Uri.TryCreate(play.Urls[0],UriKind.Absolute,out var address)||address.Scheme!="https"||address.Port!=443||!string.IsNullOrEmpty(address.UserInfo)||!string.IsNullOrEmpty(address.Fragment))throw new GatewayFailure("UNSAFE_MEDIA_URL");
  if(new[]{".kgm",".kgg",".vpr",".m4p"}.Any(x=>address.AbsolutePath.EndsWith(x,StringComparison.OrdinalIgnoreCase)))throw new GatewayFailure("PROTECTED_MEDIA_UNSUPPORTED");
  return address;
 }
 PlaybackGrant IssuePlaybackGrant(string hash,string quality,JsonElement response,GatewayOperation sourceOperation=GatewayOperation.ResolveAudio){
  var address=ValidateAuthorizedSource(hash,response);
  // Local safety ceiling, NOT a claim about the server's URL expiry. No cross-session persistence.
  var grant=new PlaybackGrant(session.Session.UserId,generation,hash,quality,address,clock.GetUtcNow().AddMinutes(1),sourceOperation);if(!grants.TryAdd(grant.Id,grant))throw new InvalidOperationException("Grant collision");return grant;
 }
 public void DemandGrant(PlaybackGrant grant){ObjectDisposedException.ThrowIf(disposed,this);if(!State.LoggedIn||grant.AccountId!=session.Session.UserId||grant.Generation!=generation||grant.NotAfter<=clock.GetUtcNow()||!grants.TryGetValue(grant.Id,out var actual)||!ReferenceEquals(actual,grant))throw new UnauthorizedAccessException("Playback grant is foreign, expired or revoked");}
 public void ReleaseGrant(PlaybackGrant grant){if(grants.TryRemove(grant.Id,out var removed))removed.Revoke();}
 internal Uri DemandAddress(PlaybackGrant grant){DemandGrant(grant);return grant.Address;}
 void DisposeCore(){if(disposed)return;disposed=true;ClearSession();wire.Dispose();}
 public async ValueTask DisposeAsync(){await gate.WaitAsync();try{DisposeCore();}finally{gate.Release();}}
 public void Dispose(){if(!gate.Wait(0))throw new InvalidOperationException("Pending operation; await DisposeAsync");try{DisposeCore();}finally{gate.Release();}}
}
