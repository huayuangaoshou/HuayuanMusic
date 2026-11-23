using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using ZLinq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class PlayerViewModel
{
    private void OnPlaybackQueueCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsPersonalFmSessionActive)
            return;

        SyncDisplayPlaybackQueue();
        SchedulePlaybackQueueCacheSave();
    }

    private void SyncDisplayPlaybackQueue()
    {
        void Update()
        {
            DisplayPlaybackQueue.Clear();
            if (IsPersonalFmSessionActive)
                DisplayPlaybackQueue.AddRange(GetPersonalFmQueueSongs());
            else
                DisplayPlaybackQueue.AddRange(_queueManager.PlaybackQueue);

            OnPropertyChanged(nameof(DisplayPlaybackQueueCount));
            OnPropertyChanged(nameof(HasDisplayPlaybackQueue));
        }

        if (Dispatcher.UIThread.CheckAccess())
            Update();
        else
            Dispatcher.UIThread.Post(Update);
    }

    private void AddLoadedSongsToQueue(IReadOnlyList<SongItem> songs)
    {
        if (songs.Count == 0)
        {
            _toastManager.CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("没有可添加的歌曲")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            return;
        }

        if (IsPersonalFmSessionActive)
            ClearPersonalFmSession();

        _queueManager.AddToEnd(songs);
        _toastManager.CreateToast()
            .OfType(NotificationType.Success)
            .WithTitle("已添加到播放列表")
            .WithContent($"已添加 {songs.Count} 首歌曲")
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Queue();
    }

    void IPlaybackCommands.AddToQueue(IReadOnlyList<SongItem> songs)
    {
        AddLoadedSongsToQueue(songs);
    }

    Task IPlaybackCommands.ReplaceQueueAsync(
        IReadOnlyList<SongItem> songs,
        SongItem? startSong,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ReplacePlaybackQueueAsync(songs, startSong);
    }

    public async Task ReplacePlaybackQueueAsync(IReadOnlyList<SongItem> songs, SongItem? startSong = null)
    {
        if (songs.Count == 0)
        {
            _toastManager.CreateToast()
                .OfType(NotificationType.Warning)
                .WithTitle("没有可播放的歌曲")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            return;
        }

        var contextList = songs as IList<SongItem> ?? songs.AsValueEnumerable().ToList();
        var normalizedStartSong = startSong is not null && contextList.Contains(startSong)
            ? startSong
            : contextList[0];
        var queueSnapshot = contextList as IReadOnlyList<SongItem> ?? contextList.AsValueEnumerable().ToList();

        if (IsPersonalFmSessionActive)
            ClearPersonalFmSession();

        _queueManager.RestoreQueue(queueSnapshot);
        await PlaySongAsync(normalizedStartSong, contextList);
    }

    [RelayCommand]
    private async Task PlaySong(SongItem? song)
    {
        await PlaySongAsync(song);
    }

    internal Task ToggleDesktopTransportAsync() => TogglePlayPause();

    [RelayCommand]
    private async Task TogglePlayPause()
    {
        if (_player.IsPlaying)
        {
            var request = _playRequestVersion;
            await _player.FadeOutAsync();
            if (request != _playRequestVersion) return;
            _player.Pause();
            IsPlayingAudio = false;
            _playbackTimer.Stop();
            ResetVisualizerBars();
        }
        else
        {
            if (_player.IsStopped && CurrentPlayingSong != null)
            {
                _ = PlaySongAsync(CurrentPlayingSong);
                return;
            }

            _player.Play();
            IsPlayingAudio = true;
            _playbackTimer.Start();
        }
    }

    [RelayCommand]
    private async Task PlayNext()
    {
        if (IsPersonalFmSessionActive)
        {
            await PlayNextPersonalFmAsync();
            return;
        }

        var next=_queueManager.GetNext(CurrentPlayingSong);if(next==null){StopAndReset();return;}_queueManager.RecordNavigation(CurrentPlayingSong,next);await PlaySongAsync(next);
    }

    [RelayCommand]
    private async Task PlayPrevious()
    {
        if (IsPersonalFmSessionActive)
        {
            await PlayPreviousPersonalFmAsync();
            return;
        }

        await PlaySongAsync(_queueManager.GetPrevious(CurrentPlayingSong));
    }

    private async Task ChangeTrackPreservingPlaybackStateAsync(bool playNext)
    {
        var wasPlaying = IsPlayingAudio;
        var wasStopped = _player.IsStopped;

        if (playNext)
            await PlayNext();
        else
            await PlayPrevious();

        if (wasPlaying || !IsPlayingAudio)
            return;

        if (wasStopped)
            StopAndReset();
        else
            await TogglePlayPause();
    }

    [RelayCommand]
    private void ToggleRepeatOneMode()
    {
        _queueManager.ToggleRepeatOne(CurrentPlayingSong);
        OnPlaybackModeChanged(saveSettings: true);
    }

    [RelayCommand]
    private void ToggleShuffleMode()
    {
        _queueManager.ToggleShuffle(CurrentPlayingSong);
        OnPlaybackModeChanged(saveSettings: true);
    }

    public PlayMode CurrentPlayMode =>
        _queueManager.IsSequentialMode ? PlayMode.Sequential :
        IsRepeatOneMode ? PlayMode.RepeatOne :
        IsShuffleMode ? PlayMode.Shuffle :
        PlayMode.Normal;

    public string CurrentPlayModeDisplayText => CurrentPlayMode switch
    {
        PlayMode.RepeatOne => "单曲循环",
        PlayMode.Shuffle => "随机播放",
        PlayMode.Sequential => "顺序播放",
        _ => "列表循环"
    };

    [RelayCommand]
    private void CyclePlayMode()
    {
        var nextMode = CurrentPlayMode switch
        {
            PlayMode.Normal => PlayMode.Sequential,
            PlayMode.Sequential => PlayMode.Shuffle,
            PlayMode.Shuffle => PlayMode.RepeatOne,
            _ => PlayMode.Normal
        };

        ApplyPlayMode(nextMode, saveSettings: true);
    }

    [RelayCommand]
    private void SetPlayMode(PlayMode mode)
    {
        ApplyPlayMode(mode, saveSettings: true);
    }

    public void ApplySavedPlaybackModePreference()
    {
        ApplyPlayMode(SettingsManager.Settings.PlaybackMode, saveSettings: false);
    }

    internal void ApplyPlayMode(PlayMode mode, bool saveSettings)
    {
        if (mode == CurrentPlayMode)
            return;

        _queueManager.SetMode(mode,CurrentPlayingSong);

        OnPlaybackModeChanged(saveSettings);
    }

    private void OnPlaybackModeChanged(bool saveSettings)
    {
        ResetTransitionPipeline(true);
        SchedulePlaybackQueueCacheSave();
        OnPropertyChanged(nameof(IsRepeatOneMode));
        OnPropertyChanged(nameof(IsShuffleMode));
        OnPropertyChanged(nameof(CurrentPlayMode));
        OnPropertyChanged(nameof(CurrentPlayModeDisplayText));

        if (!saveSettings)
            return;

        SettingsManager.Settings.PlaybackMode = CurrentPlayMode;
        SettingsManager.Save();
    }

    [RelayCommand]
    private void ClearQueue()
    {
        ClearPersonalFmSession();
        _queueManager.Clear();
        if(StartupProfileIsControlled())StopControlledPlayback();else StopAndReset();
        CurrentPlayingSong=null;DisplayedPlayingSong=null;TotalDurationSeconds=0;
        SchedulePlaybackQueueCacheSave();
    }

    [RelayCommand]
    private async Task RemoveFromQueue(SongItem song) {
        if(Equals(song,CurrentPlayingSong)){var next=_queueManager.GetNext(song);if(Equals(next,song))next=null;bool running=_player.IsPlaying;if(StartupProfileIsControlled())StopControlledPlayback();else StopAndReset();_queueManager.Remove(song);if(next!=null){if(running)await PlaySongAsync(next);else SelectQueueSongWithoutAudio(next);}else{CurrentPlayingSong=null;DisplayedPlayingSong=null;TotalDurationSeconds=0;}}
        else _queueManager.Remove(song);
        SchedulePlaybackQueueCacheSave();
    }

    [RelayCommand]
    private async Task ToggleLike()
    {
        if (CurrentPlayingSong == null)
            return;

        IsLiked = await _favoriteService.ToggleLikeAsync(CurrentPlayingSong, IsLiked);
    }

    private void StopAndReset()
    {
        _controlledAudition=false;
        Interlocked.Increment(ref _queuePlaybackEpoch);
        CancelAndDisposeDelayedVisualSwitchCancellation();
        ResetTransitionPipeline(true);
        _playbackTimer.Stop();
        _playbackCoordinator.InvalidatePendingLoads();
        _player.Stop();
        IsPlayingAudio = false;
        CurrentLyricLine = null;
        CurrentLyricIndex = -1;
        NextLyricLine = null;
        CurrentPositionSeconds = 0;
        _lyricsService.Clear();
        Interlocked.Increment(ref _lyricsLoadVersion);
        CompleteNowPlayingLyricsTransition();
        ResetTailTelemetry();
        ResetVisualizerBars();
    }

    public async Task RestoreCachedPlaybackQueueAsync()
    {
        var snapshot = await Task.Run(_queueCacheService.Load);
        if (snapshot is not { Songs.Count: > 0 })
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isRestoringCachedQueue = true;
            try
            {
                _queueManager.RestoreExact(snapshot.Songs,snapshot.OriginalSongs,snapshot.Mode);
                StopAndReset();

                var currentSong = ResolveCachedCurrentSong(snapshot);
                if (currentSong == null)
                    return;

                if (CurrentPlayingSong != null)
                    CurrentPlayingSong.IsPlaying = false;

                currentSong.IsPlaying = true;
                CurrentPlayingSong = currentSong;
                DisplayedPlayingSong = currentSong;
                IsLiked = _favoriteService.IsLiked(currentSong);
                TotalDurationSeconds = currentSong.DurationSeconds;
                CurrentPositionSeconds = 0;
                IsPlayingAudio = false;
                SyncDisplayPlaybackQueue();
            }
            finally
            {
                _isRestoringCachedQueue = false;
            }
        });
    }

    partial void OnCurrentPlayingSongChanged(SongItem? value)
    {
        SchedulePlaybackQueueCacheSave();
    }

    private SongItem? ResolveCachedCurrentSong(PlaybackQueueCacheSnapshot snapshot)
    {
        if (snapshot.Songs.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(snapshot.CurrentSongKey))
            return snapshot.Songs[0];

        return snapshot.Songs.AsValueEnumerable().FirstOrDefault(
                   song => string.Equals(
                       PlaybackQueueCacheService.BuildEntryKey(song),
                       snapshot.CurrentSongKey,
                       StringComparison.Ordinal))
               ?? snapshot.Songs[0];
    }

    private void SchedulePlaybackQueueCacheSave()
    {
        if (_isRestoringCachedQueue || IsPersonalFmSessionActive)
            return;

        Interlocked.Increment(ref _cacheGeneration);
        var previous = Interlocked.Exchange(
            ref _queueCacheSaveCancellation,
            new CancellationTokenSource());
        previous?.Cancel();

        var cancellation = _queueCacheSaveCancellation;
        if (cancellation == null)
            return;

        _ = SavePlaybackQueueCacheAfterDelayAsync(cancellation);
    }

    private async Task SavePlaybackQueueCacheAfterDelayAsync(CancellationTokenSource cancellation)
    {
        var generation=Interlocked.Read(ref _cacheGeneration);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation.Token);
            var songs = _queueManager.PlaybackQueue.AsValueEnumerable().ToList();
            var currentSong = CurrentPlayingSong;
            var original=_queueManager.OriginalQueue.ToArray();var mode=CurrentPlayMode;
            await Task.Run(() => {lock(_controlledCacheGate){if(generation==Interlocked.Read(ref _cacheGeneration))_queueCacheService.Save(songs,currentSong,original,mode,PersistControlledLocalSubset);}}, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_queueCacheSaveCancellation, cancellation))
                Interlocked.CompareExchange(ref _queueCacheSaveCancellation, null, cancellation);

            cancellation.Dispose();
        }
    }

    private void CancelAndDisposeQueueCacheSaveCancellation()
    {
        var cancellation = Interlocked.Exchange(ref _queueCacheSaveCancellation, null);
        if (cancellation == null)
            return;

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static bool StartupProfileIsControlled()=>KugouAvaloniaPlayer.Services.Startup.OfflineStartupProfile.Active;

    private void OnPlaybackEnded()
    {
        if (_controlledAudition) return;
        if(UseDesktopUnavailableHandling)
        {
            var version=_playRequestVersion;
            Dispatcher.UIThread.Post(async () =>
            {
                if(version!=_playRequestVersion||_playSongLock.CurrentCount==0||!IsPlayingAudio)return;
                var ended=CurrentPlayingSong;
                if(IsRepeatOneMode&&ended is { } current && DesktopCanPlayAutomatically?.Invoke(current)!=false)
                {
                    await PlaySongAsync(current);
                    if(_player.IsPlaying)return;
                }
                await PlayDesktopNextAvailableAsync(ended);
            });
            return;
        }
        // Offline local UI: a native EOF can be posted while an explicit load/stop owns transport.
        // Reject that stale notification; do not let it enqueue a new load into a replacement queue.
        if (StartupProfileIsControlled())
        {
            var epoch=Interlocked.Read(ref _queuePlaybackEpoch);
            if(_playSongLock.CurrentCount==0)return;
            Dispatcher.UIThread.Post(async () =>
            {
                if(epoch!=Interlocked.Read(ref _queuePlaybackEpoch)||_playSongLock.CurrentCount==0||!IsPlayingAudio)return;
                var duration=_player.GetDuration().TotalSeconds;
                if(duration<=0||_player.GetPosition().TotalSeconds<duration-.2)return;
                if(IsRepeatOneMode)await PlaySongAsync(CurrentPlayingSong);else await PlayNext();
            });
            return;
        }
        if (IsPersonalFmSessionActive)
        {
            Dispatcher.UIThread.Post(async () => await PlayNextPersonalFmAsync(true));
            return;
        }

        if (IsRepeatOneMode)
        {
            Dispatcher.UIThread.Post(async () => await PlaySongAsync(CurrentPlayingSong));
            return;
        }

        Dispatcher.UIThread.Post(() => _ = PlayNext());
    }
}
