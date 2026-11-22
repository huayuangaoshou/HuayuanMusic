using System.Net;
using System.Text;
using System.Text.Json;
using KuGou.Net.util;

namespace HuayuanPreview;

internal sealed class P2AResponses(string fixture, Func<string> scenario):HttpMessageHandler
{
    const string ListCover="https://imgessl.kugou.com/p2a/{size}/list.png";
    const string SongCover="https://imgessl.kugou.com/p2a/{size}/song.png";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var uri=request.RequestUri!;
        bool vip=(P2AWorkspace.F2Preflight||P2AWorkspace.M223Normal)&&uri.Host=="kugouvip.kugou.com"&&uri.AbsolutePath=="/v1/get_union_vip";
        if(uri.Scheme!="https"||(!vip&&uri.Host!="gateway.kugou.com")||uri.Port!=443)
            throw new UnauthorizedAccessException("Unknown offline origin");
        var query=uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)
            .Select(x=>x.Split('=',2)).ToDictionary(x=>Uri.UnescapeDataString(x[0]),x=>Uri.UnescapeDataString(x[1]));
        var body=request.Content==null?"":await request.Content.ReadAsStringAsync(ct);
        int user=fixture=="A"?7:8;
        if(!query.Remove("signature",out var signature)||signature!=KgSigner.CalcPostSignature(query,body)||
            query.GetValueOrDefault("userid")!=user.ToString()||query.GetValueOrDefault("token")!="P2A_SYNTHETIC_ONLY")
            throw new UnauthorizedAccessException("Offline SDK signature or session mismatch");
        var path=uri.AbsolutePath;
        Program.Log("P2A_SIGNED_REQUEST",new { path, method=request.Method.Method });
        var mode=scenario();
        if(path=="/v7/get_all_list" && mode is "failure" or "expired")
            return new(mode=="failure"?HttpStatusCode.ServiceUnavailable:HttpStatusCode.Unauthorized);
        if(path=="/v7/get_all_list"&&TargetFault=="wire-denied")return new(HttpStatusCode.Forbidden);
        if(path=="/v5/url"&&TargetFault=="url-business")return new(HttpStatusCode.OK)
            {Content=new StringContent("{\"status\":0,\"error_code\":1}",Encoding.UTF8,"application/json")};
        var injected=P2AWorkspace.M213Offline?Environment.GetEnvironmentVariable("M216_SYNTHETIC_FAULT"):null;
        if(path=="/v5/url"&&P2AWorkspace.M213Offline&&
           Environment.GetEnvironmentVariable("M217_OFFLINE_CASE")=="source-timeout")
            await Task.Delay(Timeout.InfiniteTimeSpan,ct);
        if(path=="/v5/url"&&injected=="source")return new(HttpStatusCode.ServiceUnavailable);
        var response=path switch {
            "/v1/get_union_vip" when vip=>new {status=1,data=new {is_vip=injected=="vip"||M223Media.Fault=="entitlement"?0:1,busi_vip=Array.Empty<object>()}},
            "/v5/url" when P2AWorkspace.M223Normal=>new {status=1,hash=query.GetValueOrDefault("hash"),
                priv_status=1,url=new[]{M223Media.Address}},
            "/v5/url" when P2AWorkspace.F2Preflight=>new {status=1,hash=new string('a',32),priv_status=1,
                url=new[]{"http://audio.kugoucdn.com/f2-synthetic.wav"}},
            "/v7/get_all_list"=>Index(TargetFault=="wrong-account"?user+1:user),
            "/pubsongs/v2/get_other_list_file_nofilt"=>Songs(query),
            "/v3/get_list_info"=>await DetailAsync(body,mode,ct),
            "/v3/search/song"=>Search(query.GetValueOrDefault("keyword")),
            _=>throw new UnauthorizedAccessException("Unexpected offline SDK path")
        };
        return new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(response),Encoding.UTF8,"application/json") };
    }

    static string? TargetFault=>P2AWorkspace.M218BTarget?Environment.GetEnvironmentVariable("M218B_TARGET_FAULT"):null;

    object Index(int user)
    {
        var count=fixture=="A"&&TargetFault!="short-index"?7:1;
        return new { status=1,data=new { userid=user,list_count=count,
            info=Enumerable.Range(1,count).Select(i=>new { name=TargetFault=="renamed-content"?$"合成目录 · 同名歌单":$"合成账号 {fixture} · 歌单 {i:00}", global_collection_id=$"p2a-{fixture}-{i}", pic=i==1?ListCover:null }) } };
    }

    object Songs(Dictionary<string,string> query)
    {
        var id=query["global_collection_id"];
        if(!id.StartsWith($"p2a-{fixture}-",StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Foreign synthetic playlist");
        if(TargetFault=="wrong-list")
            return new {status=1,data=new {count=1,global_collection_id="foreign-list",songs=new[]{new {name="合成曲目 01",hash=new string('a',32),timelen=12000}}}};
        int count=id==$"p2a-{fixture}-1"?61:2;
        int offset=int.Parse(query["begin_idx"]);
        return new { status=1,data=new { count,songs=Enumerable.Range(offset,Math.Min(50,count-offset)).Select(i=>new {
            name=TargetFault=="renamed-content"?"变更后的合成曲目":$"合成曲目 {i+1:00}",hash=TargetFault=="wrong-member"?"bad":i<2?new string('a',32):(i+10).ToString("x32"),timelen=P2AWorkspace.M223Normal?8000:12000,
            cover=i%2==0?SongCover:null,albuminfo=new { name="合成专辑" }
        }) } };
    }

    static async Task<object> DetailAsync(string body,string mode,CancellationToken ct)
    {
        using var document=JsonDocument.Parse(body);
        var id=document.RootElement.GetProperty("data")[0].GetProperty("global_collection_id").GetString()!;
        if(id.EndsWith("-5",StringComparison.Ordinal))throw new HttpRequestException("Synthetic detail unavailable");
        if(id.EndsWith("-2",StringComparison.Ordinal)&&mode=="slow")await Task.Delay(TimeSpan.FromSeconds(10),ct);
        var pic=id.EndsWith("-2",StringComparison.Ordinal)||id.EndsWith("-3",StringComparison.Ordinal)?ListCover:
            id.EndsWith("-4",StringComparison.Ordinal)?"https://invalid.example/cover.png":null;
        return new { status=1,data=new[]{new {global_collection_id=id.EndsWith("-3",StringComparison.Ordinal)?"wrong":id,pic} } };
    }

    static object Search(string? keyword)
    {
        var song=new { FileHash=new string('a',32),FileName="合成曲目 01",SingerName="离线测试",AlbumName="合成专辑",Duration=12,Image=SongCover };
        return new { status=1,data=new { total=1,lists=keyword!=null&&song.FileName.Contains(keyword,StringComparison.OrdinalIgnoreCase)?new[]{song}:[] } };
    }
}

internal sealed class P2AImageResponses:HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var uri=request.RequestUri!;
        if(request.Method!=HttpMethod.Get||uri.Scheme!="https"||uri.Host!="imgessl.kugou.com"||
            !uri.AbsolutePath.StartsWith("/p2a/400/",StringComparison.Ordinal)||
            Path.GetFileName(uri.AbsolutePath) is not ("list.png" or "song.png"))
            throw new UnauthorizedAccessException("Unknown synthetic artwork");
        using var image=new SkiaSharp.SKBitmap(120,120);
        using var canvas=new SkiaSharp.SKCanvas(image);
        canvas.Clear(uri.AbsolutePath.EndsWith("list.png")?SkiaSharp.SKColor.Parse("#245A9C"):SkiaSharp.SKColor.Parse("#227871"));
        using var ink=new SkiaSharp.SKPaint { Color=SkiaSharp.SKColors.White,IsAntialias=true };
        canvas.DrawCircle(60,60,34,ink);
        ink.Color=SkiaSharp.SKColor.Parse("#253A5D");
        canvas.DrawCircle(60,60,13,ink);
        using var png=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
        var content=new ByteArrayContent(png.ToArray());
        content.Headers.ContentType=new("image/png");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
    }
}
