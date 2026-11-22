using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services.Startup;

namespace HuayuanPreview;

// The R2 boundary mounts the existing Provider on the existing Core. Only the
// terminal HTTP response is synthetic. No platform data is admitted as audio.
internal sealed partial class P2AWorkspace : IDisposable
{
    internal static bool M223Normal=>Program.Desktop||Environment.GetEnvironmentVariable("M223_NORMAL")=="1";
    internal static bool RealCombinedVipAudio=>!Program.Desktop&&Environment.GetEnvironmentVariable("M208B_P2BPF_SINGLE")=="1";
    internal static bool RealSingleAudio=>!Program.Desktop&&(Environment.GetEnvironmentVariable("M208B_P2BP_SINGLE")=="1"||RealCombinedVipAudio);
    internal static bool RealEntitlementOnly=>!Program.Desktop&&Environment.GetEnvironmentVariable("M208B_P2BPE_ENTITLEMENT")=="1";
    internal static bool RealReadOnly=>!Program.Desktop&&(Environment.GetEnvironmentVariable("M208B_P2BR_READONLY")=="1"||RealSingleAudio||RealEntitlementOnly);
    internal static bool Admitted => Program.Desktop || Environment.GetEnvironmentVariable("M208B_R2_UI")=="1" &&
        Path.GetFileName(OfflineStartupProfile.Current.Root).StartsWith("p17-body-r3-manual-test-r2-",StringComparison.Ordinal);

    readonly CorePlaybackAdapter core;
    internal static bool M218BTarget => M219RStartup.Real||M213Offline&&Environment.GetEnvironmentVariable("M218B_OFFLINE_TARGET")=="1";
    internal static bool M220SyntheticRealLabels =>
        !M219RStartup.Real && M213Offline &&
        Environment.GetEnvironmentVariable("M218B_OFFLINE_TARGET")=="1" &&
        Environment.GetEnvironmentVariable("M220_SYNTHETIC_REAL_LABELS")=="1";
    DirectSdkGateway.TargetOccurrenceProof? targetProof;
    bool targetAttempted;
    internal bool LocalTrialPassed { get; private set; }
    readonly KugouSessionConsent consent = new();
    // Reuse the already shipped session-bound consent lifecycle. The second
    // instance is for TEST DATA playback only, never the real HTTP transport.
    readonly KugouSessionConsent m210PlaybackConsent = new();
    readonly KugouArtwork artwork;
    readonly TextBlock accountStatus = new() { TextWrapping=TextWrapping.Wrap };
    readonly Button connectA, connectB, connectReal, refresh, revoke, fail, expire, slow, singleTrial,entitlementRead;
    readonly Button m210GrantButton, m210RevokeButton;
    CancellationTokenSource? read;
    string? fixture;
    string scenario="normal";
    bool transitioning,realReadStarted,trialAttempted,trialArmed,entitlementAttempted;
    int? queueUiAdmittedId;bool queueUiPlaying,queueUiAlreadyPlayed;
    internal Task Pending { get; private set; } = Task.CompletedTask;
    internal PlatformPlaylistSnapshot? Snapshot { get; private set; }
    internal event Action? Changed;
    internal Control AccountPage { get; }
    internal bool Connected => core.Kugou.LoggedIn && (Program.Desktop || fixture!=null && consent.Approved);
    internal string? AccountKey=>core.Kugou.DirectAccountCacheKey;
    internal string Status { get; private set; } = "";
    internal bool M210Approved => M210ConsentQueueUi &&
        Connected && (M219RStartup.Active&&M220SyntheticRealLabels||m210PlaybackConsent.Reconcile(core.Kugou.LoggedIn,core.Kugou.Account,AccountKey));
    internal string Summary => Program.Desktop ? "酷狗概念版 · "+Status : M212SnapshotPreview?
        "酷狗概念版 · 本地历史歌单只读预览（未登录、不可入队） · "+Status:
        M223Normal?$"酷狗概念版 · 合成离线预览 · {Status}":
        M219RStartup.Real?$"酷狗概念版 · 本机单次短试听 · {Status}":
        RealEntitlementOnly?$"酷狗概念版 · VIP 权益只读核验 · {Status}":RealReadOnly?$"酷狗概念版 · HTTPS 账号只读 · {Status}":$"TEST DATA · 只读离线 · {Status}";

