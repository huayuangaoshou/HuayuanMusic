using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using ZLinq;

namespace KugouAvaloniaPlayer.Services;

public sealed class PlaybackQueueCacheService(ILogger<PlaybackQueueCacheService> logger)
{
    private const int CacheSchemaVersion = 3;
    private const string StoreScope = "playback_queue";
    private const string StoreKey = "app";
    private const string DefaultCover = "avares://HuayuanMusic.P1/Assets/default_song.webp";

    public string? LastReadError {get;private set;}
    public string? LastWriteError {get;private set;}
    public PlaybackQueueCacheSnapshot? Load()
    {
        try
        {
            LastReadError=null;var json = AppSqliteStore.LoadValue(StoreScope, StoreKey);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            if(json.Length>8*1024*1024)throw new InvalidDataException("队列缓存过大");
            var model = JsonSerializer.Deserialize(json, PlaybackQueueCacheJsonContext.Default.PlaybackQueueCacheFileModel);
            if (model?.Items == null || model.Items.Count == 0)
                return null;

            if(Startup.OfflineStartupProfile.Active){
                if(model.SchemaVersion is not (1 or 2 or 3)||model.Items.Count>1000||!Enum.IsDefined(model.Mode))throw new InvalidDataException("未知版本或损坏队列缓存，未恢复");
                if(model.OriginalItems.Count>1000)throw new InvalidDataException("原顺序缓存过大");var catalog=ReadCatalog();
                if(model.SchemaVersion==1){
                    // CORE-02: legacy Schema 1 has no occurrence identity or original order.
                    // Accept only a verified, unambiguous local-track sequence; read is non-mutating.
                    if(model.OriginalItems.Count!=0||model.Mode!=PlayMode.Normal||
                       model.Items.Any(x=>x.LocalTrackId<=0)||
                       model.Items.Select(x=>x.LocalTrackId).Distinct().Count()!=model.Items.Count)
                        throw new InvalidDataException("Schema1 队列含歧义或非本地条目，原缓存未覆盖");
                    var canonical=model.Items.Select(x=>CanonicalLocal(x,catalog)).ToList();
                    var currentIndex=string.IsNullOrEmpty(model.CurrentSongKey)?0:
                        canonical.FindIndex(x=>BuildSongKey(x)==model.CurrentSongKey);
                    if(currentIndex<0)throw new InvalidDataException("Schema1 当前曲目未通过身份校验，原缓存未覆盖");
                    foreach(var item in model.Items)item.QueueEntryId=Guid.NewGuid();
                    model.OriginalItems=model.Items.ToList();
                    model.CurrentSongKey=model.Items[currentIndex].QueueEntryId.ToString("D");
                }
                if(model.SchemaVersion==2){
                    // Legacy accepted only for unique tracks, mapped once into shared occurrence IDs.
                    if(model.Items.GroupBy(x=>x.LocalTrackId).Any(x=>x.Count()!=1))throw new InvalidDataException("旧队列含歧义重复歌曲");
                    var ids=model.Items.ToDictionary(x=>x.LocalTrackId,_=>Guid.NewGuid());
                    foreach(var x in model.Items.Concat(model.OriginalItems)){if(!ids.TryGetValue(x.LocalTrackId,out var id))throw new InvalidDataException("旧队列原顺序不匹配");x.QueueEntryId=id;}
                    var current=model.Items.FirstOrDefault(x=>BuildSongKey(CanonicalLocal(x,catalog))==model.CurrentSongKey);model.CurrentSongKey=current is null?"":current.QueueEntryId.ToString("D");
                }
                var visible=model.Items.Select(x=>CanonicalLocal(x,catalog)).ToList();var original=model.OriginalItems.Select(x=>CanonicalLocal(x,catalog)).ToList();
                if(visible.Any(x=>x.QueueEntryId==Guid.Empty)||visible.Select(x=>x.QueueEntryId).Distinct().Count()!=visible.Count||original.Select(x=>x.QueueEntryId).Distinct().Count()!=original.Count)throw new InvalidDataException("条目ID缺失或重复");PlaybackQueueManager.DemandCapacity(visible);PlaybackQueueManager.DemandCapacity(original);if(!visible.ToHashSet().SetEquals(original)||visible.Any(v=>BuildSongKey(v)!=BuildSongKey(original.Single(o=>o.QueueEntryId==v.QueueEntryId))))throw new InvalidDataException("缓存条目身份与曲目映射不一致");
                if(!string.IsNullOrEmpty(model.CurrentSongKey)&&!visible.Any(x=>BuildEntryKey(x)==model.CurrentSongKey))throw new InvalidDataException("缓存当前曲目不在队列");
                return new PlaybackQueueCacheSnapshot{CurrentSongKey=model.CurrentSongKey??"",Songs=visible,OriginalSongs=original,Mode=model.Mode};
            }
            model.SchemaVersion = model.SchemaVersion <= 0 ? CacheSchemaVersion : model.SchemaVersion;
            model.UpdatedAt = string.IsNullOrWhiteSpace(model.UpdatedAt)
                ? DateTimeOffset.Now.ToString("O")
                : model.UpdatedAt;
            model.Items = model.Items
                .AsValueEnumerable()
                .Where(IsValidCacheItem)
                .ToList();

            if (model.Items.Count == 0)
                return null;

            return new PlaybackQueueCacheSnapshot
            {
                CurrentSongKey = model.CurrentSongKey ?? "",
                Songs = model.Items.AsValueEnumerable().Select(ToSongItem).ToList(),OriginalSongs=(model.OriginalItems.Count>0?model.OriginalItems:model.Items).Select(ToSongItem).ToList(),Mode=model.Mode
            };
        }
        catch (Exception ex)
        {
            LastReadError=ex.Message;logger.LogWarning(ex, "读取播放队列缓存失败。");
            return null;
        }
    }

