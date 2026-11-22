using System.Text.Json;
using HuayuanMusic.Provider.Contracts;
using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services.Startup;

namespace HuayuanPreview;

// Core-owned metadata, not DLC-owned state. A DLC can be disabled/unmounted
// without deleting already imported playlist identities and track labels.
// No credentials, song URLs, DRM content, or audio files belong in this store.
internal sealed record PlatformPlaylistSnapshot(
    int Version,string ProviderId,string AccountKey,
    DateTimeOffset SyncedAt,IReadOnlyList<ProviderPlaylist> Playlists);

internal static class PlatformPlaylistSnapshotStore
{
    const int Version=1;
    const int MaxBytes=24*1024*1024;
    const int MaxPlaylists=200,MaxTracksPerPlaylist=5000,MaxTracksAll=50000;

    static string BaseFolder=>Path.Combine(KugouAvaloniaPlayer.Services.AppDataPaths.LocalRoot,"provider-playlist-snapshots");

    static string DemandAccountKey(string key)
    {
        if(key.Length!=64||key.Any(c=>!Uri.IsHexDigit(c)))
            throw new ArgumentException("不合法的本地账号分区键");
        return key.ToUpperInvariant();
    }

    static void Validate(PlatformPlaylistSnapshot snapshot)
    {
        if(snapshot.Version!=Version||snapshot.ProviderId!=KugouProvider.Identity||
           snapshot.Playlists.Count>MaxPlaylists||snapshot.SyncedAt>DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidDataException("歌单快照版本/平台/数量无效");
        DemandAccountKey(snapshot.AccountKey);
        int total=0;
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var list in snapshot.Playlists)
        {
            ArtworkUrl.Validate(list.CoverUrl);
            if(list.ProviderId!=KugouProvider.Identity||
               string.IsNullOrWhiteSpace(list.ProviderPlaylistId)||
               list.ProviderPlaylistId.Length>128||
               !ids.Add(list.ProviderPlaylistId)||
               list.Name.Length is <1 or >256||
               list.Tracks.Count>MaxTracksPerPlaylist)
                throw new InvalidDataException("歌单快照存在无效身份或过大内容");
            total=checked(total+list.Tracks.Count);
            if(total>MaxTracksAll)throw new InvalidDataException("歌单快照歌曲数量过大");
            foreach(var track in list.Tracks)
            {
                ArtworkUrl.Validate(track.CoverUrl);if(track.AlbumId is <0)throw new InvalidDataException("Invalid album identity");
                if(track.ProviderId!=KugouProvider.Identity||
                   string.IsNullOrWhiteSpace(track.ProviderTrackId)||
                   track.ProviderTrackId.Length>256||
                   track.Title.Length>1024||track.Artist.Length>1024||track.Album.Length>1024)
                    throw new InvalidDataException("歌单快照曲目信息无效");
            }
        }
    }

    internal static void Save(string accountKey,IReadOnlyList<ProviderPlaylist> lists)
    {
        string key=DemandAccountKey(accountKey);
        var payload=new PlatformPlaylistSnapshot(Version,KugouProvider.Identity,key,
            DateTimeOffset.UtcNow,Array.AsReadOnly(lists.ToArray()));
        Validate(payload);
        byte[] data=JsonSerializer.SerializeToUtf8Bytes(payload);
        if(data.Length>MaxBytes)throw new InvalidDataException("离线快照超过24MB限制");
        var folder=BaseFolder;
        OfflineStartupProfile.RejectLinks(folder);
        Directory.CreateDirectory(folder);
        var target=Path.Combine(folder,"kugou-"+key+".json");
        OfflineStartupProfile.RejectLinks(target);
        var next=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        var backup=target+".bak";
        try
        {
            using(var stream=new FileStream(next,FileMode.CreateNew,FileAccess.Write,FileShare.None,
                bufferSize:8192,options:FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(flushToDisk:true);
            }
            if(File.Exists(target))File.Replace(next,target,backup,ignoreMetadataErrors:true);
            else File.Move(next,target);
        }
        finally
        {
            if(File.Exists(next))File.Delete(next);
            Array.Clear(data);
        }
    }

    internal static PlatformPlaylistSnapshot? LoadLatest(string? accountKey=null)
    {
        var folder=BaseFolder;
        if(!Directory.Exists(folder))return null;
        OfflineStartupProfile.RejectLinks(folder);
        var pattern=accountKey==null?"kugou-*.json":"kugou-"+DemandAccountKey(accountKey)+".json";
        var paths=Directory.EnumerateFiles(folder,pattern,SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc).Take(8).ToArray();
        foreach(var path in paths)
        {
            try
            {
                OfflineStartupProfile.RejectLinks(path);
                var info=new FileInfo(path);
                if(info.Length is <=0 or >MaxBytes)continue;
                var name=Path.GetFileNameWithoutExtension(path);
                if(!name.StartsWith("kugou-",StringComparison.OrdinalIgnoreCase))continue;
                var expected=DemandAccountKey(name["kugou-".Length..]);
                var bytes=File.ReadAllBytes(path);
                var restored=JsonSerializer.Deserialize<PlatformPlaylistSnapshot>(bytes);
                if(restored==null||restored.AccountKey!=expected)continue;
                Validate(restored);
                return restored;
            }
            catch(Exception ex) when(ex is IOException or JsonException or InvalidDataException or ArgumentException)
            {
                // Corrupt snapshots never replace the current online metadata.
                // Other profiles may still have a good cache.
            }
        }
        return null;
    }
}