    internal P2AWorkspace(CorePlaybackAdapter core)
    {
        if(!Admitted)throw new UnauthorizedAccessException("P2A profile boundary");
        if(!Program.Desktop&&F2LiveStrict&&!Path.GetFileName(OfflineStartupProfile.Current.Root)
            .StartsWith("p17-body-r3-manual-test-r2-f2live-",StringComparison.Ordinal))
            throw new UnauthorizedAccessException("F2 live requires new owned profile");
        this.core=core;
        artwork=Program.Desktop||RealReadOnly?new KugouArtwork():new KugouArtwork(new P2AImageResponses(),r2Offline:true);
        artwork.Online=()=>!M219RStartup.Real&&Connected&&!RealSingleAudio&&!RealEntitlementOnly;
        artwork.Account=()=>core.Kugou.DirectAccountCacheKey??"none";
        /* Original constructor properties now assigned after choosing the transport. */
        /* P2BR_ARTWORK_BLOCK_START
        artwork=new KugouArtwork(new P2AImageResponses(),r2Offline:true) {
            Online=()=>Connected, Account=()=>core.Kugou.DirectAccountCacheKey??"none"
        };
        P2BR_ARTWORK_BLOCK_END */
        Snapshot=Program.Desktop?null:M212SnapshotPreview?M212LoadSnapshotReadOnly():
            M218BTarget?null:PlatformPlaylistSnapshotStore.LoadLatest();
        var actions=new WrapPanel();
        Button Add(string text, Func<Task> action)
        {
            var button=new Button { Content=text, Margin=new Thickness(0,0,8,8) };
            button.Click+=(_,_)=>Pending=action();
            actions.Children.Add(button);
            return button;
        }
        connectReal=Add("连接已有酷狗会话（HTTPS只读）",ConnectSavedReadOnlyAsync);
        connectA=Add((M219RStartup.Real||M220SyntheticRealLabels)?"验证已保存会话和目标成员":"连接合成账号 A",()=>ConnectAsync("A"));
        if(M219RStartup.Real||M220SyntheticRealLabels)
        {
            string? fault=M220SyntheticRealLabels?Environment.GetEnvironmentVariable("M222_UI_FAULT"):null;
            if(fault!="missing")
                Avalonia.Automation.AutomationProperties.SetAutomationId(connectA,"M222.Target.Validate");
            if(fault=="duplicate")
            {
                var duplicate=Add("验证已保存会话和目标成员",()=>Task.CompletedTask);
                Avalonia.Automation.AutomationProperties.SetAutomationId(duplicate,"M222.Target.Validate");
            }
        }
        connectB=Add("连接合成账号 B",()=>ConnectAsync("B"));
        singleTrial=Add(F2Preflight?"F2 synthetic single track":"受控验证一首歌曲（静音级短试听）",PlaySingleTrialAsync);
        entitlementRead=Add("仅查询一次当前账号 VIP 权益（不领取）",ReadEntitlementOnceAsync);
        m210GrantButton=Add("确认模拟播放权限（本次会话）",M210GrantAsync);
        m210RevokeButton=Add("撤销模拟播放权限并断开会话",M210RevokeAsync);
        refresh=Add("只读刷新",()=>RefreshAsync());
        revoke=Add(M219RStartup.Real?"撤销本次短试听":RealReadOnly?"断开本次只读会话":"撤销合成会话",RevokeAsync);
        fail=Add("测试读取失败",()=>RefreshAsync("failure"));
        expire=Add("测试账号失效",()=>RefreshAsync("expired"));
        slow=Add("测试封面超时",()=>RefreshAsync("slow"));
        AccountPage=Program.Desktop?BuildDesktopAccountPage():new ScrollViewer { Content=new StackPanel {
            Margin=new Thickness(12), Spacing=12,
            Children={
                new TextBlock { Text=M219RStartup.Real?"酷狗概念版 · 本机单次短试听":RealEntitlementOnly?"酷狗概念版 · VIP 状态只读核验":RealReadOnly?"酷狗概念版 · 真实账号 HTTPS 只读验收":"TEST DATA · 酷狗概念版离线测试",FontSize=21 },
                new TextBlock { Text=RealEntitlementOnly?"仅使用本应用已保存的账号，执行一次 HTTPS VIP 状态 GET。绝不领取、购买、播放、刷新歌单、搜索或写回。无已保存会话则立即停止。":RealReadOnly?"仅使用 HuayuanMusic 已保存的登录。单次只读歌单、歌曲和封面；无二维码、无权益/VIP、无音源、无修改，不访问其他客户端。没有保存会话则停止。":"连接只授权本次合成会话读取。歌单和封面来自本机模拟响应，撤权后保留已同步的只读快照。",TextWrapping=TextWrapping.Wrap },
                new TextBlock{
                    Text=M223Normal?"确认后，可通过普通列表或混合队列播放合成曲目。当前权限仅用于本次合成会话。":"M2-10 合成验收：账号数据与音乐均为本机合成。",
                    TextWrapping=TextWrapping.Wrap,IsVisible=M210ConsentQueueUi
                },
                new Expander {
                    Header="查看完整风险说明（M2-06B 沿用）",
                    IsExpanded=false,IsVisible=M210ConsentQueueUi&&!M223Normal,
                    Content=new TextBlock {Text=PreviewWindow.KugouRiskText,TextWrapping=TextWrapping.Wrap}
                },
                new TextBlock {
                    Text="仅检查旧账户的本地历史歌单。不会连接酷狗服务器，不会读取或复制旧登录凭据，不执行 VIP、源解析、播放或入队。非32位曲目 ID 保留原样。",
                    TextWrapping=TextWrapping.Wrap,IsVisible=M212SnapshotPreview
                },
                accountStatus, actions
            }
        }};
        Report(M212SnapshotPreview?"已加载本地历史歌单，只读且没有登录":
            Snapshot==null?"未连接":"未连接 · 可查看本机只读快照","startup");
    }