    public void Save(IReadOnlyList<SongItem> songs, SongItem? currentSong,IReadOnlyList<SongItem>? original=null,PlayMode mode=PlayMode.Normal,bool localSubset=false)
    {
        try
        {
            LastWriteError=null;PlaybackQueueManager.DemandCapacity(songs.ToArray());
            if(localSubset)
            {
                songs=songs.Where(IsDurableLocal).ToArray();
                original=original?.Where(IsDurableLocal).ToArray();
                if(currentSong!=null&&!IsDurableLocal(currentSong))currentSong=null;
            }
            else if(songs.Any(x=>ProviderStreamRegistry.IsProvider(x.LocalFilePath)))return;
            static bool IsDurableLocal(SongItem item)=>item.LocalSourceType=="LocalFile"&&item.LocalTrackId>0;
            if (songs.Count == 0)
            {
                AppSqliteStore.DeleteValue(StoreScope, StoreKey);
                return;
            }

            if(songs.Any(x=>x.QueueEntryId==Guid.Empty)||songs.Select(x=>x.QueueEntryId).Distinct().Count()!=songs.Count)
                throw new InvalidDataException("队列条目ID缺失或重复，未覆盖旧缓存");

            var model = new PlaybackQueueCacheFileModel
            {
                SchemaVersion = CacheSchemaVersion,Mode=mode,OriginalItems=(original??songs).Select(ToCacheItem).ToList(),
                UpdatedAt = DateTimeOffset.Now.ToString("O"),
                CurrentSongKey = BuildEntryKey(currentSong),
                Items = songs.AsValueEnumerable().Select(ToCacheItem).ToList()
            };

            var json = JsonSerializer.Serialize(model, PlaybackQueueCacheJsonContext.Default.PlaybackQueueCacheFileModel);
            if(json.Length>8*1024*1024)throw new InvalidDataException("缓存超过8MiB，未覆盖原缓存");
            AppSqliteStore.SaveValue(StoreScope, StoreKey, json);
        }
        catch (Exception ex)
        {
            LastWriteError=ex.Message;logger.LogWarning(ex, "写入播放队列缓存失败。");
        }
    }

    public static string BuildEntryKey(SongItem? s)=>s?.QueueEntryId.ToString("D")??"";
    public static string BuildSongKey(SongItem? song)
    {
        if (song == null)
            return "";

        if (!string.IsNullOrWhiteSpace(song.Hash))
            return $"hash:{song.Hash.Trim().ToLowerInvariant()}";

        if (!string.IsNullOrWhiteSpace(song.LocalFilePath))
            return $"local:{Path.GetFullPath(song.LocalFilePath).ToLowerInvariant()}";

        if (song.FileId > 0)
            return $"file:{song.FileId}";

        return $"song:{song.Name.Trim().ToLowerInvariant()}:{song.Singer.Trim().ToLowerInvariant()}";
    }

    private static bool IsValidCacheItem(PlaybackQueueCacheItem item)
    {
        return !string.IsNullOrWhiteSpace(item.Hash) ||
               !string.IsNullOrWhiteSpace(item.LocalFilePath) ||
               item.FileId > 0 ||
               !string.IsNullOrWhiteSpace(item.Name);
    }

