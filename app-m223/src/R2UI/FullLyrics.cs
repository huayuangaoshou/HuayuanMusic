using Avalonia.Controls;
using Avalonia.Interactivity;
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

    private void InitializeLyricOverlay()
    {
        FullLyricView.SeekCommand = new RelayCommand<TimeSpan>(position =>
        {
            if (model != null) _ = model.Player.Seek(position.TotalSeconds);
        });
        DetachedFromVisualTree += (_, _) => CloseLyricOverlay();
    }

    private void ToggleLyricOverlay()
    {
        if (lyricOpen) { CloseLyricOverlay(); return; }
        lyricOpen = true;
        LyricsOverlay.IsVisible = true;
        LyricsOverlay.Opacity = 0;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (lyricOpen) LyricsOverlay.Opacity = 1;
        }, Avalonia.Threading.DispatcherPriority.Render);
        UpdateLyricOverlay();
    }

    private void HideLyricOverlay(object? sender, RoutedEventArgs args) => CloseLyricOverlay();
    private void CloseLyricOverlay()
    {
        if (!lyricOpen) return;
        lyricOpen = false;
        lyricRequest?.Cancel();
        lyricRequest = null;
        lyricTrack = null;
        previousLocalLyrics = null;
        LyricsOverlay.Opacity = 0;
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            if (!lyricOpen) LyricsOverlay.IsVisible = false;
        }, TimeSpan.FromMilliseconds(210));
    }

    private void UpdateLyricOverlay()
    {
        if (!lyricOpen || model == null) return;
        var player = model.Player;
        var row = player.DisplayedPlayingSong;
        FullLyricView.Position = TimeSpan.FromSeconds(Math.Max(0, player.State.Position));
        FullLyricView.IsPositionClockRunning = player.State.Playing;
        if (row == null)
        {
            SetLyrics([], "播放一首歌曲后查看歌词");
            lyricTrack = null;
            return;
        }
        if (row.Track.Key == lyricTrack && (row.IsProviderTrack || ReferenceEquals(previousLocalLyrics, player.State.Lyrics))) return;
        lyricTrack = row.Track.Key;
        lyricRequest?.Cancel();
        if (row.IsProviderTrack)
        {
            SetLyrics([], "正在加载歌词…");
            _ = LoadOnlineLyricAsync(row.Track.Key);
        }
        else
        {
            previousLocalLyrics = player.State.Lyrics;
            SetLyrics(previousLocalLyrics.Select(line => new LyricLine
            {
                Text = line.Text, Start = TimeSpan.FromMilliseconds(line.StartMilliseconds)
            }).ToArray(), "未找到本地歌词");
        }
    }

    private async Task LoadOnlineLyricAsync(string key)
    {
        if (model == null) return;
        using var request = new CancellationTokenSource();
        lyricRequest = request;
        var id = key[(key.LastIndexOf(':') + 1)..];
        try
        {
            var result = await model.Concept.DesktopCall((gateway, ct) => gateway.ReadDesktopLyricAsync(id, ct), request.Token);
            var lines = result == null ? [] : await Task.Run(() => LyricsService.ParseDesktopRenderLines(result.Text, result.Format), request.Token);
            if (!lyricOpen || lyricTrack != key || request.IsCancellationRequested) return;
            SetLyrics(lines, "这首歌暂无歌词");
            Program.Log("R2_DESKTOP_LYRICS", new { lines = lines.Length, wordLines = lines.Count(line => line.Words.Count > 0) });
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!request.IsCancellationRequested && lyricOpen && key == lyricTrack)
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
        FullLyricView.Lines = lines;
        LyricEmpty.Text = empty;
        LyricEmpty.IsVisible = lines.Length == 0;
    }
}