    void Report(string message,string action)
    {
        Status=message;
        accountStatus.Text=Summary;
        if (Program.Desktop)
        {
            UpdateDesktopAccountControls();
            Changed?.Invoke();
            return;
        }
        connectReal.IsVisible=!M212SnapshotPreview&&RealReadOnly&&!Connected&&!transitioning;
        connectA.IsVisible=(!M218BTarget||!targetAttempted)&&!M212SnapshotPreview&&!RealReadOnly&&!Connected&&!transitioning;
        connectB.IsVisible=connectA.IsVisible&&!F2Preflight;
        singleTrial.IsVisible=!M209SyntheticQueueUi&&!M213RowMode&&(RealSingleAudio||F2Preflight)&&Connected&&read==null&&!transitioning&&!trialAttempted&&(realReadStarted||F2Preflight)&&Snapshot!=null;
        entitlementRead.IsVisible=RealEntitlementOnly&&Connected&&!transitioning&&!entitlementAttempted;
        m210GrantButton.IsVisible=M210ConsentQueueUi&&Connected&&!transitioning&&!M210Approved;
        m210RevokeButton.IsVisible=M210ConsentQueueUi&&Connected&&!transitioning&&M210Approved;
        refresh.IsVisible=!M218BTarget&&!M212SnapshotPreview&&!RealReadOnly&&Connected&&read==null&&!transitioning;
        fail.IsVisible=expire.IsVisible=slow.IsVisible=!M223Normal&&!M218BTarget&&!M212SnapshotPreview&&!RealReadOnly&&Connected&&read==null&&!transitioning;
        revoke.IsVisible=Connected&&!transitioning;
        Program.Log("P2A_READBACK",new { action, connected=Connected, lists=Snapshot?.Playlists.Count??0 });
        Changed?.Invoke();
    }

    internal async Task ConnectAsync(string selected)
    {
        if(M212SnapshotPreview||RealReadOnly||transitioning||Connected||(F2Preflight&&selected!="A"))return;
        if(M218BTarget&&targetAttempted)return;
        if(M218BTarget)targetAttempted=true;
        m210PlaybackConsent.Revoke();
        transitioning=true;
        try {
            await consent.RequestAsync(()=>Task.FromResult(true)); // Explicit synthetic-connect control only.
            core.EnableKugou();
            var gateway=M219RStartup.Lease is { } lease
                ?lease.Real
                    ?DirectSdkGateway.CreateSavedLocalTrial(lease,()=>Task.FromResult(trialArmed))
                    :DirectSdkGateway.CreateSyntheticLocalTrial(lease,new P2AResponses(selected,()=>scenario),
                        new F2SyntheticMedia(),()=>Task.FromResult(trialArmed))
                :CreateExistingOfflineGateway(selected);
            if(F2Preflight)gateway.OneSongDiagnostic+=stage=>Program.Log("F2_STAGE",new{stage});
            gateway.PlaylistArtworkDiagnostic+=reason=>KugouArtwork.Diagnostic(reason);
            if(!gateway.LoggedIn){gateway.Dispose();throw new UnauthorizedAccessException("Saved session unavailable");}
            try { await core.Kugou.ConnectSdkAsync(gateway); }
            catch { gateway.Dispose(); throw; }
            if(!consent.Reconcile(core.Kugou.LoggedIn,core.Kugou.Account,core.Kugou.DirectAccountCacheKey))
                throw new UnauthorizedAccessException("Session binding failed");
            fixture=selected;
            if(M218BTarget)
            {
                Program.Log("M222_TARGET_BEGIN",new{leaseMs=M219RStartup.Lease?.ElapsedMs});
                targetProof=await core.Kugou.ReadTargetOccurrenceAsync(M219RStartup.Lease?.Token??default);
                Snapshot=new PlatformPlaylistSnapshot(1,KugouProvider.Identity,targetProof.AccountPartition,
                    targetProof.VerifiedAt,Array.AsReadOnly(new[]{targetProof.First,targetProof.Target}));
                m216SessionSnapshot=Snapshot;
                Program.Log("M218B_TARGET",new{status="PASS",index=targetProof.IndexRequests,members=targetProof.MemberRequests});
            }
            else Snapshot=PlatformPlaylistSnapshotStore.LoadLatest(core.Kugou.DirectAccountCacheKey);
        }
        catch(Exception ex) {
            if(M218BTarget)
            {
                M219RStartup.Lease?.Stop();
                Program.Result=2;
                Program.Log("M218B_TARGET",new{status="FAIL",errorType=ex.GetType().Name});
            }
            consent.Revoke();
            fixture=null;
            await core.DisableKugouAsync();
            transitioning=false;
            Report("会话或目标验证失败","connect-failed");
            return;
        }
        finally { transitioning=false; }
        Report(M219RStartup.Real?"已验证目标成员，可手动入队两次":$"合成账号 {fixture} 已连接","connected");
        if(!M218BTarget)await RefreshAsync();
    }

    DirectSdkGateway CreateExistingOfflineGateway(string selected)
    {
        var catalog=new P2AResponses(selected,()=>scenario);
        if(M223Normal)
        {
            var gateway=DirectSdkGateway.CreateOfflinePlaybackSession(selected,catalog,new M223Media(),
                ()=>Task.FromResult(M210Approved));
            gateway.OneSongDiagnostic+=phase=>Program.Log("M223_SDK_PHASE",new{phase});
            return gateway;
        }
        return M218BTarget
                ?DirectSdkGateway.CreateOfflineTargetSession(catalog,new F2SyntheticMedia(),
                    ()=>Task.FromResult(trialArmed))
                :F2Preflight&&selected=="A"
                ?(F2Case is "http-original" or "queue-ui" or "queue-consent" or "queue-intent" or "m213-offline"
                    ?DirectSdkGateway.CreateOriginalHttpSyntheticF2Session(catalog,new F2SyntheticMedia(),
                        ()=>Task.FromResult(trialArmed||queueUiPlaying))
                    :DirectSdkGateway.CreateF2PreflightSession(catalog,new F2SyntheticMedia(),
                        ()=>Task.FromResult(trialArmed)))
                :DirectSdkGateway.CreateOfflineReadSession(selected,catalog);
    }

