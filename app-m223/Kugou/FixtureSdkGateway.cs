using System.Net;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.util;
using System.Text.Json;
using HuayuanMusic.Provider.Contracts;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;

namespace HuayuanMusic.Provider.Kugou;

/// <summary>Terminal transport. No socket handler/signature builder exists in this graph.</summary>
public sealed class FixtureKgTransport : IKgTransport {
 public bool Claimed,ActivateAfterClaim=true,Denied,TimeoutClaim,OfficialOnly,FailRead;
 public int ClaimCalls,Calls;
 public string? LastPath;
 public Task<JsonElement> SendAsync(KgRequest request){
  Calls++;LastPath=request.Path;
  if(FailRead)throw new IOException("Injected terminal transport outage");
  object result=request.Path switch {
   "/v1/get_union_vip"=>new {status=1,data=new {userid=101,is_vip=Claimed&&ActivateAfterClaim?1:0,busi_vip=new[]{new{is_vip=Claimed&&ActivateAfterClaim?1:0,product_type="fixture-concept",busi_type="concept",vip_end_time=DateTimeOffset.UtcNow.AddDays(1).ToString("O")}}}},
   "/youth/v1/recharge/receive_vip_listen_song"=>Claim(request),
   "/v3/search/song"=>new {status=1,data=new {total=2,lists=new[]{new {FileHash="fixture-free",FileName="夜航 · 自产测试音",SingerName="Huayuan 测试",AlbumName="离线协议样本",Duration=12},new{FileHash="fixture-member",FileName="晨光 · 模拟会员音",SingerName="Huayuan 测试",AlbumName="离线协议样本",Duration=12}}}},
   "/v5/url"=>new {status=Denied?0:1,priv_status=Denied?1:0,err_code=Denied?403:0,hash=request.Params?.GetValueOrDefault("hash")??"fixture-free",url=Denied?Array.Empty<string>():new[]{"https://audio.fixture.invalid/generated.wav"}},
   _=>throw new UnauthorizedAccessException("Unapproved SDK operation: "+request.Path)
  };
  return Task.FromResult(JsonSerializer.SerializeToElement(result));
 }
 object Claim(KgRequest request){
  if(request.Method!=HttpMethod.Post||request.Params?.GetValueOrDefault("receive_day")!=DateTime.Today.ToString("yyyy-MM-dd"))throw new UnauthorizedAccessException("Not a current-day manual claim");
  ClaimCalls++;if(TimeoutClaim)throw new TimeoutException("Claim outcome uncertain");
  if(Claimed||OfficialOnly)return new{status=0,error_code=409};Claimed=true;return new{status=1,data=new{ad_vip_num=1,ad_vip_end_time=DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()}};
 }
}

/// <summary>Reuses inherited MIT SDK clients/builders/parsers. Session is isolated in memory only.</summary>
public sealed class FixtureSdkGateway : IKugouGateway {
 readonly FixtureKgTransport transport;readonly UserClient user;readonly SearchClient search;readonly SongClient song;
 readonly KgSessionManager session;bool disposed;
 public bool IsFixture=>true;
 public FixtureSdkGateway(FixtureKgTransport transport){this.transport=transport;session=new(new CookieContainer());session.UpdateAuth("101","SYNTHETIC_NOT_A_CREDENTIAL","0","","");user=new(new RawUserApi(transport),session);search=new(new RawSearchApi(transport),session);song=new(new RawSongApi(transport,session));}
 void Demand(CancellationToken ct){ObjectDisposedException.ThrowIf(disposed,this);ct.ThrowIfCancellationRequested();}
 public async Task<Entitlement> EntitlementAsync(CancellationToken ct){Demand(ct);var value=await user.GetVipInfoAsync();if(value?.Status!=1)throw new IOException("Entitlement response not successful");var p=value.BusiVipList.FirstOrDefault();DateTimeOffset? until=DateTimeOffset.TryParse(p?.EndTime,out var end)?end:null;return new(value.IsVip==1&&p?.IsVip==1&&until>DateTimeOffset.UtcNow,p?.ProductType??"unknown",until,"SDK fixture response; REAL_PENDING");}
 public Task<DailyOffer> OfferAsync(CancellationToken ct){Demand(ct);return Task.FromResult(new DailyOffer(DateOnly.FromDateTime(DateTime.Today),transport.Claimed,true,true,transport.OfficialOnly));}
 public async Task<bool> ClaimAsync(CancellationToken ct){Demand(ct);var receipt=await user.ReceiveOneDayVipAsync();return receipt?.Status==1&&receipt.AdVipNum>0;}
 public async Task<SearchPage> SearchAsync(string keyword,int page,int size,CancellationToken ct){Demand(ct);var raw=await search.SearchRawAsync(keyword,page,size);if(!raw.TryGetProperty("status",out var status)||status.GetInt32()!=1)throw new IOException("Search rejected");var tracks=raw.GetProperty("data").Deserialize<SearchResultData>()?.Songs??[];var filtered=tracks.Where(x=>x.Name.Contains(keyword,StringComparison.OrdinalIgnoreCase)||x.Singer.Contains(keyword,StringComparison.OrdinalIgnoreCase)||keyword=="测试").Select(x=>new CatalogTrack(x.Hash,x.Name,x.Singer,x.AlbumName,x.Duration)).ToArray();return new(page,filtered.Skip((page-1)*size).Take(size).ToArray(),page*size<filtered.Length);}
 public Task<MediaPermission> PermissionAsync(string track,string quality,CancellationToken ct){Demand(ct);bool allowed=!transport.Denied&&(track=="fixture-free"||track=="fixture-member"&&transport.Claimed&&transport.ActivateAfterClaim)&&quality=="standard";return Task.FromResult(new MediaPermission(allowed,quality,allowed?"FIXTURE_SERVER_ALLOW":"FIXTURE_SERVER_DENY"));}
 public async Task<Uri> ResolveAsync(string track,string quality,CancellationToken ct){Demand(ct);var value=await song.GetPlayInfoAsync(track,quality);if(value==null||!value.IsSuccess||value.ErrCode!=0||value.PrivStatus!=0)throw new UnauthorizedAccessException("No full-track playback grant");return new Uri(value.Urls![0]);}
 public Task<string> LyricsAsync(string track,CancellationToken ct){Demand(ct);if(track is not("fixture-free" or "fixture-member"))throw new ArgumentException("Unknown track");return Task.FromResult("[00:00.00]离线合成音频 · 非平台歌曲\n[00:02.00]独立权益与播放授权\n[00:05.00]不登录，不联网\n[00:08.00]本体唯一音频引擎");}
 public Task<IReadOnlyList<ProviderPlaylist>> PlaylistsAsync(string account,CancellationToken ct){Demand(ct);IReadOnlyList<ProviderPlaylist> lists=new[]{new ProviderPlaylist(KugouProvider.Identity,account+"-liked","我喜欢 · "+account+"（模拟）",new[]{new ProviderTrackRef(KugouProvider.Identity,"fixture-free","夜航 · 自产测试音","Huayuan 测试","离线协议样本")})};return Task.FromResult(lists);}
 public void Dispose(){if(disposed)return;disposed=true;session.Session.Token="";session.Session.UserId="0";/* Entire gateway/cookie container is discarded; no shared session survives logout. */}
}
