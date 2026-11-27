using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaLyrics;
using KugouAvaloniaPlayer.Services;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;

namespace HuayuanPreview.R2;

internal sealed record LyricOverride(string Text, string Format)
{
    internal const int Limit = 512 * 1024;
    private const string Scope = "r23_lyric_overrides";

    private static string Key(string track) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(track)));

    internal static LyricOverride? Read(string track)
    {
        var value = AppSqliteStore.LoadValue(Scope, Key(track));
        return value == null || value.Length > Limit * 6 ? null : JsonSerializer.Deserialize<LyricOverride>(value);
    }

    internal void Save(string track) => AppSqliteStore.SaveValue(Scope, Key(track), JsonSerializer.Serialize(this));

    internal LyricLine[] Parse() => Text is { Length: > 0 and <= Limit } && Format is "krc" or "lrc"
        ? LyricsService.ParseDesktopRenderLines(Text, Format) : [];
}

internal static class OnlineLyricAlternatives
{
    internal sealed record Candidate(string Id, string Access, string Format, string Song, string Singer, string Duration);

    // Public lyric routes use a separate anonymous SDK transport and carry no saved account credentials.
    private static readonly HttpClient Http = new(new KgSignatureHandler(
        new KgSessionManager(new CookieContainer(), new InMemorySessionPersistence()))
    {
        InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, Credentials = null }
    })
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = LyricOverride.Limit * 3
    };
    private static readonly LyricClient Client = new(new RawLyricApi(new KgHttpTransport(Http, 1)));

    internal static async Task<Candidate[]> FindAsync(string title, string singer, CancellationToken token)
    {
        var keyword = (title + " " + singer).Trim();
        if (keyword.Length == 0) return [];
        var response = await Client.SearchLyricAsync(null, null, keyword[..Math.Min(keyword.Length, 240)], "yes")
            .WaitAsync(token);
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("candidates", out var list) || list.ValueKind != JsonValueKind.Array)
            return [];
        return list.EnumerateArray().Take(30).Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item =>
            {
                string Get(string field) => item.TryGetProperty(field, out var value) ? value.ToString() : "";
                var duration = int.TryParse(Get("duration"), out var ms) && ms > 0
                    ? TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss") : "时长未知";
                return new Candidate(Get("id"), Get("accesskey"), Get("fmt") == "lrc" ? "lrc" : "krc",
                    Get("song") is { Length: > 0 } song ? song : title,
                    Get("singer") is { Length: > 0 } artist ? artist : singer, duration);
            })
            .Where(item => item.Id.Length > 0 && item.Access.Length > 0).DistinctBy(item => item.Id).ToArray();
    }

    internal static async Task<LyricOverride> DownloadAsync(Candidate candidate, CancellationToken token)
    {
        var reply = await Client.GetLyricAsync(candidate.Id, candidate.Access, candidate.Format).WaitAsync(token);
        return new LyricOverride(reply.DecodedContent ?? "", candidate.Format);
    }
}

public partial class R2ShellView
{
    private CancellationTokenSource? otherLyricRequest;
    private string? activeLyricOverride;
    private Task lyricOverrideWrite = Task.CompletedTask;