    internal async Task M210GrantAsync()
    {
        if(!M210ConsentQueueUi||RealReadOnly||!Connected||transitioning||read!=null)return;
        // The user presses the displayed, one-time synthetic consent button.
        // NO filesystem/credential/real-network permission is written.
        if(!await m210PlaybackConsent.RequestAsync(()=>Task.FromResult(true))||
            !m210PlaybackConsent.Reconcile(core.Kugou.LoggedIn,core.Kugou.Account,AccountKey))
        {
            Program.Log("M210_CONSENT",new{state="DENIED",syntheticOnly=true});
            return;
        }
        Program.Log("M210_CONSENT",new{state="GRANTED",syntheticOnly=true,
            realMediaNetwork=false,persistentHttpAllowed=false});
        Report("已确认本次合成播放。后续入队和播放无需反复确认；不授权真实 HTTP。",
            "m210-consent-granted");
    }
    internal async Task M210RevokeAsync()
    {
        if(!M210ConsentQueueUi||!Connected||transitioning)return;
        m210PlaybackConsent.Revoke();
        Program.Log("M210_CONSENT",new{state="REVOKED",syntheticOnly=true,
            realMediaNetwork=false,persistentHttpAllowed=false});
        // Reuse existing session revocation. This invalidates queued provider
        // stream leases and stops a playing provider without deleting metadata.
        await RevokeAsync();
    }

    // Only the immutable selected playlist occurrence may become a
    // metadata-only play intent. No entitlement, URL, Grant or HTTP involved.
    internal bool M211TryPrepare(string key,out M211PlaybackIntent? intent)
    {
        intent=null;
        if(!M211StrictMemberIntent||!Connected||Snapshot==null||
            AccountKey==null||string.IsNullOrWhiteSpace(key))return false;
        var prefix="kugou-concept:"+Snapshot.AccountKey+":";
        if(!key.StartsWith(prefix,StringComparison.Ordinal))return false;
        var tail=key[prefix.Length..];
        int last=tail.LastIndexOf(':');
        if(last<=0)return false;
        string trackHash=tail[(last+1)..];
        var preceding=tail[..last];
        int middle=preceding.LastIndexOf(':');
        if(middle<=0)return false;
        string playlistId=preceding[..middle];
        if(!int.TryParse(preceding[(middle+1)..],out int occurrence))return false;
        return M211PlaybackIntentAuthority.TryPrepare(Snapshot,AccountKey,
            playlistId,occurrence,trackHash,out intent);
    }
    internal int M211MemberNumber(string key)=>
        M211TryPrepare(key,out var selected)?selected!.MembershipIndex+1:0;

    // Offline synthetic fixture: the only admitted CDN route is in-memory.
    // Explicitly not a real platform playback path. One eligible SDK hash only.
    internal bool M209CanQueue(string key)
    {
        if(!M209SyntheticQueueUi||RealReadOnly||!Connected||Snapshot==null||
            (M210ConsentQueueUi&&!M210Approved))return false;
        var prefix="kugou-concept:"+Snapshot.AccountKey+":";
        if(!key.StartsWith(prefix,StringComparison.Ordinal))return false;
        if(M211StrictMemberIntent)
            return M211TryPrepare(key,out var occurrence)&&
                occurrence!.TrackHash==new string('a',32);
        string hash=key.Split(':').Last();
        return hash==new string('a',32)&&
            Snapshot.Playlists.SelectMany(p=>p.Tracks).Any(t=>t.ProviderTrackId==hash);
    }

