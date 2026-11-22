using KuGou.Net.Abstractions;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Raw;

namespace HuayuanMusic.Kugou.Gateway;

public sealed partial class ConceptGateway {
 // KA Music v4.2.2 PlaybackSourceResolver flow (MIT, Linsxyx): privilege -> quality -> SDK URL.
 // Auth/stream admission still uses the SAME account session and SAME grant registry.
 public Task<PlaybackGrant> ResolveMatureSdkAsync(string hash,long albumId=0,long albumAudioId=0,string quality="128",CancellationToken ct=default)=>Run(async()=>{
  DemandLogin();hash=ConceptRequests.Hash(hash);
  if(quality is not("128" or "320" or "flac")||albumId<0||albumAudioId<0)throw new ArgumentException("Unreviewed quality or track identity");
  // Prevent RawSongApi.GetUrlAsync's random DFID fallback before invoking even the privilege request.
  if(string.IsNullOrWhiteSpace(identity.Dfid)||identity.Dfid=="-")throw new UnauthorizedAccessException("Approved device admission required; no random fallback");
  if(grants.Count>=32)throw new InvalidOperationException("Release old playback grants first");
  session.Session.Dfid=identity.Dfid;
  var song=new SongClient(new RawSongApi(Sdk(ct),session));
  var privileges=await song.GetPrivilegeLiteAsync(hash,ConceptRequests.Number(albumId));
  ct.ThrowIfCancellationRequested();
  var rank=AudioQuality.GetRank(quality);int highest=-1,nodes=0;
  // Same supported-quality traversal/rank selection as KA; bounded for untrusted response trees.
  var stack=new Stack<PrivilegeLiteData>(privileges??[]);
  while(stack.TryPop(out var item)){
   if(++nodes>256)throw new GatewayFailure("PRIVILEGE_TREE_TOO_LARGE");
   highest=Math.Max(highest,AudioQuality.GetRank(item.Quality));
   foreach(var related in item.RelateGoods)stack.Push(related);
  }
  var effective=highest>=0&&highest<rank?AudioQuality.Ordered[highest]:quality;
  // Metadata/privilege flags are NOT a playback grant. The final response must explicitly authorize it.
  var play=await song.GetPlayInfoAsync(hash,effective,ConceptRequests.Number(albumId),ConceptRequests.Number(albumAudioId),freePart:false);
  ct.ThrowIfCancellationRequested();
  if(play==null||!play.IsSuccess||play.ErrCode!=0||play.PrivStatus!=0)throw new GatewayFailure("SDK_SOURCE_NOT_AUTHORIZED");
  // SdkTransport already checked raw permission/preview layout before SDK parsing could default fields.
  var raw=System.Text.Json.JsonSerializer.SerializeToElement(play);
  return IssuePlaybackGrant(hash,effective,raw,GatewayOperation.SdkPlayUrl);
 },ct);
}
