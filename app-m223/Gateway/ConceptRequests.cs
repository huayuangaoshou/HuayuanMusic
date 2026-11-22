using System.Globalization;
using KuGou.Net.Protocol.Transport;

namespace HuayuanMusic.Kugou.Gateway;

public enum GatewayOperation { QrCreate, QrPoll, Entitlement, ClaimHistory, ClaimDaily, FreeListen, UserVerify, SongAuthorize, ResolveAudio, SdkSearch, SdkPlaylists, SdkPlaylistSongs, SdkLyricSearch, SdkLyricLegacy, SdkLyricDownload, SdkPrivilege, SdkPlayUrl }

/// <summary>
/// Endpoint/field adaptation from MakcRe/KuGouMusicApi (MIT, copyright 2023 MakcRe).
/// No official app IDs, Android UA, random platform dfid, advertising completion or signing secrets ported.
/// </summary>
public static class ConceptRequests {
 static readonly IReadOnlyDictionary<GatewayOperation,(string Host,string Path,HttpMethod Method,string? Router)> routes =
 new Dictionary<GatewayOperation,(string,string,HttpMethod,string?)>{
  [GatewayOperation.QrCreate]=("login-user.kugou.com","/v2/qrcode",HttpMethod.Get,null),
  [GatewayOperation.QrPoll]=("login-user.kugou.com","/v2/get_userinfo_qrcode",HttpMethod.Get,null),
  [GatewayOperation.Entitlement]=("kugouvip.kugou.com","/v1/get_union_vip",HttpMethod.Get,null),
  [GatewayOperation.ClaimHistory]=("gateway.kugou.com","/youth/v1/activity/get_month_vip_record",HttpMethod.Get,null),
  [GatewayOperation.ClaimDaily]=("gateway.kugou.com","/youth/v1/recharge/receive_vip_listen_song",HttpMethod.Post,null),
  [GatewayOperation.FreeListen]=("gateway.kugou.com","/concepts/v1/ai/recommend_song",HttpMethod.Post,null),
  [GatewayOperation.UserVerify]=("gateway.kugou.com","/v1/user_verify",HttpMethod.Get,"trackercdngz.kugou.com"),
  [GatewayOperation.SongAuthorize]=("gateway.kugou.com","/v1/authorization",HttpMethod.Get,"trackercdngz.kugou.com"),
  [GatewayOperation.SdkSearch]=("gateway.kugou.com","/v3/search/song",HttpMethod.Get,"complexsearch.kugou.com"),
  [GatewayOperation.SdkPlaylists]=("gateway.kugou.com","/v7/get_all_list",HttpMethod.Post,"cloudlist.service.kugou.com"),
  [GatewayOperation.SdkPlaylistSongs]=("gateway.kugou.com","/pubsongs/v2/get_other_list_file_nofilt",HttpMethod.Get,null),
  [GatewayOperation.SdkLyricSearch]=("lyrics.kugou.com","/v1/search",HttpMethod.Get,null),
  [GatewayOperation.SdkLyricLegacy]=("lyrics.kugou.com","/search",HttpMethod.Get,null),
  [GatewayOperation.SdkLyricDownload]=("lyrics.kugou.com","/download",HttpMethod.Get,null),
  [GatewayOperation.SdkPrivilege]=("gateway.kugou.com","/v2/get_res_privilege/lite",HttpMethod.Post,"media.store.kugou.com"),
  [GatewayOperation.SdkPlayUrl]=("gateway.kugou.com","/v5/url",HttpMethod.Get,"trackercdn.kugou.com"),
  [GatewayOperation.ResolveAudio]=("gateway.kugou.com","/tracker/v5/url",HttpMethod.Get,null)
 };
 public static KgRequest Create(GatewayOperation op) {
  var route=routes[op];return new(){BaseUrl="https://"+route.Host,Path=route.Path,Method=route.Method,SpecificRouter=route.Router,ClearDefaultParams=true,SignatureType=op is GatewayOperation.QrCreate or GatewayOperation.QrPoll?SignatureType.Web:op==GatewayOperation.SdkPlayUrl?SignatureType.V5:SignatureType.Default};
 }
 public static void Validate(GatewayOperation op,KgRequest r){var expected=Create(op);if(r.BaseUrl!=expected.BaseUrl||r.Path!=expected.Path||r.Method!=expected.Method||r.SpecificRouter!=expected.SpecificRouter||r.SignatureType!=expected.SignatureType||r.CustomHeaders?.Count>0||r.SessionOverrides?.Count>0||r.BinaryBody!=null||r.RawBody!=null||r.NotSignature||!r.ClearDefaultParams)throw new UnauthorizedAccessException("Request is outside the reviewed endpoint catalog");}
 public static string Hash(string hash){if(hash.Length!=32||hash.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Expected a 32-character song hash");return hash.ToLowerInvariant();}
 public static string Number(long value)=>value>=0?value.ToString(CultureInfo.InvariantCulture):throw new ArgumentOutOfRangeException(nameof(value));
 public static KgRequest Authorize(string hash,long albumAudioId,string userAuth){var r=Create(GatewayOperation.SongAuthorize);r.Params=new(){["module_id"]="51",["authorization"]=userAuth,["hash"]=Hash(hash),["album_audio_id"]=Number(albumAudioId)};return r;}
 public static KgRequest Resolve(string hash,long albumId,long albumAudioId,string trackAuth,long openTime,string quality){
  if(string.IsNullOrWhiteSpace(trackAuth)||openTime<=0)throw new UnauthorizedAccessException("Both per-song auth and open_time are required");
  if(quality is not("128" or "320" or "flac"))throw new NotSupportedException("Only reviewed basic audio qualities are supported");
  var r=Create(GatewayOperation.ResolveAudio);r.Params=new(){["module_id"]="51",["cmd"]="26",["behavior"]="play",["hash"]=Hash(hash),["album_id"]=Number(albumId),["album_audio_id"]=Number(albumAudioId),["auth"]=trackAuth,["open_time"]=Number(openTime),["quality"]=quality,["cdnBackup"]="1"};return r;
 }
}
