using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview;

// One-shot bridge across a verified PLAYLIST OCCURRENCE and the unique Core.
// It does not create a new queue, media transport, account or permissions.
internal sealed partial class P2AWorkspace
{
    readonly List<Guid> m213AddedEntries=new();
    CoreQueueEntry[]? m213OriginalQueue;
    M211PlaybackIntent? m213Selected;
    float m213InitialVolume;
    Guid m216InitialEntry;
    PlatformPlaylistSnapshot? m216SessionSnapshot;
    long m216SessionGeneration;

    bool M216CurrentSnapshot()=>Connected&&core.Kugou.Enabled&&
        ReferenceEquals(Snapshot,m216SessionSnapshot)&&Snapshot is {Version:1} current&&
        (M218BTarget?targetProof!=null&&core.Kugou.MatchesTarget(targetProof):
            core.Kugou.Generation==m216SessionGeneration&&DateTimeOffset.UtcNow-current.SyncedAt<=TimeSpan.FromMinutes(10));

    bool M216OriginalPlayback()=>!core.State.Playing&&!core.State.Paused&&
        (m213OriginalQueue==null||core.State.CurrentEntryId==m216InitialEntry);

    bool M216ExactQueue()=>m213OriginalQueue!=null&&m213CoreTrackId is int trackId&&
        core.State.QueueEntries.SequenceEqual(m213OriginalQueue.Concat(
            m213AddedEntries.Select(id=>new CoreQueueEntry(id,trackId))));

    internal async Task M216StopAsync()
    {
        if(CancelTrial())
        {
            await core.StopAsync();
            await Pending;
        }
        else if(m213Selected!=null&&!m213Finished)
        {
            await Pending;
            await RevokeAsync();
        }
        else if(M219RStartup.Active)
        {
            M219RStartup.Lease?.Stop();
            await Pending;
            await RevokeAsync();
            await core.StopAsync();
        }
        else await core.StopAsync();
    }
    int? m213CoreTrackId;
    bool m213MediaStarted,m213Finished;
    int m213UiActions,m214CleanupErrors;

    internal bool M213CanQueue(string key)
    {
        if(!M213RowMode||!M216CurrentSnapshot()||!M216OriginalPlayback()||Snapshot==null||trialAttempted||
           transitioning||read!=null||m213Finished||
           (M213Offline&&!M210Approved)||
           !M211TryPrepare(key,out var proof)||proof==null)
            return false;
        if(Snapshot.Playlists.Count<2||Snapshot.Playlists[1].Tracks.Count==0||
           !string.Equals(Snapshot.Playlists[1].ProviderPlaylistId,proof.PlaylistId,StringComparison.Ordinal)||
           proof.MembershipIndex!=0||
           !string.Equals(Snapshot.Playlists[1].Tracks[0].ProviderTrackId,proof.TrackHash,StringComparison.OrdinalIgnoreCase))
            return false;
        if(!M213Offline&&!M213RealOnce&&!M219RStartup.Real)return false;
        if(m213Selected!=null &&
           (m213Selected.PlaylistId!=proof.PlaylistId||
            m213Selected.MembershipIndex!=proof.MembershipIndex||
            m213Selected.TrackHash!=proof.TrackHash||
            m213Selected.AccountPartition!=proof.AccountPartition))return false;
        return core.Kugou.CatalogTrackForPlaylist(proof.TrackHash)!=null;
    }

