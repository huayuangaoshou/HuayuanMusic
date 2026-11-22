using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Protocol.Session;
using HuayuanMusic.Provider.Contracts;
namespace HuayuanMusic.Provider.Kugou;
// A per-refresh permit, not a general-purpose endpoint authorization.
public sealed class PlaylistCoverDetails(KgSessionManager session) {
 HashSet<string> ids=new(StringComparer.Ordinal);CancellationTokenSource? active;string owner="";int remaining,wireRemaining;
 public Action<string>? Diagnostic;
 public IDisposable Begin(IEnumerable<string> missing,CancellationToken ct){Cancel();owner=session.Session.UserId;ids=missing.Take(20).ToHashSet(StringComparer.Ordinal);remaining=20;wireRemaining=20;active=CancellationTokenSource.CreateLinkedTokenSource(ct);active.CancelAfter(TimeSpan.FromSeconds(30));return new End(this);}
 public void Cancel(){active?.Cancel();active?.Dispose();active=null;ids.Clear();}
 sealed class End(PlaylistCoverDetails scope):IDisposable{public void Dispose()=>scope.Cancel();}
 public async Task<string?> Fetch(string id,Func<Task<PlaylistInfo?>> read,CancellationToken ct){
  if(active==null||!ids.Contains(id)||remaining--<=0){Diagnostic?.Invoke("DETAIL_LIMIT");return null;}
  try{ct.ThrowIfCancellationRequested();active.Token.ThrowIfCancellationRequested();if(owner!=session.Session.UserId)throw new UnauthorizedAccessException();
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(active.Token);timeout.CancelAfter(TimeSpan.FromSeconds(3));current=timeout.Token;
   var result=await read().WaitAsync(timeout.Token);ct.ThrowIfCancellationRequested();timeout.Token.ThrowIfCancellationRequested();
   if(owner!=session.Session.UserId||result?.GlobalId!=id){Diagnostic?.Invoke("DETAIL_IDENTITY_REJECTED");return null;}
   var cover=ArtworkUrl.Normalize(result.PicUrl);Diagnostic?.Invoke(cover==null?"DETAIL_NO_COVER":"DETAIL_COVER_FOUND");return cover;
  }catch(Exception e) when(e is not OutOfMemoryException){ct.ThrowIfCancellationRequested();Diagnostic?.Invoke("DETAIL_FAILED");return null;}
 }
 CancellationToken current;
 public async Task<HttpResponseMessage> Send(HttpRequestMessage request,Func<CancellationToken,Task<HttpResponseMessage>> send,CancellationToken ct){
  var uri=request.RequestUri;
  if(active==null||active.IsCancellationRequested||current.IsCancellationRequested||owner=="0"||string.IsNullOrWhiteSpace(session.Session.Token)||owner!=session.Session.UserId||uri==null||uri.Scheme!="https"||uri.Host!="gateway.kugou.com"||uri.Port!=443||uri.AbsolutePath!="/v3/get_list_info"||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||request.Method!=HttpMethod.Post||request.Headers.Contains("Cookie")||request.Headers.Contains("Authorization")||!request.Headers.TryGetValues("x-router",out var routers)||!routers.SequenceEqual(new[]{"pubsongs.kugou.com"})||request.Content==null||request.Content.Headers.ContentType?.MediaType!="application/json")throw new UnauthorizedAccessException("Playlist detail scope refused");
  var query=System.Web.HttpUtility.ParseQueryString(uri.Query);if(query.GetValues("userid") is { } uid&&(uid.Length!=1||uid[0]!=owner)||query.GetValues("token") is { } token&&(token.Length!=1||token[0]!=session.Session.Token))throw new UnauthorizedAccessException("Detail query identity refused");
  if(Interlocked.Decrement(ref wireRemaining)<0)throw new UnauthorizedAccessException("Detail wire budget exceeded");
  await request.Content.LoadIntoBufferAsync(8192);using var body=JsonDocument.Parse(await request.Content.ReadAsByteArrayAsync(ct));var obj=body.RootElement;
  if(obj.EnumerateObject().Count()!=3||obj.GetProperty("userid").GetString()!=owner||obj.GetProperty("token").GetString()!=session.Session.Token||obj.GetProperty("data").GetArrayLength()!=1)throw new UnauthorizedAccessException("Playlist detail account/body refused");
  var item=obj.GetProperty("data")[0];if(item.EnumerateObject().Count()!=1||!ids.Contains(item.GetProperty("global_collection_id").GetString()??""))throw new UnauthorizedAccessException("Playlist detail identity refused");
  using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,current,active.Token);var response=await send(linked.Token);try{await response.Content.LoadIntoBufferAsync(1_048_576,linked.Token);return response;}catch{response.Dispose();throw;}
 }
}
