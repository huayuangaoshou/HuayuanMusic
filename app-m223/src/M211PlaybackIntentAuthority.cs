using System;
using System.Linq;
using HuayuanMusic.Provider.Contracts;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview;

// Pure immutable metadata selection. A proof is NOT a media grant, an
// entitlement, an account session, an HTTP approval or a playable URI.
internal sealed record M211PlaybackIntent(
    string AccountPartition, string PlaylistId, int MembershipIndex,
    string TrackHash, DateTimeOffset SnapshotTime);

internal static class M211PlaybackIntentAuthority
{
    static bool IsHash(string? value,int size)=>
        value?.Length==size&&value.All(Uri.IsHexDigit);

    // Member index is REQUIRED even when hashes repeat in one playlist.
    // Explicitly fail closed if the saved account/snapshot has changed.
    internal static bool TryPrepare(PlatformPlaylistSnapshot? snapshot,
        string? currentAccountPartition,string? playlistId,int memberIndex,
        string? expectedTrackHash,out M211PlaybackIntent? intent)
    {
        intent=null;
        if(snapshot is null||snapshot.Version!=1||
            snapshot.ProviderId!=KugouProvider.Identity||
            !IsHash(snapshot.AccountKey,64)||
            !IsHash(currentAccountPartition,64)||
            !snapshot.AccountKey.Equals(currentAccountPartition,StringComparison.OrdinalIgnoreCase)||
            string.IsNullOrWhiteSpace(playlistId)||memberIndex<0||
            !IsHash(expectedTrackHash,32)||
            snapshot.SyncedAt>DateTimeOffset.UtcNow.AddMinutes(5))
            return false;
        var playlists=snapshot.Playlists.Where(p=>
            p.ProviderId==KugouProvider.Identity&&
            string.Equals(p.ProviderPlaylistId,playlistId,StringComparison.Ordinal)).Take(2).ToArray();
        if(playlists.Length!=1)return false;
        var playlist=playlists[0];
        if(memberIndex>=playlist.Tracks.Count)return false;
        var track=playlist.Tracks[memberIndex];
        if(track.ProviderId!=KugouProvider.Identity||
           !IsHash(track.ProviderTrackId,32)||
           !track.ProviderTrackId.Equals(expectedTrackHash,StringComparison.OrdinalIgnoreCase))
            return false;
        intent=new M211PlaybackIntent(snapshot.AccountKey,playlist.ProviderPlaylistId,
            memberIndex,track.ProviderTrackId,snapshot.SyncedAt);
        return true;
    }

    // All cases exercise only in-memory records, never a disk account,
    // actual SDK or network handler.
    internal static (int Passed,int Cases) OfflineSelfTest()
    {
        int passed=0,cases=0;
        void Check(bool ok,string label){cases++;if(!ok)throw new InvalidOperationException("M211_INTENT_"+label);passed++;}
        var provider=KugouProvider.Identity;
        var hash=new string('a',32);
        var other=new string('b',32);
        var partition=new string('C',64);
        var duplicates=new[] {
            new ProviderTrackRef(provider,hash,"synthetic A","artist","album"),
            new ProviderTrackRef(provider,hash,"synthetic A","artist","album"),
            new ProviderTrackRef(provider,other,"synthetic B","artist","album")
        };
        var lists=new[]{
            new ProviderPlaylist(provider,"synthetic-playlist-1","synthetic",duplicates),
            new ProviderPlaylist(provider,"synthetic-playlist-2","synthetic",new[]{duplicates[0]})
        };
        var snap=new PlatformPlaylistSnapshot(1,provider,partition,DateTimeOffset.UtcNow,lists);
        bool Resolve(string? acc,string? list,int index,string? track,
            PlatformPlaylistSnapshot? data,out M211PlaybackIntent? proof)=>
            TryPrepare(data,acc,list,index,track,out proof);
        Check(Resolve(partition,"synthetic-playlist-1",0,hash,snap,out var first)&&first!=null,
            "FIRST_OCCURRENCE");
        Check(Resolve(partition,"synthetic-playlist-1",1,hash,snap,out var second)&&
            second!=null&&second.MembershipIndex!=first!.MembershipIndex,
            "DUPLICATES_DIFFER_BY_MEMBER_INDEX");
        Check(Resolve(partition,"synthetic-playlist-2",0,hash,snap,out var third)&&
            third!=null&&third.PlaylistId!=first!.PlaylistId,
            "SAME_HASH_OTHER_PLAYLIST");
        Check(Resolve(partition.ToLowerInvariant(),"synthetic-playlist-1",2,other,snap,out _),
            "ACCOUNT_HEX_CASE");
        Check(!Resolve(new string('D',64),"synthetic-playlist-1",0,hash,snap,out _),
            "ACCOUNT_SWAP_DENIED");
        Check(!Resolve(null,"synthetic-playlist-1",0,hash,snap,out _),"MISSING_ACCOUNT_DENIED");
        Check(!Resolve(partition,"unknown-playlist",0,hash,snap,out _),"FOREIGN_PLAYLIST_DENIED");
        Check(!Resolve(partition,"synthetic-playlist-1",3,hash,snap,out _),"OUT_OF_RANGE_DENIED");
        Check(!Resolve(partition,"synthetic-playlist-1",-1,hash,snap,out _),"NEGATIVE_MEMBER_DENIED");
        Check(!Resolve(partition,"synthetic-playlist-1",2,hash,snap,out _),"TRACK_MISMATCH_DENIED");
        Check(!Resolve(partition,"synthetic-playlist-1",0,"wrong",snap,out _),"BAD_HASH_DENIED");
        Check(!Resolve(partition,"",0,hash,snap,out _),"EMPTY_PLAYLIST_DENIED");
        Check(!Resolve(partition,"synthetic-playlist-1",0,hash,null,out _),"NO_SNAPSHOT_DENIED");
        var badSnapshot=snap with {ProviderId="foreign.provider"};
        Check(!Resolve(partition,"synthetic-playlist-1",0,hash,badSnapshot,out _),"FOREIGN_PROVIDER_DENIED");
        var badTrack=new ProviderTrackRef("foreign.provider",hash,"synthetic","artist","album");
        var badLists=new[]{lists[0] with{Tracks=new[]{badTrack}}};
        Check(!Resolve(partition,"synthetic-playlist-1",0,hash,snap with{Playlists=badLists},out _),
            "FOREIGN_TRACK_DENIED");
        var duplicatePlaylist=snap with{Playlists=new[]{lists[0],lists[0]}};
        Check(!Resolve(partition,"synthetic-playlist-1",0,hash,duplicatePlaylist,out _),
            "DUPLICATE_PLAYLIST_ID_DENIED");
        Check(!Resolve(partition,"synthetic-playlist-1",0,hash,
                snap with{SyncedAt=DateTimeOffset.UtcNow.AddDays(1)},out _),
            "FUTURE_TIMESTAMP_DENIED");
        if(cases!=17||passed!=17)throw new InvalidOperationException("M211_CASE_CARDINALITY");
        return (passed,cases);
    }
}
