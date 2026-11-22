using System.Net;

namespace HuayuanMusic.Kugou.Gateway;

public sealed class OpenedAudio:IDisposable {
 readonly IDisposable owner;CancellationTokenRegistration revocation;int disposed;
 public Stream Stream{get;}public long Length{get;}
 public OpenedAudio(Stream stream,long length,IDisposable owner,CancellationToken revoked){Stream=stream;Length=length;this.owner=owner;revocation=revoked.Register(Dispose);}
 public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;revocation.Unregister();Stream.Dispose();owner.Dispose();}
 public override string ToString()=>"OpenedAudio [owned response stream]";
}
/// <summary>Authorized streaming only. Returns ownership to the existing BASS stream loader, never a second player.</summary>
public sealed class AuthorizedAudioTransport:IDisposable {
 readonly HttpClient http;readonly GatewayPermit permit;
 internal AuthorizedAudioTransport(HttpClient http,GatewayPermit permit){this.http=http;this.permit=permit.Snapshot();}
 public static AuthorizedAudioTransport CreateLive(GatewayPermit permit){
  permit.Demand(permit.Operations.Contains(GatewayOperation.SdkPlayUrl)?GatewayOperation.SdkPlayUrl:GatewayOperation.ResolveAudio);if(!permit.LiveNetworkApproved||!permit.AllowAudioTransport||permit.MediaHosts.Count==0)throw new UnauthorizedAccessException("Audio transport needs separate explicit approval and exact CDN hosts");
  return new(new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false,UseProxy=false,Credentials=null}){Timeout=TimeSpan.FromSeconds(20)},permit);
 }
 public async Task<OpenedAudio> OpenAsync(ConceptGateway owner,PlaybackGrant grant,CancellationToken ct=default){
  permit.Demand(grant.SourceOperation);if(!permit.AllowAudioTransport)throw new UnauthorizedAccessException("Audio transfer not approved");
  var uri=owner.DemandAddress(grant);
  if(!permit.MediaHosts.Contains(uri.IdnHost)||uri.IsLoopback||IPAddress.TryParse(uri.IdnHost,out _)||uri.HostNameType!=UriHostNameType.Dns)throw new UnauthorizedAccessException("Media CDN is not approved");
  using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.UserAgent.ParseAdd("HuayuanMusic-KugouDLC/0.1 (Windows)");
  // No account cookie, authorization header or SDK token is forwarded to a CDN.
  HttpResponseMessage? response=null;
  try{
   response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);if(!response.IsSuccessStatusCode)throw new GatewayFailure("MEDIA_HTTP_"+(int)response.StatusCode);
   if(response.Content.Headers.ContentLength is not >0 or >134217728)throw new GatewayFailure("MEDIA_LENGTH_UNSUPPORTED");
   string type=response.Content.Headers.ContentType?.MediaType??"";
   if(!new[]{"audio/mpeg","audio/flac","audio/x-flac","audio/wav","audio/x-wav"}.Contains(type,StringComparer.OrdinalIgnoreCase))throw new GatewayFailure("MEDIA_TYPE_NOT_VERIFIED");
   owner.DemandGrant(grant);var input=await response.Content.ReadAsStreamAsync(ct);owner.DemandGrant(grant);
   var audio=new OpenedAudio(input,response.Content.Headers.ContentLength.Value,response,grant.Revocation);response=null;return audio;
  }catch(HttpRequestException){throw new GatewayFailure("MEDIA_TRANSPORT_FAILED");}finally{response?.Dispose();}
 }
 public void Dispose()=>http.Dispose();
}
