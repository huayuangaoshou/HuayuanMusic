using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaLyrics;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Services;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    private CancellationTokenSource? lyricRequest;
    private string? lyricTrack;
    private CoreLyric[]? previousLocalLyrics;
    private bool lyricOpen;
    private DesktopLyricWindow? desktopLyrics;
    internal bool DesktopLyricsVisible => desktopLyrics != null;
    internal event Action<bool>? DesktopLyricsVisibilityChanged;
    private LyricLine[] displayedLyrics = [];
    private string lyricMessage = "";
    private bool closingLyricsForExit;
    private Task lyricPreferencesWrite = Task.CompletedTask;

    private void InitializeLyricOverlay()
    {
        FullLyricView.SeekCommand = new RelayCommand<TimeSpan>(position =>
        {
            if (model != null)
            {
                Program.Log("R2_LYRIC_LINE_SEEK", new { seconds = position.TotalSeconds });
                _ = model.Player.Seek(position.TotalSeconds);
            }
        });
        InstallLyricsMenu();
        InstallDesktopLyricsMenu(DesktopLyricToggle);
        InstallDesktopLyricsMenu(FullDesktopLyricToggle);
        UpdateDesktopLyricButtons();
        DetachedFromVisualTree += (_, _) =>
        {
            CloseLyricOverlay();
            if (desktopLyrics != null) desktopLyrics.Close();
            ReleaseLyricsIfHidden();
        };
    }

    private void ToggleLyricOverlay()
    {
        if (lyricOpen) { CloseLyricOverlay(); return; }
        lyricOpen = true;
        MainDock.IsVisible = false;
        LyricsOverlay.IsVisible = true;
        LyricsOverlay.Opacity = 0;
        SetFadeDirection(LyricsOverlay, entering: true);
        FullLyricView.EnableAnimation = true;
        FullLyricView.IsActive = true;
        CancelSuggestions();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (lyricOpen) LyricsOverlay.Opacity = 1;
        }, Avalonia.Threading.DispatcherPriority.Render);
        UpdateVolume();
        UpdateProgress();
    }

    private void HideLyricOverlay(object? sender, RoutedEventArgs args) => CloseLyricOverlay();
    private void CloseLyricOverlay()
    {
        if (!lyricOpen) return;
        lyricOpen = false;
        CancelComments();
        ReleaseLyricsIfHidden();
        SetFadeDirection(LyricsOverlay, entering: false);
        LyricsOverlay.Opacity = 0;
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            if (!lyricOpen)
            {
                FullLyricView.IsActive = false;
                LyricsOverlay.IsVisible = false;
                MainDock.IsVisible = true;
            }
        }, DesignTokens.MotionEffectsSlow.Duration);
    }

    private void UpdateFullLyricClock()
    {
        if (!lyricOpen || model?.Player is not { } player) return;
        FullLyricView.Position = TimeSpan.FromSeconds(Math.Max(0, player.State.Position));
        FullLyricView.IsPositionClockRunning = player.State.Playing;
    }

    private void UpdateLyricOverlay()
    {
        if ((!lyricOpen && desktopLyrics == null) || model == null) return;
        var player = model.Player;
        var row = player.DisplayedPlayingSong;
        if (lyricOpen) UpdateCommentTrack(row);
        UpdateFullLyricClock();
        if (row == null)
        {
            if (lyricTrack != null || lyricMessage.Length == 0)
                SetLyrics([], "播放一首歌曲后查看歌词");
            lyricRequest?.Cancel();
            otherLyricRequest?.Cancel();
            activeLyricOverride = null;
            lyricTrack = null;
            return;
        }
        if (row.Track.Key == lyricTrack && (activeLyricOverride == lyricTrack ||
            row.IsProviderTrack || ReferenceEquals(previousLocalLyrics, player.State.Lyrics))) return;
        if (row.Track.Key != lyricTrack)
        {
            activeLyricOverride = null;
            otherLyricRequest?.Cancel();
        }
        lyricTrack = row.Track.Key;
        lyricRequest?.Cancel();
        previousLocalLyrics = row.IsProviderTrack ? null : player.State.Lyrics;
        SetLyrics([], "正在加载歌词…");
        _ = LoadTrackLyricsAsync(row, previousLocalLyrics);
    }

    private async Task LoadTrackLyricsAsync(TrackRow track, CoreLyric[]? local)
    {
        if (model == null) return;
        var key = track.Track.Key;
        using var request = new CancellationTokenSource();
        lyricRequest = request;
        try
        {
            LyricLine[] saved = [];
            try { saved = await Task.Run(() => LyricOverride.Read(key)?.Parse() ?? [], request.Token); }
            catch (Exception error) when (error is not OperationCanceledException)
            { Program.Log("R23_LYRIC_OVERRIDE_READ_FAILED", new { type = error.GetType().Name }); }
            if ((!lyricOpen && desktopLyrics == null) || lyricTrack != key || request.IsCancellationRequested) return;
            if (saved.Length > 0)
            {
                activeLyricOverride = key;
                SetLyrics(saved, "");
                Program.Log("R23_LYRIC_OVERRIDE_RESTORED", new { lines = saved.Length });
                return;
            }
            if (!track.IsProviderTrack)
            {
                SetLyrics((local ?? []).Select(line => new LyricLine
                {
                    Text = line.Text, Start = TimeSpan.FromMilliseconds(line.StartMilliseconds), Translation = line.Translation
                }).ToArray(), "未找到本地歌词");
                return;
            }
            var id = key[(key.LastIndexOf(':') + 1)..];
            var result = await model.Concept.DesktopCall((gateway, ct) => gateway.ReadDesktopLyricAsync(id, ct), request.Token);
            var lines = result == null ? [] : await Task.Run(() => LyricsService.ParseDesktopRenderLines(result.Text, result.Format), request.Token);
            if ((!lyricOpen && desktopLyrics == null) || lyricTrack != key || request.IsCancellationRequested) return;
            SetLyrics(lines, "这首歌暂无歌词");
            Program.Log("R2_DESKTOP_LYRICS", new { lines = lines.Length, wordLines = lines.Count(line => line.Words.Count > 0), translations = lines.Count(line => !string.IsNullOrWhiteSpace(line.Translation)) });
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!request.IsCancellationRequested && (lyricOpen || desktopLyrics != null) && key == lyricTrack)
                SetLyrics([], "歌词暂时无法加载");
            Program.Log("R2_DESKTOP_LYRICS_FAILED", new { type = error.GetType().Name });
        }
        finally
        {
            if (ReferenceEquals(lyricRequest, request)) lyricRequest = null;
        }
    }

    private void SetLyrics(LyricLine[] lines, string empty)
    {
        // The same timed lines feed both renderers. A line without word timing is a single sweep.
        lines = lines.Select((line, index) =>
        {
            if (line.Words.Count > 0 || string.IsNullOrWhiteSpace(line.Text)) return line;
            var duration = line.Duration > TimeSpan.Zero ? line.Duration :
                index + 1 < lines.Length ? lines[index + 1].Start - line.Start : TimeSpan.FromSeconds(3);
            if (duration <= TimeSpan.Zero) duration = TimeSpan.FromSeconds(3);
            return new LyricLine
            {
                Start = line.Start, Duration = duration, Text = line.Text, Translation = line.Translation,
                Romanization = line.Romanization, TranslationWords = line.TranslationWords,
                Words = [new LyricWord { Start = line.Start, Duration = duration, Text = line.Text }]
            };
        }).ToArray();
        displayedLyrics = lines;
        lyricMessage = empty;
        FullLyricView.Lines = lines;
        LyricEmpty.Text = empty;
        LyricEmpty.IsVisible = lines.Length == 0;
        TranslationToggle.IsEnabled = lines.Any(line => !string.IsNullOrWhiteSpace(line.Translation));
        ToolTip.SetTip(TranslationToggle, TranslationToggle.IsEnabled ? "显示或隐藏译文" : "当前曲目暂无译文");
        FullLyricView.ShowTranslation = showTranslation && TranslationToggle.IsEnabled;
        desktopLyrics?.SetLyrics(lines, empty);
    }

    private void ReleaseLyricsIfHidden()
    {
        if (lyricOpen || desktopLyrics != null) return;
        lyricRequest?.Cancel();
        otherLyricRequest?.Cancel();
        lyricRequest = null;
        lyricTrack = null;
        activeLyricOverride = null;
        previousLocalLyrics = null;
        displayedLyrics = [];
        lyricMessage = "";
    }

    private void ToggleDesktopLyrics(object? sender, RoutedEventArgs args)
        => ToggleDesktopLyricsFromHost();

    internal void ToggleDesktopLyricsFromHost()
    {
        CancelSuggestions();
        if (desktopLyrics != null) desktopLyrics.Close();
        else OpenDesktopLyrics();
    }

    internal void UnlockDesktopLyricsFromHost() => desktopLyrics?.SetLocked(false);

    internal void ResetDesktopLyricsFromHost()
    {
        if(desktopLyrics!=null)desktopLyrics.ResetAppearance();
        else QueueLyricPreferences(new DesktopLyricOptions(Serif: DesktopPreferences.DesktopLyrics.Serif));
    }

    private void OpenDesktopLyrics()
    {
        if (desktopLyrics != null || model == null || Window is not { } owner) return;
        var lyrics = new DesktopLyricWindow(owner, model.Player, DesktopPreferences.DesktopLyrics,
            showTranslation, () => _ = SetTranslationAsync(!showTranslation));
        desktopLyrics = lyrics;
        lyrics.LockChanged += UpdateDesktopLyricButtons;
        lyrics.PreferencesChanged += QueueLyricPreferences;
        lyrics.Closing += (_, _) => QueueLyricPreferences(lyrics.Capture(closingLyricsForExit));
        lyrics.Closed += (_, _) =>
        {
            desktopLyrics = null;
            DesktopLyricsVisibilityChanged?.Invoke(false);
            UpdateDesktopLyricButtons();
            ReleaseLyricsIfHidden();
        };
        lyrics.Show();
        DesktopLyricsVisibilityChanged?.Invoke(true);
        if (lyricMessage.Length > 0) lyrics.SetLyrics(displayedLyrics, lyricMessage);
        UpdateDesktopLyricButtons();
        UpdateLyricOverlay();
        lyrics.Sync(model.Player.State);
    }

    internal void RestoreDesktopLyrics()
    {
        if (DesktopPreferences.DesktopLyrics.Visible) OpenDesktopLyrics();
    }

    internal Task CloseDesktopLyricsForExitAsync()
    {
        closingLyricsForExit = true;
        desktopLyrics?.Close();
        return lyricPreferencesWrite;
    }

    private void QueueLyricPreferences(DesktopLyricOptions options)
    {
        lyricPreferencesWrite = SaveAsync(lyricPreferencesWrite, options);
        async Task SaveAsync(Task previous, DesktopLyricOptions options)
        {
            await previous;
            await RunPreferenceActionAsync("desktop_lyrics", () => DesktopPreferences.SetDesktopLyricsAsync(options));
        }
    }

    private void UpdateDesktopLyricButtons()
    {
        foreach (var button in new[] { DesktopLyricToggle, FullDesktopLyricToggle })
        {
            button.Classes.Set("selected", desktopLyrics != null);
            ToolTip.SetTip(button, desktopLyrics == null ? "开启桌面歌词" : "关闭桌面歌词 · 右键解锁或重置");
        }
    }
}
