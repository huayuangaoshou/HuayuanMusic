using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HuayuanMusic.Provider.Kugou;

// F2 changes ONLY the opt-in media HTTPS handler. The SDK's VIP,
// playlist, login and song-permission clients remain untouched.
public sealed partial class DirectSdkGateway
{
    static bool F2ProxyRequested(bool combinedVipAudio)=>
        combinedVipAudio &&
        Environment.GetEnvironmentVariable("M208B_P2BPF2_MEDIA_SYSTEM_PROXY")=="1";

    // F3 only applies to the already bounded real F2 VIP+single-song mode.
    // It never changes catalog, SDK authorization or synthetic handling.
    static bool F3OriginalHttpRequested(bool combinedVipAudio)=>
        F2ProxyRequested(combinedVipAudio) &&
        Environment.GetEnvironmentVariable("M208B_F2_LIVE_ONCE")=="1" &&
        Environment.GetEnvironmentVariable("M208B_F3_HTTP_ONCE")=="1" &&
        Environment.GetEnvironmentVariable("M208B_F3_HTTP_RISK_APPROVED")=="1";

    static HttpMessageHandler CreateDesktopMediaHandler() =>
        new DesktopMediaGuard(new SocketsHttpHandler
        {
            UseProxy = true,
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            DefaultProxyCredentials = null,
            AutomaticDecompression = DecompressionMethods.None
        });

    static HttpMessageHandler CreateMediaClientHandler(HttpMessageHandler? fixture,
        bool synthetic,bool readOnlyCatalog,bool oneAudioTrial,bool combinedVipAudio)
    {
        if(fixture!=null)return fixture; // constrained to synthetic by caller
        if(synthetic||(readOnlyCatalog&&!oneAudioTrial))return new OfflineDenied();
        if(F3OriginalHttpRequested(combinedVipAudio))
            return NewOriginalHttpMediaTransport(HttpClient.DefaultProxy);
        if(!F2ProxyRequested(combinedVipAudio))
            return NewAudioSockets(false,null);
        return NewHttpsOnlyMediaTransport(HttpClient.DefaultProxy);
    }

    static SocketsHttpHandler NewAudioSockets(bool viaProxy,IWebProxy? proxy,bool allowOriginalHttp=false)=>
        new()
        {
            UseCookies=false,AllowAutoRedirect=false,
            UseProxy=viaProxy,Proxy=viaProxy?new NoBypassAudioProxy(
                proxy??throw new ArgumentNullException(nameof(proxy)),allowOriginalHttp):null,
            Credentials=null,DefaultProxyCredentials=null,
            AutomaticDecompression=DecompressionMethods.None,
            MaxConnectionsPerServer=1
        };

    static SocketsHttpHandler NewSdkSockets(bool singleSong)
    {
        var sockets=new SocketsHttpHandler
        {
            UseCookies=false,AllowAutoRedirect=false,UseProxy=false,Credentials=null
        };
        if(singleSong)sockets.ConnectCallback=ConnectOnceAsync;
        return sockets;
    }

    sealed class ConnectionBudget(Uri endpoint)
    {
        internal readonly Uri Endpoint=endpoint;
        internal int Used;
    }

    static readonly HttpRequestOptionsKey<ConnectionBudget> ConnectionKey=new("Huayuan.SingleConnection");

    static void SetSingleConnection(HttpRequestMessage request,Uri endpoint)
    {
        request.Options.Set(ConnectionKey,new ConnectionBudget(endpoint));
        // Avoid reuse and HTTP/2 retries inside the framework. A transport
        // failure cannot create a second socket for this HTTP/1.1 request.
        request.Version=HttpVersion.Version11;
        request.VersionPolicy=HttpVersionPolicy.RequestVersionExact;
        request.Headers.ConnectionClose=true;
    }

    static async ValueTask<Stream> ConnectOnceAsync(SocketsHttpConnectionContext context,CancellationToken token)
    {
        if(!context.InitialRequestMessage.Options.TryGetValue(ConnectionKey,out var budget)||
           Interlocked.Exchange(ref budget.Used,1)!=0||
           !context.DnsEndPoint.Host.Equals(budget.Endpoint.IdnHost,StringComparison.OrdinalIgnoreCase)||
           context.DnsEndPoint.Port!=budget.Endpoint.Port)
            throw new UnauthorizedAccessException("HTTP transport requires its one pinned connection");

        var socket=new Socket(SocketType.Stream,ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(context.DnsEndPoint,token);
            return new NetworkStream(socket,ownsSocket:true);
        }
        catch { socket.Dispose(); throw; }
    }

