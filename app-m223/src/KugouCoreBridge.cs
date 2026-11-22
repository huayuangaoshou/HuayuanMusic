using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using KugouAvaloniaPlayer.ViewModels;

namespace HuayuanPreview;

internal sealed partial class CorePlaybackAdapter {
 internal readonly KugouProvider Kugou=Program.Desktop ? new() : new(_=>new FixtureSdkGateway(new FixtureKgTransport()));
 readonly Dictionary<int,string> providerArtworkAccounts=new();
 internal string ProviderArtworkAccount(int id)=>providerArtworkAccounts.GetValueOrDefault(id,"none");
 readonly Dictionary<int,string> providerTrackKeys=new();
 readonly List<string> providerSources=new();int providerId=1_000_000;
 internal void InitializeKugou(){TrustedModuleDescriptor.Current.Validate(1);Providers.Mount(Kugou);Kugou.Revoked+=ProviderRevoked;}
 void ProviderRevoked(){
  // Called by UI-thread lifecycle operations. Local playback is never stopped by provider disable.
  if(P2AWorkspace.M223Normal){
   root.Player.RemoveControlledProviderEntries();
   foreach(var id in providerTrackKeys.Keys){items.Remove(id);providerArtworkAccounts.Remove(id);}
   providerTrackKeys.Clear();RebuildTrackSnapshot();collectionDirty=true;
  }
  else if(ProviderStreamRegistry.IsProvider(root.Player.CurrentPlayingSong?.LocalFilePath)){root.Coordinator.InvalidatePendingLoads();commands.Stop();}
  foreach(var source in providerSources)ProviderStreamRegistry.Remove(source);providerSources.Clear();
 }
 internal async Task<int> AdmitKugouAsync(CatalogTrack track,string quality,CancellationToken ct=default){
  var retained=root.Player.PlaybackQueue.Select(x=>x.LocalFilePath)
    .Append(root.Player.CurrentPlayingSong?.LocalFilePath).ToHashSet();
  var unused=items.Where(x=>x.Value.LocalSourceType=="ProviderStream"&&
    !retained.Contains(x.Value.LocalFilePath)).ToArray();
  foreach(var stale in unused)
  {
   items.Remove(stale.Key);providerArtworkAccounts.Remove(stale.Key);providerTrackKeys.Remove(stale.Key);
   if(Kugou.IsDirectSdk&&stale.Value.LocalFilePath is { } orphan &&
      providerSources.Remove(orphan))ProviderStreamRegistry.Remove(orphan);
  }
  if(!Program.Desktop&&items.Count(x=>x.Value.LocalSourceType=="ProviderStream")>=32)
    throw new InvalidOperationException("平台临时曲目上限32，请先移除不用的队列条目");
  // Live source permission is requested when BASS opens the queue entry, not
  // when the user adds it. A 2-minute CDN grant must NEVER be stored in a
  // queue that can wait arbitrarily long.
  if(Kugou.IsDirectSdk)
  {
   if(!Kugou.LoggedIn||Kugou.CatalogTrackForPlaylist(track.Id)==null)
    throw new UnauthorizedAccessException("当前登录账号没有这首歌的已核对元数据，请重新搜索或同步歌单");
   var epoch=Kugou.Generation;
   var alias=Kugou.Account;
   bool SessionCurrent()=>Kugou.Enabled&&Kugou.IsDirectSdk&&Kugou.LoggedIn&&
       Kugou.Generation==epoch&&Kugou.Account==alias;
   string lazyHandle=ProviderStreamRegistry.Register(
    SessionCurrent,
    async loadCancellation=>{
     using var linked=CancellationTokenSource.CreateLinkedTokenSource(loadCancellation,ct);
     var cancellation=linked.Token;
     if(!SessionCurrent())throw new UnauthorizedAccessException("酷狗队列曲目当前会话已撤销");
     // Keep only stage/type diagnostics, never signed query, hash, token, CDN URL or title.
     HuayuanMusic.Provider.Kugou.MediaGrant? fresh=null;
     string stage="SDK_PERMISSION_RESOLVE";
     try
     {
      // Fresh, account-scoped source authorization for EVERY playback and replay.
      var requested=Program.Desktop?R2.DesktopPreferences.Quality:quality;
      var effective=requested;
      if(Program.Desktop && requested!="standard")
      {
       try { effective=await Kugou.UseDesktopCatalogAsync(gateway=>gateway.ReadDesktopQualityAsync(track,requested,cancellation),cancellation); }
       catch(Exception error) when(SessionCurrent() && !cancellation.IsCancellationRequested &&
        error is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or TaskCanceledException)
       { effective="standard"; }
      }
      async Task<HuayuanMusic.Kugou.Gateway.OpenedAudio> OpenAuthorizedQualityAsync()
      {
       stage="SDK_PERMISSION_RESOLVE";
       cancellation.ThrowIfCancellationRequested();
       if(!SessionCurrent())throw new UnauthorizedAccessException("酷狗账号在音源解析期间发生变化");
       fresh=await Kugou.ResolveAsync(track.Id,effective,cancellation);
       cancellation.ThrowIfCancellationRequested();
       if(!SessionCurrent())throw new UnauthorizedAccessException("酷狗账号在音源解析期间发生变化");
       stage="CDN_AUDIO_OPEN";
       return await Kugou.OpenSdkAudioAsync(fresh,cancellation);
      }
      HuayuanMusic.Kugou.Gateway.OpenedAudio opened;
      try { opened=await OpenAuthorizedQualityAsync(); }
      catch(Exception error) when(Program.Desktop && effective!="standard" && SessionCurrent() &&
       !cancellation.IsCancellationRequested && error is UnauthorizedAccessException or HttpRequestException or InvalidDataException)
      {
       if(fresh!=null) Kugou.ReleaseGrant(fresh);
       Program.Log("DESKTOP_QUALITY_FALLBACK",new { from=effective, stage, type=error.GetType().Name });
       fresh=null;
       effective="standard";
       opened=await OpenAuthorizedQualityAsync();
      }
      if(Program.Desktop)Program.Log("DESKTOP_QUALITY",new{requested,effective});
      stage="AUDIO_STREAM_BUFFERED";
      if(Program.Desktop||P2AWorkspace.RealSingleAudio)Program.Log("PROVIDER_SOURCE_STAGE",new{stage,bufferBytes=opened.Length});
      return new ProviderStreamRegistry.Input(opened.Stream,opened.Length,
          new PlaybackGrantAudioOwner(opened,Kugou,fresh!));
     }
     catch(Exception ex)
     {
      if(fresh!=null)Kugou.ReleaseGrant(fresh);
      if(Program.Desktop||P2AWorkspace.RealSingleAudio)Program.Log("PROVIDER_SOURCE_STAGE",new{stage,errorType=ex.GetType().Name});
      throw;
     }
    },!Program.Desktop&&P2AWorkspace.M223Normal?"[00:00.10]合成第一句\n[00:01.00]合成第二句":"",allowPreparation:false);
   try{return AdmitProviderSong(track,lazyHandle,false);}
   catch{ProviderStreamRegistry.Remove(lazyHandle);throw;}
  }
  var grant=await Kugou.ResolveAsync(track.Id,quality,ct);string lyric;
  try{lyric=await Kugou.LyricsAsync(track.Id,ct);}catch{Kugou.ReleaseGrant(grant);throw;}
  if(Kugou.SdkConnected){
   string sdkHandle;
   try{sdkHandle=ProviderStreamRegistry.Register(()=>{try{Kugou.DemandGrant(grant);return true;}catch(InvalidOperationException){return false;}catch(UnauthorizedAccessException){return false;}},async cancel=>{var opened=await Kugou.OpenSdkAudioAsync(grant,cancel);return new ProviderStreamRegistry.Input(opened.Stream,opened.Length,opened);},lyric);}catch{Kugou.ReleaseGrant(grant);throw;}
   return AdmitProviderSong(track,sdkHandle,false);
  }
  // Fixture stream is read ONLY from an already SHA-admitted mathematical WAV, not arbitrary files/URLs.
  var media=Path.Combine(OfflineStartupProfile.Current.Media,"scan","a.wav");
  OfflineStartupProfile.Current.DemandMedia(media);
  string handle=ProviderStreamRegistry.Register(()=>{try{Kugou.DemandGrant(grant);return true;}catch(InvalidOperationException){return false;}catch(UnauthorizedAccessException){return false;}},async cancel=>{
   Kugou.DemandGrant(grant);OfflineStartupProfile.Current.DemandMedia(media);
   using var handler=new FixtureAudioHandler(media);using var client=new HttpClient(handler);
   var response=await client.GetAsync(Kugou.DemandGrant(grant).Address,HttpCompletionOption.ResponseHeadersRead,cancel);
   try{response.EnsureSuccessStatusCode();var stream=await response.Content.ReadAsStreamAsync(cancel);return new ProviderStreamRegistry.Input(stream,response.Content.Headers.ContentLength??0,response);}catch{response.Dispose();throw;}
  },lyric);
  return AdmitProviderSong(track,handle,true);
 }
 int AdmitProviderSong(CatalogTrack track,string handle,bool fixture){
  providerSources.Add(handle);int id=++providerId;
  var song=new SongItem{Cover=HuayuanMusic.Provider.Contracts.ArtworkUrl.Normalize(track.Cover),Name=track.Title+(fixture?"（Mock合成音）":""),Singer=track.Artist,AlbumName=track.Album,DurationSeconds=track.Seconds,Hash="",LocalFilePath=handle,LocalSourceType="ProviderStream",LocalTrackId=0};
  if(P2AWorkspace.M223Normal)providerTrackKeys[id]=$"kugou-concept:{Kugou.DirectAccountCacheKey}:{track.Id}";
  providerArtworkAccounts[id]=Kugou.DirectAccountCacheKey??Kugou.Account??"none";items.Add(id,song);RebuildTrackSnapshot();collectionDirty=true;Publish();return id;
 }
 void RebuildTrackSnapshot()=>trackSnapshot=items.Select(x=>new CoreTrack(x.Key,x.Value.LocalSourceType=="ProviderStream"?providerTrackKeys.GetValueOrDefault(x.Key,"kugou-concept:"+x.Key):LocalMusicRecordStore.Key(x.Value),x.Value.DisplayTitle,x.Value.Singer,x.Value.AlbumName,x.Value.DurationSeconds,x.Value.Cover)).ToArray();
 internal void EnableKugou(){Providers.Enable(KugouProvider.Identity);Kugou.Enable();}
 internal void RemoveKugou(){Providers.Unmount(KugouProvider.Identity);}
 internal void EnableProviderLoadTrace(bool enableSeamless){
  root.Player.IsSeamlessTransitionEnabled=enableSeamless;
  if(root.Coordinator is PlaybackCoordinator coordinator)
   coordinator.ProviderLoadTrace=(operation,origin,phase)=>Program.Log("M215_CORE_LOAD",new{operation,origin,phase});
 }
 internal int ProviderStreamCount=>providerSources.Count;
 internal async Task DisableKugouAsync(){await Kugou.DisableAsync();Providers.Disable(KugouProvider.Identity);collectionDirty=true;Publish();}
 sealed class PlaybackGrantAudioOwner:IDisposable
 {
  readonly HuayuanMusic.Kugou.Gateway.OpenedAudio audio;
  readonly KugouProvider provider;
  readonly MediaGrant grant;
  int disposed;
  internal PlaybackGrantAudioOwner(HuayuanMusic.Kugou.Gateway.OpenedAudio audio,
      KugouProvider provider,MediaGrant grant)
  {this.audio=audio;this.provider=provider;this.grant=grant;}
  public void Dispose()
  {
   if(Interlocked.Exchange(ref disposed,1)!=0)return;
   try{audio.Dispose();}
   finally{provider.ReleaseGrant(grant);}
  }
 }
 sealed class FixtureAudioHandler(string path):HttpMessageHandler {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
   ct.ThrowIfCancellationRequested();if(request.RequestUri?.Scheme!="https"||request.RequestUri.Host!="audio.fixture.invalid")throw new UnauthorizedAccessException("No real media transport");
   OfflineStartupProfile.Current.DemandMedia(path);var content=new StreamContent(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read));
   return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=content});
  }
 }
}