    private static PlaybackQueueCacheItem ToCacheItem(SongItem song)
    {
        return new PlaybackQueueCacheItem
        {
            QueueEntryId=song.QueueEntryId, Hash = song.Hash,
            FileId = song.FileId,
            Name = song.Name,
            Singer = song.Singer,
            Singers = song.Singers?.AsValueEnumerable().ToList() ?? new List<SingerLite>(),
            AlbumId = song.AlbumId,
            AlbumAudioId = song.AlbumAudioId,
            AlbumName = song.AlbumName,
            AudioId = song.AudioId,
            Cover = song.Cover,
            DurationSeconds = song.DurationSeconds,
            LocalFilePath = song.LocalFilePath,
            LocalSourceType = song.LocalSourceType,
            LocalTrackId = song.LocalTrackId,
            RemoteUrl = song.RemoteUrl,
            PlaybackSource = song.PlaybackSource
        };
    }

    private static Dictionary<long,LocalTrackItem> ReadCatalog(){using var c=AppDatabase.CreateConnection();using var q=c.CreateCommand();q.CommandText="SELECT id,title,artist,album,duration_seconds,source_type,local_path,cover_path,remote_url FROM tracks";using var r=q.ExecuteReader();var rows=new Dictionary<long,LocalTrackItem>();while(r.Read())rows[r.GetInt64(0)]=new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDouble(4),r.GetString(5),r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.IsDBNull(8)?null:r.GetString(8));return rows;}
    private static SongItem CanonicalLocal(PlaybackQueueCacheItem cached,Dictionary<long,LocalTrackItem> rows){
        if(!string.IsNullOrEmpty(cached.RemoteUrl)||cached.LocalSourceType!="LocalFile"||cached.LocalTrackId<=0||cached.LocalFilePath==null||!rows.TryGetValue(cached.LocalTrackId,out var track)||track.SourceType!="LocalFile"||!string.IsNullOrEmpty(track.RemoteUrl)||!string.Equals(track.LocalPath,cached.LocalFilePath,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("缓存身份、来源或路径不符");
        if (Startup.OfflineStartupProfile.Active) Startup.OfflineStartupProfile.Current.DemandMedia(track.LocalPath);var song=LocalMusicLibraryViewModel.ToSongItem(track);song.QueueEntryId=cached.QueueEntryId;return song;
    }

    private static SongItem ToSongItem(PlaybackQueueCacheItem item)
    {
        return new SongItem
        {
            Name = string.IsNullOrWhiteSpace(item.Name) ? "未知" : item.Name,
            Singer = string.IsNullOrWhiteSpace(item.Singer) ? "未知" : item.Singer,
            QueueEntryId=item.QueueEntryId, Hash = item.Hash,
            AlbumId = item.AlbumId,
            AlbumAudioId = item.AlbumAudioId,
            AlbumName = item.AlbumName,
            AudioId = item.AudioId,
            FileId = item.FileId,
            Singers = item.Singers ?? new List<SingerLite>(),
            Cover = string.IsNullOrWhiteSpace(item.Cover) ? DefaultCover : item.Cover,
            DurationSeconds = item.DurationSeconds,
            LocalFilePath = item.LocalFilePath,
            LocalSourceType = item.LocalSourceType,
            LocalTrackId = item.LocalTrackId,
            RemoteUrl = item.RemoteUrl,
            PlaybackSource = item.PlaybackSource
        };
    }
}

public sealed class PlaybackQueueCacheSnapshot
{
    public string CurrentSongKey { get; set; } = "";
    public List<SongItem> Songs { get; set; } = new();
    public List<SongItem> OriginalSongs {get;set;}=new();
    public PlayMode Mode {get;set;}=PlayMode.Normal;
}

public sealed class PlaybackQueueCacheFileModel
{
    public int SchemaVersion { get; set; } = 2;
    public PlayMode Mode {get;set;}=PlayMode.Normal;
    public List<PlaybackQueueCacheItem> OriginalItems {get;set;}=new();
    public string UpdatedAt { get; set; } = "";
    public string? CurrentSongKey { get; set; }
    public List<PlaybackQueueCacheItem> Items { get; set; } = new();
}

public sealed class PlaybackQueueCacheItem
{
    public Guid QueueEntryId { get; set; }
    public string Hash { get; set; } = "";
    public long FileId { get; set; }
    public string Name { get; set; } = "";
    public string Singer { get; set; } = "";
    public List<SingerLite> Singers { get; set; } = new();
    public string AlbumId { get; set; } = "";
    public long AlbumAudioId { get; set; }
    public string AlbumName { get; set; } = "";
    public long AudioId { get; set; }
    public string? Cover { get; set; }
    public double DurationSeconds { get; set; }
    public string? LocalFilePath { get; set; }
    public string? LocalSourceType { get; set; }
    public long LocalTrackId { get; set; }
    public string? RemoteUrl { get; set; }
    public SongPlaybackSource PlaybackSource { get; set; } = SongPlaybackSource.Default;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(PlaybackQueueCacheFileModel))]
internal partial class PlaybackQueueCacheJsonContext : JsonSerializerContext
{
}
