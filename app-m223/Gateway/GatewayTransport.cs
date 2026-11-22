using System.Net;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;

namespace HuayuanMusic.Kugou.Gateway;

// A trusted host supplies these only after verifying platform permission AND separate human test consent.
// Strings in a configuration file are not, by themselves, proof of legal authorization.
public sealed class AuthorizedIdentity {
 public required string AppId{get;init;}public required string QrIssuerAppId{get;init;}public required string SourceAppId{get;init;}public required string ClientVersion{get;init;}
 public required string PlatformPermissionReference{get;init;}public required string Dfid{get;init;}public required string Mid{get;init;}public required string Uuid{get;init;}
 public IReadOnlyDictionary<string,string> ApprovedProtocolParameters{get;init=>field=value.ToFrozenDictionary(StringComparer.Ordinal);} = FrozenDictionary<string,string>.Empty;
 public string? ApprovedProductId{get;init;}public string? ApprovedPageId{get;init;}
 public override string ToString()=>"AuthorizedIdentity [redacted]";
 internal void Validate(){foreach(var pair in ApprovedProtocolParameters){if(pair.Key is not("version" or "pidversion" or "ppage_id" or "platform" or "client")||string.IsNullOrWhiteSpace(pair.Value)||pair.Value.Length>256||pair.Value.Any(char.IsControl))throw new UnauthorizedAccessException("Unreviewed protocol context");}foreach(var s in new[]{AppId,QrIssuerAppId,SourceAppId,ClientVersion,PlatformPermissionReference,Mid,Uuid})if(string.IsNullOrWhiteSpace(s)||s.Length>256||s.Any(char.IsControl))throw new UnauthorizedAccessException("Incomplete approved application/device identity");if(!AppId.All(char.IsAsciiDigit)||!QrIssuerAppId.All(char.IsAsciiDigit)||!ClientVersion.All(char.IsAsciiDigit)||!SourceAppId.All(char.IsAsciiDigit))throw new UnauthorizedAccessException("Invalid identity fields");}
}
public sealed class GatewayPermit {
 public IReadOnlySet<string> QrImageHosts{get;init;}=new HashSet<string>();
 public bool LiveNetworkApproved{get;init;}
 public required string HumanApprovalReference{get;init;}public required DateTimeOffset NotAfter{get;init;}
 public required IReadOnlySet<GatewayOperation> Operations{get;init;}
 public required IReadOnlySet<string> MediaHosts{get;init;}public bool AllowAudioTransport{get;init;}
 internal void Demand(GatewayOperation op){if(string.IsNullOrWhiteSpace(HumanApprovalReference)||NotAfter<=DateTimeOffset.UtcNow||!Operations.Contains(op))throw new UnauthorizedAccessException("Operation has no current explicit test approval");}
 internal GatewayPermit Snapshot()=>new(){QrImageHosts=QrImageHosts.ToFrozenSet(StringComparer.OrdinalIgnoreCase),LiveNetworkApproved=LiveNetworkApproved,HumanApprovalReference=HumanApprovalReference,NotAfter=NotAfter,Operations=Operations.ToFrozenSet(),MediaHosts=MediaHosts.ToFrozenSet(StringComparer.OrdinalIgnoreCase),AllowAudioTransport=AllowAudioTransport};
 public override string ToString()=>"GatewayPermit [scoped]";
}
public interface IConceptWire:IDisposable {
 Task<byte[]> ReadQrImageAsync(Uri uri,CancellationToken ct)=>throw new UnauthorizedAccessException("QR image transport not approved");
 Task<JsonElement> SendAsync(GatewayOperation operation,KgRequest request,string userId,string token,CancellationToken ct);
}
public interface IApprovedRequestSigner {
 // Implementations must use only the application's own authorized key material.
 HttpRequestMessage Build(GatewayOperation operation,KgRequest request,AuthorizedIdentity identity,string userId,string token);
}
public sealed class CredentialBoundSigner : IApprovedRequestSigner,IDisposable {
 string androidKey,webKey,mediaKey;
 public CredentialBoundSigner(string androidKey,string webKey,string mediaKey){if(new[]{androidKey,webKey,mediaKey}.Any(string.IsNullOrWhiteSpace))throw new UnauthorizedAccessException("Authorized keys are required; no default keys");this.androidKey=androidKey;this.webKey=webKey;this.mediaKey=mediaKey;}
 public HttpRequestMessage Build(GatewayOperation op,KgRequest request,AuthorizedIdentity id,string userId,string token){
  ConceptRequests.Validate(op,request);id.Validate();if(op is GatewayOperation.UserVerify or GatewayOperation.SongAuthorize or GatewayOperation.ResolveAudio or GatewayOperation.SdkPlayUrl or GatewayOperation.SdkPrivilege && (string.IsNullOrWhiteSpace(id.Dfid)||id.Dfid=="-"))throw new UnauthorizedAccessException("Approved platform device admission required for auth/media; no random fallback");if(androidKey.Length==0)throw new ObjectDisposedException(nameof(CredentialBoundSigner));
  var p=new Dictionary<string,string>(request.Params,StringComparer.Ordinal);
  if(p.Keys.Any(k=>k is "appid" or "srcappid" or "clientver" or "dfid" or "mid" or "uuid" or "userid" or "token" or "key" or "signature" or "page_id" or "pid"))throw new UnauthorizedAccessException("Caller cannot override approved identity");
  p["appid"]=op==GatewayOperation.QrCreate?id.QrIssuerAppId:id.AppId;if(op is GatewayOperation.QrCreate or GatewayOperation.QrPoll or GatewayOperation.UserVerify or GatewayOperation.SongAuthorize or GatewayOperation.ResolveAudio or GatewayOperation.FreeListen)p["srcappid"]=id.SourceAppId;p["clientver"]=id.ClientVersion;p["dfid"]=string.IsNullOrEmpty(id.Dfid)?"-":id.Dfid;p["mid"]=id.Mid;p["uuid"]=id.Uuid;p["clienttime"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
  if(!string.IsNullOrEmpty(token)){p["userid"]=userId;p["token"]=token;}
  if(op is GatewayOperation.ResolveAudio or GatewayOperation.SdkPlayUrl){
   // Mature V5 key formula, parameterized with OUR approved identity/key. No KuGouConfig default salt.
   // V5 version/pidversion are supplied by the mature SDK, not our global app version.
   p["key"]=KgUtils.Md5(p["hash"]+mediaKey+id.AppId+id.Mid+userId);
   if(id.ApprovedProductId!=null)p["pid"]=id.ApprovedProductId;if(id.ApprovedPageId!=null)p["page_id"]=id.ApprovedPageId;
  }
  foreach(var pair in id.ApprovedProtocolParameters){
   if(op==GatewayOperation.SdkPlayUrl&&pair.Key is "version" or "pidversion" or "ppage_id")p[pair.Key]=pair.Value;
   if(op==GatewayOperation.SdkSearch&&pair.Key=="platform")p[pair.Key]=pair.Value;
   if(op is GatewayOperation.SdkLyricSearch or GatewayOperation.SdkLyricLegacy or GatewayOperation.SdkLyricDownload &&pair.Key=="client")p[pair.Key]=pair.Value;
  }
  string body=request.Body==null?"":request.BodyTypeInfo!=null||request.Body is System.Text.Json.Nodes.JsonNode?SdkRequestSerialization.Serialize(request.Body,request.BodyTypeInfo):JsonSerializer.Serialize(request.Body);
  p["signature"]=KgSigner.CalcPostSignature(p,body,request.SignatureType==SignatureType.Web?webKey:androidKey);
  var query=string.Join("&",p.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>Uri.EscapeDataString(x.Key)+"="+Uri.EscapeDataString(x.Value)));
  var message=new HttpRequestMessage(request.Method,request.BaseUrl+request.Path+"?"+query);
  message.Headers.UserAgent.ParseAdd("HuayuanMusic-KugouDLC/0.1 (Windows)");
  if(request.SpecificRouter!=null)message.Headers.Add("x-router",request.SpecificRouter);
  if(request.Method==HttpMethod.Post){message.Content=request.Body==null?new ByteArrayContent([]):new StringContent(body,Encoding.UTF8,"application/json");}
  return message;
 }
 public void Dispose(){androidKey=webKey=mediaKey="";}
 public override string ToString()=>"CredentialBoundSigner [redacted]";
}

