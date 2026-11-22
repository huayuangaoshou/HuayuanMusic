using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

/// <summary>Independent local metadata. Never migrates, reads or deletes platform caches.</summary>
public static class LocalMusicRecordStore
{
    public const string Scope = "huayuan_local_music_records_v1";
    private static readonly object Gate = new();
    public static event Action? FavoritesChanged;
    public static bool IsLocal(SongItem song) =>
        !string.Equals(song.LocalSourceType, LocalMusicLibraryService.SourceTypeJellyfin, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(song.LocalFilePath) && Path.IsPathFullyQualified(song.LocalFilePath);
    public static string Key(SongItem song) => IsLocal(song)
        ? Path.GetFullPath(song.LocalFilePath!).ToUpperInvariant()
        : throw new ArgumentException("仅支持本地文件记录。", nameof(song));
    public static IReadOnlyList<SongItem> LoadFavorites() { lock(Gate) return Read("favorites").Items.Select(PlaybackHistoryService.ToSongItem).ToArray(); }
    public static IReadOnlyList<SongItem> LoadHistory() { lock(Gate) return Read("history").Items.Select(PlaybackHistoryService.ToSongItem).ToArray(); }
    public static bool IsFavorite(SongItem song)
    {
        if(!IsLocal(song))return false;
        lock(Gate)return Read("favorites").Items.Any(i=>ItemKey(i)==Key(song));
    }
    public static bool ToggleFavorite(SongItem song)
    {
        bool result;
        lock(Gate){var data=Read("favorites");result=!data.Items.Any(i=>ItemKey(i)==Key(song));SetFavoriteCore(data,song,result);}
        FavoritesChanged?.Invoke();return result;
    }
    public static void SetFavorite(SongItem song,bool value)
    {
        lock(Gate)SetFavoriteCore(Read("favorites"),song,value);
        FavoritesChanged?.Invoke();
    }
    private static void SetFavoriteCore(PlaybackHistoryFileModel data,SongItem song,bool value)
    {
        var key=Key(song);data.Items.RemoveAll(i=>ItemKey(i)==key);
        if(value)data.Items.Insert(0,Snapshot(song));
        if(data.Items.Count>10000)throw new InvalidOperationException("本地收藏数量超过 10000，原数据未覆盖。");
        Save("favorites",data);
    }
    public static void RecordPlayed(SongItem song)
    {
        lock(Gate){var data=Read("history");var key=Key(song);data.Items.RemoveAll(i=>ItemKey(i)==key);data.Items.Insert(0,Snapshot(song));data.Items=data.Items.Take(100).ToList();Save("history",data);}
    }
    public static void ClearHistory(){lock(Gate)Save("history",new PlaybackHistoryFileModel{UserId="local-only"});}
    public static void ClearFavorites(){lock(Gate)Save("favorites",new PlaybackHistoryFileModel{UserId="local-only"});FavoritesChanged?.Invoke();}
    private static string ItemKey(PlaybackHistoryItem item)=>Key(PlaybackHistoryService.ToSongItem(item));
    private static PlaybackHistoryItem Snapshot(SongItem song)
    {
        var item=PlaybackHistoryService.ToHistoryItem(song);
        item.LocalFilePath=Path.GetFullPath(song.LocalFilePath!);item.LocalSourceType="LocalFile";
        item.Hash="";item.FileId=0;item.AlbumId="";item.AlbumAudioId=0;item.AudioId=0;item.RemoteUrl=null;item.PlaybackSource=SongPlaybackSource.Default;
        if(item.Cover?.StartsWith("http",StringComparison.OrdinalIgnoreCase)==true)item.Cover="avares://HuayuanMusic.P1/Assets/default_song.webp";
        item.PlayedAt=DateTimeOffset.UtcNow.ToString("O");return item;
    }
    private static PlaybackHistoryFileModel Read(string key)
    {
        var json=AppSqliteStore.LoadValue(Scope,key);
        if(string.IsNullOrWhiteSpace(json))return new PlaybackHistoryFileModel{UserId="local-only"};
        var data=JsonSerializer.Deserialize(json,PlaybackHistoryJsonContext.Default.PlaybackHistoryFileModel)
            ??throw new InvalidDataException("本地记录无法读取，原数据未覆盖。");
        if(data.UserId!="local-only"||data.Items==null||data.Items.Any(i=>!IsLocal(PlaybackHistoryService.ToSongItem(i))))
            throw new InvalidDataException("本地记录分区或文件标识无效，原数据未覆盖。");
        return data;
    }
    private static void Save(string key,PlaybackHistoryFileModel data)
    {
        data.UserId="local-only";data.UpdatedAt=DateTimeOffset.UtcNow.ToString("O");
        AppSqliteStore.SaveValue(Scope,key,JsonSerializer.Serialize(data,PlaybackHistoryJsonContext.Default.PlaybackHistoryFileModel));
    }
}