    static HttpMessageHandler NewHttpsOnlyMediaTransport(IWebProxy proxy)=>
        new HttpsOnlyMediaGuard(new PinnedProxyTransport(proxy,false));

    static HttpMessageHandler NewOriginalHttpMediaTransport(IWebProxy proxy)=>
        new F3HttpMediaGuard(new PinnedProxyTransport(proxy,true));

    // Validate before entering SocketsHttpHandler, then give it a fixed route.
    // A failed dynamic proxy lookup must never become an implicit direct route.
    sealed class PinnedProxyTransport(IWebProxy configured,bool allowHttp):HttpMessageHandler
    {
        HttpMessageInvoker? sender;
        int used;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(Interlocked.Exchange(ref used,1)!=0)
                throw new UnauthorizedAccessException("F2 media transport already used");
            var proxy=new NoBypassAudioProxy(configured,allowHttp).GetProxy(request.RequestUri!);
            var sockets=NewAudioSockets(true,new FixedAudioProxy(proxy),allowHttp);
            if(allowHttp)
            {
                SetSingleConnection(request,proxy);
                sockets.ConnectCallback=ConnectOnceAsync;
            }
            sender=new HttpMessageInvoker(sockets);
            return sender.SendAsync(request,ct);
        }
        protected override void Dispose(bool disposing){if(disposing)sender?.Dispose();base.Dispose(disposing);}
    }

    sealed class FixedAudioProxy(Uri proxy):IWebProxy
    {
        public ICredentials? Credentials { get=>null; set { if(value!=null)throw new UnauthorizedAccessException(); } }
        public Uri GetProxy(Uri destination)=>proxy;
        public bool IsBypassed(Uri destination)=>false;
    }

    // Never silently go direct when the Windows proxy is absent, bypassed,
    // misconfigured or unavailable. Do not forward proxy credentials.
    sealed class NoBypassAudioProxy(IWebProxy configured,bool allowHttp=false):IWebProxy
    {
        public ICredentials? Credentials
        {
            get=>null;
            set {if(value!=null)throw new UnauthorizedAccessException("F2 forbids proxy credentials");}
        }
        public bool IsBypassed(Uri destination)=>false; // GetProxy must validate
        public Uri GetProxy(Uri destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if(destination.Scheme!=Uri.UriSchemeHttps&&
               !(allowHttp&&destination.Scheme==Uri.UriSchemeHttp))
                throw new UnauthorizedAccessException("F2/F3 rejects destination scheme");
            Uri? proxy=configured.GetProxy(destination);
            if(proxy==null||configured.IsBypassed(destination)||
               proxy.Equals(destination)||proxy.IdnHost.Equals(destination.IdnHost,StringComparison.OrdinalIgnoreCase)||
               !string.IsNullOrEmpty(proxy.UserInfo)||proxy.IsFile||
               proxy.Scheme is not ("http" or "https" or "socks4" or "socks5"))
                throw new UnauthorizedAccessException("F2 requires a usable, non-bypassed system proxy");
            return proxy;
        }
    }

    private static bool ContainsLoginQuery(Uri uri)
    {
        foreach (var field in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Uri.UnescapeDataString(field.Split('=', 2)[0])
                .Replace("_", "").Replace("-", "").ToLowerInvariant();
            if (key is "token" or "accesstoken" or "refreshtoken" or "authorization" or "cookie" or "password" or "sessionid")
                return true;
        }
        return false;
    }

    private sealed class DesktopMediaGuard(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri;
            if (url == null || !TrustedMedia(url) || url.AbsolutePath == "/" ||
                request.Method != HttpMethod.Get || request.Content != null ||
                request.Headers.Contains("Cookie") || request.Headers.Contains("Authorization") ||
                request.Headers.Contains("Proxy-Authorization") || ContainsLoginQuery(url))
                throw new UnauthorizedAccessException("Unauthorized media request");

            // Forward the original request, including any Range header, to the system proxy.
            return base.SendAsync(request, cancellationToken);
        }
    }

    sealed class HttpsOnlyMediaGuard(HttpMessageHandler inner):DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var url=request.RequestUri;
            if(url==null||url.Scheme!=Uri.UriSchemeHttps||request.Method!=HttpMethod.Get||
               !string.IsNullOrEmpty(url.UserInfo)||!string.IsNullOrEmpty(url.Fragment)||
               request.Headers.Contains("Cookie")||request.Headers.Contains("Authorization")||request.Headers.Contains("Proxy-Authorization")||
               request.Content!=null)
                throw new UnauthorizedAccessException("F2 media HTTPS-only GET gate rejected request");
            return base.SendAsync(request,cancellationToken);
        }
    }

    // F3: original HTTP protocol permitted only behind a one-shot, non-bypass
    // proxy, never via native BASS, redirect, cookies or authentication.
    sealed class F3HttpMediaGuard(HttpMessageHandler inner):DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,CancellationToken ct)
        {
            var u=request.RequestUri;
            if(u==null||u.Scheme!=Uri.UriSchemeHttp||!TrustedMedia(u)||u.AbsolutePath=="/"||
               request.Method!=HttpMethod.Get||request.Content!=null||request.Headers.Any())
                throw new UnauthorizedAccessException("F3 original media gate rejected");
            return base.SendAsync(request,ct);
        }
    }

    sealed class SyntheticProxy(Uri? destination,bool bypass=false):IWebProxy
    {
        public ICredentials? Credentials {get;set;}
        public Uri GetProxy(Uri url)=>destination??url;
        public bool IsBypassed(Uri url)=>bypass;
    }

    // Every endpoint here is ".invalid" or a local in-process fixture proxy.
    // No public DNS, provider requests, real network or signed media URLs.
    public static async Task<(int Passed,int Cases,int Connects)> TestF2ProxyOfflineAsync()
    {
        int passed=0,cases=0,connects=0;
        void Pass(string name,bool condition)
        {
            cases++;
            if(!condition)throw new InvalidOperationException("F2_TEST_FAILED_"+name);
            passed++;
        }
        const string fake="https://audio.synthetic.invalid/test.mp3";
        Uri target=new(fake);
        var unavailable=new SyntheticProxy(null,true);
        var noBypass=new NoBypassAudioProxy(unavailable);
        bool rejected=false;
        try{noBypass.GetProxy(target);}catch(UnauthorizedAccessException){rejected=true;}
        Pass("BYPASS_FAIL_CLOSED",rejected);
        rejected=false;
        try{new NoBypassAudioProxy(new SyntheticProxy(target)).GetProxy(target);}
        catch(UnauthorizedAccessException){rejected=true;}
        Pass("DIRECT_PROXY_FAIL_CLOSED",rejected);
        rejected=false;
        try{new NoBypassAudioProxy(new SyntheticProxy(new Uri(
            "http://user:password@127.0.0.1:49152"))).GetProxy(target);}
        catch(UnauthorizedAccessException){rejected=true;}
        Pass("CREDENTIAL_IN_PROXY_URI_REJECTED",rejected);
        rejected=false;
        try{new NoBypassAudioProxy(new SyntheticProxy(new Uri(
            "ftp://127.0.0.1:49152"))).GetProxy(target);}
        catch(UnauthorizedAccessException){rejected=true;}
        Pass("UNSUPPORTED_PROXY_PROTOCOL_REJECTED",rejected);
        rejected=false;
        try{new NoBypassAudioProxy(new SyntheticProxy(new Uri(
            "http://127.0.0.1:49152"))){Credentials=new NetworkCredential("x","y")};}
        catch(UnauthorizedAccessException){rejected=true;}
        Pass("PROXY_CREDS_REFUSED",rejected);

        // A fake proxy accepts exactly one CONNECT and returns 502. This
        // proves SocketsHttpHandler tunneled through it instead of using DNS.
        using(var listener=new TcpListener(IPAddress.Loopback,0))
        {
            listener.Start();
            int port=((IPEndPoint)listener.LocalEndpoint).Port;
            using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(7));
            var proxyTask=Task.Run(async ()=>{
                using var tcp=await listener.AcceptTcpClientAsync(cancel.Token);
                await using var stream=tcp.GetStream();
                var request=new List<byte>();
                var item=new byte[1];
                while(request.Count<8192)
                {
                    int n=await stream.ReadAsync(item,cancel.Token);
                    if(n==0)break;
                    request.Add(item[0]);
                    int len=request.Count;
                    if(len>=4&&request[len-4]==13&&request[len-3]==10&&
                       request[len-2]==13&&request[len-1]==10)break;
                }
                string headers=Encoding.ASCII.GetString(request.ToArray());
                if(!headers.StartsWith("CONNECT audio.synthetic.invalid:443 ",StringComparison.Ordinal)||
                   headers.Contains("Proxy-Authorization",StringComparison.OrdinalIgnoreCase)||
                   headers.Contains("Cookie:",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("F2_CONNECT_HEADER_INVALID");
                byte[] error=Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(error,cancel.Token);
                await stream.FlushAsync(cancel.Token);
                return true;
            },cancel.Token);
            using var media=new HttpClient(NewHttpsOnlyMediaTransport(
                new SyntheticProxy(new Uri($"http://127.0.0.1:{port}")))){
                Timeout=TimeSpan.FromSeconds(6)};
            rejected=false;
            try{using var response=await media.GetAsync(target,cancel.Token);}
            catch(HttpRequestException){rejected=true;}
            bool seen=await proxyTask;
            connects++;
            Pass("LOCAL_PROXY_CONNECT_502_FAILS_CLOSED",rejected&&seen);
            rejected=false;
            try{using var response=await media.GetAsync(target);}
            catch(UnauthorizedAccessException){rejected=true;}
            if(!rejected)throw new InvalidOperationException("F2_SECOND_CONNECT_NOT_BLOCKED");
            using var bypassClient=new HttpClient(NewHttpsOnlyMediaTransport(unavailable));
            rejected=false;
            try{using var response=await bypassClient.GetAsync(target);}
            catch(UnauthorizedAccessException){rejected=true;}
            if(!rejected)throw new InvalidOperationException("F2_SOCKET_BYPASS_NOT_BLOCKED");
            rejected=false;
            try{using var response=await media.GetAsync("http://audio.synthetic.invalid/test.mp3");}
            catch(UnauthorizedAccessException){rejected=true;}
            Pass("PLAINTEXT_MEDIA_ALWAYS_BLOCKED",rejected);
            rejected=false;
            try{using var req=new HttpRequestMessage(HttpMethod.Post,target);
                using var response=await media.SendAsync(req);}
            catch(UnauthorizedAccessException){rejected=true;}
            Pass("MEDIA_POST_ALWAYS_BLOCKED",rejected);
            rejected=false;
            try{using var req=new HttpRequestMessage(HttpMethod.Get,target);
                req.Headers.TryAddWithoutValidation("Cookie","private=1");
                using var response=await media.SendAsync(req);}
            catch(UnauthorizedAccessException){rejected=true;}
            Pass("MEDIA_COOKIE_ALWAYS_BLOCKED",rejected);
            rejected=false;
            try{using var req=new HttpRequestMessage(HttpMethod.Get,target);
                req.Headers.TryAddWithoutValidation("Authorization","Bearer dummy");
                using var response=await media.SendAsync(req);}
            catch(UnauthorizedAccessException){rejected=true;}
            Pass("MEDIA_AUTH_HEADER_ALWAYS_BLOCKED",rejected);
        }

        // Force an unreachable loopback proxy: never try a direct fallback.
        var temp=new TcpListener(IPAddress.Loopback,0);
        temp.Start();int closedPort=((IPEndPoint)temp.LocalEndpoint).Port;
        temp.Stop();
        using(var media=new HttpClient(NewHttpsOnlyMediaTransport(
            new SyntheticProxy(new Uri($"http://127.0.0.1:{closedPort}")))){
            Timeout=TimeSpan.FromSeconds(4)})
        {
            rejected=false;
            try{using var response=await media.GetAsync(target);}
            catch(HttpRequestException){rejected=true;}
            Pass("UNREACHABLE_PROXY_NO_DIRECT_FALLBACK",rejected);
        }

        // Gate is opt-in AND restricted to P2BP-F combined single-audio mode.
        var old=Environment.GetEnvironmentVariable("M208B_P2BPF2_MEDIA_SYSTEM_PROXY");
        try
        {
            Environment.SetEnvironmentVariable("M208B_P2BPF2_MEDIA_SYSTEM_PROXY","1");
            Pass("PROXY_REQUIRES_COMBINED_MODE",
                 !F2ProxyRequested(false)&&F2ProxyRequested(true));
            using var enabled=CreateMediaClientHandler(null,false,true,true,true);
            Pass("OPTO_IN_HTTPS_GUARD",enabled is HttpsOnlyMediaGuard);
            using var disabled=CreateMediaClientHandler(null,false,true,true,false);
            Pass("OTHER_MODE_UNCHANGED",disabled is SocketsHttpHandler);
            using var synthetic=CreateMediaClientHandler(null,true,true,true,true);
            Pass("SYNTHETIC_MODE_NO_REAL_SOCKET",synthetic is OfflineDenied);
        }
        finally{Environment.SetEnvironmentVariable("M208B_P2BPF2_MEDIA_SYSTEM_PROXY",old);}
        if(passed!=15||cases!=15||connects!=1)
            throw new InvalidOperationException("F2_MISMATCHED_CASE_TOTAL");
        return (passed,cases,connects);
    }
}