    internal void M213Submit(string key,bool play)
    {
        var previous=Pending;
        Pending=Submit();
        async Task Submit()
        {
            await previous;
            if(!M213CanQueue(key))
            {
                Program.Log("M213_REJECTED",new{reason="IDENTITY_OR_SESSION_OR_QUOTA",play});
                if(m213Selected!=null&&!m213Finished&&!trialAttempted)
                    await RevokeAsync();
                return;
            }
            transitioning=true;
            try
            {
                if(!M216CurrentSnapshot()||!M216OriginalPlayback()||!M211TryPrepare(key,out var proof)||proof==null)
                    throw new UnauthorizedAccessException("Current member identity changed");
                if(m213Selected==null)
                {
                    m213OriginalQueue=core.State.QueueEntries.ToArray();
                    m213InitialVolume=core.State.Volume;
                    m216InitialEntry=core.State.CurrentEntryId;
                    m213Selected=proof;
                }
                if(!play)
                {
                    if(m213AddedEntries.Count>=2||trialAttempted)
                        throw new UnauthorizedAccessException("Only two enqueue actions permitted");
                    var before=core.State.QueueEntries.ToArray();
                    var originalPlayback=core.State.CurrentEntryId;
                    bool originalIsPlaying=core.State.Playing;
                    var sourceCounter=core.Kugou.RealSongSourceRequestCount;
                    if(sourceCounter!=0||core.State.Error.Length>0)
                        throw new UnauthorizedAccessException("Song source budget already consumed");
                    if(m213CoreTrackId==null)
                    {
                        var track=core.Kugou.CatalogTrackForPlaylist(proof.TrackHash)
                            ??throw new UnauthorizedAccessException("SDK has not primed this selected track");
                        m213CoreTrackId=await core.AdmitKugouAsync(track,"standard");
                    }
                    if(!core.State.Tracks.Any(t=>t.Id==m213CoreTrackId))
                        throw new IOException("Core source registration unavailable");
                    await core.QueueAsync("append",m213CoreTrackId.Value);
                    var added=core.State.QueueEntries.Skip(before.Length).ToArray();
                    if(added.Length==1&&added[0].TrackId==m213CoreTrackId)
                        m213AddedEntries.Add(added[0].EntryId);
                    if(core.State.Error.Length>0)throw new IOException("Queue append failed");
                    if(added.Length!=1||added[0].TrackId!=m213CoreTrackId||
                       !core.State.QueueEntries.Take(before.Length).SequenceEqual(before)||
                       core.State.QueueEntries.Select(t=>t.EntryId).Distinct().Count()!=core.State.QueueEntries.Length||
                       core.State.CurrentEntryId!=originalPlayback||
                       core.State.Playing!=originalIsPlaying||
                       core.Kugou.RealSongSourceRequestCount!=0)
                        throw new IOException("Queue changed, playback started, or media was requested while enqueuing");
                    m213UiActions++;
                    Program.Log("M213_QUEUE",new{
                        action="append",actionCount=m213UiActions,
                        countBefore=before.Length,countAfter=core.State.QueueEntries.Length,
                        originalOrderUnchanged=true,entryIdsDistinct=true,
                        sourceRequests=core.Kugou.RealSongSourceRequestCount,
                        realMediaRequested=false});
                    transitioning=false;
                    Report("已追加选定歌单成员到统一队列。此操作没有访问在线音源。","m213-append");
                    return;
                }
                if(m213AddedEntries.Count!=2||m213UiActions!=2||trialAttempted||
                   core.Kugou.RealSongSourceRequestCount!=0||
                   m213Selected==null||m213Selected.AccountPartition!=proof.AccountPartition||
                   m213Selected.PlaylistId!=proof.PlaylistId||
                   m213Selected.MembershipIndex!=proof.MembershipIndex||
                   m213Selected.TrackHash!=proof.TrackHash||
                   !M216ExactQueue())
                    throw new UnauthorizedAccessException("Play requires exactly two proven appends");
                trialAttempted=true;
                m213MediaStarted=true;
                await M213RunSingleAsync(key,proof);
            }
            catch(Exception e)
            {
                Program.Log("M213_STAGE_BLOCKED",new{stage=play?"play":"append",type=e.GetType().Name});
                if(!m213MediaStarted)
                {
                    await M213CleanupAsync();
                    Program.Log("M213_TRIAL",new{status="NOT_PASSED",
                        failureStage="PRE_MEDIA",sourceRequests=core.Kugou.RealSongSourceRequestCount,
                        queueUnchanged=m213OriginalQueue==null||
                            core.State.QueueEntries.SequenceEqual(m213OriginalQueue)});
                }
            }
            finally {transitioning=false;}
        }
    }

