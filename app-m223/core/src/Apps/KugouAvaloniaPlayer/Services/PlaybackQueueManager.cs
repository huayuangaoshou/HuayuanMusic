using System.Linq;
using System;
using System.Collections.Generic;
using ZLinq;
using Avalonia.Collections;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

public class PlaybackQueueManager
{
    public static int MaxQueueSize => Startup.OfflineStartupProfile.Active ? 1000 : 5000;
    private readonly Random _random = new();
    private readonly Stack<SongItem> _shuffleHistory=new();
    public bool IsSequentialMode {get;private set;}
    public static void DemandCapacity(IReadOnlyCollection<SongItem> songs){
        if(songs.Count>MaxQueueSize||songs.Any(x=>x is null))throw new InvalidOperationException($"队列最多{MaxQueueSize}条，未截断或改变队列");
    }
    static List<SongItem> Occurrences(IEnumerable<SongItem> source){
        var ids=new HashSet<Guid>();var result=new List<SongItem>();
        foreach(var input in source){var song=input;if(song.QueueEntryId==Guid.Empty)song.QueueEntryId=Guid.NewGuid();
            if(!ids.Add(song.QueueEntryId)){song=input.NewQueueEntry();ids.Add(song.QueueEntryId);}result.Add(song);}
        return result;
    }
    public void RecordNavigation(SongItem? previous,SongItem target){if(IsShuffleMode&&previous!=null&&!Equals(previous,target)){_shuffleHistory.Push(previous);if(_shuffleHistory.Count>1000){var recent=_shuffleHistory.Take(1000).Reverse().ToArray();_shuffleHistory.Clear();foreach(var x in recent)_shuffleHistory.Push(x);}}}
    public void SetMode(Models.PlayMode mode,SongItem? current){if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));IsSequentialMode=mode==Models.PlayMode.Sequential;IsRepeatOneMode=mode==Models.PlayMode.RepeatOne;SetShuffleMode(mode==Models.PlayMode.Shuffle,current);_shuffleHistory.Clear();}
    public void Move(int from,int to){if(from<0||from>=PlaybackQueue.Count||to<0||to>=PlaybackQueue.Count)throw new ArgumentOutOfRangeException();if(from==to)return;var song=PlaybackQueue[from];PlaybackQueue.RemoveAt(from);PlaybackQueue.Insert(to,song);OriginalQueue.Remove(song);var anchor=to+1<PlaybackQueue.Count?PlaybackQueue[to+1]:null;int at=anchor==null?-1:OriginalQueue.IndexOf(anchor);if(at<0)OriginalQueue.Add(song);else OriginalQueue.Insert(at,song);}
    public void RestoreExact(IReadOnlyList<SongItem> visible,IReadOnlyList<SongItem> original,Models.PlayMode mode){DemandCapacity(visible.ToArray());DemandCapacity(original.ToArray());if(visible.Any(x=>x.QueueEntryId==Guid.Empty)||visible.Select(x=>x.QueueEntryId).Distinct().Count()!=visible.Count||!visible.ToHashSet().SetEquals(original)||visible.Any(v=>PlaybackQueueCacheService.BuildSongKey(v)!=PlaybackQueueCacheService.BuildSongKey(original.Single(o=>o.QueueEntryId==v.QueueEntryId))))throw new InvalidOperationException("缓存原始顺序集合不一致");Clear();IsShuffleMode=mode==Models.PlayMode.Shuffle;IsRepeatOneMode=mode==Models.PlayMode.RepeatOne;IsSequentialMode=mode==Models.PlayMode.Sequential;OriginalQueue.AddRange(original);PlaybackQueue.AddRange(visible);}


    public AvaloniaList<SongItem> PlaybackQueue { get; } = new();
    public List<SongItem> OriginalQueue { get; } = new();

    public bool IsRepeatOneMode { get; private set; }
    public bool IsShuffleMode { get; private set; }

    /// <summary>
    ///     初始化或替换播放队列
    /// </summary>
    public void SetupQueue(SongItem song, IList<SongItem>? contextList)
    {
        if (contextList is { Count: > 0 })
        {
            DemandCapacity(contextList.ToArray());if(!contextList.Contains(song))throw new InvalidOperationException("播放曲目不在队列上下文");
            _shuffleHistory.Clear();OriginalQueue.Clear();
            PlaybackQueue.Clear();

            var finalList = Occurrences(contextList);

            OriginalQueue.AddRange(finalList);

            if (IsShuffleMode)
            {
                var shuffleList = new List<SongItem>(finalList);
                shuffleList.Remove(song);

                ShuffleInPlace(shuffleList);

                PlaybackQueue.Add(song);
                PlaybackQueue.AddRange(shuffleList);
            }
            else
            {
                PlaybackQueue.AddRange(finalList);
            }
        }
        else if (PlaybackQueue.Count == 0)
        {
            if(song.QueueEntryId==Guid.Empty)song.QueueEntryId=Guid.NewGuid();OriginalQueue.Add(song);
            PlaybackQueue.Add(song);
        }
    }

    public SongItem? GetNext(SongItem? currentPlaying)
    {
        if (PlaybackQueue.Count == 0) return null;
        var idx = currentPlaying != null ? PlaybackQueue.IndexOf(currentPlaying) : -1;
        if(IsSequentialMode&&idx==PlaybackQueue.Count-1)return null;
        var nextIdx = (idx + 1) % PlaybackQueue.Count;
        return PlaybackQueue[nextIdx];
    }

    public SongItem? GetPrevious(SongItem? currentPlaying)
    {
        if (PlaybackQueue.Count == 0) return null;
        var idx = currentPlaying != null ? PlaybackQueue.IndexOf(currentPlaying) : -1;
        if(IsShuffleMode){while(_shuffleHistory.Count>0){var previous=_shuffleHistory.Pop();if(PlaybackQueue.Contains(previous))return previous;}return currentPlaying??PlaybackQueue[0];}
        if(IsSequentialMode&&idx<=0)return currentPlaying??PlaybackQueue[0];
        var prevIdx = idx - 1;
        if (prevIdx < 0) prevIdx = PlaybackQueue.Count - 1;
        return PlaybackQueue[prevIdx];
    }

    public void RestoreQueue(IReadOnlyList<SongItem> songs)
    {
        DemandCapacity(songs.ToArray());
        _shuffleHistory.Clear();OriginalQueue.Clear();
        PlaybackQueue.Clear();

        if (songs.Count == 0)
            return;

        var entries=Occurrences(songs);OriginalQueue.AddRange(entries);
        PlaybackQueue.AddRange(entries);
    }

    public void AddToNext(SongItem song, SongItem? currentPlaying)
    {
        if(song.QueueEntryId==Guid.Empty){
            var matches=PlaybackQueue.Where(x=>PlaybackQueueCacheService.BuildSongKey(x)==PlaybackQueueCacheService.BuildSongKey(song)).ToArray();
            if(matches.Length>1)throw new InvalidOperationException("歌曲有多个队列条目，请选择具体条目");
            if(matches.Length==1)song=matches[0];else song.QueueEntryId=Guid.NewGuid();
        }
        if(Equals(song,currentPlaying))return;
        if(PlaybackQueue.Count>0&&(currentPlaying==null||!PlaybackQueue.Contains(currentPlaying)))throw new InvalidOperationException("当前曲目不在队列；未插入。请选定队列曲目或明确追加末尾");
        if(!PlaybackQueue.Contains(song)&&PlaybackQueue.Count>=MaxQueueSize)throw new InvalidOperationException("播放队列已满");
        PlaybackQueue.Remove(song);
        OriginalQueue.Remove(song);

        var originalIdx = currentPlaying != null ? OriginalQueue.IndexOf(currentPlaying) : -1;
        if (originalIdx >= 0 && originalIdx < OriginalQueue.Count)
            OriginalQueue.Insert(originalIdx + 1, song);
        else
            OriginalQueue.Add(song);

        var playIdx = currentPlaying != null ? PlaybackQueue.IndexOf(currentPlaying) : -1;
        if (playIdx >= 0 && playIdx < PlaybackQueue.Count)
            PlaybackQueue.Insert(playIdx + 1, song);
        else
            PlaybackQueue.Add(song);
    }

    public void AddToEnd(IReadOnlyList<SongItem> songs)
    {
        if (songs.Count == 0)
            return;

        DemandCapacity(PlaybackQueue.Concat(songs).ToArray());
        var entries=songs.Select(s=>s.NewQueueEntry()).ToArray();
        OriginalQueue.AddRange(entries);PlaybackQueue.AddRange(entries);
    }

    public void Remove(SongItem song)
    {
        PlaybackQueue.Remove(song);
        OriginalQueue.Remove(song);
    }

    public void Clear()
    {
        _shuffleHistory.Clear();
        PlaybackQueue.Clear();
        OriginalQueue.Clear();
    }

    public bool ToggleRepeatOne(SongItem? currentSong)
    {
        IsSequentialMode=false;IsRepeatOneMode = !IsRepeatOneMode;
        if (IsRepeatOneMode && IsShuffleMode)
            SetShuffleMode(false, currentSong);

        return IsRepeatOneMode;
    }

    public bool ToggleShuffle(SongItem? currentSong)
    {
        IsSequentialMode=false;SetShuffleMode(!IsShuffleMode, currentSong);
        if (IsShuffleMode)
            IsRepeatOneMode = false;

        return IsShuffleMode;
    }

    private void SetShuffleMode(bool value, SongItem? currentSong)
    {
        if (IsShuffleMode == value)
            return;

        IsShuffleMode = value;
        if (PlaybackQueue.Count == 0)
            return;

        if (IsShuffleMode)
        {
            var songsToShuffle = PlaybackQueue.AsValueEnumerable().Where(x => !Equals(x,currentSong)).ToList();
            var n = songsToShuffle.Count;
            while (n > 1)
            {
                n--;
                var k = _random.Next(n + 1);
                (songsToShuffle[k], songsToShuffle[n]) = (songsToShuffle[n], songsToShuffle[k]);
            }

            PlaybackQueue.Clear();
            if (currentSong != null && OriginalQueue.Contains(currentSong)) PlaybackQueue.Add(currentSong);
            PlaybackQueue.AddRange(songsToShuffle);
        }
        else
        {
            if (OriginalQueue.Count != PlaybackQueue.Count)
            {
                OriginalQueue.Clear();
                OriginalQueue.AddRange(PlaybackQueue);
            }

            PlaybackQueue.Clear();
            PlaybackQueue.AddRange(OriginalQueue);
        }
    }
    
    private static List<SongItem> TakeWindowAround(
        IList<SongItem> source,
        SongItem current,
        int maxCount)
    {
        if (source.Count <= maxCount)
            return source.AsValueEnumerable().ToList();

        var currentIndex = source.IndexOf(current);
        if (currentIndex < 0)
        {
            return source
                .AsValueEnumerable()
                .Take(maxCount)
                .ToList();
        }
        
        var before = maxCount / 2;
        var after = maxCount - before - 1;

        var start = currentIndex - before;
        var end = currentIndex + after;
        
        if (start < 0)
        {
            end += -start;
            start = 0;
        }
        
        if (end >= source.Count)
        {
            var overflow = end - source.Count + 1;
            start = Math.Max(0, start - overflow);
            end = source.Count - 1;
        }

        var count = end - start + 1;

        return source
            .AsValueEnumerable()
            .Skip(start)
            .Take(count)
            .ToList();
    }
    
    private void ShuffleInPlace(List<SongItem> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
