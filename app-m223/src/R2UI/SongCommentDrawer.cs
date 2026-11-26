using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    private TrackRow? drawerCommentTrack;
    private CancellationTokenSource? drawerCommentRequest;
    private int drawerCommentPage;
    private readonly HashSet<long> drawerCommentIds = [];

    private void ShowCurrentComments(object? sender, RoutedEventArgs args)
    {
        if (model?.Player.DisplayedPlayingSong is { } row) OpenSongComments(row);
    }

    internal void OpenSongComments(TrackRow row)
    {
        if (!row.CanUseOnlineActions) return;
        drawerCommentRequest?.Cancel();
        drawerCommentRequest = null;
        drawerCommentTrack = row;
        drawerCommentPage = 1;
        drawerCommentIds.Clear();
        DrawerCommentTitle.Text = row.Name;
        DrawerCommentCount.Text = "";
        DrawerCommentHot.Children.Clear();
        DrawerCommentLatest.Children.Clear();
        DrawerCommentMore.IsVisible = false;
        DrawerCommentScroll.Offset = default;
        if (queueOpen) SetQueueOpen(false);
        CommentDrawerLayer.IsVisible = true;
        CommentDrawerLayer.IsHitTestVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (drawerCommentTrack == null) return;
            SetFadeDirection(CommentDrawerShade, entering: true);
            CommentDrawerShade.Opacity = 1;
            CommentDrawer.RenderTransform = TransformOperations.Parse("translateX(0px)");
        }, DispatcherPriority.Loaded);
        _ = FetchDrawerCommentsAsync();
    }

    private void CloseSongComments(object? sender, RoutedEventArgs args) => CloseSongComments();
    private void CloseSongCommentsFromShade(object? sender, PointerPressedEventArgs args) => CloseSongComments();

    private void CloseSongComments()
    {
        if (drawerCommentTrack == null) return;
        drawerCommentTrack = null;
        drawerCommentRequest?.Cancel();
        drawerCommentRequest = null;
        CommentDrawerLayer.IsHitTestVisible = false;
        SetFadeDirection(CommentDrawerShade, entering: false);
        CommentDrawerShade.Opacity = 0;
        CommentDrawer.RenderTransform = TransformOperations.Parse("translateX(444px)");
        DispatcherTimer.RunOnce(() =>
        {
            if (drawerCommentTrack == null) CommentDrawerLayer.IsVisible = false;
        }, DesignTokens.MotionSpatialDefault.Duration);
    }

    private async void LoadDrawerComments(object? sender, RoutedEventArgs args) => await FetchDrawerCommentsAsync();

    private async Task FetchDrawerCommentsAsync()
    {
        if (model is not { } shell || drawerCommentTrack is not { } row || !row.CanUseOnlineActions ||
            drawerCommentRequest != null || drawerCommentPage < 1) return;
        using var request = new CancellationTokenSource();
        drawerCommentRequest = request;
        int page = drawerCommentPage;
        string key = row.CatalogIdentity;
        string id = key[(key.LastIndexOf(':') + 1)..];
        DrawerCommentStatus.Text = "正在加载评论…";
        DrawerCommentMore.IsEnabled = false;
        try
        {
            var data = await shell.Concept.DesktopCall((gateway, ct) => gateway.ReadDesktopCommentsAsync(id, page, ct), request.Token);
            if (request.IsCancellationRequested || !row.CanUseOnlineActions) return;
            DrawerCommentStatus.Text = data == null || (data.Total == 0 && data.Hot.Length == 0 && data.Latest.Length == 0)
                ? "这首歌暂无评论" : "";
            if (data != null)
            {
                DrawerCommentCount.Text = data.Total > 0 ? data.Total.ToString("N0") : "";
                if (page == 1)
                {
                    if (data.Hot.Length > 0) DrawerCommentHot.Children.Add(SectionHeading(data.PlatformHot ? "精彩评论" : "本页热评"));
                    foreach (var entry in data.Hot) DrawerCommentHot.Children.Add(CommentCard(entry));
                    if (data.Latest.Length > 0) DrawerCommentLatest.Children.Add(SectionHeading("最新评论"));
                }
                foreach (var entry in data.Latest)
                    if (entry.Id == 0 || drawerCommentIds.Add(entry.Id))
                        DrawerCommentLatest.Children.Add(CommentCard(entry));
                Program.Log("R2_DRAWER_COMMENTS", new { data.Source, data.Total, page, hot = data.Hot.Length, latest = data.Latest.Length });
            }
            DrawerCommentMore.Content = "加载更多评论";
            DrawerCommentMore.IsVisible = data?.More == true;
            drawerCommentPage = data?.More == true ? page + 1 : -1;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request.IsCancellationRequested) return;
            DrawerCommentStatus.Text = "暂时无法加载评论";
            DrawerCommentMore.Content = "重试";
            DrawerCommentMore.IsVisible = true;
            Program.Log("R2_DRAWER_COMMENTS_FAILED", new { type = error.GetType().Name });
        }
        finally
        {
            if (ReferenceEquals(drawerCommentRequest, request))
            {
                drawerCommentRequest = null;
                DrawerCommentMore.IsEnabled = true;
            }
        }
    }
}