    async Task M213RunSingleAsync(string key,M211PlaybackIntent proof)
    {
        var outcome="NOT_PASSED";var reason="NONE";
        bool started=false,vipAttempted=false;
        double rms=0;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(M219RStartup.Lease?.Token??default);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        trialCancellation=deadline;
        try
        {
            // Explicit UI click and snapshot/account membership are checked
            // again immediately before the first allowed live request.
            if(!M213CanRevalidate(key,proof))
                throw new UnauthorizedAccessException("Account or selected membership changed");
            if(M218BTarget&&(targetProof==null||targetProof.MemberIndex!=proof.MembershipIndex||
                targetProof.Target.ProviderPlaylistId!=proof.PlaylistId||targetProof.Track.Id!=proof.TrackHash||
                !await core.Kugou.ConsumeTargetAsync(targetProof,deadline.Token)))
                throw new UnauthorizedAccessException("Target occurrence is invalid or spent");
            vipAttempted=true;
            var ent=await core.Kugou.RefreshEntitlementAsync(deadline.Token);
            Program.Log("P2BPF_VIP",new{status=ent.Active?"ACTIVE":"INACTIVE"});
            if(!ent.Active||!Connected)
                throw new UnauthorizedAccessException("Current VIP or session no longer permits playback");
            await core.VolumeAsync(0.02);
            if(!M213CanRevalidate(key,proof))
                throw new UnauthorizedAccessException("Selected track invalidated before media");
            trialArmed=true;
            var cutoff=new SimpleAudio.PlaybackCutoff(deadline.Token,persistHistory:!M219RStartup.Active);
            await core.M217PlayBoundedAsync(m213AddedEntries[1],cutoff);
            if(core.State.Error.Length>0||core.State.CurrentEntryId!=m213AddedEntries[1]||
               !core.State.Playing)
                throw new IOException("Selected Core queue entry did not start");
            started=true;
            Program.Log("M217_BOUNDED_STARTED",new{started});
            if(M214SimulateSecondOpen)
            {
                bool rejected=false;
                try
                {
                    // This is the same existing provider ResolveAsync used by the
                    // Core's lazy stream callback. It cannot create real traffic:
                    // this test is admitted ONLY with the synthetic offline SDK.
                    await core.Kugou.ResolveAsync(proof.TrackHash,"standard",deadline.Token);
                }
                catch(UnauthorizedAccessException){rejected=true;}
                if(!rejected||core.Kugou.RealSongSourceRequestCount!=1||
                   !core.State.Playing||core.State.CurrentEntryId!=m213AddedEntries[1])
                    throw new InvalidOperationException("M214 second provider open must be rejected before SDK GET");
                Program.Log("M214_DUPLICATE_OPEN_PROBE",new{
                    rejectedBeforeSdk=true,sourceGets=core.Kugou.RealSongSourceRequestCount,
                    firstPlaybackSurvived=true,syntheticOnly=true});
            }
            for(int i=0;i<4;i++)
            {
                await Task.Delay(500,deadline.Token);
                rms=Math.Max(rms,core.TrialDecodedRms);
            }
            if(core.Kugou.RealSongSourceRequestCount!=1||rms<=0||
               core.State.CurrentEntryId!=m213AddedEntries[1]||!core.State.Playing)
                throw new IOException("Single media quota or audible decoding proof absent");
            if(M213Offline&&Environment.GetEnvironmentVariable("M217_OFFLINE_CASE")=="stall")
            {
                Program.Log("M217_UI_STALL",new{phase="begin"});
                Thread.Sleep(5500); // Offline fault injection crosses the native cutoff.
                Program.Log("M217_UI_STALL",new{phase="end"});
            }
            var ended=await cutoff.Completion.WaitAsync(deadline.Token);
            Program.Log("M217_NATIVE_CUTOFF",new{milliseconds=ended.Milliseconds,stopped=ended.NativeStopped});
            if(!ended.NativeStopped||ended.Milliseconds>6000)
            {
                Program.Result=2;
                throw new IOException("Native playback exceeded cutoff or failed to stop");
            }
            outcome="PASS";
        }
        catch(Exception ex)
        {
            reason=ex.GetType().Name;
            if(M218BTarget&&!deadline.IsCancellationRequested)Program.Result=2;
            if(M213Offline&&Environment.GetEnvironmentVariable("M217_OFFLINE_CASE") is "source-timeout" or "media-fault")
                Program.Result=2;
        }
        finally
        {
            var observedSources=core.Kugou.RealSongSourceRequestCount;
            trialArmed=false;
            trialCancellation=null;
            Program.Log("M213_TRIAL_PHASE",new{phase="BEFORE_CLEANUP",status=outcome,sourceRequests=observedSources});
            bool cleanupFinished=false;
            try
            {
                await M213CleanupAsync();
                cleanupFinished=true;
            }
            catch(Exception ex)
            {
                m214CleanupErrors++;
                Program.Log("M214_CLEANUP_UNEXPECTED",new{type=ex.GetType().Name});
            }
            // Minimal terminal event MUST precede richer readback. A failure
            // to inspect a disposed SDK/queue must not escape the async task
            // and prevent the Windows Closing lifecycle from finishing.
            Program.Log("M214_TRIAL_TERMINAL",new{
                playbackPhaseStatus=outcome,finalVerified=false,
                errorType=reason,started,
                sourceRequests=observedSources,cleanupFinished,
                cleanupErrors=m214CleanupErrors,synthetic=M213Offline});
            Program.Log("M213_TRIAL_PHASE",new{phase="AFTER_CLEANUP"});
            try
            {
                if(M214InjectReadbackFault)
                    throw new InvalidOperationException("Synthetic final readback failure");
                var coreSnapshot=core.State;
                bool queueRestored=m213OriginalQueue!=null&&
                    coreSnapshot.QueueEntries.SequenceEqual(m213OriginalQueue)&&coreSnapshot.CurrentEntryId==m216InitialEntry;
                bool volumeRestored=m213OriginalQueue!=null&&
                    coreSnapshot.Volume==m213InitialVolume;
                bool sessionCleared=!Connected&&!core.Kugou.LoggedIn&&!core.Kugou.Enabled;
                bool stopped=!coreSnapshot.Playing&&!coreSnapshot.Paused;
                int remaining=core.ProviderStreamCount;
                if(!cleanupFinished||m214CleanupErrors!=0||!queueRestored||
                   !volumeRestored||!sessionCleared||!stopped||remaining!=0)
                    outcome="NOT_PASSED";
                LocalTrialPassed=outcome=="PASS";
                if(M219RStartup.Real&&!LocalTrialPassed)Program.Result=2;
                Program.Log("M213_TRIAL",new{
                    status=outcome,errorType=reason,started,
                    decodedRmsPeak=Math.Round(rms,7),vipReads=vipAttempted?1:0,
                    sourceRequests=observedSources,selectedPlaylistOrdinal=2,
                    selectedMemberOrdinal=1,appendActions=m213UiActions,
                    queuedEntriesHadDistinctIds=m213AddedEntries.Distinct().Count()==2,
                    queueUnchanged=queueRestored,
                    volumeRestored,sessionCleared,stopped,providerTracks=remaining,
                    noRemoteWrite=true,noAutoRetry=true,synthetic=M213Offline
                });
            }
            catch(Exception ex)
            {
                outcome="NOT_PASSED";
                Program.Result=2; // An unverified terminal readback must never appear as success.
                Program.Log("M214_TRIAL_READBACK_BLOCKED",new{type=ex.GetType().Name,
                    sourceRequests=observedSources,cleanupFinished});
            }
            m213Finished=true;
            try
            {
                Report(outcome=="PASS"?
                    "选中歌单成员的受控单曲已结束，队列和音源许可已回收":
                    "本次受控点播未通过，已撤销许可并回退队列","m213-trial-complete");
            }
            catch(Exception ex)
            {
                Program.Log("M214_UI_REPORT_BLOCKED",new{type=ex.GetType().Name});
            }
        }
    }

