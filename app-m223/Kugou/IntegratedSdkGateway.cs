using HuayuanMusic.Kugou.Gateway;
using HuayuanMusic.Provider.Contracts;

namespace HuayuanMusic.Provider.Kugou;

/// <summary>Only SDK/domain mapping and grant ownership. No sockets, engine, queue or database.</summary>
public enum KugouPlaybackPath { MatureSdk, ConceptAuth }

public sealed class IntegratedSdkGateway : IRealSdkGateway {
 public KugouPlaybackPath PlaybackPath{get;}
 readonly ConceptGateway concept;readonly AuthorizedAudioTransport audio;
 readonly Dictionary<string,CatalogTrack> tracks=new(StringComparer.Ordinal);
 readonly Dictionary<(string,string),Uri> pending=new();
 readonly System.Collections.Concurrent.ConcurrentDictionary<Uri,PlaybackGrant> grants=new();
 bool disposed;
 public bool IsFixture=>false;
 public bool LoggedIn=>concept.State.LoggedIn;
 public string Alias=>concept.State.Alias;
 public ActivityApproval? ApprovedActivity{get;set;}
 public event Action? Revoked;
 public IntegratedSdkGateway(ConceptGateway concept,AuthorizedAudioTransport audio,KugouPlaybackPath playbackPath=KugouPlaybackPath.MatureSdk){if(!Enum.IsDefined(playbackPath))throw new ArgumentOutOfRangeException(nameof(playbackPath));PlaybackPath=playbackPath;this.concept=concept;this.audio=audio;concept.AuthorizationRevoked+=OnRevoked;}
 void OnRevoked(){pending.Clear();grants.Clear();Revoked?.Invoke();}
 public Task<QrChallenge> BeginQrAsync(CancellationToken ct)=>concept.BeginQrAsync(ct);
 public Task<KuGou.Net.Abstractions.Models.QrLoginStatus> PollQrAsync(CancellationToken ct)=>concept.PollQrAsync(ct);
 public Task CancelLoginAsync(CancellationToken ct)=>concept.LogoutAsync(ct);
 public Task<KuGou.Net.Abstractions.Models.VipReceiveHistoryResponse> ReadClaimHistoryAsync(CancellationToken ct)=>concept.ReadClaimHistoryAsync(ct);
 public async Task<Entitlement> EntitlementAsync(CancellationToken ct){var e=await concept.ReadEntitlementAsync(ct);return Map(e);}
 static Entitlement Map(KuGou.Net.Abstractions.Models.UserVipResponse e){
  var p=e.BusiVipList.FirstOrDefault(x=>x.BusiType=="concept");
  DateTimeOffset? until=DateTimeOffset.TryParse(p?.EndTime,out var time)?time:null;
  return new(e.IsVip==1&&p?.IsVip==1&&until>DateTimeOffset.UtcNow,p?.ProductType??"unknown",until,"SDK response; claim, entitlement and song permission are independent");
 }
 // Integrated path uses the EXISTING guarded gateway claim ledger, not the fixture claim orchestrator.
 public Task<DailyOffer> OfferAsync(CancellationToken ct)=>throw new NotSupportedException("Use existing guarded manual activity flow");
 public Task<bool> ClaimAsync(CancellationToken ct)=>throw new NotSupportedException("Use existing guarded manual activity flow");
 public async Task<ClaimReceipt> ClaimTodayAsync(bool click,CancellationToken ct){
  var rules=ApprovedActivity??throw new UnauthorizedAccessException("Current platform activity approval absent");
  var receipt=await concept.ClaimTodayAsync(rules,click,ct);
  return new(receipt.Claim switch {ClaimAcknowledgement.Confirmed=>ClaimState.ClaimSuccess,ClaimAcknowledgement.AlreadyReceived=>ClaimState.AlreadyReceived,ClaimAcknowledgement.Rejected=>ClaimState.Rejected,_=>ClaimState.OutcomeUnknown},receipt.Entitlement==null?new(false,"unknown",null,"Entitlement refresh unavailable"):Map(receipt.Entitlement));
 }
 public async Task<SearchPage> SearchAsync(string keyword,int page,int size,CancellationToken ct){
  var data=await concept.SearchSdkAsync(keyword,page,size,ct);
  var found=(data.Songs??[]).Select(x=>new CatalogTrack(ConceptRequests.Hash(x.Hash),x.Name,x.Singer,x.AlbumName,x.Duration,long.TryParse(x.AlbumId,out var album)?album:0,Cover:ArtworkUrl.Normalize(x.Cover))).ToArray();
  if(found.Length>size)throw new InvalidDataException("Search page exceeds requested bound");
  foreach(var t in found){if(!tracks.ContainsKey(t.Id)&&tracks.Count>=10000)throw new InvalidOperationException("Catalog metadata capacity reached");tracks[t.Id]=t;}
  return new(page,Array.AsReadOnly(found),(long)page*size<data.Total);
 }
 static string Quality(string quality)=>quality switch {"standard"=>"128","320"=>"320","flac"=>"flac",_=>throw new NotSupportedException("Unreviewed quality")};
 public async Task<MediaPermission> PermissionAsync(string track,string quality,CancellationToken ct){
  track=ConceptRequests.Hash(track);if(!tracks.ContainsKey(track))throw new UnauthorizedAccessException("Read catalog metadata first");
  // Permission is established ONLY by the existing account -> song -> source Auth chain.
  if(pending.Remove((track,quality),out var previous))Release(previous);
  var metadata=tracks[track];var grant=PlaybackPath==KugouPlaybackPath.MatureSdk
   ?await concept.ResolveMatureSdkAsync(track,metadata.AlbumId,metadata.AlbumAudioId,Quality(quality),ct)
   :await concept.ResolveAuthorizedAsync(track,metadata.AlbumId,metadata.AlbumAudioId,Quality(quality),ct);
  var handle=new Uri("kugou-grant://"+Guid.NewGuid().ToString("N"));if(!grants.TryAdd(handle,grant)){concept.ReleaseGrant(grant);throw new InvalidOperationException("Grant collision");}pending[(track,quality)]=handle;
  return new(true,quality,"SERVER_SOURCE_AUTHORIZED; audio not yet decoded",grant.Quality);
 }
 public Task<Uri> ResolveAsync(string track,string quality,CancellationToken ct){ct.ThrowIfCancellationRequested();if(!pending.Remove((track,quality),out var handle)||!Allows(handle))throw new UnauthorizedAccessException("No current song grant");return Task.FromResult(handle);}
 public bool Allows(Uri handle){if(disposed||!grants.TryGetValue(handle,out var g))return false;try{concept.DemandGrant(g);return true;}catch(UnauthorizedAccessException){return false;}catch(ObjectDisposedException){return false;}}
 public Task<OpenedAudio> OpenAsync(Uri handle,CancellationToken ct){if(!Allows(handle))throw new UnauthorizedAccessException("Foreign, expired or revoked media handle");if(!grants.TryGetValue(handle,out var grant))throw new UnauthorizedAccessException("Revoked media handle");return audio.OpenAsync(concept,grant,ct);}
 public void Release(Uri handle){if(grants.TryRemove(handle,out var g))concept.ReleaseGrant(g);}
 public Task<string> LyricsAsync(string track,CancellationToken ct)=>concept.LyricsSdkAsync(track,ct);
 public async Task<IReadOnlyList<ProviderPlaylist>> PlaylistsAsync(string account,CancellationToken ct){
  if(account!=Alias)throw new UnauthorizedAccessException("Account alias mismatch");
  var lists=new List<ProviderPlaylist>();int totalSongs=0;
  for(int page=1;page<=4;page++){
   var result=await concept.PlaylistsSdkAsync(page,50,ct);
   if(result.Playlists.Count>50||result.ListCount>200)throw new InvalidOperationException("More than 200 playlists; explicit paged UI required");
   foreach(var list in result.Playlists){
    if(string.IsNullOrEmpty(list.GlobalId))throw new InvalidDataException("Playlist has no reliable global identity");
    var songs=new List<ProviderTrackRef>();int expectedCount=-1;
    for(int songPage=1;songPage<=20;songPage++){
     var chunk=await concept.PlaylistSongsSdkAsync(list.GlobalId,songPage,50,ct);
     if(expectedCount!=-1&&expectedCount!=chunk.Count)throw new InvalidDataException("Playlist changed during pagination");expectedCount=chunk.Count;
     if(chunk.Songs.Count>50||chunk.Count>1000||totalSongs+chunk.Songs.Count>10000)throw new InvalidOperationException("Playlist sync bound exceeded; no partial cache published");
     foreach(var t in chunk.Songs){var hash=ConceptRequests.Hash(t.Hash);songs.Add(new(KugouProvider.Identity,hash,t.Name,string.Join(" / ",t.Singers.Select(s=>s.Name)),t.Album?.Name??""){CoverUrl=ArtworkUrl.Normalize(t.Cover??t.Album?.Cover),AlbumId=long.TryParse(t.AlbumId,out var aid)&&aid>0?aid:null});if(!tracks.ContainsKey(hash)&&tracks.Count>=10000)throw new InvalidOperationException("Catalog metadata capacity reached");tracks[hash]=new(hash,t.Name,string.Join(" / ",t.Singers.Select(s=>s.Name)),t.Album?.Name??"",t.DurationMs/1000,long.TryParse(t.AlbumId,out var album)?album:0,Cover:ArtworkUrl.Normalize(t.Cover??t.Album?.Cover));}
     totalSongs+=chunk.Songs.Count;if(songPage*50>=chunk.Count)break;
    }
    if(songs.Count!=expectedCount)throw new InvalidDataException("Incomplete playlist song page");
    if(lists.Any(x=>x.ProviderPlaylistId==list.GlobalId))throw new InvalidDataException("Repeated playlist page");
    lists.Add(new(KugouProvider.Identity,list.GlobalId,list.Name,Array.AsReadOnly(songs.ToArray())){CoverUrl=ArtworkUrl.Normalize(list.Pic)});
   }
   if(page*50>=result.ListCount)return Array.AsReadOnly(lists.ToArray());
  }
  throw new InvalidDataException("Incomplete playlist pagination");
 }
 public void Dispose(){if(disposed)return;concept.AuthorizationRevoked-=OnRevoked;foreach(var g in grants.Values)concept.ReleaseGrant(g);pending.Clear();grants.Clear();tracks.Clear();concept.Dispose();audio.Dispose();disposed=true;}
}
