using System.Net;
using System.Net.Sockets;
using System.Text;
namespace HuayuanMusic.Provider.Kugou;

// F3 local-only probes: no public DNS/HTTP, only a loopback fake proxy and
// in-process negative handlers. This does NOT authorize real original HTTP.
public sealed partial class DirectSdkGateway
{
    sealed class F3ProbeInner:HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("F3_GUARD_FAILED_TO_REJECT");
        }
    }
    public static async Task<(int Passed,int Cases,int LoopbackRequests)> TestF3OriginalHttpTransportOfflineAsync()
    {
        int passed=0,cases=0,seen=0;
        void Check(string label,bool condition)
        {
            cases++;if(!condition)throw new InvalidOperationException("F3_POLICY_"+label);passed++;
        }
        // Single real TCP socket to LOCAL loopback, no provider IP or DNS.
        using(var listener=new TcpListener(IPAddress.Loopback,0))
        {
            listener.Start();
            int port=((IPEndPoint)listener.LocalEndpoint).Port;
            using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var handler=Task.Run(async()=>{
                using var tcp=await listener.AcceptTcpClientAsync(limit.Token);
                await using var stream=tcp.GetStream();
                var bytes=new List<byte>();
                var item=new byte[1];
                while(bytes.Count<8192)
                {
                    var n=await stream.ReadAsync(item,limit.Token);
                    if(n==0)break;
                    bytes.Add(item[0]);
                    int k=bytes.Count;
                    if(k>=4&&bytes[k-4]==13&&bytes[k-3]==10&&bytes[k-2]==13&&bytes[k-1]==10)break;
                }
                var header=Encoding.ASCII.GetString(bytes.ToArray());
                if(!header.StartsWith("GET http://audio.kugoucdn.com/f3-dry.wav HTTP/1.1",StringComparison.Ordinal)||
                   header.Contains("Cookie:",StringComparison.OrdinalIgnoreCase)||
                   header.Contains("Authorization:",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("F3_PROXY_REQUEST_BOUNDARY");
                byte[] bad=Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(bad,limit.Token);
                seen++;return true;
            },limit.Token);
            using var client=new HttpClient(NewOriginalHttpMediaTransport(
                new SyntheticProxy(new Uri($"http://127.0.0.1:{port}"))))
                {Timeout=TimeSpan.FromSeconds(6)};
            using(var res=await client.GetAsync("http://audio.kugoucdn.com/f3-dry.wav",limit.Token))
                Check("EXACT_ORIGINAL_HTTP_PROXY_SINGLE_502",res.StatusCode==HttpStatusCode.BadGateway);
            Check("ONLY_ONE_LOCAL_PROXY_HOP",await handler&&seen==1);
            bool secondDenied=false;
            try{using var r=await client.GetAsync("http://audio.kugoucdn.com/f3-dry.wav",limit.Token);}
            catch(UnauthorizedAccessException){secondDenied=true;}
            Check("NO_SECOND_MEDIA_SEND",secondDenied&&seen==1);
        }
        using(var scope=new F3ProbeInner())
        using(var guard=new HttpClient(new F3HttpMediaGuard(scope)))
        {
            async Task Reject(string label,HttpRequestMessage req)
            {
                bool denied=false;
                try{using(req){using var res=await guard.SendAsync(req);}}
                catch(UnauthorizedAccessException){denied=true;}
                Check(label,denied&&scope.Calls==0);
            }
            var invalid="http://audio.kugoucdn.com/f3-test";
            await Reject("POST_FORBIDDEN",new HttpRequestMessage(HttpMethod.Post,invalid));
            await Reject("HTTPS_POST_FORBIDDEN",new HttpRequestMessage(HttpMethod.Post,"https://audio.kugoucdn.com/f3-test"));
            await Reject("LOOPBACK_TARGET_FORBIDDEN",new HttpRequestMessage(HttpMethod.Get,"http://127.0.0.1/f3-test"));
            await Reject("FTP_FORBIDDEN",new HttpRequestMessage(HttpMethod.Get,"ftp://audio.kugoucdn.com/f3-test"));
            await Reject("USERINFO_FORBIDDEN",new HttpRequestMessage(HttpMethod.Get,"http://abc@audio.kugoucdn.com/f3-test"));
            await Reject("FRAGMENT_FORBIDDEN",new HttpRequestMessage(HttpMethod.Get,"http://audio.kugoucdn.com/f3-test#fragment"));
            var cookie=new HttpRequestMessage(HttpMethod.Get,invalid);
            cookie.Headers.TryAddWithoutValidation("Cookie","secret=1");
            await Reject("COOKIE_FORBIDDEN",cookie);
            var auth=new HttpRequestMessage(HttpMethod.Get,invalid);
            auth.Headers.TryAddWithoutValidation("Authorization","Bearer secret");
            await Reject("AUTH_FORBIDDEN",auth);
        }
        if(cases!=11||passed!=11||seen!=1)throw new InvalidOperationException("F3_POLICY_COUNT");
        return (passed,cases,seen);
    }
}
