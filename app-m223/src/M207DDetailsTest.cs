using System.Net;
using System.Text;
using System.Text.Json;
using HuayuanMusic.Provider.Kugou;
using HuayuanMusic.Provider.Contracts;
using KuGou.Net.Clients;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 sealed class DetailFake:HttpMessageHandler {
  internal int Calls;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
   Calls++;using var b=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));var id=b.RootElement.GetProperty("data")[0].GetProperty("global_collection_id").GetString()!;
   if(id=="g4")throw new HttpRequestException("synthetic offline");if(id=="g5")await Task.Delay(10000,ct);
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{status=1,data=new[]{new{global_collection_id=id=="g3"?"wrong":id,pic=id=="g2"?"":"http://imge.kugou.com/a/{size}/cover.jpg"}}}),Encoding.UTF8,"application/json")};
  }
 }
 sealed class DetailGate(PlaylistCoverDetails scope,HttpMessageHandler inner):DelegatingHandler(inner){protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>scope.Send(r,t=>base.SendAsync(r,t),ct);}
 IReadOnlyList<ProviderPlaylist>? m207dLists;
 internal async Task M207DDetails(Action<bool,string> check){
  var session=new KgSessionManager(new CookieContainer());session.UpdateAuth("7","SYNTHETIC_NOT_REAL","0","",null);var scope=new PlaylistCoverDetails(session);scope.Diagnostic=reason=>KugouArtwork.Diagnostic(reason);var fake=new DetailFake();
  using var client=new HttpClient(new KgSignatureHandler(session){InnerHandler=new DetailGate(scope,fake)});var sdk=new PlaylistClient(new RawPlaylistApi(new KgHttpTransport(client),NullLogger<RawPlaylistApi>.Instance),session);
  var lists=Enumerable.Range(0,7).Select(i=>new ProviderPlaylist(KugouProvider.Identity,"g"+i,"Synthetic playlist",Array.Empty<ProviderTrackRef>()){CoverUrl=i==0?"https://imge.kugou.com/a/400/direct.jpg":null}).ToArray();
  using(scope.Begin(lists.Where(x=>x.CoverUrl==null).Select(x=>x.ProviderPlaylistId),CancellationToken.None)){
   for(int i=1;i<7;i++){var id=lists[i].ProviderPlaylistId;var cover=await scope.Fetch(id,()=>sdk.GetInfoAsync(id),CancellationToken.None);lists[i]=lists[i] with{CoverUrl=cover};}
   check(fake.Calls==6,"Seven playlists with six missing list covers issue exactly six bounded details");check(lists.Count(x=>x.CoverUrl!=null)==3,"Details fill only matching valid official covers; absent, mismatch, offline and timeout keep metadata");
   int before=fake.Calls;await scope.Fetch("not-listed",()=>sdk.GetInfoAsync("not-listed"),CancellationToken.None);check(fake.Calls==before,"Unknown playlist cannot use detail permit");
   m207dLists=lists;
   foreach(var kind in new[]{"host","route","account","playlist","body"}){
    bool leaked=false;await scope.Fetch("g1",async()=>{var payload=new{userid=kind=="account"?"8":"7",token="SYNTHETIC_NOT_REAL",data=new[]{new{global_collection_id=kind=="playlist"?"other":"g1"}}};using var req=new HttpRequestMessage(HttpMethod.Post,kind=="host"?"https://evil.test/v3/get_list_info":kind=="route"?"https://gateway.kugou.com/write":"https://gateway.kugou.com/v3/get_list_info"){Content=new StringContent(kind=="body"?"{}":JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json")};req.Headers.Add("x-router","pubsongs.kugou.com");using var res=await scope.Send(req,t=>{leaked=true;throw new Exception("Must not send");},CancellationToken.None);return null;},CancellationToken.None);
    check(!leaked&&fake.Calls==before,"Malformed detail scope rejected before transport "+kind);
   }
   session.UpdateAuth("8","SYNTHETIC_B","0","",null);await scope.Fetch("g1",()=>sdk.GetInfoAsync("g1"),CancellationToken.None);check(fake.Calls==before,"Account switch invalidates old detail scope");
  }
  session.UpdateAuth("7","SYNTHETIC_NOT_REAL","0","",null);using(var cancel=new CancellationTokenSource())using(scope.Begin(new[]{"g5"},cancel.Token)){
   var pending=scope.Fetch("g5",()=>sdk.GetInfoAsync("g5"),cancel.Token);await Task.Delay(50);cancel.Cancel();bool refused=false;try{await pending;}catch(OperationCanceledException){refused=true;}check(refused,"Refresh cancellation interrupts detail IO");
  }
  using(scope.Begin(new[]{"g1"},CancellationToken.None)){int start=fake.Calls;for(int i=0;i<21;i++)await scope.Fetch("g1",()=>sdk.GetInfoAsync("g1"),CancellationToken.None);check(fake.Calls-start==20,"Twenty-query refresh cap is enforced before transport");}
  using(scope.Begin(new[]{"g5"},CancellationToken.None)){var pending=scope.Fetch("g5",()=>sdk.GetInfoAsync("g5"),CancellationToken.None);await Task.Delay(50);scope.Cancel();check(await pending==null,"DLC revocation cancels pending detail without clearing metadata");}
 }
}
