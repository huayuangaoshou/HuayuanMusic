using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    private bool showTranslation = DesktopPreferences.Behavior.Translation;
    private bool commentsOpen;
    private string? commentTrack;
    private CancellationTokenSource? commentRequest;
    private int nextCommentPage;
    private readonly HashSet<long> shownComments = [];

    private async void ToggleTranslation(object? sender, RoutedEventArgs args)
    {
        showTranslation = !showTranslation;
        TranslationToggle.Classes.Set("selected", showTranslation);
        FullLyricView.ShowTranslation = showTranslation;
        await PersistPreferenceAsync(options => options with { Translation = showTranslation });
    }

    private void InstallLyricsMenu()
    {
        TranslationToggle.Classes.Set("selected", showTranslation);
        FullLyricView.ContextMenu = new ContextMenu
        {
            Items =
            {
                MenuItemFor("复制当前歌词", () => CopyTextAsync(FullLyricView, LyricText(FullLyricView.ActiveLine))),
                MenuItemFor("复制全部歌词", () => CopyTextAsync(FullLyricView,
                    string.Join(Environment.NewLine, displayedLyrics.Select(LyricText))))
            }
        };
    }

    private static string LyricText(AvaloniaLyrics.LyricLine? line) =>
        line == null ? "" : string.IsNullOrWhiteSpace(line.Translation) ? line.Text : line.Text + Environment.NewLine + line.Translation;

    internal static async Task CopyTextAsync(Control owner, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || TopLevel.GetTopLevel(owner)?.Clipboard is not { } clipboard) return;
        try { await clipboard.SetTextAsync(text); }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        { Program.Log("R2_CLIPBOARD_FAILED", new { type = error.GetType().Name }); }
    }

    private static MenuItem MenuItemFor(string label, Func<Task> action)
    {
        var item = new MenuItem { Header = label };
        item.Click += async (_, _) => await action();
        return item;
    }

    private void InstallDesktopLyricsMenu(Button button)
    {
        var unlock = MenuItemFor("解锁桌面歌词", () =>
        {
            desktopLyrics?.SetLocked(false);
            return Task.CompletedTask;
        });
        var reset = MenuItemFor("重置位置与字号", () =>
        {
            if (desktopLyrics != null) desktopLyrics.ResetAppearance();
            else QueueLyricPreferences(new DesktopLyricOptions());
            return Task.CompletedTask;
        });
        var menu = new ContextMenu { Items = { unlock, reset } };
        menu.Opening += (_, _) => unlock.IsEnabled = desktopLyrics?.Locked == true;
        button.ContextMenu = menu;
    }

    private void ShowLyricsTab(object? sender, RoutedEventArgs args) => SelectCommentTab(false);
    private void ShowCommentsTab(object? sender, RoutedEventArgs args) => SelectCommentTab(true);
    private void SelectCommentTab(bool comments)
    {
        commentsOpen = comments;
        LyricsTab.Classes.Set("selected", !comments);
        CommentsTab.Classes.Set("selected", comments);
        LyricsPane.IsVisible = !comments;
        FullLyricView.IsActive = !comments && lyricOpen;
        CommentsPane.IsVisible = comments;
        if (!comments) return;
        CommentsPane.Transitions ??= [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }];
        CommentsPane.Opacity = 0;
        Dispatcher.UIThread.Post(() => { if (commentsOpen) CommentsPane.Opacity = 1; });
        if (nextCommentPage == 1 && commentRequest == null) _ = FetchCommentsAsync();
    }

    private void UpdateCommentTrack(TrackRow? row)
    {
        if (row?.IsProviderTrack != true && commentsOpen) SelectCommentTab(false);
        var key = row?.IsProviderTrack == true ? row.CatalogIdentity : null;
        if (commentTrack == key && nextCommentPage != 0) return;
        commentRequest?.Cancel();
        commentRequest = null;
        commentTrack = key;
        nextCommentPage = 1;
        shownComments.Clear();
        HotComments.Children.Clear();
        LatestComments.Children.Clear();
        CommentBadge.IsVisible = false;
        MoreComments.IsVisible = false;
        CommentsPane.Offset = default;
        CommentsStatus.Text = key == null ? "本地歌曲暂无在线评论" : "打开评论查看听众留言";
        CommentsStatus.IsVisible = true;
        if (commentsOpen && key != null) _ = FetchCommentsAsync();
    }

    private void CancelComments()
    {
        commentRequest?.Cancel();
        commentRequest = null;
        nextCommentPage = 0;
        SelectCommentTab(false);
    }

    private async void LoadMoreComments(object? sender, RoutedEventArgs args) => await FetchCommentsAsync();

    private async Task FetchCommentsAsync()
    {
        if (!lyricOpen || !commentsOpen || commentRequest != null || model == null || commentTrack is not { } key) return;
        var page = nextCommentPage;
        if (page < 1) return;
        var id = key[(key.LastIndexOf(':') + 1)..];
        using var request = new CancellationTokenSource();
        commentRequest = request;
        MoreComments.IsEnabled = false;
        CommentsStatus.Text = "正在加载评论…";
        CommentsStatus.IsVisible = page == 1;
        try
        {
            var data = await model.Concept.DesktopCall((gateway, ct) => gateway.ReadDesktopCommentsAsync(id, page, ct), request.Token);
            if (!lyricOpen || commentTrack != key || request.IsCancellationRequested) return;
            if (data == null || data.Latest.Length == 0 && data.Hot.Length == 0)
            {
                CommentsStatus.Text = "这首歌暂无评论";
                CommentsStatus.IsVisible = page == 1;
                nextCommentPage = -1;
                MoreComments.IsVisible = false;
                return;
            }

            CommentBadge.IsVisible = data.Total > 0;
            CommentCount.Text = data.Total.ToString("N0");
            if (page == 1)
            {
                if (data.Hot.Length > 0)
                {
                    HotComments.Children.Add(SectionHeading(data.PlatformHot ? "精彩评论" : "本页热评"));
                    foreach (var item in data.Hot) HotComments.Children.Add(CommentCard(item));
                }
                LatestComments.Children.Add(SectionHeading("最新评论"));
            }
            foreach (var item in data.Latest.Where(item => item.Id == 0 || shownComments.Add(item.Id)))
                LatestComments.Children.Add(CommentCard(item));
            CommentsStatus.IsVisible = false;
            MoreComments.Content = "加载更多评论";
            MoreComments.IsVisible = data.More;
            nextCommentPage = data.More ? page + 1 : -1;
            Program.Log("R2_DESKTOP_COMMENTS", new { page, total = data.Total, hot = data.Hot.Length, latest = data.Latest.Length, data.PlatformHot, data.Source });
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!request.IsCancellationRequested)
            {
                CommentsStatus.Text = "暂时无法加载评论";
                CommentsStatus.IsVisible = page == 1;
                MoreComments.Content = "重试加载评论";
                MoreComments.IsVisible = true;
                Program.Log("R2_DESKTOP_COMMENTS_FAILED", new { type = error.GetType().Name });
            }
        }
        finally
        {
            if (ReferenceEquals(commentRequest, request))
            {
                commentRequest = null;
                MoreComments.IsEnabled = true;
            }
        }
    }

    private static TextBlock SectionHeading(string title) =>
        new() { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, 7, 0, 0) };

    private Border CommentCard(DesktopComment item)
    {
        var profile = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var avatar = model!.Concept.Cover(model.Concept.AccountKey, item.Avatar, 30);
        profile.Children.Add(new Border { Child = avatar, Width = 30, Height = 30, CornerRadius = new CornerRadius(15), ClipToBounds = true });
        profile.Children.Add(new SelectableTextBlock { Text = string.IsNullOrWhiteSpace(item.Author) ? "酷狗听众" : item.Author, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        profile.Children.Add(new TextBlock { Text = item.Time, Classes = { "muted" }, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        if (item.Likes > 0)
            profile.Children.Add(new TextBlock { Text = $"赞 {item.Likes:N0}", Classes = { "muted" }, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        var body = new SelectableTextBlock { Text = item.Body, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 22 };
        return new Border
        {
            Classes = { "glass" }, Padding = new Thickness(13, 9),
            Child = new StackPanel { Spacing = 5, Children = { profile, body } }
        };
    }
}