    internal void M209Submit(string key,bool start)
    {
        var previous=Pending;
        Pending=Run();
        async Task Run()
        {
            await previous;
            if(!M209CanQueue(key)||read!=null||transitioning)
            {
                Program.Log("M209_UI_REJECTED",new{reason="MODE_OR_SESSION"});
                return;
            }
            if(M211StrictMemberIntent)
            {
                if(!M211TryPrepare(key,out var selected)||selected==null)
                    throw new UnauthorizedAccessException("Invalid selected song occurrence");
                Program.Log("M211_INTENT_BIND",new{
                    action=start?"play":"append",playlistBound=true,
                    occurrence=selected.MembershipIndex,accountBound=true,
                    metadataOnly=true,network="NONE"});
            }
            transitioning=true;
            int beforeCount=core.State.QueueEntries.Length;
            var before=core.State.QueueEntries.ToArray();
            var beforeCurrent=core.State.CurrentEntryId;
            bool beforePlaying=core.State.Playing;
            try
            {
                if(start&&queueUiAlreadyPlayed)
                    throw new UnauthorizedAccessException("Synthetic one-shot audio already used");
                var hash=key.Split(':').Last();
                var track=core.Kugou.CatalogTrackForPlaylist(hash)
                    ??throw new UnauthorizedAccessException("No currently verified synthetic catalog metadata");
                if(queueUiAdmittedId is null)
                    queueUiAdmittedId=await core.AdmitKugouAsync(track,"standard");
                if(!core.State.Tracks.Any(t=>t.Id==queueUiAdmittedId))
                    throw new InvalidOperationException("The synthetic provider track has been revoked");
                await core.QueueAsync("append",queueUiAdmittedId.Value);
                if(core.State.Error.Length>0)throw new IOException("Core queue append failed");
                var added=core.State.QueueEntries.Skip(beforeCount).ToArray();
                if(added.Length!=1||added[0].TrackId!=queueUiAdmittedId||
                    !core.State.QueueEntries.Take(beforeCount).SequenceEqual(before)||
                    core.State.QueueEntries.Select(e=>e.EntryId).Distinct().Count()!=core.State.QueueEntries.Length)
                    throw new InvalidOperationException("Queue identity or prior ordering changed");
                if(!start&&
                    (core.State.CurrentEntryId!=beforeCurrent||core.State.Playing!=beforePlaying))
                    throw new InvalidOperationException("Append unexpectedly changed playback");
                double rms=0;
                if(start)
                {
                    // The offline F2 wire contract requires one entitlement response
                    // before it admits a song-source request. Synthetic data only.
                    var entitlement=await core.Kugou.RefreshEntitlementAsync(CancellationToken.None);
                    if(!entitlement.Active)throw new UnauthorizedAccessException("Synthetic entitlement denied");
                    Program.Log("M209_SYNTHETIC_VIP",new {active=true,network="SYNTHETIC_ONLY"});
                    queueUiAlreadyPlayed=true;
                    queueUiPlaying=true;
                    try
                    {
                        await core.QueueEntryAsync("play",added[0].EntryId);
                        if(core.State.Error.Length>0||core.State.CurrentEntryId!=added[0].EntryId||
                           !core.State.Playing)throw new IOException("Core did not start selected synthetic entry");
                        await Task.Delay(500);
                        rms=core.TrialDecodedRms;
                    }
                    finally{queueUiPlaying=false;}
                }
                Program.Log("M209_QUEUE_UI",new{
                    action=start?"play":"append",beforeCount,afterCount=core.State.QueueEntries.Length,
                    beforeOrderPreserved=true,distinctEntryIds=true,
                    priorPlaybackUnchanged=!start,played=start&&core.State.Playing,
                    rmsPeak=Math.Round(rms,7),syntheticOnly=true,
                    realHttpRequests=0,sourceRequests=core.Kugou.RealSongSourceRequestCount});
                Report(start?"已启动合成单曲，原队列保留":"合成曲目已追加到统一队列，原队列保留",
                    start?"m209-play":"m209-append");
            }
            catch(Exception e)
            {
                Program.Log("M209_QUEUE_UI_ERROR",new{action=start?"play":"append",type=e.GetType().Name});
                Report("合成队列操作未通过，请查看隔离验收报告","m209-blocked");
            }
            finally{
                transitioning=false;
                if(M210ConsentQueueUi)Report(Status,"m210-ui-ready");
            }
        }
    }

