using System.Net;
using System.Net.Http.Headers;
using HuayuanMusic.Kugou.Gateway;
namespace HuayuanMusic.Provider.Kugou;

// Synthetic media response tests. A controlled in-memory handler is injected
// ONLY when the SDK is itself in synthetic offline mode. Zero live requests.
public sealed partial class DirectSdkGateway
{
    sealed class MediaFixtureHandler(Func<HttpRequestMessage,HttpResponseMessage> responder):HttpMessageHandler
    {
        internal int Forwarded,HttpSeen;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            Interlocked.Increment(ref Forwarded);
            if(req.RequestUri?.Scheme!="https"){Interlocked.Increment(ref HttpSeen);throw new UnauthorizedAccessException("TEST_HTTP_LEAK");}
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(responder(req));
        }
    }

    static HttpResponseMessage MediaReply(HttpStatusCode status=HttpStatusCode.OK,
       string mime="audio/mpeg",int bytes=2048,long? declared=null)
    {
        var content=new ByteArrayContent(new byte[bytes]);
        content.Headers.ContentType=new MediaTypeHeaderValue(mime);
        if(declared.HasValue)content.Headers.ContentLength=declared.Value;
        return new HttpResponseMessage(status){Content=content};
    }

    public static async Task<(int Passed,int Cases,int LeakedHttp)> TestMediaOpenBranchesAsync()
    {
        int cases=0,passed=0,httpLeak=0;
        async Task Case(string name,string source,Func<HttpRequestMessage,HttpResponseMessage> fake,
                        Type? expect=null,bool revoke=false,bool cancel=false,
                        string? requiredStage=null,int expectedForwards=1)
        {
            cases++;
            var stages=new List<string>();
            var h=new MediaFixtureHandler(fake);
            // Test-only gateway: synthetic account "7", in-memory catalog transport.
            using var gateway=new DirectSdkGateway(_=>Task.FromResult(false),
                 ()=>Task.FromResult(false),new FakeEntitlementReply(),"7",
                 true,true,false,false,h);
            gateway.OneSongDiagnostic+=stages.Add;
            var uri=new Uri("kugou-grant://"+Guid.NewGuid().ToString("N"));
            var grant=new DirectGrant("7",gateway.generation,
                 "00000000000000000000000000000000","standard",new Uri(source));
            if(!gateway.grants.TryAdd(uri,grant))throw new InvalidOperationException("TEST_GRANT_INSERT");
            using var cts=new CancellationTokenSource();
            if(revoke)grant.Revoke();
            if(cancel)cts.Cancel();
            Exception? failure=null;
            try
            {
                using var opened=await gateway.OpenAsync(uri,cts.Token);
                if(expect!=null)throw new InvalidOperationException(name+" expected rejection");
                if(opened.Length<1024 || opened.Stream.Length<1024)
                    throw new InvalidDataException(name+" invalid audio returned");
            }
            catch(Exception e){failure=e;}
            // Expected rejection types are explicit; no swallowed errors.
            if(expect==null && failure!=null)
                throw new InvalidOperationException(name+" failed: "+failure.GetType().Name);
            if(expect!=null && (failure==null||!expect.IsAssignableFrom(failure.GetType())))
                throw new InvalidOperationException(name+" wrong rejection: "+failure?.GetType().Name);
            if(h.Forwarded!=expectedForwards || h.HttpSeen!=0)
                throw new InvalidOperationException(name+" transport count/scheme mismatch");
            if(requiredStage!=null && !stages.Contains(requiredStage))
                throw new InvalidOperationException(name+" missing stage: "+requiredStage);
            httpLeak+=h.HttpSeen;passed++;
        }
        const string https="https://audio.kugoucdn.com/media.mp3";
        const string http="http://audio.kugoucdn.com/media.mp3";
        await Case("HTTPS_200",https,_=>MediaReply(),requiredStage:"SDK_CDN_AUDIO_BUFFERED");
        await Case("HTTP_UPGRADE_200",http,_=>MediaReply(),
            requiredStage:"SDK_CDN_HTTPS_UPGRADE_OK");
        await Case("HTTP_UPGRADE_403_FALLBACK_DENIED",http,_=>MediaReply(HttpStatusCode.Forbidden),
            typeof(UnauthorizedAccessException),requiredStage:"SDK_PLAIN_HTTP_FALLBACK_DENIED");
        await Case("HTTP_UPGRADE_TRANSPORT_ERROR",http,_=>throw new HttpRequestException("synthetic"),
            typeof(UnauthorizedAccessException),requiredStage:"SDK_CDN_HTTPS_UPGRADE_TRANSPORT_FAILED");
        await Case("DIRECT_HTTPS_CDN_403",https,_=>MediaReply(HttpStatusCode.Forbidden),
            typeof(HttpRequestException),requiredStage:"SDK_CDN_NON_200");
        await Case("DIRECT_HTTPS_CDN_REDIRECT",https,_=>MediaReply(HttpStatusCode.Redirect),
            typeof(HttpRequestException),requiredStage:"SDK_CDN_NON_200");
        await Case("TEXT_HTML_REJECT",https,_=>MediaReply(mime:"text/html"),
            typeof(InvalidDataException));
        await Case("JSON_REJECT",https,_=>MediaReply(mime:"application/json"),
            typeof(InvalidDataException));
        await Case("LARGE_DECLARED_REJECT",https,_=>MediaReply(declared:25*1024*1024),
            typeof(InvalidDataException));
        await Case("SHORT_CONTENT_REJECT",https,_=>MediaReply(bytes:100),
            typeof(InvalidDataException));
        await Case("REVOKED_GRANT",https,_=>MediaReply(),
            typeof(UnauthorizedAccessException),revoke:true,requiredStage:"SDK_AUDIO_GRANT_INVALID_AT_OPEN",
            expectedForwards:0);
        await Case("CANCELLED_TOKEN",https,_=>MediaReply(),
            typeof(OperationCanceledException),cancel:true,expectedForwards:1);
        await Case("FOREIGN_MEDIA_DOMAIN","https://example.org/audio.mp3",_=>MediaReply(),
            typeof(UnauthorizedAccessException),requiredStage:"SDK_AUDIO_ORIGIN_REJECTED",
            expectedForwards:0);
        if(cases!=13||passed!=13||httpLeak!=0)throw new InvalidOperationException("MEDIA_CASE_COUNTS");
        return (passed,cases,httpLeak);
    }
}
