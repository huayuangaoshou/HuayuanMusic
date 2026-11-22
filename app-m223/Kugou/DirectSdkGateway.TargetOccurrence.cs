using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HuayuanMusic.Provider.Contracts;
using KuGou.Net.Protocol.Session;

namespace HuayuanMusic.Provider.Kugou;

public sealed partial class DirectSdkGateway
{
    static readonly byte[] TargetBindingKey=RandomNumberGenerator.GetBytes(32);
    readonly TargetOccurrenceWire? targetWire;
    TargetOccurrenceProof? targetProof;
    int targetStarted,targetConsumed;
    readonly LocalTrialLease? localTrial;
    readonly byte[]? trialBinding;
    readonly long trialGeneration;
    bool TrialSessionCurrent()=>localTrial!=null&&!localTrial.Token.IsCancellationRequested&&
        trialBinding!=null&&TargetBindingMatches(trialBinding,trialGeneration);
    void DemandTrialSession()
    {
        if(!TrialSessionCurrent())throw new UnauthorizedAccessException("Trial session unavailable or revoked");
    }

    public static DirectSdkGateway CreateSavedLocalTrial(LocalTrialLease lease,Func<Task<bool>> approved)
    {
        if(!lease.Real)throw new UnauthorizedAccessException("Real consent required");
        return new(_=>Task.FromResult(false),approved,null,null,true,true,false,true,null,true,lease);
    }

    public static DirectSdkGateway CreateSyntheticLocalTrial(LocalTrialLease lease,HttpMessageHandler catalog,
        HttpMessageHandler media,Func<Task<bool>> approved)
    {
        if(lease.Real)throw new UnauthorizedAccessException("Synthetic consent required");
        return new(_=>Task.FromResult(true),approved,catalog??throw new ArgumentNullException(nameof(catalog)),
            "7",true,true,false,true,media??throw new ArgumentNullException(nameof(media)),true,lease);
    }