    // One-shot saved-session read. No QR challenge, no media, no VIP/claim, no search.
    internal async Task ConnectSavedReadOnlyAsync()
    {
        if(!RealReadOnly||transitioning||Connected)return;
        transitioning=true;
        try
        {
            if(!await consent.RequestAsync(()=>Task.FromResult(true)))return;
            var gateway=RealEntitlementOnly
                ?DirectSdkGateway.CreateSavedEntitlementReadOnlySession()
                :RealCombinedVipAudio?DirectSdkGateway.CreateSavedCombinedVipSingleAudioSession(
                    ()=>Task.FromResult(trialArmed&&trialAttempted))
                :RealSingleAudio?DirectSdkGateway.CreateSavedSingleAudioSession(
                    ()=>Task.FromResult(trialArmed&&trialAttempted))
                :DirectSdkGateway.CreateSavedReadOnlySession();
            if(RealSingleAudio)gateway.OneSongDiagnostic+=stage=>Program.Log("P2BP_SDK_STAGE",new{stage});
            if(!gateway.LoggedIn)
            {
                gateway.Dispose();consent.Revoke();fixture=null;
                Program.Log("P2BR_CONNECT",new{state="NO_SAVED_SESSION"});
                Report("未找到本应用保存的酷狗登录。未发送任何网络请求。","no-saved-session");
                return;
            }
            core.EnableKugou();
            try{await core.Kugou.ConnectSdkAsync(gateway);}
            catch{gateway.Dispose();throw;}
            if(!consent.Reconcile(core.Kugou.LoggedIn,core.Kugou.Account,core.Kugou.DirectAccountCacheKey))
                throw new UnauthorizedAccessException("保存的账号绑定检查失败");
            fixture="SAVED";
            if(!RealEntitlementOnly)Snapshot=PlatformPlaylistSnapshotStore.LoadLatest(core.Kugou.DirectAccountCacheKey!);
            if(F2LiveStrict)
            {
                if(Snapshot==null||Snapshot.Playlists.Count!=7||
                   Snapshot.Playlists.Sum(p=>p.Tracks.Count)!=2875)
                    throw new UnauthorizedAccessException("F2 live requires previously verified account snapshot");
                var candidate=M213RealOnce
                    ?Snapshot.Playlists.Count>1&&Snapshot.Playlists[1].Tracks.Count>0
                       ?Snapshot.Playlists[1].Tracks[0]:null
                    :Snapshot.Playlists.SelectMany(p=>p.Tracks)
                       .FirstOrDefault(t=>t.ProviderTrackId.Length==32&&t.ProviderTrackId.All(Uri.IsHexDigit));
                if(candidate==null)throw new UnauthorizedAccessException("No bound snapshot candidate");
                if(M213RealOnce &&
                   (!M211PlaybackIntentAuthority.TryPrepare(Snapshot,core.Kugou.DirectAccountCacheKey,
                       Snapshot.Playlists[1].ProviderPlaylistId,0,candidate.ProviderTrackId,out var intended)
                    || intended==null))
                    throw new UnauthorizedAccessException("Selected occurrence is not bound to this SDK account");
                gateway.PrimeF2LiveSnapshotCandidate(candidate);
                realReadStarted=true;
                Program.Log("F2_LIVE_SNAPSHOT",new{validated=true,lists=Snapshot.Playlists.Count,
                    entries=Snapshot.Playlists.Sum(p=>p.Tracks.Count),catalogGets=0,
                    m213SelectedOccurrence=M213RealOnce?"LIST_2_MEMBER_1":"NONE"});
            }
            Program.Log("P2BR_CONNECT",new{state="SAVED_SESSION_READY",hasSavedSession=true,accountKeyHashPresent=core.Kugou.DirectAccountCacheKey!=null});
        }
        catch(Exception ex)
        {
            consent.Revoke();fixture=null;
            await core.DisableKugouAsync();
            Program.Log("P2BR_CONNECT",new{state="FAILED",type=ex.GetType().Name});
            Report("已有登录未通过连接验证，未读取歌单","connect-failed");
            return;
        }
        finally{transitioning=false;}
        if(RealEntitlementOnly){Report("已恢复账号内存会话；尚未发出网络请求，请点击只读权益查询","connected-entitlement");return;}
        if(F2LiveStrict){Report(M213RealOnce?
            "已绑定指定歌单记录。先两次入队，不申请音源，再点播放进行一次短试听":
            "已绑定账号只读快照；未联网刷新歌单；仅待本轮一次试听",
            "f2live-snapshot-ready");return;}
        Report("已连接已保存会话，仅执行一次只读刷新","connected-readonly");
        await RefreshAsync();
    }

    async Task ReadAsync(string action, Func<CancellationToken,Task> operation)
    {
        if(!Connected||read!=null||transitioning)return;
        using var cancel=new CancellationTokenSource();
        if(RealReadOnly)cancel.CancelAfter(TimeSpan.FromSeconds(100));
        read=cancel;
        Report(Program.Desktop?"正在同步酷狗":RealReadOnly?"官方歌单 · 正在执行一次只读同步":$"合成账号 {fixture} · 正在读取",action+"-start");
        string result;
        try { await operation(cancel.Token); result=Program.Desktop?"同步完成":RealReadOnly?"真实账号 · 官方歌单只读同步完成":$"合成账号 {fixture} · 读取完成"; }
        catch(OperationCanceledException) when(cancel.IsCancellationRequested) { result="读取已取消"; }
        catch(HttpRequestException ex) when(ex.StatusCode==HttpStatusCode.Unauthorized) {
            read=null;
            if (Program.Desktop) await CloseDesktopConnectionAsync(); else await RevokeAsync();
            Report(Program.Desktop?"登录已失效，请重新扫码":RealReadOnly?"账号会话失效 · 已断开 · 保留只读快照":"合成会话已失效 · 已撤权 · 保留只读快照","expired");
            return;
        }
        catch(Exception ex) {
            Program.Log("P2A_READ_FAILURE",new { action, type=ex.GetType().Name });
            result=Program.Desktop?"酷狗请求失败，可重新连接或刷新":RealReadOnly?"真实只读读取失败 · 保留已有快照":"读取失败 · 保留已有结果";
        }
        finally { if(ReferenceEquals(read,cancel))read=null; }
        if(!transitioning)Report(result,action+"-finish");
    }

    internal Task RefreshAsync(string scenario="normal"){if(M218BTarget)throw new UnauthorizedAccessException("Full sync unavailable in target scope");if(RealReadOnly){if(realReadStarted)return Task.CompletedTask;realReadStarted=true;}return ReadAsync("refresh",async ct=>{
        var account=core.Kugou.Account!;
        var key=core.Kugou.DirectAccountCacheKey!;
        this.scenario=scenario;
        if(M213RowMode)m216SessionSnapshot=null;
        try {
            var lists=await core.Kugou.ReadAsync(account,ct);
            ct.ThrowIfCancellationRequested();
            if(Program.Desktop ? !Connected || key != AccountKey : !consent.Reconcile(core.Kugou.LoggedIn,core.Kugou.Account,core.Kugou.DirectAccountCacheKey))
                throw new UnauthorizedAccessException("Account changed during playlist sync");
            PlatformPlaylistSnapshotStore.Save(key,lists);
            Snapshot=PlatformPlaylistSnapshotStore.LoadLatest(key)??throw new IOException("Snapshot readback missing");
            if(M213RowMode)
            {
                m216SessionGeneration=core.Kugou.Generation;
                m216SessionSnapshot=Snapshot;
            }
            if (Program.Desktop) Program.Log("DESKTOP_PLAYLIST_SYNC", new
            {
                playlists = Snapshot.Playlists.Count,
                members = Snapshot.Playlists.Sum(list => list.Tracks.Count)
            });
            if(RealReadOnly)Program.Log("P2BR_READ_RESULT",new{
                status="PASS",playlistCount=Snapshot.Playlists.Count,
                trackMembershipCount=Snapshot.Playlists.Sum(p=>p.Tracks.Count),
                playlistCoverMetadataCount=Snapshot.Playlists.Count(p=>p.CoverUrl!=null),
                songCoverMetadataCount=Snapshot.Playlists.Sum(p=>p.Tracks.Count(t=>t.CoverUrl!=null)),
                accountPartitionValid=Snapshot.AccountKey==key,
                providerSourceRequests=core.Kugou.RealSongSourceRequestCount,
                providerStreamCount=core.ProviderStreamCount
            });
        }
        finally { this.scenario="normal"; }
    });}