public sealed class AuthorizedHttpWire : IConceptWire {
 readonly HttpClient http;readonly AuthorizedIdentity identity;readonly GatewayPermit permit;readonly IApprovedRequestSigner signer;
 // Test construction accepts a terminal handler. The executable does not expose live construction.
 internal AuthorizedHttpWire(HttpClient http,AuthorizedIdentity identity,GatewayPermit permit,IApprovedRequestSigner signer){identity.Validate();this.http=http;this.identity=identity;this.permit=permit.Snapshot();this.signer=signer;}
 public static AuthorizedHttpWire CreateLive(AuthorizedIdentity identity,GatewayPermit permit,IApprovedRequestSigner signer){
  identity.Validate();if(!permit.LiveNetworkApproved||permit.Operations.Count==0||string.IsNullOrWhiteSpace(permit.HumanApprovalReference)||permit.NotAfter<=DateTimeOffset.UtcNow)throw new UnauthorizedAccessException("No live test authorization");
  return new(new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false,UseProxy=false,Credentials=null}){Timeout=TimeSpan.FromSeconds(20)},identity,permit,signer);
 }
 public async Task<JsonElement> SendAsync(GatewayOperation op,KgRequest request,string userId,string token,CancellationToken ct){
  permit.Demand(op);ConceptRequests.Validate(op,request);ct.ThrowIfCancellationRequested();using var message=signer.Build(op,request,identity,userId,token);
  // A signer cannot replace the reviewed host/path or leak credentials to a redirected destination.
  var expected=ConceptRequests.Create(op);var uri=message.RequestUri;
  if(uri==null||uri.Scheme!="https"||uri.Host!=new Uri(expected.BaseUrl!).Host||uri.Port!=443||uri.AbsolutePath!=expected.Path||message.Method!=expected.Method||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Fragment))throw new UnauthorizedAccessException("Signer changed the approved destination");
  try{
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(20));
   using var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
   if((int)response.StatusCode is >=300 and <400)throw new GatewayFailure("HTTP_REDIRECT_REFUSED");
   if(response.StatusCode==HttpStatusCode.Unauthorized)throw new GatewayFailure("ACCOUNT_EXPIRED");
   if(!response.IsSuccessStatusCode)throw new GatewayFailure("HTTP_"+(int)response.StatusCode);
   if(response.Content.Headers.ContentLength>1024*1024)throw new GatewayFailure("RESPONSE_TOO_LARGE");
   await using var input=await response.Content.ReadAsStreamAsync(timeout.Token);using var buffer=new MemoryStream();byte[] block=new byte[8192];int n;
   while((n=await input.ReadAsync(block,timeout.Token))>0){if(buffer.Length+n>1024*1024)throw new GatewayFailure("RESPONSE_TOO_LARGE");buffer.Write(block,0,n);}
   using var json=JsonDocument.Parse(buffer.ToArray(),new JsonDocumentOptions{MaxDepth=32});return json.RootElement.Clone();
  }catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new GatewayFailure("HTTP_TIMEOUT");}catch(HttpRequestException){throw new GatewayFailure("HTTP_TRANSPORT_FAILED");}catch(JsonException){throw new GatewayFailure("INVALID_JSON");}
  // Deliberately no retries: GET auth can issue grants, and POST claim is a mutation.
 }
 public async Task<byte[]> ReadQrImageAsync(Uri uri,CancellationToken ct){
  permit.Demand(GatewayOperation.QrCreate);
  if(!permit.LiveNetworkApproved||uri.Scheme!="https"||uri.Port!=443||uri.HostNameType!=UriHostNameType.Dns||uri.IsLoopback||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Fragment)||!permit.QrImageHosts.Contains(uri.IdnHost))throw new UnauthorizedAccessException("QR image origin not explicitly approved");
  using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
  if(response.Content.Headers.ContentType?.MediaType!="image/png"||response.Content.Headers.ContentLength>1048576)throw new GatewayFailure("QR_PNG_REQUIRED");
  using var stream=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();byte[] block=new byte[8192];int n;
  while((n=await stream.ReadAsync(block,ct))>0){if(output.Length+n>1048576)throw new GatewayFailure("QR_IMAGE_TOO_LARGE");output.Write(block,0,n);}return output.ToArray();
 }
 public void Dispose()=>http.Dispose();
}
public sealed class GatewayFailure(string code):Exception(code){public string Code=>Message;}
