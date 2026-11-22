using System.Net;
using System.Net.Http.Headers;
namespace HuayuanMusic.Provider.Kugou;

// Test-only: original HTTP is exercised by a memory-backed HttpMessageHandler.
// A real account never has a factory that grants the synthetic transport route.
public sealed partial class DirectSdkGateway
{
    sealed class KaOriginalSourceFixture(bool allowHttp, HttpStatusCode status):HttpMessageHandler
    {
        internal int Requests,Plain,Secure;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(Interlocked.Increment(ref Requests)>1||req.Method!=HttpMethod.Get||
                req.RequestUri?.IdnHost!="audio.kugoucdn.com" ||
                req.Headers.Contains("Cookie")||req.Headers.Contains("Authorization")||
                req.Headers.Contains("Proxy-Authorization")||req.Content!=null)
                throw new UnauthorizedAccessException("Synthetic KA fixture scope violation");
            if(req.RequestUri.Scheme=="http")
            {
                Interlocked.Increment(ref Plain);
                if(!allowHttp)throw new UnauthorizedAccessException("Synthetic HTTP not admitted");
            }
            else if(req.RequestUri.Scheme=="https")Interlocked.Increment(ref Secure);
            else throw new UnauthorizedAccessException("Unsupported synthetic scheme");
            var content=new ByteArrayContent(new byte[2048]);
            content.Headers.ContentType=new MediaTypeHeaderValue("audio/mpeg");
            return Task.FromResult(new HttpResponseMessage(status){Content=content});
        }
    }

    public static async Task<(int Passed,int Cases,int HttpRequestsSynthetic)> TestOriginalHttpCompatOfflineAsync()
    {
        int cases=0,passed=0,plain=0;
        async Task Case(string label,bool consent,bool revoked,string media,
            HttpStatusCode status,Type? error,int http,int https,
            string requiredStage,bool tryAgain=false)
        {
            cases++;
            var h=new KaOriginalSourceFixture(consent,status);
            var stages=new List<string>();
            using var gateway=new DirectSdkGateway(_=>Task.FromResult(consent),
                ()=>Task.FromResult(false),new FakeEntitlementReply(),"7",
                true,true,false,true,h);
            gateway.OneSongDiagnostic+=stages.Add;
            var handle=new Uri("kugou-grant://"+Guid.NewGuid().ToString("N"));
            var grant=new DirectGrant("7",gateway.generation,
                "00000000000000000000000000000000","standard",new Uri(media));
            if(!gateway.grants.TryAdd(handle,grant))throw new InvalidOperationException("SYNTHETIC_GRANT");
            if(revoked)grant.Revoke();
            Exception? caught=null;
            try
            {
                using var audio=await gateway.OpenAsync(handle,CancellationToken.None);
                if(error!=null)throw new InvalidOperationException(label+" EXPECTED_EXCEPTION");
                if(audio.Length!=2048)throw new InvalidDataException(label+" wrong bytes");
            }
            catch(Exception e){caught=e;}
            if((error==null&&caught!=null)||
               (error!=null&&(caught==null||!error.IsAssignableFrom(caught.GetType()))))
                throw new InvalidOperationException(label+" exception="+caught?.GetType().Name);
            if(!stages.Contains(requiredStage)||h.Plain!=http||h.Secure!=https||
                h.Requests!=http+https)
                throw new InvalidOperationException(label+" transport/stage mismatch");
            if(tryAgain)
            {
                bool refused=false;
                try{using var audio=await gateway.OpenAsync(handle,CancellationToken.None);}
                catch(UnauthorizedAccessException){refused=true;}
                if(!refused||h.Requests!=1)throw new InvalidOperationException("SECOND_MEDIA_REQUEST_ACCEPTED");
            }
            plain+=h.Plain;passed++;
        }
        const string http="http://audio.kugoucdn.com/f2-synthetic.wav";
        await Case("ORIGINAL_HTTP_EXPLICIT_SYNTHETIC_ONCE",
            true,false,http,HttpStatusCode.OK,null,1,0,
            "SDK_ORIGINAL_HTTP_SYNTHETIC_ONLY",tryAgain:true);
        await Case("DEFAULT_HTTPS_UPGRADE_403_NO_HTTP",
            false,false,http,HttpStatusCode.Forbidden,typeof(UnauthorizedAccessException),0,1,
            "SDK_PLAIN_HTTP_FALLBACK_DENIED");
        await Case("ORIGINAL_HTTP_SYNTHETIC_403_FAILS_NO_RETRY",
            true,false,http,HttpStatusCode.Forbidden,typeof(HttpRequestException),1,0,
            "SDK_CDN_NON_200");
        await Case("FOREIGN_CDN_NEVER_SENT",
            true,false,"http://example.org/foreign.wav",HttpStatusCode.OK,
            typeof(UnauthorizedAccessException),0,0,"SDK_AUDIO_ORIGIN_REJECTED");
        await Case("REVOKED_GRANT_NEVER_SENT",
            true,true,http,HttpStatusCode.OK,typeof(UnauthorizedAccessException),0,0,
            "SDK_AUDIO_GRANT_INVALID_AT_OPEN");
        if(cases!=5||passed!=5||plain!=2)
            throw new InvalidOperationException("KA_COMPAT_SYNTHETIC_POLICY_CASE_COUNT");
        return (passed,cases,plain);
    }
}
