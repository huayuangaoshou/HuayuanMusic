using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using HuayuanMusic.Kugou.Gateway;
using HuayuanMusic.Provider.Contracts;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace HuayuanMusic.Provider.Kugou;

/// <summary>
/// Isolated compatibility adapter for KuGou.Net's already tested QR, search, V5 URL path.
/// Does not own playback, queue, BASS, database, VIP claim, or another client session.
/// Its opaque grant is consumed only through the existing KugouProvider owner.
/// Not an official KuGou SDK / not approved for public distribution.
/// </summary>
public sealed partial class DirectSdkGateway : IRealSdkGateway
{
    readonly KgSessionManager session;
    readonly PlaylistCoverDetails details;long playlistReadEpoch;
    public void CancelPlaylistDetails(){Interlocked.Increment(ref playlistReadEpoch);details.Cancel();}
    public event Action<string>? PlaylistArtworkDiagnostic;
    readonly HttpClient apiHttp, qrImages, mediaHttp;
    readonly LoginClient login;
    readonly SearchClient search;
    readonly RawSongApi songs;
    readonly UserClient user;
    readonly PlaylistClient playlist;
    readonly Func<string, Task<bool>> confirmPlainHttp;
    readonly Func<Task<bool>>? approveOnlineAudio;
    readonly SemaphoreSlim gate=new(1,1);
    readonly Dictionary<(string UserId,DateOnly Day),ClaimState> conceptClaimTries=new();
    const string ConceptClaimSourceId="90139"; // 听歌领 VIP 活动，与 core VipEntitlementService 同款
    readonly Dictionary<string,CatalogTrack> catalog=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Hash,string Quality),Uri> pending=new();
    readonly ConcurrentDictionary<Uri,DirectGrant> grants=new();
    readonly string alias="kugou-direct-"+Guid.NewGuid().ToString("N");
    string? qrKey;
    DateTimeOffset lastPoll;
    readonly bool desktop,offline,readOnlyCatalog,oneAudioTrial,entitlementOnly,combinedVipAudio;
    bool disposed,verifiedVip;
    long generation,sourceRequestCount;

    sealed class DirectGrant(string uid,long gen,string hash,string quality,Uri media)
    {
        internal readonly string UserId=uid,Hash=hash,Quality=quality;
        internal readonly long Generation=gen;
        internal readonly Uri Media=media;
        internal readonly DateTimeOffset Expires=DateTimeOffset.UtcNow.AddMinutes(2);
        internal readonly CancellationTokenSource Revocation=new();
        internal void Revoke(){try{Revocation.Cancel();}catch(ObjectDisposedException){}}
    }

    public DirectSdkGateway(Func<string,Task<bool>> confirmPlainHttp,Func<Task<bool>>? approveOnlineAudio=null)
        : this(confirmPlainHttp,approveOnlineAudio,null,null,false) { }

    public static DirectSdkGateway CreateDesktopSession() =>
        new(_ => Task.FromResult(false), null, null, null, false, desktop: true);

    public static DirectSdkGateway CreateOfflineReadSession(string fixture, HttpMessageHandler transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        string user = fixture switch { "A"=>"7", "B"=>"8", _=>throw new ArgumentOutOfRangeException(nameof(fixture)) };
        return new DirectSdkGateway(_=>Task.FromResult(false),()=>Task.FromResult(false),transport,user,false);
    }

    public static DirectSdkGateway CreateOfflinePlaybackSession(string fixture, HttpMessageHandler catalog,
        HttpMessageHandler media, Func<Task<bool>> approved)
    {
        string user = fixture switch { "A"=>"7", "B"=>"8", _=>throw new ArgumentOutOfRangeException(nameof(fixture)) };
        return new(_=>Task.FromResult(false),approved,catalog??throw new ArgumentNullException(nameof(catalog)),
            user,false,offlineMediaFixture:new HttpsOnlyMediaGuard(media??throw new ArgumentNullException(nameof(media))));
    }

    public static DirectSdkGateway CreateF2PreflightSession(HttpMessageHandler catalog,
        HttpMessageHandler media,Func<Task<bool>> approved)=>
        new(_=>Task.FromResult(false),approved,catalog??throw new ArgumentNullException(nameof(catalog)),
            "7",true,true,false,true,new HttpsOnlyMediaGuard(media??throw new ArgumentNullException(nameof(media))));

    // A completely synthetic offline variant for end-to-end UI/Core/BASS testing.
    // There is no real account factory or environment-based live HTTP switch.
    public static DirectSdkGateway CreateOriginalHttpSyntheticF2Session(HttpMessageHandler catalog,
        HttpMessageHandler media,Func<Task<bool>> approved)=>
        new(_=>Task.FromResult(true),approved,catalog??throw new ArgumentNullException(nameof(catalog)),
            "7",true,true,false,true,media??throw new ArgumentNullException(nameof(media)));

    // This mode never requests a QR code, entitlement, VIP, media grant, or playback source.
    // Existing Windows credentials are loaded by the original SDK in memory; nothing is exported.
    public static DirectSdkGateway CreateSavedReadOnlySession()=>
        new(_=>Task.FromResult(false),()=>Task.FromResult(false),null,null,true);

    // Manual one-song test only. This cannot QR login, claim membership,
    // change remote playlists, open arbitrary HTTP, or request a second source.
    public static DirectSdkGateway CreateSavedSingleAudioSession(Func<Task<bool>> approved)=>
        new(_=>Task.FromResult(false),approved,null,null,true,true);

    // Explicit P2BP-E one-shot read-only entitlement path. No media or catalog.
    public static DirectSdkGateway CreateSavedEntitlementReadOnlySession()=>
        new(_=>Task.FromResult(false),null,null,null,true,false,true);

    // P2BP-F uses the exact same session for one live VIP check and one song.
    public static DirectSdkGateway CreateSavedCombinedVipSingleAudioSession(Func<Task<bool>> approved)=>
        new(_=>Task.FromResult(false),approved,null,null,true,true,false,true);

    DirectSdkGateway(Func<string,Task<bool>> confirmPlainHttp,Func<Task<bool>>? approveOnlineAudio,
        HttpMessageHandler? offlineTransport,string? offlineUser,bool readOnlyCatalog,bool oneAudioTrial=false,bool entitlementOnly=false,bool combinedVipAudio=false, HttpMessageHandler? offlineMediaFixture=null,bool targetOccurrence=false,LocalTrialLease? localTrial=null,bool desktop=false)
    {
        this.desktop=desktop;
        offline=offlineTransport!=null;
        this.localTrial=localTrial;
        if(localTrial!=null)localTrial.Attach(!offline);
        var trialProxy=localTrial?.Real==true?ReadLocalTrialProxy():null;
        if(offlineMediaFixture!=null&&!offline)throw new UnauthorizedAccessException("Only synthetic sessions can inject audio transport");
        this.readOnlyCatalog=readOnlyCatalog;this.oneAudioTrial=oneAudioTrial;this.entitlementOnly=entitlementOnly;this.combinedVipAudio=combinedVipAudio;
        this.confirmPlainHttp=confirmPlainHttp??throw new ArgumentNullException(nameof(confirmPlainHttp));
        this.approveOnlineAudio=approveOnlineAudio;
        session=new KgSessionManager(new CookieContainer());
        details=new PlaylistCoverDetails(session);details.Diagnostic=reason=>PlaylistArtworkDiagnostic?.Invoke(reason);
        HttpMessageHandler transportBoundary=offlineTransport??NewSdkSockets(combinedVipAudio);
        if(entitlementOnly)transportBoundary=new EntitlementOnlyWire(transportBoundary);
        else if(combinedVipAudio)transportBoundary=new SingleSongVipWire(transportBoundary);
        else if(readOnlyCatalog)transportBoundary=new ReadOnlyCatalogWire(transportBoundary,oneAudioTrial);
        if(targetOccurrence)
        {
            if((!offline&&localTrial?.Real!=true)||(offline&&offlineMediaFixture==null)||!combinedVipAudio)
                throw new UnauthorizedAccessException("Target occurrence candidate requires synthetic handlers");
            targetWire=new TargetOccurrenceWire(session,()=>targetProof,
                ()=>Volatile.Read(ref targetConsumed)==1&&targetProof is { } p&&MatchesTarget(p,p.ProviderGeneration),
                transportBoundary,localTrial==null?null:DemandTrialSession,localTrial?.Token??default,SongDiag);
            transportBoundary=targetWire;
        }
        var allowed=new SdkCatalogReadHandler(session,details,transportBoundary,desktop);
        apiHttp=new HttpClient(new KgSignatureHandler(session){InnerHandler=allowed},true)
            {Timeout=TimeSpan.FromSeconds(12)};
        qrImages=new HttpClient(offline||readOnlyCatalog?(HttpMessageHandler)new OfflineDenied():new SocketsHttpHandler{
            UseCookies=false,AllowAutoRedirect=false,UseProxy=false,Credentials=null})
            {Timeout=TimeSpan.FromSeconds(10)};
        // F2: opt-in HTTPS media only through the current system proxy.
        // Default and all catalog/QR/VIP transports remain exactly as before.
        mediaHttp=new HttpClient(desktop ? CreateDesktopMediaHandler() :
            localTrial==null?CreateMediaClientHandler(offlineMediaFixture,offline,
            readOnlyCatalog,oneAudioTrial,combinedVipAudio):
            new TrialMediaWire(DemandTrialSession,localTrial.Token,SongDiag,trialProxy==null
                ?new F3HttpMediaGuard(offlineMediaFixture!)
                :NewOriginalHttpMediaTransport(trialProxy)))
            {Timeout=TimeSpan.FromSeconds(30)};
        var transport=new KgHttpTransport(apiHttp,oneAudioTrial||combinedVipAudio||offlineMediaFixture!=null?1:3);
        login=new LoginClient(new RawLoginApi(transport,session,NullLogger<RawLoginApi>.Instance),
            session,NullLogger<LoginClient>.Instance);
        search=new SearchClient(new RawSearchApi(transport),session);
        songs=new RawSongApi(transport,session);
        user=new UserClient(new RawUserApi(transport),session);
        playlist=new PlaylistClient(new RawPlaylistApi(transport,NullLogger<RawPlaylistApi>.Instance),session);
        // User opt-in happened when SaveCurrentSession was clicked. Loading does not
        // issue network requests and never reads another app or the earlier probe slot.
        if(offline)session.UpdateAuth(offlineUser!,"P2A_SYNTHETIC_ONLY","0","",null);
        else
        {
            try
            {
                if (DirectSdkCredentialStore.TryLoad(out var saved) && saved != null)
                    session.UpdateAuth(saved.UserId, saved.Token, "0", "", null);
            }
            catch (InvalidDataException) when (desktop)
            {
                // A stale saved login can be replaced through ordinary QR login.
            }
        }
        if(localTrial!=null&&LoggedIn)
        {
            trialBinding=TargetBinding();
            trialGeneration=generation;
        }
    }

    sealed class OfflineDenied:HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>
            throw new UnauthorizedAccessException("Read-only QR / audio / media transport denied");
    }

    // The last network hop, independent of the SDK's broader catalog gate.
    // Crucially, this forbids QR, VIP/claim, media URL, permission, music, and writes.
    sealed class ReadOnlyCatalogWire(HttpMessageHandler inner,bool oneAudioTrial):DelegatingHandler(inner)
    {
        int calls,mediaRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(Interlocked.Increment(ref calls)>250)throw new UnauthorizedAccessException("P2B-R request budget exhausted");
            var u=req.RequestUri;
            if(u==null||u.Scheme!=Uri.UriSchemeHttps||u.Port!=443||
               !string.Equals(u.Host,"gateway.kugou.com",StringComparison.OrdinalIgnoreCase)||
               !string.IsNullOrEmpty(u.UserInfo)||req.Headers.Contains("Cookie")||
               req.Headers.Contains("Authorization"))
                throw new UnauthorizedAccessException("P2B-R HTTPS catalog-only origin required");
            bool allow=
                (u.AbsolutePath=="/v7/get_all_list"&&req.Method==HttpMethod.Post)||
                (u.AbsolutePath=="/pubsongs/v2/get_other_list_file_nofilt"&&req.Method==HttpMethod.Get)||
                (u.AbsolutePath=="/v3/get_list_info"&&req.Method==HttpMethod.Post);
            if(!allow&&oneAudioTrial&&u.AbsolutePath=="/v5/url"&&req.Method==HttpMethod.Get){
                if(Interlocked.Increment(ref mediaRequests)>1)
                    throw new UnauthorizedAccessException("P2B-P single audio request already used");
                allow=true;
            }
            if(!allow)throw new UnauthorizedAccessException("P2B-P non-admitted network endpoint blocked");
            return base.SendAsync(req,ct);
        }
    }

    // P2BP-F: strictly bounded original SDK GETs. The signature/catalog
    // handler remains upstream and validates request body, session and pages.
    internal sealed class SingleSongVipWire(HttpMessageHandler inner):DelegatingHandler(inner)
    {
        int requests,vipGets,sourceGets;
        internal static bool Catalog(HttpRequestMessage req)
        {
            var u=req.RequestUri;
            return u!=null&&u.Host.Equals("gateway.kugou.com",StringComparison.OrdinalIgnoreCase)&&
                ((u.AbsolutePath=="/v7/get_all_list"&&req.Method==HttpMethod.Post)||
                 (u.AbsolutePath=="/pubsongs/v2/get_other_list_file_nofilt"&&req.Method==HttpMethod.Get)||
                 (u.AbsolutePath=="/v3/get_list_info"&&req.Method==HttpMethod.Post));
        }
        internal static bool Source(HttpRequestMessage req)
        {
            var u=req.RequestUri;
            return u!=null&&u.Host.Equals("gateway.kugou.com",StringComparison.OrdinalIgnoreCase)&&
                u.AbsolutePath=="/v5/url"&&req.Method==HttpMethod.Get;
        }
        internal static bool AdmittedShape(HttpRequestMessage req)
        {
            var u=req.RequestUri;
            return u!=null&&u.Scheme==Uri.UriSchemeHttps&&u.Port==443&&
                u.HostNameType==UriHostNameType.Dns&&!u.IsLoopback&&
                u.Query.Length<16384&&string.IsNullOrEmpty(u.Fragment)&&
                string.IsNullOrEmpty(u.UserInfo)&&
                !req.Headers.Contains("Cookie")&&!req.Headers.Contains("Authorization")&&
                (Catalog(req)||Source(req)||EntitlementOnlyWire.Admit(req));
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(Interlocked.Increment(ref requests)>200||!AdmittedShape(req))
                throw new UnauthorizedAccessException("P2BP-F restricts HTTPS catalog + one VIP + one source");
            if(Environment.GetEnvironmentVariable("M208B_F2_LIVE_ONCE")=="1"&&Catalog(req))
                throw new UnauthorizedAccessException("F2 live forbids all remote catalog requests");
            if(EntitlementOnlyWire.Admit(req))
            {
                if(Interlocked.Increment(ref vipGets)!=1)
                    throw new UnauthorizedAccessException("P2BP-F VIP read-once quota");
            }
            else if(Source(req))
            {
                if(Volatile.Read(ref vipGets)!=1||Interlocked.Increment(ref sourceGets)!=1)
                    throw new UnauthorizedAccessException("P2BP-F VIP-before-source and single-source quota");
            }
            SetSingleConnection(req,req.RequestUri!);
            return base.SendAsync(req,ct);
        }
    }

    // Pure in-memory network-boundary regression, no credentials or live requests.
    public static async Task<(int Allowed,int Denied,int Forwarded)> TestSingleSongVipWireAsync()
    {
        var mock=new FakeEntitlementReply();int allow=0,deny=0;
        using(var client=new HttpClient(new SingleSongVipWire(mock)))
        {
            // Media must not precede VIP, even if catalog has been read.
            using(var premature=new HttpRequestMessage(HttpMethod.Get,
                "https://gateway.kugou.com/v5/url?hash=abc"))try{
                using var response=await client.SendAsync(premature);
                throw new InvalidOperationException("Media before VIP forwarded");
            }catch(UnauthorizedAccessException){deny++;}
            using(var vip=new HttpRequestMessage(HttpMethod.Get,
                "https://kugouvip.kugou.com/v1/get_union_vip?busi_type=concept")){
                using var response=await client.SendAsync(vip);if(response.IsSuccessStatusCode)allow++;
            }
            using(var source=new HttpRequestMessage(HttpMethod.Get,
                "https://gateway.kugou.com/v5/url?hash=abc")){
                using var response=await client.SendAsync(source);if(response.IsSuccessStatusCode)allow++;
            }
            foreach(var u in new[]{
                "https://kugouvip.kugou.com/v1/get_union_vip?busi_type=concept",
                "https://gateway.kugou.com/v5/url?hash=abc"})
            {
                using var req=new HttpRequestMessage(HttpMethod.Get,u);
                try{using var response=await client.SendAsync(req);
                    throw new InvalidOperationException("Second request was forwarded");
                }catch(UnauthorizedAccessException){deny++;}
            }
        }
        foreach(var (method,url) in new[]{
            (HttpMethod.Post,"https://kugouvip.kugou.com/v1/get_union_vip?busi_type=concept"),
            (HttpMethod.Get,"http://kugouvip.kugou.com/v1/get_union_vip?busi_type=concept"),
            (HttpMethod.Get,"https://kugouvip.kugou.com/v1/claim?busi_type=concept"),
            (HttpMethod.Get,"https://kugouvip.kugou.com/v1/get_union_vip?busi_type=concept_extra"),
            (HttpMethod.Get,"https://gateway.kugou.com/v2/qrcode"),
            (HttpMethod.Get,"https://evil.example/v5/url"),
            (HttpMethod.Get,"https://gateway.kugou.com/v5/url/other"),
            (HttpMethod.Post,"https://gateway.kugou.com/v5/url?hash=abc")})
        {
            using var client=new HttpClient(new SingleSongVipWire(new FakeEntitlementReply()));
            using var req=new HttpRequestMessage(method,url);
            try{using var response=await client.SendAsync(req);
                throw new InvalidOperationException("Forbidden method/path forwarded");
            }catch(UnauthorizedAccessException){deny++;}
        }
        if(allow!=2||deny!=11||mock.Forwarded!=2)
            throw new InvalidOperationException("P2BP-F allow/deny counts mismatch");
        return (allow,deny,mock.Forwarded);
    }

    // Single GET, fixed VIP endpoint, same signed SDK path. Cannot reach
    // claim, offer, payment, playlist, audio, QR, HTTP, or redirects.
    internal sealed class EntitlementOnlyWire(HttpMessageHandler inner):DelegatingHandler(inner)
    {
        int requests;
        internal static bool Admit(HttpRequestMessage req)
        {
            var u=req.RequestUri;
            return u!=null && u.Scheme==Uri.UriSchemeHttps && u.Port==443
                && string.Equals(u.Host,"kugouvip.kugou.com",StringComparison.OrdinalIgnoreCase)
                && u.AbsolutePath=="/v1/get_union_vip" && req.Method==HttpMethod.Get
                && !string.IsNullOrEmpty(u.Query)
                && Uri.UnescapeDataString(u.Query.TrimStart('?')).Split('&')
                    .Count(kv=>kv=="busi_type=concept")==1
                && Uri.UnescapeDataString(u.Query.TrimStart('?')).Split('&')
                    .Count(kv=>kv.StartsWith("busi_type=",StringComparison.Ordinal))==1
                && string.IsNullOrEmpty(u.UserInfo) && req.Content==null
                && !req.Headers.Contains("Cookie") && !req.Headers.Contains("Authorization");
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(Interlocked.Increment(ref requests)!=1 ||
               !Admit(req))throw new UnauthorizedAccessException("P2BP-E one-shot VIP read-only GET only");
            return base.SendAsync(req,ct);
        }
    }

    public static async Task<(int Allowed,int Denied,int Forwarded)> TestEntitlementWireAsync()
    {
        int allow=0,deny=0;
        var fake=new FakeEntitlementReply();
        using var transport=new HttpClient(new EntitlementOnlyWire(fake));
        var urls=new[]{
            "https://kugouvip.kugou.com/v1/get_union_vip?busi_type=concept",
            "https://kugouvip.kugou.com/v1/get_union_vip?busi_type=concept"
        };
        foreach(var url in urls)
        {
            using var req=new HttpRequestMessage(HttpMethod.Get,url);
            try{using var rsp=await transport.SendAsync(req);if(rsp.IsSuccessStatusCode)allow++;}
            catch(UnauthorizedAccessException){deny++;}
        }
        using(var other=new HttpClient(new EntitlementOnlyWire(fake)))
        {
            var invalid=new[]{
                (HttpMethod.Post,"https://kugouvip.kugou.com/v1/get_union_vip?busi_type=concept"),
                (HttpMethod.Get,"http://kugouvip.kugou.com/v1/get_union_vip?busi_type=concept"),
                (HttpMethod.Get,"https://kugouvip.kugou.com/v1/get_union_vip?busi_type=other"),
                (HttpMethod.Get,"https://kugouvip.kugou.com/v1/claim?busi_type=concept"),
                (HttpMethod.Get,"https://gateway.kugou.com/v5/url?busi_type=concept"),
                (HttpMethod.Get,"https://kugouvip.kugou.com/v1/get_union_vip?busi_type=concept@evil.example")
            };
            // A fresh handler per negative request prevents the 1-request budget
            // from masking route/method rejection.
            foreach(var (method,url) in invalid)
            {
                using var client=new HttpClient(new EntitlementOnlyWire(fake));
                using var req=new HttpRequestMessage(method,url);
                try{using var rsp=await client.SendAsync(req);throw new InvalidOperationException("Negative policy accidentally forwarded");}
                catch(UnauthorizedAccessException){deny++;}
            }
        }
        if(allow!=1||deny!=7||fake.Forwarded!=1)
            throw new InvalidOperationException("P2BP-E wire allow/deny contract");
        return (allow,deny,fake.Forwarded);
    }
    sealed class FakeEntitlementReply:HttpMessageHandler
    {
        internal int Forwarded;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Interlocked.Increment(ref Forwarded);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    public string Alias=>alias;
    // Stable, non-reversible local cache partition; raw account id is not saved with playlists.
    public string? AccountCacheKey=>!LoggedIn?null:
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("HuayuanMusic:kugou:v1:"+session.Session.UserId)));
    public bool LoggedIn=>session.Session.UserId!="0"&&!string.IsNullOrWhiteSpace(session.Session.Token);
    public bool IsFixture=>false;
    public long SourceRequestCount=>Interlocked.Read(ref sourceRequestCount);
    public event Action? Revoked;
    public event Action<string>? OneSongDiagnostic;
    void SongDiag(string stage){if(desktop||oneAudioTrial||offline)OneSongDiagnostic?.Invoke(stage);}

    void Demand(){ObjectDisposedException.ThrowIf(disposed,this);}
    void DemandLogin(){Demand();if(!LoggedIn)throw new UnauthorizedAccessException("请先用自己的酷狗账号扫码登录");}
    async Task<T> Serial<T>(Func<Task<T>> f,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try{Demand();ct.ThrowIfCancellationRequested();return await f();}
        finally{gate.Release();}
    }
    void Revoke()
    {
        CancelPlaylistDetails();generation++;
        pending.Clear();
        foreach(var grant in grants.Values)grant.Revoke();
        grants.Clear();
        verifiedVip=false;
        Revoked?.Invoke();
    }
    public void AdoptImportedSession()
    {
        Demand();
        if(offline)throw new UnauthorizedAccessException("P2A fixture cannot load credentials");
        if(LoggedIn)throw new InvalidOperationException("请先退出当前账号，再导入实验登录");
        if(!DirectSdkCredentialStore.TryLoad(out var saved)||saved==null)
            throw new InvalidOperationException("找不到可恢复的集成登录");
        session.UpdateAuth(saved.UserId,saved.Token,"0","",null);
        qrKey=null;
        Revoke();
    }
    public void SaveCurrentSession()
    {
        DemandLogin();
        if(offline)throw new UnauthorizedAccessException("P2A fixture cannot save credentials");
        DirectSdkCredentialStore.Save(session.Session.UserId,session.Session.Token);
    }
    public void DeleteSavedSession()
    {
        if(offline)throw new UnauthorizedAccessException("P2A fixture cannot delete credentials");
        DirectSdkCredentialStore.Delete();
    }

    public Task<QrChallenge> BeginQrAsync(CancellationToken ct)=>Serial(async()=>{
        if(LoggedIn)throw new InvalidOperationException("请先退出当前账号再扫码");
        var data=await login.GetQrCodeAsync().WaitAsync(ct);
        if(data==null||data.Status!=1||string.IsNullOrWhiteSpace(data.Qrcode)||
           data.Qrcode.Length>512||string.IsNullOrWhiteSpace(data.QrcodeImg))
            throw new InvalidDataException("二维码接口未返回有效内容");
        var pixels=await ReadQrAsync(data.QrcodeImg,ct);
        qrKey=data.Qrcode;
        lastPoll=DateTimeOffset.MinValue;
        return new QrChallenge(new Uri("https://h5.kugou.com/apps/loginQRCode/html/index.html"),pixels);
    },ct);
    async Task<byte[]> ReadQrAsync(string src,CancellationToken ct)
    {
        if(src.StartsWith("data:image/png;base64,",StringComparison.OrdinalIgnoreCase))
        {
            var b=Convert.FromBase64String(src["data:image/png;base64,".Length..]);
            if(b.Length is 0 or >1_048_576)throw new InvalidDataException("二维码图片大小无效");
            return b;
        }
        if(!Uri.TryCreate(src,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Port!=443||
           !AllowedKugouHost(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo))
            throw new UnauthorizedAccessException("二维码图片来源不受信任");
        using var res=await qrImages.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
        res.EnsureSuccessStatusCode();
        if(res.Content.Headers.ContentLength>1_048_576)throw new InvalidDataException("二维码图片过大");
        await using var input=await res.Content.ReadAsStreamAsync(ct);
        using var output=new MemoryStream();
        var bytes=new byte[8192];int n;
        while((n=await input.ReadAsync(bytes,ct))>0)
        {
            if(output.Length+n>1_048_576)throw new InvalidDataException("二维码图片超过上限");
            output.Write(bytes,0,n);
        }
        return output.ToArray();
    }

    public Task<QrLoginStatus> PollQrAsync(CancellationToken ct)=>Serial(async()=>{
        if(qrKey==null)throw new InvalidOperationException("请先创建扫码二维码");
        if(DateTimeOffset.UtcNow-lastPoll<TimeSpan.FromSeconds(2))
            throw new InvalidOperationException("二维码轮询间隔过短");
        lastPoll=DateTimeOffset.UtcNow;
        var response=await login.CheckQrStatusAsync(qrKey).WaitAsync(ct);
        if(response==null||!Enum.IsDefined(response.QrStatus))
            throw new InvalidDataException("二维码状态不可解析");
        if(response.QrStatus==QrLoginStatus.Success)
        {
            if(!response.IsSuccess||!LoggedIn)throw new InvalidDataException("没有收到有效账号授权");
            qrKey=null;Revoke();
        }
        else if(response.QrStatus==QrLoginStatus.Expired)qrKey=null;
        return response.QrStatus;
    },ct);
    public Task CancelLoginAsync(CancellationToken ct)=>Serial(()=>{
        qrKey=null;
        if(!LoggedIn){session.Logout();Revoke();}
        return Task.FromResult(0);
    },ct);

    public Task<Entitlement> EntitlementAsync(CancellationToken ct)=>Serial(()=>EntitlementCore(ct),ct);
    async Task<Entitlement> EntitlementCore(CancellationToken ct){
        DemandLogin();
        var data=await user.GetVipInfoAsync().WaitAsync(ct);
        if(data?.Status!=1)throw new InvalidDataException("酷狗 VIP 状态查询未成功");
        var active=data.IsVip==1||data.IsConceptVip||data.IsSuperVip;
        verifiedVip=active;
        var best=data.BusiVipList.FirstOrDefault(v=>v.IsVip==1);
        DateTimeOffset? until=DateTimeOffset.TryParse(best?.EndTime,out var t)?t:null;
        return new Entitlement(active,best?.ProductType??"unknown",until,"Live read-only account entitlement");
    }
    // Read-only history on the SAME SDK account/session used by playlists and audio.
    // This never calls the separate ConceptGateway, and cannot mutate membership.
    public Task<VipReceiveHistoryResponse> ReadClaimHistoryAsync(CancellationToken ct)=>Serial(async()=>{
        DemandLogin();
        var data=await user.GetVipRecordAsync().WaitAsync(ct)
            ??throw new InvalidDataException("概念版领取记录为空");
        ConceptVipHistory.Validate(data,DateTimeOffset.UtcNow);
        return data;
    },ct);
    // One-session preflight only: same UserClient and account partition used
    // for R5 playlists/audio. Never reserves a slot or dispatches a claim.
    public Task<ConceptVipClaimPreview> PreviewClaimAsync(ActivityApproval? reviewed,
        string isolatedLedgerRoot,CancellationToken ct=default)=>Serial(async()=>{
        DemandLogin();
        var history=await user.GetVipRecordAsync().WaitAsync(ct)
            ??throw new InvalidDataException("概念版领取记录为空");
        var status=ConceptVipHistory.Validate(history,DateTimeOffset.UtcNow);
        var accountKey=AccountCacheKey??throw new UnauthorizedAccessException("Missing account partition");
        var prior=new ConceptVipClaimLedger(isolatedLedgerRoot).Read(accountKey,status.ServerDay);
        return new ConceptVipClaimPreview(status,
            ConceptVipHistory.Assess(status,reviewed),prior?.Outcome);
    },ct);
    // 对齐借鉴项目 core VipEntitlementService 的登录自动领取行为（用户本人账号、每日一次）。
    // 防重：服务端 VipRecord 当日已领为权威判据；Unknown 当日绝不二次 POST。
    public Task<DailyOffer> OfferAsync(CancellationToken ct)=>Serial(async()=>{
        DemandLogin();
        var history=await user.GetVipRecordAsync().WaitAsync(ct)
            ??throw new InvalidDataException("概念版领取记录为空");
        var status=ConceptVipHistory.Validate(history,DateTimeOffset.UtcNow);
        return new DailyOffer(status.ServerDay,status.ReceivedToday,true,true,false);
    },ct);
    public async Task<bool> ClaimAsync(CancellationToken ct){
        var r=await ClaimTodayAsync(true,ct);
        return r.State==ClaimState.ClaimSuccess;
    }
    public Task<ClaimReceipt> ClaimTodayAsync(bool explicitUserClick,CancellationToken ct)=>Serial(async()=>{
        DemandLogin();
        var history=await user.GetVipRecordAsync().WaitAsync(ct)
            ??throw new InvalidDataException("概念版领取记录为空");
        var status=ConceptVipHistory.Validate(history,DateTimeOffset.UtcNow);
        var day=status.ServerDay;
        var userId=session.Session.UserId;
        var ent=await EntitlementCore(ct);
        if(status.ReceivedToday)return new ClaimReceipt(ClaimState.AlreadyReceived,ent);
        var key=(userId,day);
        if(conceptClaimTries.TryGetValue(key,out var prior)&&prior==ClaimState.OutcomeUnknown)
            return new ClaimReceipt(ClaimState.OutcomeUnknown,ent);
        if(!explicitUserClick)throw new UnauthorizedAccessException("自动领取需用户明确授权（扫码登录须知已披露）");
        conceptClaimTries[key]=ClaimState.OutcomeUnknown;
        ClaimState state;
        try{
            var r=await user.ReceiveOneDayVipAsync(ConceptClaimSourceId,day).WaitAsync(ct);
            var after=await user.GetVipRecordAsync().WaitAsync(ct);
            var confirmed=after!=null&&after.Items.Any(x=>
                x.Day==day.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)&&x.IsReceived==1);
            state=confirmed||r?.AdVipNum>0?ClaimState.ClaimSuccess:ClaimState.Rejected;
        }catch(Exception e)when(e is OperationCanceledException or TimeoutException
            or HttpRequestException or InvalidOperationException
            or InvalidDataException or UnauthorizedAccessException){
            return new ClaimReceipt(ClaimState.OutcomeUnknown,ent); // 结果未知：当日不再重试
        }
        conceptClaimTries[key]=state;
        var ent2=await EntitlementCore(ct);
        return new ClaimReceipt(state,ent2);
    },ct);

    public Task<SearchPage> SearchAsync(string keyword,int page,int size,CancellationToken ct)=>Serial(async()=>{
        DemandLogin();
        if(keyword.Length is 0 or >120||page is <1 or >1000||size is <1 or >50)
            throw new ArgumentException("搜索参数超出范围");
        var found=await search.SearchAsync(keyword,page,"song",size).WaitAsync(ct);
        if(found.Count>size)throw new InvalidDataException("搜索结果超出请求上限");
        var tracks=found.Where(s=>s.Hash.Length==32&&s.Hash.All(Uri.IsHexDigit)).Select(s=>
            new CatalogTrack(s.Hash.ToLowerInvariant(),s.Name,s.Singer,s.AlbumName,s.Duration,
                long.TryParse(s.AlbumId,out var album)?album:0,Cover:ArtworkUrl.Normalize(s.Cover))).ToArray();
        foreach(var track in tracks)
        {
            if(!catalog.ContainsKey(track.Id)&&catalog.Count>=10000)
                throw new InvalidOperationException("歌曲目录缓存上限");
            catalog[track.Id]=track;
        }
        return new SearchPage(page,Array.AsReadOnly(tracks),tracks.Length==size);
    },ct);

    static string Quality(string quality)=>quality switch
    {
        "standard"=>"128","320"=>"320","flac"=>"flac",
        _=>throw new NotSupportedException("不支持的音质")
    };
    static string? Field(JsonElement json,string name)=>
        json.ValueKind==JsonValueKind.Object&&json.TryGetProperty(name,out var item)?item.ToString():null;
    static JsonElement Payload(JsonElement root)=>root.ValueKind==JsonValueKind.Object&&
        root.TryGetProperty("data",out var v)?v:root;
    private static string? PreferredDesktopUrl(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("url", out var urls) || urls.ValueKind != JsonValueKind.Array)
            return FirstUrl(payload);

        var trusted = urls.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>()
            .Where(TrustedMedia)
            .ToArray();
        return (trusted.FirstOrDefault(uri => uri.Scheme == Uri.UriSchemeHttps) ??
            trusted.FirstOrDefault())?.AbsoluteUri;
    }

    static string? FirstUrl(JsonElement payload)
    {
        if(payload.ValueKind!=JsonValueKind.Object||!payload.TryGetProperty("url",out var urls))return null;
        if(urls.ValueKind==JsonValueKind.String)return urls.GetString();
        if(urls.ValueKind!=JsonValueKind.Array)return null;
        return urls.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String)
            .Select(x=>x.GetString()).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
    }

    // The account-matched, previously verified snapshot supplies exactly one
    // candidate locally. No catalog calls are allowed in strict F2-LIVE mode.
    public void PrimeF2LiveSnapshotCandidate(ProviderTrackRef track)
    {
        DemandLogin();
        if(Environment.GetEnvironmentVariable("M208B_F2_LIVE_ONCE")!="1"||
           !combinedVipAudio||offline||catalog.Count!=0||
           track.ProviderId!=KugouProvider.Identity||
           track.ProviderTrackId.Length!=32||!track.ProviderTrackId.All(Uri.IsHexDigit)||
           track.AlbumId is <0||string.IsNullOrWhiteSpace(track.Title))
            throw new UnauthorizedAccessException("F2 live snapshot candidate rejected");
        var hash=track.ProviderTrackId.ToLowerInvariant();
        catalog.Add(hash,new CatalogTrack(hash,track.Title,track.Artist,track.Album,
             0,track.AlbumId.GetValueOrDefault()));
    }

    public Task<MediaPermission> PermissionAsync(string track,string quality,CancellationToken ct)=>Serial(async()=>{
        DemandLogin();
        if(!desktop && (approveOnlineAudio==null||!await approveOnlineAudio().WaitAsync(ct)))
            throw new UnauthorizedAccessException("尚未单独批准在线音源请求；未发送歌曲音源请求");
        ct.ThrowIfCancellationRequested();
        track=track.ToLowerInvariant();
        if(!catalog.TryGetValue(track,out var info))
            throw new UnauthorizedAccessException("先从当前账号搜索或读取该歌曲");
        var requested=Quality(quality);
        // M2-14: a SINGLE SDK source request is permitted for any one-song
        // gateway, even if BASS opens the provider lazy stream twice.
        // Consume the budget BEFORE calling the SDK's network method.
        // The existing lower SingleSongVipWire guard remains independent.
        if(oneAudioTrial||combinedVipAudio)
        {
            if(Interlocked.CompareExchange(ref sourceRequestCount,1,0)!=0)
            {
                SongDiag("SDK_SOURCE_BUDGET_EXHAUSTED_PRE_GET");
                throw new UnauthorizedAccessException("Single-song source budget already consumed");
            }
        }
        else Interlocked.Increment(ref sourceRequestCount);
        // Any mutation of the old grant must be AFTER the quota check:
        // an unintended second load may not revoke the first stream.
        if(pending.Remove((track,quality),out var old))Release(old);
        if(grants.Count>=32)throw new InvalidOperationException("当前在线音源上限 32");
        SongDiag("SDK_SINGLE_GETURL_BEGIN");
        var response=await songs.GetUrlAsync(track,requested,info.AlbumId.ToString(),freePart:false).WaitAsync(ct);
        SongDiag("SDK_SINGLE_GETURL_RESPONSE");
        if(Field(response,"status")!="1"){SongDiag("SDK_SOURCE_STATUS_REJECTED");}
        if(Field(response,"status")!="1")
            throw new UnauthorizedAccessException("酷狗未授权播放，错误码 "+(Field(response,"errcode")??"unknown"));
        foreach(var n in new[]{"errcode","error_code"})
            if(int.TryParse(Field(response,n),out var topCode)&&topCode!=0)
                throw new UnauthorizedAccessException("酷狗音源业务错误："+topCode);
        var data=Payload(response);
        var echoed=Field(data,"hash");
        if(!string.IsNullOrEmpty(echoed)&&!string.Equals(echoed,track,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("服务端返回的音源 Hash 与当前歌曲不一致");
        if(data.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("音源数据格式无效");
        if(int.TryParse(Field(data,"err_code"),out var code)&&code!=0)
            throw new UnauthorizedAccessException("音源业务错误 "+code);
        var priv=int.TryParse(Field(data,"priv_status"),out var p)?p:-1;
        if(priv==1&&(desktop||!verifiedVip)){SongDiag("SDK_VIP_ENTITLEMENT_UNVERIFIED");}
        if(priv==1&&(desktop||!verifiedVip))
        {
            // Playback is an explicit user action. Read-only entitlement
            // verification may be performed on demand; never claim/upgrade VIP.
            var vip=await user.GetVipInfoAsync().WaitAsync(ct);
            if(vip?.Status!=1)
                throw new UnauthorizedAccessException("本次 VIP 权益核实失败，不会继续下载音频");
            verifiedVip=vip.IsVip==1||vip.IsConceptVip||vip.IsSuperVip;
            if(!verifiedVip)
                throw new UnauthorizedAccessException("当前账号未确认具备这首 VIP 歌曲的播放权益");
        }
        if(priv==10){SongDiag("SDK_PAID_ALBUM_BLOCKED");throw new UnauthorizedAccessException("需单独购买专辑，未授权");}
        if(priv is not(0 or 1)){SongDiag("SDK_UNKNOWN_PERMISSION_STATUS");throw new UnauthorizedAccessException("未知的歌曲权限状态："+priv);}
        if(int.TryParse(Field(data,"is_free_part"),out var part)&&part!=0)
            throw new UnauthorizedAccessException("仅允许试听片段，不作为完整音源");
        var source=desktop?PreferredDesktopUrl(data):FirstUrl(data);
        if(!Uri.TryCreate(source,UriKind.Absolute,out var address)||!TrustedMedia(address)){
            SongDiag("SDK_MEDIA_URL_UNTRUSTED");
            throw new UnauthorizedAccessException("返回地址不在已核对的酷狗媒体来源范围内");
        }
        SongDiag("SDK_PERMISSION_GRANTED");
        var handle=new Uri("kugou-grant://"+Guid.NewGuid().ToString("N"));
        var grant=new DirectGrant(session.Session.UserId,generation,track,quality,address);
        if(!grants.TryAdd(handle,grant))throw new InvalidOperationException("音源许可登记冲突");
        pending[(track,quality)]=handle;
        return new MediaPermission(true,quality,"SDK_ACCOUNT_SONG_SOURCE_APPROVED",quality);
    },ct);

    public Task<Uri> ResolveAsync(string track,string quality,CancellationToken ct)=>Serial(()=>{
        DemandLogin();
        ct.ThrowIfCancellationRequested();
        if(!pending.Remove((track.ToLowerInvariant(),quality),out var handle)||!Allows(handle))
            throw new UnauthorizedAccessException("没有当前歌曲的有效音源许可");
        return Task.FromResult(handle);
    },ct);
    public bool Allows(Uri handle)
    {
        if(disposed||!LoggedIn||!grants.TryGetValue(handle,out var grant))return false;
        return grant.Generation==generation&&grant.UserId==session.Session.UserId&&
            grant.Expires>DateTimeOffset.UtcNow&&!grant.Revocation.IsCancellationRequested;
    }
    public void Release(Uri handle)
    {
        if(grants.TryRemove(handle,out var grant))grant.Revoke();
    }

    int combinedMediaOpens;
    public async Task<OpenedAudio> OpenAsync(Uri handle,CancellationToken ct)
    {
        SongDiag("SDK_OPEN_AUDIO_BEGIN");
        if(!Allows(handle)||!grants.TryGetValue(handle,out var grant)){
            SongDiag("SDK_AUDIO_GRANT_INVALID_AT_OPEN");
            throw new UnauthorizedAccessException("音源许可过期或已撤销");
        }
        if(combinedVipAudio&&Interlocked.Increment(ref combinedMediaOpens)!=1)
            throw new UnauthorizedAccessException("F2 single media request already used");
        SongDiag("SDK_AUDIO_GRANT_VALID_AT_OPEN");
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,grant.Revocation.Token,localTrial?.Token??default);
        var token=linked.Token;
        var source=grant.Media;
        if(!TrustedMedia(source)){
            SongDiag("SDK_AUDIO_ORIGIN_REJECTED");
            throw new UnauthorizedAccessException("音源地址不在酷狗受限 CDN 范围");
        }
        if(localTrial!=null&&(source.Scheme!="http"||!TrialSessionCurrent()))
            throw new UnauthorizedAccessException("Trial only admits the original HTTP grant");
        if(source.Scheme=="https")SongDiag("SDK_CDN_DIRECT_HTTPS");
        HttpResponseMessage response;
        if (desktop)
        {
            SongDiag(source.Scheme == Uri.UriSchemeHttp ? "SDK_CDN_DIRECT_HTTP" : "SDK_CDN_DESKTOP_HTTPS");
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, null);
            response = await mediaHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        }
        // Compare KA Music's original URL transport only with a process-local
        // synthetic SDK and an injected in-memory media fixture. There is
        // deliberately NO factory exposing original HTTP for a real account.
        else if(source.Scheme=="http"&&offline&&await confirmPlainHttp(source.Host))
        {
            SongDiag("SDK_ORIGINAL_HTTP_SYNTHETIC_ONLY");
            token.ThrowIfCancellationRequested();
            using var req=new HttpRequestMessage(HttpMethod.Get,source);
            response=await mediaHttp.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,token);
        }
        else if(source.Scheme=="http"&&!offline&&(localTrial?.Real==true||F3OriginalHttpRequested(combinedVipAudio)))
        {
            SongDiag("SDK_ORIGINAL_HTTP_F3_AUTHORIZED_ONCE");
            token.ThrowIfCancellationRequested();
            using var req=new HttpRequestMessage(HttpMethod.Get,source);
            response=await mediaHttp.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,token);
        }
        else if(source.Scheme=="http")
        {
            SongDiag("SDK_CDN_HTTPS_UPGRADE_REQUIRED");
            var https=new UriBuilder(source){Scheme="https",Port=443}.Uri;
            HttpResponseMessage? secure=null;
            try
            {
                using var req=new HttpRequestMessage(HttpMethod.Get,https);
                secure=await mediaHttp.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,token);
                if(secure.StatusCode==HttpStatusCode.OK){
                    SongDiag("SDK_CDN_HTTPS_UPGRADE_OK");response=secure;secure=null;
                }
                else{
                    SongDiag("SDK_CDN_HTTPS_UPGRADE_NON_200");
                    secure.Dispose();secure=null;response=null!;
                }
            }
            catch(HttpRequestException){
                SongDiag("SDK_CDN_HTTPS_UPGRADE_TRANSPORT_FAILED");
                secure?.Dispose();response=null!;
            }
            if(response==null)
            {
                if(!await confirmPlainHttp(source.Host)){
                    SongDiag("SDK_PLAIN_HTTP_FALLBACK_DENIED");
                    throw new UnauthorizedAccessException("当前候选只允许HTTPS音频，不会回退明文HTTP");
                }
                token.ThrowIfCancellationRequested();
                using var req=new HttpRequestMessage(HttpMethod.Get,source);
                response=await mediaHttp.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,token);
            }
        }
        else
        {
            using var req=new HttpRequestMessage(HttpMethod.Get,source);
            response=await mediaHttp.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,token);
        }
        using(response)
        {
            bool fullRange = desktop && response.StatusCode == HttpStatusCode.PartialContent;
            SongDiag(fullRange ? "SDK_CDN_HTTP_206" : response.StatusCode == HttpStatusCode.OK
                ? "SDK_CDN_HTTP_200" : "SDK_CDN_NON_200");
            if (response.StatusCode != HttpStatusCode.OK && !fullRange)
                throw new HttpRequestException("CDN media HTTP " + (int)response.StatusCode);

            long? expectedLength = response.Content.Headers.ContentLength;
            if (fullRange)
            {
                var range = response.Content.Headers.ContentRange;
                if (range == null || range.Unit != "bytes" || range.From != 0 ||
                    range.Length is not > 0 || range.To != range.Length - 1 ||
                    (expectedLength.HasValue && expectedLength != range.Length))
                    throw new InvalidDataException("CDN returned an incomplete media range");
                expectedLength = range.Length;
            }

            const int limit=24*1024*1024;
            if(expectedLength>limit)
                throw new InvalidDataException("当前音频超过媒体缓冲上限");
            var mime=response.Content.Headers.ContentType?.MediaType??"";
            if(mime.StartsWith("text/",StringComparison.OrdinalIgnoreCase)||
               mime.Contains("json",StringComparison.OrdinalIgnoreCase)||
               mime.Contains("xml",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CDN 没有返回音频");
            var buffer=new MemoryStream();
            bool handed=false;
            try
            {
                await using var upstream=await response.Content.ReadAsStreamAsync(token);
                var data=new byte[16384];int n;
                while((n=await upstream.ReadAsync(data,token))>0)
                {
                    if(buffer.Length+n>limit)throw new InvalidDataException("媒体超出24MiB上限");
                    await buffer.WriteAsync(data.AsMemory(0,n),token);
                }
                if(buffer.Length<1024 || (desktop && expectedLength.HasValue && buffer.Length != expectedLength))throw new InvalidDataException("音频内容不完整");
                SongDiag("SDK_CDN_AUDIO_BUFFERED");
                if(!Allows(handle))throw new UnauthorizedAccessException("缓冲期间许可已撤销");
                buffer.Position=0;
                // Caller owns the memory audio via existing ProviderStreamRegistry and BASS.
                var opened=new OpenedAudio(buffer,buffer.Length,buffer,grant.Revocation.Token);
                handed=true;
                return opened;
            }
            finally{if(!handed)buffer.Dispose();}
        }
    }

    public Task<string> LyricsAsync(string track,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Do not add a second unofficial lyric protocol while playback is being accepted.
        return Task.FromResult("");
    }
    public CatalogTrack? CatalogTrackForPlaylist(string id)=>
        catalog.TryGetValue(id,out var track)?track:null;

    public Task<IReadOnlyList<ProviderPlaylist>> PlaylistsAsync(string account,CancellationToken ct)=>Serial(async()=>{
        DemandLogin();
        if(!string.Equals(account,Alias,StringComparison.Ordinal))
            throw new UnauthorizedAccessException("歌单账户身份不一致");
        var coverEpoch=Volatile.Read(ref playlistReadEpoch);
        // One-way snapshot: reject inconsistent pages. Nothing is written back.
        var collected=new List<ProviderPlaylist>();
        var discovered=new Dictionary<string,CatalogTrack>(StringComparer.OrdinalIgnoreCase);
        var allIds=new HashSet<string>(StringComparer.Ordinal);
        int? expectedLists=null;
        int allSongs=0;
        for(int page=1;page<=4;page++)
        {
            ct.ThrowIfCancellationRequested();
            var index=await user.GetPlaylistsAsync(page,50).WaitAsync(ct)
                ??throw new InvalidDataException("酷狗没有返回歌单列表");
            if(index.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)!=session.Session.UserId)
                throw new UnauthorizedAccessException("酷狗返回了其他账号的歌单列表");
            if(index.ListCount is <0 or >200||index.Playlists.Count>50||
               expectedLists.HasValue&&expectedLists.Value!=index.ListCount)
                throw new InvalidDataException("歌单数量超出范围或分页中途改变");
            expectedLists=index.ListCount;
            foreach(var list in index.Playlists)
            {
                ct.ThrowIfCancellationRequested();
                var listId=list.GlobalId;
                if(string.IsNullOrWhiteSpace(listId)||listId.Length>128||
                   listId.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_')||
                   !allIds.Add(listId)||string.IsNullOrWhiteSpace(list.Name)||
                   list.Name.Length>256)
                    throw new InvalidDataException("歌单身份、名称缺失或页间重复");
                var content=new List<ProviderTrackRef>();
                int? songCount=null;
                for(int songPage=1;songPage<=100;songPage++)
                {
                    var response=await playlist.GetSongsAsync(listId,songPage,50).WaitAsync(ct)
                        ??throw new InvalidDataException("歌单歌曲页不存在");
                    if(response.Count<0)
                        throw new InvalidDataException("SONG_COUNT_NEGATIVE");
                    if(response.Count>5000)
                        throw new InvalidDataException("PLAYLIST_LARGE_TOTAL="+response.Count+"（当前单歌单上限5000）");
                    if(songCount.HasValue&&songCount.Value!=response.Count)
                        throw new InvalidDataException("SONG_COUNT_CHANGED="+songCount.Value+"→"+response.Count);
                    if(response.Songs.Count>50)
                        throw new InvalidDataException("SONG_PAGE_TOO_LARGE="+response.Songs.Count);
                    if(allSongs+response.Songs.Count>50000)
                        throw new InvalidDataException("ACCOUNT_TRACK_LIMIT_REACHED=50000");
                    songCount=response.Count;
                    foreach(var item in response.Songs)
                    {
                        var hash=item.Hash??"";
                        bool valid=hash.Length==32&&hash.All(Uri.IsHexDigit);
                        var id=valid?hash.ToLowerInvariant():"unavailable:"+listId+":"+item.FileId;
                        var artists=item.Singers==null?"":string.Join(" / ",item.Singers.Select(s=>s.Name));
                        var name=string.IsNullOrWhiteSpace(item.Name)?"未知曲目":item.Name;
                        var album=item.Album?.Name??"";
                        content.Add(new ProviderTrackRef(KugouProvider.Identity,id,name,artists,album){CoverUrl=ArtworkUrl.Normalize(item.Cover??item.Album?.Cover),AlbumId=long.TryParse(item.AlbumId,out var aid)&&aid>0?aid:null});
                        if(valid&&!discovered.ContainsKey(id))
                        {
                            long albumId=long.TryParse(item.AlbumId,out var parsed)?parsed:0;
                            discovered.Add(id,new CatalogTrack(id,name,artists,album,
                                Math.Max(0,item.DurationMs/1000),albumId,Cover:ArtworkUrl.Normalize(item.Cover??item.Album?.Cover)));
                        }
                    }
                    allSongs+=response.Songs.Count;
                    if(songPage*50>=response.Count)break;
                    if(response.Songs.Count==0)
                        throw new InvalidDataException("歌单出现非预期的空分页");
                }
                if(content.Count!=songCount)
                    throw new InvalidDataException("歌单歌曲列表读取不完整");
                var listCover=ArtworkUrl.Normalize(list.Pic);if(listCover==null)PlaylistArtworkDiagnostic?.Invoke(string.IsNullOrWhiteSpace(list.Pic)?"PLAYLIST_LIST_NO_FIELD":"PLAYLIST_LIST_URL_REJECTED");
                collected.Add(new ProviderPlaylist(KugouProvider.Identity,listId,list.Name,
                    Array.AsReadOnly(content.ToArray())){CoverUrl=listCover});
            }
            if(collected.Count>=index.ListCount)break;
            if(index.Playlists.Count==0)
                throw new InvalidDataException("歌单索引页提前结束");
        }
        if(collected.Count!=expectedLists)
            throw new InvalidDataException("歌单索引未完整读取，旧快照保持不变");
        // Only this explicit, account-bound read refresh can request missing official covers.
        if(coverEpoch==Volatile.Read(ref playlistReadEpoch))using(details.Begin(collected.Where(x=>x.CoverUrl==null).Select(x=>x.ProviderPlaylistId),ct)){
            for(int i=0;i<collected.Count;i++)if(collected[i].CoverUrl==null){
                if(coverEpoch!=Volatile.Read(ref playlistReadEpoch))break;
                var id=collected[i].ProviderPlaylistId;
                var cover=await details.Fetch(id,()=>playlist.GetInfoAsync(id),ct);
                if(cover!=null)collected[i]=collected[i] with{CoverUrl=cover};
            }
        }
        // Publish playable metadata only after entire one-way sync succeeded.
        if(catalog.Count+discovered.Count>50000)
            throw new InvalidOperationException("曲目元数据超过内存上限");
        foreach(var pair in discovered)catalog[pair.Key]=pair.Value;
        return (IReadOnlyList<ProviderPlaylist>)Array.AsReadOnly(collected.ToArray());
    },ct);

    static bool AllowedKugouHost(string host)=>host.Equals("kugou.com",StringComparison.OrdinalIgnoreCase)||
        host.EndsWith(".kugou.com",StringComparison.OrdinalIgnoreCase);
    static bool TrustedMedia(Uri u)
    {
        if(u.Scheme is not("https" or "http")||!u.IsDefaultPort||u.IsLoopback||
           u.HostNameType!=UriHostNameType.Dns||!string.IsNullOrEmpty(u.UserInfo)||
           !string.IsNullOrEmpty(u.Fragment)||u.Query.Length>16384)return false;
        if(new[]{".kgm",".kgg",".vpr",".m4p"}.Any(x=>
           u.AbsolutePath.EndsWith(x,StringComparison.OrdinalIgnoreCase)))return false;
        return new[]{"kugou.com","kugoucdn.com","kgimg.com","kgcdn.com","kugou.net"}.Any(root=>
           u.Host.Equals(root,StringComparison.OrdinalIgnoreCase)||
           u.Host.EndsWith("."+root,StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if(disposed)return;CancelPlaylistDetails();
        disposed=true;
        foreach(var g in grants.Values)g.Revoke();
        pending.Clear();grants.Clear();catalog.Clear();qrKey=null;
        session.Logout();Revoked?.Invoke();
        apiHttp.Dispose();qrImages.Dispose();mediaHttp.Dispose();gate.Dispose();
    }
    sealed class SdkCatalogReadHandler(KgSessionManager session,PlaylistCoverDetails details,HttpMessageHandler inner,bool desktop=false):DelegatingHandler(inner)
    {
        int requests;
        internal static Dictionary<string,string> Query(Uri uri)
        {
            var dict=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var pair in uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
            {
                var parts=pair.Split('=',2);
                var key=Uri.UnescapeDataString(parts[0]);
                var value=parts.Length>1?Uri.UnescapeDataString(parts[1]):"";
                if(!dict.TryAdd(key,value))throw new UnauthorizedAccessException("重复的 SDK 查询字段");
            }
            return dict;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if(!desktop&&Interlocked.Increment(ref requests)>1500)
                throw new UnauthorizedAccessException("本次客户端请求达到 1500 次上限");
            var uri=req.RequestUri;
            if(uri==null||uri.Scheme!="https"||uri.Port!=443||
                !string.IsNullOrEmpty(uri.UserInfo)||req.Headers.Contains("Cookie"))
                throw new UnauthorizedAccessException("不安全的 SDK 请求");
            if(uri.AbsolutePath=="/v3/get_list_info")return await details.Send(req,token=>base.SendAsync(req,token),ct);
            bool index=uri.Host=="gateway.kugou.com"&&
                uri.AbsolutePath=="/v7/get_all_list"&&req.Method==HttpMethod.Post;
            bool playlistSongs=uri.Host=="gateway.kugou.com"&&
                uri.AbsolutePath=="/pubsongs/v2/get_other_list_file_nofilt"&&req.Method==HttpMethod.Get;
            // Read-only concept benefit history shares the SAME SDK session.
            // The predicate is exercised independently with offline fake requests.
            bool conceptHistory=ConceptVipHistory.IsBoundedHistoryGet(req);
            if(conceptHistory &&
               (session.Session.UserId=="0"||string.IsNullOrWhiteSpace(session.Session.Token)))
                throw new UnauthorizedAccessException("只读领取记录需要当前登录账号");
            bool conceptClaim=uri.Host=="gateway.kugou.com"&&
                uri.AbsolutePath=="/youth/v1/recharge/receive_vip_listen_song"&&
                req.Method==HttpMethod.Post&&
                (uri.Query.Contains("source_id=90139",StringComparison.Ordinal));
            bool original=req.Method==HttpMethod.Get&&
                (uri.Host=="login-user.kugou.com"&&
                   uri.AbsolutePath is "/v2/qrcode" or "/v2/get_userinfo_qrcode"||
                 uri.Host=="gateway.kugou.com"&&
                   uri.AbsolutePath is "/v3/search/song" or "/v5/url"||
                 uri.Host=="kugouvip.kugou.com"&&uri.AbsolutePath=="/v1/get_union_vip");
            if(!index&&!playlistSongs&&!original&&!conceptHistory&&!conceptClaim)
                throw new UnauthorizedAccessException("请求不在酷狗扫码、搜索、VIP、单曲音源、只读领取记录或只读歌单接口内");
            if(index)
            {
                if(req.Content==null||req.Content.Headers.ContentLength>8192||
                    !req.Headers.TryGetValues("x-router",out var routers)||
                    !routers.SequenceEqual(new[]{"cloudlist.service.kugou.com"}))
                    throw new UnauthorizedAccessException("歌单索引请求缺少预期路由或正文");
                using var body=JsonDocument.Parse(await req.Content.ReadAsStreamAsync(ct));
                var obj=body.RootElement;
                if(obj.ValueKind!=JsonValueKind.Object||obj.EnumerateObject().Count()!=6||
                   obj.GetProperty("userid").GetString()!=session.Session.UserId||
                   obj.GetProperty("token").GetString()!=session.Session.Token||
                   obj.GetProperty("total_ver").GetInt32()!=979||
                   obj.GetProperty("type").GetInt32()!=2||
                   obj.GetProperty("page").GetInt32() is <1 or >4||
                   obj.GetProperty("pagesize").GetInt32()!=50)
                    throw new UnauthorizedAccessException("歌单请求正文不是 SDK 原始只读列表查询");
                var query=Query(uri);
                if(query.GetValueOrDefault("userid")!=session.Session.UserId||
                   query.GetValueOrDefault("token")!=session.Session.Token)
                    throw new UnauthorizedAccessException("歌单请求与当前账户授权不一致");
            }
            if(playlistSongs)
            {
                var query=Query(uri);
                string gid=query.GetValueOrDefault("global_collection_id")??"";
                if(gid.Length is <1 or >128||
                   gid.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_')||
                   !int.TryParse(query.GetValueOrDefault("begin_idx"),out var begin)||
                   begin is <0 or >4950||begin%50!=0||
                   query.GetValueOrDefault("pagesize")!="50"||
                   req.Content!=null)
                    throw new UnauthorizedAccessException("歌单曲目请求不符合分页只读约束");
            }
            return await base.SendAsync(req,ct);
        }
    }
}
