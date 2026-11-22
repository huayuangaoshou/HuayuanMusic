using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services.Startup;

namespace HuayuanPreview;

internal sealed partial class P2AWorkspace
{
    internal static bool M212SnapshotPreview => F2Case=="snapshot-readonly";

    // An offline historic snapshot is NOT a live account session.
    // M216 admits only fixtures below the current synthetic profile.
    static PlatformPlaylistSnapshot M212LoadSnapshotReadOnly()
    {
        if(!M212SnapshotPreview||RealReadOnly||F2LiveStrict)
            throw new UnauthorizedAccessException("M212 only accepts offline preview scope");
        string allowed=Path.Combine(OfflineStartupProfile.Current.Root,"provider-playlist-snapshots");
        string? userPath=Environment.GetEnvironmentVariable("M212_SNAPSHOT_FILE");
        string? pin=Environment.GetEnvironmentVariable("M212_SNAPSHOT_SHA256");
        if(string.IsNullOrWhiteSpace(userPath)||pin?.Length!=64||!pin.All(Uri.IsHexDigit))
            throw new UnauthorizedAccessException("M212 source and digest not provided");
        string path=Path.GetFullPath(userPath);
        if(!path.StartsWith(allowed+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||
           !string.Equals(Path.GetFileName(Path.GetDirectoryName(path)),
                          "provider-playlist-snapshots",StringComparison.OrdinalIgnoreCase)||
           !Path.GetFileName(path).StartsWith("kugou-",StringComparison.OrdinalIgnoreCase)||
           !path.EndsWith(".json",StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("M216 only reads the current isolated fixture directory");
        OfflineStartupProfile.RejectLinks(path);
        var info=new FileInfo(path);
        if(info.Length is <=0 or >24*1024*1024)
            throw new InvalidDataException("M212 snapshot size boundary");
        byte[] bytes=File.ReadAllBytes(path);
        try
        {
            string actual=Convert.ToHexString(SHA256.HashData(bytes));
            if(!actual.Equals(pin,StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("M212 snapshot digest changed");
            var snap=JsonSerializer.Deserialize<PlatformPlaylistSnapshot>(bytes)
                ??throw new InvalidDataException("M212 invalid snapshot");
            int n=snap.Playlists.Sum(x=>x.Tracks.Count);
            if(snap.Version!=1||snap.ProviderId!=KugouProvider.Identity||
               snap.AccountKey.Length!=64||!snap.AccountKey.All(Uri.IsHexDigit)||
               !Path.GetFileNameWithoutExtension(path).Equals("kugou-"+snap.AccountKey,StringComparison.OrdinalIgnoreCase)||
               snap.Playlists.Count!=7||n!=2875||
               snap.SyncedAt>DateTimeOffset.UtcNow.AddMinutes(5)||
               snap.Playlists.GroupBy(x=>x.ProviderPlaylistId,StringComparer.Ordinal).Any(g=>g.Count()!=1)||
               snap.Playlists.Any(p=>p.ProviderId!=KugouProvider.Identity||
                   string.IsNullOrWhiteSpace(p.ProviderPlaylistId)||
                   p.Tracks.Any(t=>t.ProviderId!=KugouProvider.Identity||
                       string.IsNullOrWhiteSpace(t.ProviderTrackId))))
                throw new InvalidDataException("M212 historic snapshot identity rejected");
            int eligible=snap.Playlists.Sum(p=>p.Tracks.Count(t=>
                t.ProviderTrackId.Length==32&&t.ProviderTrackId.All(Uri.IsHexDigit)));
            Program.Log("M212_HISTORIC_SNAPSHOT",new{
                lists=snap.Playlists.Count,members=n,eligibleIdFormat=eligible,
                otherIdFormat=n-eligible,readOnly=true,connected=false,
                vipGets=0,mediaGets=0,realHttpAllowed=false});
            return snap;
        }
        finally { Array.Clear(bytes); }
    }

    // This is a metadata-only inspection. It MUST NOT enqueue, open a media
    // source, access account credentials, or issue any SDK call.
    internal void M212InspectMember(long playlistOrdinal,long memberOrdinal,string rowKey)
    {
        if(!M212SnapshotPreview||Connected||Snapshot==null||
           playlistOrdinal<1||playlistOrdinal>Snapshot.Playlists.Count||
           memberOrdinal<1||string.IsNullOrWhiteSpace(rowKey))
        {
            Program.Log("M212_SELECTION_REJECTED",new{reason="SCOPE"});
            return;
        }
        var list=Snapshot.Playlists[(int)playlistOrdinal-1];
        int index=checked((int)memberOrdinal-1);
        if(index>=list.Tracks.Count)
        {
            Program.Log("M212_SELECTION_REJECTED",new{reason="MEMBER_RANGE"});
            return;
        }
        string actualHash=list.Tracks[index].ProviderTrackId;
        string expectedKey="kugou-concept:"+Snapshot.AccountKey+":"+
            list.ProviderPlaylistId+":"+index+":"+actualHash;
        if(!rowKey.Equals(expectedKey,StringComparison.Ordinal))
        {
            Program.Log("M212_SELECTION_REJECTED",new{reason="STALE_ROW_KEY"});
            return;
        }
        bool standard=actualHash.Length==32&&actualHash.All(Uri.IsHexDigit);
        bool metadataProven=standard&&M211PlaybackIntentAuthority.TryPrepare(
            Snapshot,Snapshot.AccountKey,list.ProviderPlaylistId,index,actualHash,out _);
        string state=metadataProven?"FORMAT_COMPATIBLE_METADATA_ONLY":
            standard?"IDENTITY_NOT_VERIFIED":"UNSUPPORTED_IDENTIFIER_FORMAT";
        Program.Log("M212_SELECTION",new{
            playlistOrdinal,memberOrdinal,
            state,metadataOnly=true,realAccountConnected=false,
            requestedMedia=false,approvedHttp=false,queueChanged=false});
        Report(state switch {
            "FORMAT_COMPATIBLE_METADATA_ONLY"=>"已核验历史歌单成员身份。未登录、未入队、未播放。",
            "UNSUPPORTED_IDENTIFIER_FORMAT"=>"这条歌曲使用另一种平台标识格式，保留原记录，暂不准入播放。",
            _=>"歌单成员身份未通过核验，不执行播放。"
        },"m212-inspect");
    }
}