    sealed class TrialMediaWire(Action demand,CancellationToken leaseToken,Action<string> audit,HttpMessageHandler inner):DelegatingHandler(inner)
    {
        int sent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            demand();
            if(Interlocked.Exchange(ref sent,1)!=0)throw new UnauthorizedAccessException("Trial media spent");
            audit("M219R_MEDIA_ADMITTED");
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,leaseToken);
            return await base.SendAsync(req,linked.Token);
        }
    }

    // In-process capability, with reference identity enforced by its issuing gateway.
    public sealed class TargetOccurrenceProof
    {
        internal TargetOccurrenceProof(long providerGeneration,long gatewayGeneration,byte[] binding,
            string partition,ProviderPlaylist first,ProviderPlaylist target,CatalogTrack track)
        {
            ProviderGeneration=providerGeneration;
            GatewayGeneration=gatewayGeneration;
            Binding=binding;
            AccountPartition=partition;
            First=first;
            Target=target;
            Track=track;
        }

        internal long ProviderGeneration { get; }
        internal long GatewayGeneration { get; }
        internal byte[] Binding { get; }
        internal long Issued { get; }=Stopwatch.GetTimestamp();
        public DateTimeOffset VerifiedAt { get; }=DateTimeOffset.UtcNow;
        public string AccountPartition { get; }
        public ProviderPlaylist First { get; }
        public ProviderPlaylist Target { get; }
        public CatalogTrack Track { get; }
        public int MemberIndex=>0;
        public int IndexRequests=>1;
        public int MemberRequests=>1;
    }

    public static DirectSdkGateway CreateOfflineTargetSession(HttpMessageHandler catalog,
        HttpMessageHandler media,Func<Task<bool>> approved)=>
        new(_=>Task.FromResult(true),approved,catalog??throw new ArgumentNullException(nameof(catalog)),
            "7",true,true,false,true,media??throw new ArgumentNullException(nameof(media)),true);

    byte[] TargetBinding()
    {
        var input=Encoding.UTF8.GetBytes(session.Session.UserId+"\0"+session.Session.Token);
        try{return HMACSHA256.HashData(TargetBindingKey,input);}
        finally{CryptographicOperations.ZeroMemory(input);}
    }

    static bool TargetListId(string? value)=>value is {Length:>0 and <=128}&&
        value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');

    internal Task<TargetOccurrenceProof> ReadTargetOccurrenceAsync(long providerGeneration,CancellationToken ct)=>Serial(async()=>{
        DemandLogin();
        if(targetWire==null||Interlocked.Exchange(ref targetStarted,1)!=0)
            throw new UnauthorizedAccessException("Target read unavailable or spent");

        byte[] binding=TargetBinding();
        long authGeneration=generation;
        targetWire.ReadCancellation=ct;
        targetWire.ReadActive=true;
        try
        {
            var index=await user.GetPlaylistsAsync(1,50);
            ct.ThrowIfCancellationRequested();
            if(index is not {Status:1}||index.ErrorCode.GetValueOrDefault()!=0||
               index.UserId.ToString(CultureInfo.InvariantCulture)!=session.Session.UserId||
               index.ListCount<2||index.Playlists==null||index.Playlists.Count<2||
               index.Playlists.Count>Math.Min(50,index.ListCount)||!TargetBindingMatches(binding,authGeneration))
                throw new UnauthorizedAccessException("Target index identity or shape rejected");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var list in index.Playlists)
                if(list==null||!TargetListId(list.GlobalId)||!ids.Add(list.GlobalId)||
                   string.IsNullOrWhiteSpace(list.Name)||list.Name.Length>256)
                    throw new InvalidDataException("Target index list identity rejected");

            var first=index.Playlists[0];
            var target=index.Playlists[1];
            targetWire.Select(target.GlobalId);
            var members=await playlist.GetSongsAsync(target.GlobalId,1,50);
            ct.ThrowIfCancellationRequested();
            if(members is not {Status:1}||members.ErrorCode.GetValueOrDefault()!=0||
               members.Songs==null||members.Songs.Count==0||members.Songs.Count!=Math.Min(50,members.Count)||
               !TargetBindingMatches(binding,authGeneration)||!targetWire.Complete||
               (members.EchoPlaylistId!=null&&members.EchoPlaylistId!=target.GlobalId)||
               (members.EchoBeginIndex.HasValue&&members.EchoBeginIndex!=0)||
               (members.EchoUserId.HasValue&&members.EchoUserId!=index.UserId))
                throw new InvalidDataException("Target membership response rejected");

            var item=members.Songs[0];
            if(item?.Hash is not {Length:32} hash||!hash.All(Uri.IsHexDigit)||
               string.IsNullOrWhiteSpace(item.Name)||item.Name.Length>1024||item.DurationMs<=0)
                throw new InvalidDataException("Target member identity rejected");
            var id=hash.ToLowerInvariant();
            var artists=item.Singers==null?"":string.Join(" / ",item.Singers.Select(s=>s.Name));
            var album=item.Album?.Name??"";
            if(artists.Length>1024||album.Length>1024)throw new InvalidDataException("Target metadata too large");
            var track=new CatalogTrack(id,item.Name,artists,album,item.DurationMs/1000,
                long.TryParse(item.AlbumId,out long albumId)&&albumId>0?albumId:0);
            var member=new ProviderTrackRef(KugouProvider.Identity,id,track.Title,track.Artist,track.Album);
            var result=new TargetOccurrenceProof(providerGeneration,authGeneration,binding,AccountCacheKey!,
                new ProviderPlaylist(KugouProvider.Identity,first.GlobalId,first.Name,Array.Empty<ProviderTrackRef>()),
                new ProviderPlaylist(KugouProvider.Identity,target.GlobalId,target.Name,Array.AsReadOnly(new[]{member})),track);
            catalog.Add(id,track);
            targetProof=result;
            return result;
        }
        finally{targetWire.ReadActive=false;targetWire.ReadCancellation=default;}
    },ct);

    bool TargetBindingMatches(byte[] binding,long expectedGeneration)=>
        !disposed&&LoggedIn&&generation==expectedGeneration&&
        CryptographicOperations.FixedTimeEquals(binding,TargetBinding());

    internal bool MatchesTarget(TargetOccurrenceProof proof,long providerGeneration)=>
        MatchesTargetAt(proof,providerGeneration,Stopwatch.GetTimestamp());

    bool MatchesTargetAt(TargetOccurrenceProof proof,long providerGeneration,long now)=>
        ReferenceEquals(targetProof,proof)&&proof.ProviderGeneration==providerGeneration&&
        proof.AccountPartition==AccountCacheKey&&Stopwatch.GetElapsedTime(proof.Issued,now)<TimeSpan.FromMinutes(2)&&
        TargetBindingMatches(proof.Binding,proof.GatewayGeneration);

    internal bool ConsumeTarget(TargetOccurrenceProof proof,long providerGeneration)=>
        MatchesTarget(proof,providerGeneration)&&Interlocked.CompareExchange(ref targetConsumed,1,0)==0;

    // Offline policy probes use the same signed SDK requests and last-hop guard.
    public async Task<int> TestTargetPolicyOfflineAsync(DirectSdkGateway other,CancellationToken ct)
    {
        if(!offline||targetWire==null||!other.offline)
            throw new UnauthorizedAccessException("Synthetic test only");
        static async Task Denied(Func<Task> operation)
        {
            try{await operation();}
            catch(UnauthorizedAccessException){return;}
            throw new InvalidOperationException("Target policy admitted a rejected operation");
        }
        await Denied(()=>user.GetPlaylistsAsync(1,50));
        var p=await ReadTargetOccurrenceAsync(5,ct);
        await Denied(()=>ReadTargetOccurrenceAsync(5,ct));
        targetWire.ReadActive=true;
        try
        {
            await Denied(()=>user.GetPlaylistsAsync(1,50));
            await Denied(()=>user.GetPlaylistsAsync(2,50));
            await Denied(()=>playlist.GetSongsAsync(p.Target.ProviderPlaylistId,1,50));
            await Denied(()=>playlist.GetSongsAsync(p.Target.ProviderPlaylistId,2,50));
            await Denied(()=>playlist.GetSongsAsync(p.First.ProviderPlaylistId,1,50));
            await Denied(()=>SearchAsync("synthetic",1,10,ct));
        }
        finally{targetWire.ReadActive=false;}
        if(MatchesTarget(p,6)||other.MatchesTarget(p,5)||
           MatchesTargetAt(p,5,p.Issued+Stopwatch.Frequency*121))
            throw new InvalidOperationException("Target proof survived scope change");
        var consumers=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Task.Run(()=>ConsumeTarget(p,5),ct)));
        if(consumers.Count(x=>x)!=1||ConsumeTarget(p,5))
            throw new InvalidOperationException("Target proof replayed");
        session.UpdateAuth(session.Session.UserId,"synthetic-replacement","0","",null);
        if(MatchesTarget(p,5))throw new InvalidOperationException("Target proof survived token replacement");
        return 13;
    }

    // Runs after SDK signature/shape checking and before the existing independent VIP/source wire.
    sealed class TargetOccurrenceWire(KgSessionManager session,Func<TargetOccurrenceProof?> proof,
        Func<bool> playbackAdmitted,HttpMessageHandler inner,Action? demand,CancellationToken leaseToken,Action<string> audit):DelegatingHandler(inner)
    {
        int index,members;
        string? playlistId;
        internal CancellationToken ReadCancellation { get; set; }
        internal bool ReadActive { get; set; }
        internal bool Complete=>Volatile.Read(ref index)==1&&Volatile.Read(ref members)==1;
        internal void Select(string id)=>Volatile.Write(ref playlistId,id);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            demand?.Invoke();
            var uri=req.RequestUri!;
            var query=SdkCatalogReadHandler.Query(uri);
            if(query.GetValueOrDefault("userid")!=session.Session.UserId||
               query.GetValueOrDefault("token")!=session.Session.Token)
                throw new UnauthorizedAccessException("Target wire session mismatch");
            if(uri.Host=="gateway.kugou.com"&&uri.AbsolutePath=="/v7/get_all_list"&&req.Method==HttpMethod.Post)
            {
                using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
                if(!ReadActive||body.RootElement.GetProperty("page").GetInt32()!=1||
                   Interlocked.CompareExchange(ref index,1,0)!=0)
                    throw new UnauthorizedAccessException("Target index scope spent or invalid");
            }
            else if(uri.Host=="gateway.kugou.com"&&uri.AbsolutePath=="/pubsongs/v2/get_other_list_file_nofilt"&&req.Method==HttpMethod.Get)
            {
                string? selected=Volatile.Read(ref playlistId);
                if(!ReadActive||selected==null||query.GetValueOrDefault("global_collection_id")!=selected||
                   query.GetValueOrDefault("begin_idx")!="0"||query.GetValueOrDefault("pagesize")!="50"||
                   Interlocked.CompareExchange(ref members,1,0)!=0)
                    throw new UnauthorizedAccessException("Target members scope spent or invalid");
            }
            else if(playbackAdmitted()&&
                    (EntitlementOnlyWire.Admit(req)||SingleSongVipWire.Source(req)&&
                     query.GetValueOrDefault("hash")==proof()?.Track.Id))
            {
                if(demand==null)return await base.SendAsync(req,ct);
            }
            else
                throw new UnauthorizedAccessException("Operation outside target scope");
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,ReadCancellation,leaseToken);
            if(demand!=null)
                audit(uri.AbsolutePath switch {
                    "/v7/get_all_list"=>"M219R_INDEX_ADMITTED",
                    "/pubsongs/v2/get_other_list_file_nofilt"=>"M219R_MEMBER_ADMITTED",
                    "/v1/get_union_vip"=>"M219R_VIP_ADMITTED",
                    _=>"M219R_SOURCE_ADMITTED"
                });
            var response=await base.SendAsync(req,linked.Token);
            try
            {
                await response.Content.LoadIntoBufferAsync(2*1024*1024,linked.Token);
                return response;
            }
            catch{response.Dispose();throw;}
        }
    }
}