    private Task SearchOtherLyricsAsync() => ChangeLyricSourceAsync(async (track, token) =>
    {
        var candidates = await OnlineLyricAlternatives.FindAsync(track.Name, track.Singer, token);
        if (candidates.Length == 0)
        {
            ShowToast("没有找到其他版本");
            return null;
        }

        var list = new ListBox
        {
            ItemsSource = candidates,
            SelectedIndex = 0,
            MinWidth = 280,
            MaxHeight = 330,
            ItemTemplate = new FuncDataTemplate<OnlineLyricAlternatives.Candidate>((item, _) => item == null ? null :
                new StackPanel
                {
                    Spacing = DesignTokens.Space1,
                    Children =
                    {
                        new TextBlock { Text = item.Song, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = $"{item.Singer} · {item.Duration}", Classes = { "muted" } }
                    }
                })
        };
        var confirm = new Button { Content = "使用选中歌词", Classes = { "primary" }, IsDefault = true };
        var cancel = new Button { Content = "取消", IsCancel = true };
        var panel = new StackPanel
        {
            Spacing = DesignTokens.Space3,
            Children = { list, new StackPanel { Spacing = DesignTokens.Space2, Orientation = Orientation.Horizontal, Children = { cancel, confirm } } }
        };
        token.ThrowIfCancellationRequested();
        var dialog = ModalOverlay.TryShow<OnlineLyricAlternatives.Candidate?>(this, panel, new("选择歌词版本", 480, InitialFocus: list));
        if (dialog == null) return null;
        confirm.Click += (_, _) => { if (list.SelectedItem is OnlineLyricAlternatives.Candidate item) dialog.Complete(item); };
        cancel.Click += (_, _) => dialog.Dismiss();
        using var registration = token.Register(() => Dispatcher.UIThread.Post(dialog.Dismiss));
        var selected = await dialog.Completion;
        return selected == null ? null : await OnlineLyricAlternatives.DownloadAsync(selected, token);
    }, "暂时无法加载歌词版本", "没有找到可用的歌词内容");

    private Task ImportLocalLyricsAsync() => ChangeLyricSourceAsync(async (_, token) =>
    {
        if (TopLevel.GetTopLevel(this) is not { } owner) return null;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入本地 LRC",
            FileTypeFilter = [new FilePickerFileType("LRC 歌词") { Patterns = ["*.lrc"] }]
        });
        try
        {
            token.ThrowIfCancellationRequested();
            if (files.FirstOrDefault() is not { } file) return null;
            if (!file.Name.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            await using var stream = await file.OpenReadAsync();
            var bytes = new byte[LyricOverride.Limit + 1];
            var count = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, token);
            if (count > LyricOverride.Limit) throw new InvalidDataException();
            using var reader = new StreamReader(new MemoryStream(bytes, 0, count), detectEncodingFromByteOrderMarks: true);
            return new LyricOverride(await reader.ReadToEndAsync(token), "lrc");
        }
        finally { foreach (var file in files) file.Dispose(); }
    }, "暂时无法读取这个 LRC 文件", "LRC 中没有可用的时间轴歌词");

    private async Task ChangeLyricSourceAsync(Func<TrackRow, CancellationToken, Task<LyricOverride?>> load,
        string failed, string empty)
    {
        if (model?.Player.DisplayedPlayingSong is not { } track) return;
        var key = track.Track.Key;
        otherLyricRequest?.Cancel();
        using var request = new CancellationTokenSource();
        otherLyricRequest = request;
        try
        {
            var source = await load(track, request.Token);
            if (source == null || request.IsCancellationRequested || lyricTrack != key) return;
            var lines = await Task.Run(source.Parse, request.Token);
            if (request.IsCancellationRequested || lyricTrack != key) return;
            if (lines.Length == 0) { ShowToast(empty); return; }

            lyricRequest?.Cancel();
            activeLyricOverride = key;
            SetLyrics(lines, "");
            lyricOverrideWrite = SaveAsync(lyricOverrideWrite);
            await lyricOverrideWrite;

            async Task SaveAsync(Task previous)
            {
                await previous;
                try
                {
                    await Task.Run(() => source.Save(key));
                    Program.Log("R23_LYRIC_OVERRIDE_SAVED", new { format = source.Format, lines = lines.Length });
                    if (!request.IsCancellationRequested && lyricTrack == key) ShowToast("已更换歌词");
                }
                catch (Exception error)
                {
                    Program.Log("R23_LYRIC_OVERRIDE_WRITE_FAILED", new { type = error.GetType().Name });
                    if (!request.IsCancellationRequested && lyricTrack == key) ShowToast("歌词已换，本次未能记住");
                }
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error)
        {
            Program.Log("R23_LYRIC_SOURCE_FAILED", new { type = error.GetType().Name });
            if (!request.IsCancellationRequested && lyricTrack == key) ShowToast(failed);
        }
        finally
        {
            if (ReferenceEquals(otherLyricRequest, request)) otherLyricRequest = null;
        }
    }
}
