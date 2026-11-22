using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HuayuanMusic.Provider.Contracts;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
namespace HuayuanPreview;
// One existing bounded cache for all accounts. Account is part of the opaque cache key.
internal sealed class KugouArtwork : IDisposable {
 readonly BoundedDiskCachedWebImageLoader cache;readonly HttpClient client;readonly SemaphoreSlim slots=new(4);CancellationTokenSource session=new();bool disposed;
 internal Func<bool> Online=()=>false;internal Func<string> Account=()=>"none";
 internal KugouArtwork(HttpMessageHandler? synthetic=null,long diskBytes=256L*1024*1024,int entries=200,bool r2Offline=false){
  if(synthetic!=null&&!OfflineGuiApplication.SelfTest&&!(r2Offline&&P2AWorkspace.Admitted))throw new UnauthorizedAccessException("Synthetic artwork transport only in admitted tests");
  var wire=synthetic??(OfflineGuiApplication.SelfTest||r2Offline?new Denied():new SocketsHttpHandler{UseCookies=false,AllowAutoRedirect=false,UseProxy=false,Credentials=null});
  client=new HttpClient(new Restricted(this,wire)){Timeout=TimeSpan.FromSeconds(8)};
  cache=new BoundedDiskCachedWebImageLoader(Path.Combine(AppDataPaths.LocalRoot,"kugou-artwork-cache"),TimeSpan.FromDays(7),client,entries,32L*1024*1024,diskBytes);
 }
 static readonly System.Collections.Concurrent.ConcurrentDictionary<string,int> diagnosticCounts=new();
 internal static void Diagnostic(string category,int? status=null){var count=diagnosticCounts.AddOrUpdate(category,1,(_,n)=>n+1);if(count>8&&(count&(count-1))!=0)return;Program.Log("ARTWORK_DIAGNOSTIC",new{category,status});}
 internal static string Key(string account,string source)=>"https://artwork.invalid/"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account)))+"/"+Convert.ToHexString(Encoding.UTF8.GetBytes(source));
 internal Task<Bitmap?> Load(string account,string? source,int width,bool online,CancellationToken ct)=>LoadCore(account,source,width,online,ct);
 async Task<Bitmap?> LoadCore(string account,string? source,int width,bool online,CancellationToken ct){
  var url=ArtworkUrl.Normalize(source);if(url==null||disposed){Diagnostic(string.IsNullOrWhiteSpace(source)?"NO_FIELD":"URL_REJECTED");return null;}
  var epoch=session.Token;using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,epoch);
  await slots.WaitAsync(linked.Token).ConfigureAwait(false);
  try{linked.Token.ThrowIfCancellationRequested();
   // Offline-only keys share cached bytes but never acquire network permission.
   var key=Key(account,url);var permit=online&&Online()&&account==Account();
   using var lease=new Lease(this,key,permit,linked.Token);
   var bitmap=await Task.Run(()=>cache.ProvideImageAsync(key,Math.Clamp(width,1,600)),linked.Token).ConfigureAwait(false);
   if(linked.IsCancellationRequested){bitmap?.Dispose();return null;}return bitmap;
  }finally{slots.Release();}
 }
 readonly Dictionary<string,List<(bool Permit,CancellationToken Token)>> leases=new();readonly object sync=new();
 sealed class Lease:IDisposable{readonly KugouArtwork owner;readonly string key;readonly (bool,CancellationToken) item;internal Lease(KugouArtwork o,string k,bool p,CancellationToken t){owner=o;key=k;item=(p,t);lock(o.sync){if(!o.leases.TryGetValue(k,out var l))o.leases[k]=l=new();l.Add(item);}}public void Dispose(){lock(owner.sync){if(owner.leases.TryGetValue(key,out var l)){l.Remove(item);if(l.Count==0)owner.leases.Remove(key);}}}}
 internal void Revoke(){if(disposed)return;var old=session;session=new();old.Cancel();old.Dispose();}
 public void Dispose(){if(disposed)return;disposed=true;session.Cancel();session.Dispose();cache.Dispose();client.Dispose();}
 sealed class Denied:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>throw new UnauthorizedAccessException("Offline artwork transport refused");}
 sealed class Restricted(KugouArtwork owner,HttpMessageHandler inner):DelegatingHandler(inner){
  async Task<HttpResponseMessage> SendImage(HttpRequestMessage r,CancellationToken ct){try{return await base.SendAsync(r,ct);}catch(OperationCanceledException){Diagnostic("REQUEST_TIMEOUT_OR_CANCELLED");throw;}catch(HttpRequestException){Diagnostic("REQUEST_FAILED");throw;}}
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
   var virtualUrl=r.RequestUri?.AbsoluteUri??"";CancellationToken leaseToken=default;bool allow=false;
   lock(owner.sync)if(owner.leases.TryGetValue(virtualUrl,out var list)){var live=list.FirstOrDefault(x=>x.Permit&&!x.Token.IsCancellationRequested);allow=live.Permit;leaseToken=live.Token;}
   if(!allow||!owner.Online()||!virtualUrl.StartsWith("https://artwork.invalid/"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.Account())))+"/",StringComparison.Ordinal)){Diagnostic("NETWORK_GATE_DENIED");return new HttpResponseMessage(HttpStatusCode.Forbidden);}
   using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,leaseToken);
   var text=Encoding.UTF8.GetString(Convert.FromHexString(r.RequestUri!.Segments.Last()));var clean=ArtworkUrl.Normalize(text);
   if(clean!=text||r.Method!=HttpMethod.Get||r.Headers.Any(h=>h.Key is "Cookie" or "Authorization"))throw new UnauthorizedAccessException("Invalid artwork origin");
   using var outgoing=new HttpRequestMessage(HttpMethod.Get,clean);
   using var response=await SendImage(outgoing,linked.Token).ConfigureAwait(false);
   if(response.StatusCode!=HttpStatusCode.OK||response.Content.Headers.ContentLength>4*1024*1024){Diagnostic("REQUEST_FAILED",(int)response.StatusCode);throw new InvalidDataException("Artwork response refused");}
   if(response.Content.Headers.ContentType?.MediaType is not("image/png" or "image/jpeg" or "image/webp"))throw new InvalidDataException("Artwork MIME refused");
   using var stream=await response.Content.ReadAsStreamAsync(linked.Token);using var data=new MemoryStream();var buf=new byte[8192];int n;
   while((n=await stream.ReadAsync(buf,linked.Token))>0){if(data.Length+n>4*1024*1024)throw new InvalidDataException("Artwork byte limit");data.Write(buf,0,n);}
   var bytes=data.ToArray();using var codec=SkiaSharp.SKCodec.Create(new MemoryStream(bytes));
   if(codec==null){Diagnostic("DECODE_FAILED");throw new InvalidDataException("Artwork decode refused");}
   if(codec.Info.Width is <=0 or >4096||codec.Info.Height is <=0 or >4096||(long)codec.Info.Width*codec.Info.Height>4_194_304){Diagnostic("DECODE_FAILED");throw new InvalidDataException("Artwork pixel limit");}
   linked.Token.ThrowIfCancellationRequested();return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
  }
 }
}
public sealed partial class PreviewWindow {
 KugouArtwork? kugouArtwork;
 internal KugouArtwork Artwork=>kugouArtwork??=CreateArtwork();
 KugouArtwork CreateArtwork(){var a=new KugouArtwork();a.Account=ArtworkAccount;a.Online=()=>core is CorePlaybackAdapter c&&kugouConsent.Approved&&c.Kugou.SdkConnected&&c.Kugou.LoggedIn;Closed+=(_,_)=>a.Dispose();return a;}
 void RevokeArtwork()=>kugouArtwork?.Revoke();
 string ArtworkAccount()=>core is CorePlaybackAdapter a?a.Kugou.DirectAccountCacheKey??a.Kugou.Account??"none":"none";
 ArtworkTile ArtworkImage(string? source,double size,string? account=null,bool online=true,Func<bool>? eligible=null)=>new(Artwork,account??ArtworkAccount(),source,size,online,eligible);
 internal sealed class ArtworkTile:Border {
  readonly KugouArtwork loader;readonly string account;readonly string? source;readonly bool online;readonly Func<bool>? eligible;readonly Image image;CancellationTokenSource? pending;Bitmap? owned;int version;
  internal string? Identity=>source;internal bool IsArtworkLoaded=>image.Source is Bitmap;internal Bitmap? Current=>image.Source as Bitmap;
  internal ArtworkTile(KugouArtwork loader,string account,string? source,double size,bool online,Func<bool>? eligible){this.loader=loader;this.account=account;this.source=ArtworkUrl.Normalize(source);this.online=online;this.eligible=eligible;Width=Height=size;Background=Brush.Parse("#e6e9ef");CornerRadius=new(5);ClipToBounds=true;
   image=new Image{Stretch=Stretch.UniformToFill};Child=new Grid{Children={new TextBlock{Text="♫",Foreground=Brushes.Gray,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center},image}};
   AttachedToVisualTree+=(_,_)=>Reload();DetachedFromVisualTree+=(_,_)=>Cancel();
  }
  internal void Cancel(bool keepBitmap=false){version++;pending?.Cancel();pending?.Dispose();pending=null;if(!keepBitmap){image.Source=null;owned?.Dispose();owned=null;}}
  internal void Reload(){Cancel();if(source==null||eligible?.Invoke()==false)return;pending=new();_ = Apply(version,pending.Token);}
  async Task Apply(int epoch,CancellationToken token){Bitmap? bitmap=null;try{bitmap=await loader.Load(account,source,(int)Math.Ceiling(Width*(TopLevel.GetTopLevel(this)?.RenderScaling??1)),online,token);await Dispatcher.UIThread.InvokeAsync(()=>{if(epoch!=version||token.IsCancellationRequested){bitmap?.Dispose();bitmap=null;return;}owned=bitmap;image.Source=bitmap;bitmap=null;});}catch(OperationCanceledException){bitmap?.Dispose();KugouArtwork.Diagnostic("CANCELLED");}catch{bitmap?.Dispose();KugouArtwork.Diagnostic("LOAD_FAILED");} }
 }
}
