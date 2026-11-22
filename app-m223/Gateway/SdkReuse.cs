using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Transport;
using KuGou.Net.util;
using Microsoft.Extensions.Logging.Abstractions;

namespace HuayuanMusic.Kugou.Gateway;

// Reuse SDK builders/clients/parsers against the SAME serialized, memory-only QR session.
// No KgClient/KgTransport construction: those would reintroduce default identity/signing.
public sealed partial class ConceptGateway {
 sealed class SdkTransport(ConceptGateway owner,CancellationToken ct,ActivityApproval? activity=null):IKgTransport {
  public async Task<JsonElement> SendAsync(KgRequest request) {
   ct.ThrowIfCancellationRequested();
   var op=request.Path switch {
    "/v2/qrcode"=>GatewayOperation.QrCreate,
    "/v2/get_userinfo_qrcode"=>GatewayOperation.QrPoll,
    "/youth/v1/recharge/receive_vip_listen_song"=>GatewayOperation.ClaimDaily,
    "/v2/get_res_privilege/lite"=>GatewayOperation.SdkPrivilege,
    "/v5/url"=>GatewayOperation.SdkPlayUrl,
    "/v3/search/song"=>GatewayOperation.SdkSearch,
    "/v7/get_all_list"=>GatewayOperation.SdkPlaylists,
    "/pubsongs/v2/get_other_list_file_nofilt"=>GatewayOperation.SdkPlaylistSongs,
    "/v1/search"=>GatewayOperation.SdkLyricSearch,
    "/search"=>GatewayOperation.SdkLyricLegacy,
    "/download"=>GatewayOperation.SdkLyricDownload,
    "/v1/get_union_vip"=>GatewayOperation.Entitlement,
    "/youth/v1/activity/get_month_vip_record"=>GatewayOperation.ClaimHistory,
    _=>throw new UnauthorizedAccessException("SDK route outside integrated read catalog")
   };
   var expected=ConceptRequests.Create(op);
   if(request.Method!=expected.Method||request.SpecificRouter!=expected.SpecificRouter||
      (request.BaseUrl!=null&&request.BaseUrl!=expected.BaseUrl)||request.NotSignature||
      (request.SpecificDfid!=null&&(op!=GatewayOperation.SdkPlayUrl||request.SpecificDfid!=owner.identity.Dfid))||request.CustomHeaders?.Count>0||request.SessionOverrides?.Count>0||
      request.RawBody!=null||request.BinaryBody!=null||request.SignatureType!=expected.SignatureType)
    throw new UnauthorizedAccessException("SDK request overrides rejected");
   expected.Params=new(request.Params,StringComparer.Ordinal);
   if(op is GatewayOperation.QrCreate or GatewayOperation.QrPoll){
    foreach(var key in new[]{"appid","clientver","srcappid"})expected.Params.Remove(key);
    if(op==GatewayOperation.QrCreate)expected.Params["qrcode_txt"]="https://h5.kugou.com/apps/loginQRCode/html/index.html?appid="+owner.identity.AppId+"&";
   }
   if(op==GatewayOperation.ClaimDaily){
    if(activity==null)throw new UnauthorizedAccessException("Claim must pass the existing activity gate");
    expected.Params["source_id"]=activity.SourceId;expected.Params["receive_day"]=activity.Day.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
   }
   // These TWO legacy builder defaults are discarded, never used as our identity.
   if(op==GatewayOperation.SdkPlayUrl){
    // Drop the legacy product/UI context before the trusted signer applies OUR identity.
    foreach(var key in new[]{"clientver","page_id","pid","ppage_id"})expected.Params.Remove(key);
   }
   foreach(var pair in new[]{("appid",KuGouConfig.AppId),("clientver",KuGouConfig.ClientVer)})
    if(expected.Params.Remove(pair.Item1,out var value)&&value!=pair.Item2)
     throw new UnauthorizedAccessException("Unexpected SDK identity override");
   foreach(var pair in new[]{("userid",owner.session.Session.UserId),("token",owner.session.Session.Token)})
    if(expected.Params.Remove(pair.Item1,out var value)&&value!=pair.Item2)
     throw new UnauthorizedAccessException("Foreign SDK session");
   // Preserve public SDK search/lyric format selectors; these are not app IDs, signing keys or device credentials.
   expected.Body=request.Body;expected.BodyTypeInfo=request.BodyTypeInfo;
   if(op==GatewayOperation.SdkPrivilege){
    var body=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(request.Body,request.BodyTypeInfo!))!.AsObject();
    body["appid"]=owner.identity.AppId;body["clientver"]=owner.identity.ClientVersion;expected.Body=body;expected.BodyTypeInfo=null;
   }
   var account=op is GatewayOperation.ClaimDaily or GatewayOperation.SdkPrivilege or GatewayOperation.SdkPlayUrl or GatewayOperation.SdkPlaylists or GatewayOperation.SdkPlaylistSongs or GatewayOperation.Entitlement or GatewayOperation.ClaimHistory;
   var raw=await owner.wire.SendAsync(op,expected,account?owner.session.Session.UserId:"",account?owner.session.Session.Token:"",ct);
   if(op is not (GatewayOperation.SdkLyricSearch or GatewayOperation.SdkLyricLegacy or GatewayOperation.SdkLyricDownload))CheckSuccess(raw);
   if(op==GatewayOperation.SdkPlayUrl)ValidateAuthorizedSource(expected.Params["hash"],raw);
   var data=Data(raw);
   var fields=op switch {GatewayOperation.SdkSearch=>("lists","total"),GatewayOperation.SdkPlaylists=>("info","list_count"),GatewayOperation.SdkPlaylistSongs=>("songs","count"),_=>("","")};
   if(fields.Item1.Length>0&&(!data.TryGetProperty(fields.Item1,out var entries)||entries.ValueKind!=JsonValueKind.Array||!data.TryGetProperty(fields.Item2,out var count)||!count.TryGetInt32(out var total)||total<0))throw new GatewayFailure("UNKNOWN_SDK_METADATA_LAYOUT");
   if(op==GatewayOperation.SdkPlaylists&&(!data.TryGetProperty("userid",out var uid)||uid.ToString()!=owner.session.Session.UserId))throw new GatewayFailure("SDK_ACCOUNT_MISMATCH");
   return raw;
  }
 }
 IKgTransport Sdk(CancellationToken ct)=>new SdkTransport(this,ct);
 public Task<SearchResultData> SearchSdkAsync(string keyword,int page=1,int size=20,CancellationToken ct=default)=>Run(async()=>{
  DemandLogin();if(string.IsNullOrWhiteSpace(keyword)||keyword.Length>120||page<1||page>1000||size<1||size>50)throw new ArgumentException("Search bounds");
  var raw=await new SearchClient(new RawSearchApi(Sdk(ct)),session).SearchRawAsync(keyword.Trim(),page,size);
  CheckSuccess(raw);return Data(raw).Deserialize<SearchResultData>()??throw new GatewayFailure("INVALID_SDK_SEARCH");
 },ct);
 public Task<UserPlaylistResponse> PlaylistsSdkAsync(int page=1,int size=30,CancellationToken ct=default)=>Run(async()=>{
  DemandLogin();if(page<1||page>200||size<1||size>50)throw new ArgumentException("Playlist bounds");
  return await new UserClient(new RawUserApi(Sdk(ct)),session).GetPlaylistsAsync(page,size)??throw new GatewayFailure("INVALID_SDK_PLAYLISTS");
 },ct);
 public Task<PlaylistSongResponse> PlaylistSongsSdkAsync(string id,int page=1,int size=50,CancellationToken ct=default)=>Run(async()=>{
  DemandLogin();if(string.IsNullOrWhiteSpace(id)||id.Length>128||id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_')||page<1||page>20||size<1||size>50)throw new ArgumentException("Playlist song bounds");
  return await new PlaylistClient(new RawPlaylistApi(Sdk(ct),NullLogger<RawPlaylistApi>.Instance),session).GetSongsAsync(id,page,size)??throw new GatewayFailure("INVALID_SDK_SONGS");
 },ct);
 public Task<string> LyricsSdkAsync(string hash,CancellationToken ct=default)=>Run(async()=>{
  DemandLogin();hash=ConceptRequests.Hash(hash);var client=new LyricClient(new RawLyricApi(Sdk(ct)));
  var raw=await client.SearchLyricAsync(hash,null,null,null);
  if(!raw.TryGetProperty("candidates",out var candidates)||candidates.ValueKind!=JsonValueKind.Array||candidates.GetArrayLength()==0)return "";
  var candidate=candidates[0];var id=candidate.GetProperty("id").ToString();var key=candidate.GetProperty("accesskey").GetString();
  if(id.Length>128||string.IsNullOrEmpty(key)||key.Length>512)throw new GatewayFailure("INVALID_LYRIC_CANDIDATE");
  // Plain LRC only: no new KRC decoder or protected audio implementation.
  var result=await client.GetLyricAsync(id,key,"lrc");var text=result.DecodedContent??"";
  if(text.Length>131072)throw new GatewayFailure("LYRICS_TOO_LARGE");return text;
 },ct);
}