    internal Task SearchAsync(string term,Action<SearchPage> show){if(RealReadOnly||M219RStartup.Active)throw new UnauthorizedAccessException("P2B-R 仅授权歌单，不含搜索");return ReadAsync("search",async ct=>{
        var page=await core.Kugou.SearchAsync(term,ct:ct);
        ct.ThrowIfCancellationRequested();
        show(page);
    });}

    // One request only, one explicit user-visible action. No playlists, audio,
    // credits, payment, claim history or remote write may be requested in this mode.
    internal async Task ReadEntitlementOnceAsync()
    {
        if(!RealEntitlementOnly||!Connected||entitlementAttempted||transitioning||read!=null)return;
        entitlementAttempted=true;
        string outcome="UNKNOWN",errorType="NONE";
        transitioning=true;
        Report("正在执行一次 VIP 只读 GET；不领取、不播放","entitlement-start");
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(18));
            var value=await core.Kugou.RefreshEntitlementAsync(deadline.Token);
            if(!Connected)throw new UnauthorizedAccessException("账号会话在查询期间失效");
            outcome=value.Active?"ACTIVE":"INACTIVE";
        }
        catch(Exception ex){errorType=ex.GetType().Name;}
        finally
        {
            try{
                consent.Revoke();artwork.Revoke();
                await core.DisableKugouAsync();
            }catch(Exception ex){Program.Log("P2BPE_CLEANUP",new{type=ex.GetType().Name});}
            fixture=null;transitioning=false;
            Program.Log("P2BPE_ENTITLEMENT_RESULT",new{
                status=outcome,errorType,oneReadOnlyGet=true,
                vipClaimRequests=0,purchaseRequests=0,mediaSourceRequests=0,
                officialPlaylistWriteRequests=0,noPersistentChange=true
            });
            Report(outcome=="ACTIVE"?"当前账号 VIP 权益：已确认有效（本轮未播放）":
                outcome=="INACTIVE"?"当前账号 VIP 权益：未确认有效（不领取）":
                "当前账号 VIP 权益：查询未完成或返回未知（不推断无会员）",
                "entitlement-finish");
        }
    }

    // Isolated 6-second proof. The official playlist remains remote read-only,
    // and the live source handle is ephemeral in the one existing Core queue.
    internal async Task PlaySingleTrialAsync()
    {
        if(!(RealSingleAudio||F2Preflight)||!Connected||trialAttempted||read!=null||transitioning||Snapshot==null)return;
        trialAttempted=true;trialArmed=true;transitioning=true;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(40));
        trialCancellation=deadline;
        Report("单曲验证中","trial-start");
        Guid created=Guid.Empty;
        int? admitted=null;
        var original=core.State.QueueEntries.ToArray();
        var before=original.Select(x=>x.EntryId).ToHashSet();
        var initialVolume=core.State.Volume;
        bool started=false;double maxDecodedRms=0;long sourceRequestsBeforeRevoke=0;
        string outcome="BLOCKED",reason="NONE";
        try
        {
            var eligible=Snapshot.Playlists.SelectMany(x=>x.Tracks)
                .Where(x=>x.ProviderTrackId.Length==32&&x.ProviderTrackId.All(Uri.IsHexDigit))
                .Select(x=>core.Kugou.CatalogTrackForPlaylist(x.ProviderTrackId))
                .FirstOrDefault(x=>x!=null);
            if(eligible==null)throw new InvalidDataException("当前只读快照中不存在已授权 SDK 核对的可尝试曲目");
            if(core.Kugou.RealSongSourceRequestCount!=0)throw new UnauthorizedAccessException("单曲额度已消耗");
            if(RealCombinedVipAudio||F2Preflight)
            {
                // A fresh read for THIS active SDK account, never a cached P2BP-E
                // entitlement from a previous process or user session.
                using var authLimit=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                authLimit.CancelAfter(TimeSpan.FromSeconds(18));
                var membership=await core.Kugou.RefreshEntitlementAsync(authLimit.Token);
                if(!Connected)throw new UnauthorizedAccessException("SDK account changed during VIP read");
                Program.Log("P2BPF_VIP",new{status=membership.Active?"ACTIVE":"INACTIVE"});
                if(!membership.Active)
                    throw new UnauthorizedAccessException("Current live VIP entitlement is not active");
            }
            await core.VolumeAsync(0.02);
            int id=await core.AdmitKugouAsync(eligible,"standard",deadline.Token);
            admitted=id;
            await core.QueueAsync("append",id);
            if(core.State.Error.Length!=0)throw new IOException("核心队列追加未通过");
            var entry=core.State.QueueEntries.Where(x=>!before.Contains(x.EntryId)).ToArray();
            if(entry.Length!=1||entry[0].TrackId!=id)
                throw new InvalidDataException("单曲队列 EntryId 回读不一致");
            created=entry[0].EntryId;
            await core.QueueEntryAsync("play",created);
            deadline.Token.ThrowIfCancellationRequested();
            if(core.State.Error.Length!=0||!core.State.Playing||core.State.CurrentEntryId!=created)
                throw new IOException("平台音源未被唯一 Core 成功播放");
            started=true;
            if(F2Preflight)Program.Log("F2_ACTIVE_BASS",new{devices=PreviewWindow.F2BassDevices()});
            for(int sample=0;sample<(F2Preflight?4:12);sample++){
                await Task.Delay(500,deadline.Token);
                maxDecodedRms=Math.Max(maxDecodedRms,core.TrialDecodedRms);
            }
            outcome=core.State.CurrentEntryId==created&&core.State.Playing&&maxDecodedRms>0?"PASS":"PARTIAL";
        }
        catch(Exception e){reason=e.GetType().Name;}
        finally
        {
            trialArmed=false;trialCancellation=null;
            sourceRequestsBeforeRevoke=core.Kugou.RealSongSourceRequestCount;
            try{await core.StopAsync();}catch(Exception e){Program.Log("P2BP_CLEANUP",new{step="stop",type=e.GetType().Name});}
            // Filter by the admitted track and new entry identity even after a partial append.
            foreach(var entryId in core.State.QueueEntries.Where(x=>x.TrackId==admitted&&!before.Contains(x.EntryId)).Select(x=>x.EntryId).ToArray())
                try{await core.QueueEntryAsync("remove",entryId);}
                catch(Exception e){Program.Log("P2BP_CLEANUP",new{step="remove",type=e.GetType().Name});}
            try{await core.DisableKugouAsync();}
            catch(Exception e){Program.Log("P2BP_CLEANUP",new{step="provider-disable",type=e.GetType().Name});}
            consent.Revoke();fixture=null;artwork.Revoke();
            try{await core.VolumeAsync(initialVolume);}catch{}
            Program.Log("P2BP_SINGLE",new{
                status=outcome,errorType=reason,started,decodedRmsPeak=Math.Round(maxDecodedRms,7),
                liveEntitlementRequired=RealCombinedVipAudio,
                sourceRequests=sourceRequestsBeforeRevoke,
                providerTracks=core.ProviderStreamCount,
                queueBefore=before.Count,queueAfter=core.State.QueueEntries.Length,
                queueUnchanged=original.SequenceEqual(core.State.QueueEntries),
                volumeRestored=core.State.Volume==initialVolume,
                stopped=!core.State.Playing&&!core.State.Paused,
                sessionCleared=!Connected&&!core.Kugou.LoggedIn&&!core.Kugou.Enabled&&core.Kugou.Account==null,
                cancelled=deadline.IsCancellationRequested,
                oneCore=true,accountTokenPersisted=false,
                vipClaimRequests=0,remoteWriteRequests=0
            });
            transitioning=false;
            Report(outcome=="PASS"?"单曲短时解码完成，测试音源与权限已撤销":
               "受控单曲未通过，未扩大授权；原本地队列保留","single-audio-result");
        }
    }

    internal async Task RevokeAsync()
    {
        if(transitioning||fixture==null)return;
        M219RStartup.Lease?.Stop();
        transitioning=true;
        try {
            if(M213RowMode&&m213Selected!=null&&!m213Finished)
            {
                var sourceRequests=core.Kugou.RealSongSourceRequestCount;
                await M213CleanupAsync();
                Program.Log("M216_REVOKE_CLEANUP",new{
                    queueRestored=m213OriginalQueue!.SequenceEqual(core.State.QueueEntries),
                    selectionRestored=core.State.CurrentEntryId==m216InitialEntry,
                    sourceRequests,cleanupErrors=m214CleanupErrors});
                m216SessionSnapshot=null;
                transitioning=false;
                Report("已撤权并回退试听队列","revoked");
                return;
            }
            m216SessionSnapshot=null;
            m210PlaybackConsent.Revoke();
            consent.Revoke();
            read?.Cancel();
            artwork.Revoke();
            core.Kugou.CancelPlaylistDetails();
            await core.DisableKugouAsync();
            fixture=null;
        }
        finally { transitioning=false; }
        if(M218BTarget){targetProof=null;Snapshot=null;m216SessionSnapshot=null;}
        Report(RealReadOnly?"已断开真实账号只读会话 · 保留官方歌单快照":"已撤销合成会话 · 保留只读快照","revoked");
    }

    internal int CatalogDuration(string trackId) => core.Kugou.CatalogTrackForPlaylist(trackId)?.Seconds ?? 0;

    internal Task<Avalonia.Media.Imaging.Bitmap?> LoadLocalCover(string source) => artwork.LoadLocal(source);

    internal Control Cover(string? account,string? url,double size)=>new PreviewWindow.ArtworkTile(
        artwork,account??"none",url,size,Connected,null);

    public void Dispose()=>artwork.Dispose();
}
