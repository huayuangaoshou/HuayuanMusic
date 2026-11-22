using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HuayuanMusic.Provider.Contracts;
using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 sealed class ArtworkSynthetic:HttpMessageHandler {
  internal int Calls,Active,Maximum;internal bool Block;internal readonly List<string> Paths=new();
  internal static byte[] Png(SkiaSharp.SKColor color,int w=32,int h=32){using var bmp=new SkiaSharp.SKBitmap(w,h);bmp.Erase(color);using var image=SkiaSharp.SKImage.FromBitmap(bmp);using var data=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);return data.ToArray();}
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
   ct.ThrowIfCancellationRequested();Interlocked.Increment(ref Calls);lock(Paths)Paths.Add(r.RequestUri!.AbsolutePath);
   if(Block)throw new HttpRequestException("Synthetic disconnected");if(r.Headers.Any()||r.RequestUri!.Query.Length!=0)throw new Exception("Unexpected artwork credentials");
   int n=Interlocked.Increment(ref Active);Maximum=Math.Max(Maximum,n);try{await Task.Delay(r.RequestUri!.AbsolutePath.Contains("slow")?600:35,ct);
    string path=r.RequestUri.AbsolutePath;var content=new ByteArrayContent(path.Contains("bad")?new byte[]{1,2,3}:Png(path.Contains("blue")?SkiaSharp.SKColors.Blue:SkiaSharp.SKColors.Red,path.Contains("pixels")?5000:32,32));content.Headers.ContentType=new("image/png");
    if(path.Contains("large"))content.Headers.ContentLength=5*1024*1024;
    return new(path.Contains("redirect")?HttpStatusCode.Redirect:path.Contains("missing")?HttpStatusCode.NotFound:HttpStatusCode.OK){Content=content};
   }finally{Interlocked.Decrement(ref Active);}
  }
 }
 internal async Task M207BArtworkTest(OfflineWindowRoot root,CorePlaybackAdapter adapter,CatalogTrack song,IReadOnlyList<ProviderPlaylist> sdkLists){
  int checks=0;void Check(bool ok,string label){Program.Log(ok?"M207B_PASS":"M207B_FAIL",new{label});if(!ok)throw new Exception(label);checks++;}
  async Task Shot(string name){var path=Path.Combine(Program.Session,name+".ready");Program.Log("CAPTURE",new{name,hwnd=TryGetPlatformHandle()!.Handle.ToInt64(),RenderScaling});File.WriteAllText(path,"ready");for(int i=0;i<250&&!File.Exists(path+".done");i++)await Task.Delay(20);Check(File.Exists(path+".done"),"Screenshot "+name);}
  async Task Click(Control c,string text){c.GetVisualDescendants().OfType<Button>().First(b=>b.Content?.ToString()==text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await PendingCoreOperations;await Task.Delay(180);}
  async Task Settle(){for(int i=0;i<80;i++){await Task.Delay(50);if(kugouOfficialPlaylists!.GetVisualDescendants().OfType<ArtworkTile>().Where(t=>t.Identity!=null).All(t=>t.IsArtworkLoaded))return;}throw new Exception("Artwork not settled");}
  Check(song.Cover=="https://imgessl.kugou.com/song/400/a.png"&&sdkLists[0].CoverUrl=="https://imgessl.kugou.com/list/400/a.png"&&sdkLists[0].Tracks[0].CoverUrl==song.Cover&&sdkLists[0].Tracks[0].AlbumId==17,"Existing SDK parser and Integrated gateway preserve playlist/song/album identity");
  foreach(var bad in new[]{"http://evil.test/a.png","https://evil.test/a.png","https://imgessl.kugou.com.evil.test/a.png","https://user:pass@imgessl.kugou.com/a.png","https://imgessl.kugou.com/a.png?token=secret","https://imgessl.kugou.com/token/secret.png","file:///a.png","https://127.0.0.1/a.png","https://imgessl.kugou.com/%74oken.png"})Check(ArtworkUrl.Normalize(bad)==null,"Unsafe image metadata refused "+checks);
  if(Environment.GetEnvironmentVariable("M207C_REPAIR_SUITE")=="1"){
   Check(ArtworkUrl.Normalize("http://imge.kugou.com/a/{size}/cover.jpg")=="https://imge.kugou.com/a/400/cover.jpg","Legacy HTTP metadata upgrades to HTTPS without HTTP permission");
   Check(ArtworkUrl.Normalize(" //imgessl.kugou.com/a/%7Bsize%7D/cover.jpg ")=="https://imgessl.kugou.com/a/400/cover.jpg","Protocol-relative encoded size template is compatible");
   var alternate=JsonSerializer.Deserialize<KuGou.Net.Abstractions.Models.PlaylistSong>("{\"albuminfo\":{\"sizable_cover\":\"http://imge.kugou.com/a/{size}/cover.jpg\"}}");Check(ArtworkUrl.Normalize(alternate?.Cover??alternate?.Album?.Cover)=="https://imge.kugou.com/a/400/cover.jpg","Nested album metadata cover fallback");
   Check(ArtworkUrl.Normalize("http://imge.kugou.com/a.jpg?token=x")==null,"Upgrading never admits sensitive query parameters");
  }
  if(Environment.GetEnvironmentVariable("M207D_PLAYLIST_SUITE")=="1")await M207DDetails(Check);
  var wire=new ArtworkSynthetic();kugouArtwork?.Dispose();kugouArtwork=new KugouArtwork(wire);var art=kugouArtwork;bool online=true;string account=adapter.Kugou.Account!;art.Account=()=>account;art.Online=()=>online;Closed+=(_,_)=>art.Dispose();
  await kugouConsent.RequestAsync(()=>Task.FromResult(true));kugouConsent.Reconcile(true,account,null);
  string Url(string name)=>"https://imgessl.kugou.com/"+name+".png";
  var tracks=Enumerable.Range(1,61).Select(i=>new ProviderTrackRef(KugouProvider.Identity,song.Id,"封面合成曲目 "+i,"合成歌手","合成专辑"){CoverUrl=Url(i%2==0?"blue-"+i:"red-"+i),AlbumId=17}).ToArray();
  var lists=new[]{new ProviderPlaylist(KugouProvider.Identity,"art-a","红色封面歌单 · 合成",tracks){CoverUrl=Url("red-list")},new ProviderPlaylist(KugouProvider.Identity,"art-b","蓝色封面歌单 · 合成",tracks.Take(3).ToArray()){CoverUrl=Url("blue-list")}};
  playlistCatalogSelfTest=_=>song with{Cover=Url("red-1")};ShowKugouOfficialPlaylists(adapter,lists);await Task.Delay(250);Check(wire.Calls==2,"Collapsed playlist headers load without opening song pages");
  var section=kugouOfficialPlaylists!;var ex=section.Children.OfType<Expander>().ToArray();foreach(var e in ex)e.IsExpanded=true;await Task.Delay(200);Check(wire.Calls==2,"Expanding loads only two playlist headers");
  var view=(StackPanel)ex[0].Content!;await Click(view,"查看曲目（每页 50 首）");await Settle();Check(view.GetVisualDescendants().OfType<ArtworkTile>().Count()==50,"Only current fifty rows have artwork");
  var tiles=view.GetVisualDescendants().OfType<ArtworkTile>().ToArray();
  using(var mem=new MemoryStream()){tiles[1].Current!.Save(mem,Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);using var b=SkiaSharp.SKBitmap.Decode(mem.ToArray());Check(b.GetPixel(0,0).Blue>200&&b.GetPixel(0,0).Red<20,"Decoded blue belongs to second song");}
  Check(wire.Maximum<=4,"Image pipeline concurrency never exceeds four");
  kugouPageScroll!.Offset=new Vector(0,section.TranslatePoint(new Point(0,0),kugouPageCache!)!.Value.Y);await Task.Delay(100);await Shot("artwork-fifty");
  view.GetVisualDescendants().OfType<Button>().Where(b=>b.Content?.ToString()=="播放此曲").Skip(1).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await PendingCoreOperations;Check(adapter.State.Tracks.First(t=>t.Id==adapter.State.CurrentId).Cover==tracks[1].CoverUrl,"Selecting second song admits its own blue cover despite a shared audio hash");
  ex[0].IsExpanded=false;await Click((StackPanel)ex[1].Content!,"查看曲目（每页 50 首）");await Shot("artwork-two-playlists");ex[0].IsExpanded=true;await Task.Delay(150);
  await Click(view,"下一页");await Settle();Check(view.GetVisualDescendants().OfType<ArtworkTile>().Count()==11,"Second page has eleven corresponding covers");
  await Click(view,"播放此曲");Check(adapter.State.Playing&&adapter.State.Tracks.First(x=>x.Id==adapter.State.CurrentId).Cover==Url("red-51"),"SongItem and core snapshot retain artwork during real shared BASS play");
  Check(playerArtwork!.GetVisualDescendants().OfType<ArtworkTile>().Single().Identity==Url("red-51"),"Bottom player artwork identity matches admitted song");
  SetQueue(true);await Task.Delay(150);Check(queueCurrentArtwork!.GetVisualDescendants().OfType<ArtworkTile>().Single().Identity==Url("red-51"),"Unique queue artwork matches same song");SetQueue(false);
  Go("正在播放");await Task.Delay(200);Check(this.GetVisualDescendants().OfType<ArtworkTile>().Any(t=>t.Identity==Url("red-51")&&t.Width>200),"Playback detail uses same song cover");await Shot("artwork-playing");Go("平台");await Task.Delay(100);await Shot("artwork-eleven");
  var detached=ArtworkImage(Url("slow-detached"),36,account);section.Children.Add(detached);await Task.Delay(60);section.Children.Remove(detached);await Task.Delay(650);Check(!detached.IsArtworkLoaded&&detached.Current==null,"Unmounted control cancels and releases late bitmap");
  PlatformPlaylistSnapshotStore.Save(new string('A',64),lists);var restored=PlatformPlaylistSnapshotStore.LoadLatest();Check(restored!.Playlists[0].CoverUrl==lists[0].CoverUrl&&restored.Playlists[0].Tracks[60].CoverUrl==tracks[60].CoverUrl,"Version1 roundtrip retains all artwork identities");
  bool refused=false;try{PlatformPlaylistSnapshotStore.Save(new string('B',64),new[]{lists[0] with{CoverUrl=Url("red")+"?token=secret"}});}catch(ArgumentException){refused=true;}Check(refused,"Secret URL rejected before snapshot persistence");
  var legacyJson=JsonSerializer.Serialize(restored);var node=System.Text.Json.Nodes.JsonNode.Parse(legacyJson)!;foreach(var list in node["Playlists"]!.AsArray()){list!.AsObject().Remove("CoverUrl");foreach(var tr in list["Tracks"]!.AsArray()){tr!.AsObject().Remove("CoverUrl");tr.AsObject().Remove("AlbumId");}}
  var legacy=JsonSerializer.Deserialize<PlatformPlaylistSnapshot>(node.ToJsonString())!;Check(legacy.Version==1&&legacy.Playlists[0].CoverUrl==null&&legacy.Playlists[0].Tracks[0].CoverUrl==null,"Old Version1 snapshot needs no migration or network");
  var legacyPath=Path.Combine(OfflineStartupProfile.Current.Local,"provider-playlist-snapshots","kugou-"+new string('A',64)+".json");File.WriteAllText(legacyPath,node.ToJsonString());int legacyRequests=wire.Calls;var oldRead=PlatformPlaylistSnapshotStore.LoadLatest();Check(oldRead?.Playlists[0].CoverUrl==null&&wire.Calls==legacyRequests&&File.ReadAllText(legacyPath)==node.ToJsonString(),"Real snapshot reader restores old bytes without rewriting or networking");
  foreach(var name in new[]{"bad","missing","redirect","large","pixels"}){try{using var bitmap=await art.Load(account,Url(name),64,true,CancellationToken.None);Check(bitmap==null,"Invalid image stays placeholder "+name);}catch{Check(true,"Invalid image refused "+name);}}
  var joinedBefore=wire.Calls;var joined=await Task.WhenAll(Enumerable.Range(0,3).Select(_=>art.Load(account,Url("joined-image"),64,true,CancellationToken.None)));Check(joined.All(b=>b!=null)&&wire.Calls==joinedBefore+1,"Concurrent identical requests merge in the original bounded cache");foreach(var image in joined)image?.Dispose();
  using(var cancel=new CancellationTokenSource()){int started=wire.Calls;var queued=Enumerable.Range(0,16).Select(i=>art.Load(account,Url("slow-queued-"+i),64,true,cancel.Token)).ToArray();await Task.Delay(70);cancel.Cancel();try{var unused=await Task.WhenAll(queued);foreach(var image in unused)image?.Dispose();}catch(OperationCanceledException){}Check(wire.Calls-started<=4&&wire.Active==0,"Cancelled page drops queued requests and closes in-flight reads");}
  wire.Block=true;bool disconnected=false;try{using var bitmap=await art.Load(account,Url("offline"),64,true,CancellationToken.None);disconnected=bitmap==null;}catch{disconnected=true;}wire.Block=false;Check(disconnected,"Disconnected image request does not remove song metadata");
  int before=wire.Calls;using(var cached=await art.Load(account,tracks[0].CoverUrl,64,false,CancellationToken.None))Check(cached!=null&&wire.Calls==before,"Offline cache hit has no transport request");
  using(var absent=await art.Load(account,Url("not-cached"),64,false,CancellationToken.None))Check(absent==null&&wire.Calls==before,"Offline cache miss never reaches transport");
  var slow=art.Load(account,Url("slow-red"),64,true,CancellationToken.None);await Task.Delay(70);art.Revoke();bool revoked=false;try{using var b=await slow;revoked=b==null;}catch(OperationCanceledException){revoked=true;}Check(revoked,"Revocation cancels outstanding fetch and discards its result");
  account="other-account";before=wire.Calls;using(var wrong=await art.Load(adapter.Kugou.Account!,Url("new-old-account"),64,true,CancellationToken.None))Check(wrong==null&&wire.Calls==before,"Old account cannot start requests in new account context");
  using(var other=await art.Load(account,tracks[0].CoverUrl,64,true,CancellationToken.None))Check(other!=null&&wire.Calls==before+1,"Different account does not reuse prior account cache entry");account=adapter.Kugou.Account!;
  online=false;before=wire.Calls;ShowKugouOfficialPlaylists(adapter,lists,true,DateTimeOffset.UtcNow,new string('A',64));foreach(var e in section.Children.OfType<Expander>())e.IsExpanded=true;await Task.Delay(150);await Click((StackPanel)section.Children.OfType<Expander>().First().Content!,"查看曲目（每页 50 首）");Check(wire.Calls==before,"Offline snapshot rendering starts no image network requests");await Shot("artwork-offline");
  var disk=Path.Combine(OfflineStartupProfile.Current.Local,"synthetic-artwork-eviction");Directory.CreateDirectory(disk);
  for(int i=0;i<4;i++)File.WriteAllBytes(Path.Combine(disk,"synthetic-"+i),new byte[100]);
  var evictionWire=new ArtworkSynthetic();using(var client=new HttpClient(evictionWire))using(var bounded=new BoundedDiskCachedWebImageLoader(disk,TimeSpan.FromDays(7),client,1,1024,256)){
   string one=Url("eviction-1"),two=Url("eviction-2");using(var b=await bounded.ProvideImageAsync(one,32)){};for(int i=0;i<50&&Directory.GetFiles(disk).Sum(f=>new FileInfo(f).Length)>256;i++)await Task.Delay(30);
   Check(Directory.GetFiles(disk).Sum(f=>new FileInfo(f).Length)<=256,"Existing global disk cache maintenance evicts above budget");
   using(var b=await bounded.ProvideImageAsync(two,32)){};var file=Path.Combine(disk,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(one))).ToLowerInvariant());if(File.Exists(file))File.Delete(file);
   int calls=evictionWire.Calls;using(var b=await bounded.ProvideImageAsync(one,32)){};Check(evictionWire.Calls==calls+1,"Existing one-entry memory LRU evicts older encoded image");
  }
  var local=adapter.State.Tracks.First(t=>t.Id<1000000).Id;await adapter.PlayAsync(local);Check(root.Coordinator.Player.IsPlaying,"Local BASS unaffected by unavailable cover transport");await adapter.StopAsync();
  if(Environment.GetEnvironmentVariable("M207D_PLAYLIST_SUITE")=="1"){
   online=true;ShowKugouOfficialPlaylists(adapter,m207dLists!);await Task.Delay(350);
   Check(section.Children.OfType<Expander>().Count()==7&&section.Children.OfType<Expander>().All(e=>!e.IsExpanded)&&section.GetVisualDescendants().OfType<ArtworkTile>().Count(t=>t.IsArtworkLoaded)==3,"Seven collapsed headers show three genuine provided covers, four explicit placeholders");
   kugouPageScroll!.Offset=new Vector(0,section.TranslatePoint(new Point(0,0),kugouPageCache!)!.Value.Y);await Task.Delay(100);await Shot("playlist-seven-collapsed");online=false;
  }
  Program.Log("M207B_COMPLETE",new{checks,imageRequests=wire.Calls,maxConcurrency=wire.Maximum,realRequests=0});
 }
}