    bool M213CanRevalidate(string key,M211PlaybackIntent original)=>
        M216CurrentSnapshot()&&M216OriginalPlayback()&&M216ExactQueue()&&consent.Reconcile(core.Kugou.LoggedIn,core.Kugou.Account,AccountKey)&&
        (!M213Offline||M210Approved)&&
        M211TryPrepare(key,out var fresh)&&fresh!=null&&
        fresh.AccountPartition==original.AccountPartition&&
        fresh.PlaylistId==original.PlaylistId&&
        fresh.MembershipIndex==original.MembershipIndex&&
        fresh.TrackHash==original.TrackHash&&
        fresh.SnapshotTime==original.SnapshotTime;

    async Task M213CleanupAsync()
    {
        M219RStartup.Lease?.Stop();
        Program.Log("M213_CLEANUP_STAGE",new{phase="STOP_BEGIN"});
        try {await core.StopAsync();} catch(Exception ex) {
            m214CleanupErrors++;Program.Log("M213_CLEANUP_ERROR",new{stage="stop",type=ex.GetType().Name});}
        Program.Log("M213_CLEANUP_STAGE",new{phase="STOP_END",entries=m213AddedEntries.Count});
        int removal=0;
        foreach(var id in m213AddedEntries)
        {
            removal++;
            Program.Log("M213_CLEANUP_STAGE",new{phase="REMOVE_BEGIN",index=removal});
            try{
                await core.QueueEntryAsync("remove",id);
                if(core.State.Error.Length!=0||core.State.QueueEntries.Any(e=>e.EntryId==id))
                    throw new IOException("Queue removal failed");
            }
            catch(Exception ex){m214CleanupErrors++;Program.Log("M213_CLEANUP_ERROR",new{stage="remove",type=ex.GetType().Name});}
            Program.Log("M213_CLEANUP_STAGE",new{phase="REMOVE_END",index=removal});
        }
        Program.Log("M213_CLEANUP_STAGE",new{phase="DISCONNECT_BEGIN"});
        try{await core.DisableKugouAsync();}
        catch(Exception ex){m214CleanupErrors++;Program.Log("M213_CLEANUP_ERROR",new{stage="disconnect",type=ex.GetType().Name});}
        consent.Revoke();
        m210PlaybackConsent.Revoke();
        fixture=null;
        if(M218BTarget){targetProof=null;Snapshot=null;m216SessionSnapshot=null;}
        artwork.Revoke();
        if(m213OriginalQueue!=null)
            try{
                core.M216RestoreStoppedSelection(m216InitialEntry);
                await core.VolumeAsync(m213InitialVolume);
                if(core.State.Error.Length!=0)throw new IOException("Volume restore failed");
            }
            catch(Exception ex){m214CleanupErrors++;Program.Log("M213_CLEANUP_ERROR",new{stage="volume",type=ex.GetType().Name});}
        Program.Log("M213_CLEANUP_STAGE",new{phase="CLEANUP_DONE"});
        m213Finished=true;
    }
}
