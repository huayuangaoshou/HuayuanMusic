using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview.R2;

public sealed partial class WorkspaceState
{
    private R2Page? onlinePage;
    private R2Page? onlineSongsPage;
    private R2Page? onlineDetailPage;
    private ContentControl? onlineResults;
    private SelectableTextBlock? onlineStatus;
    private SelectableTextBlock? onlineHeading;
    private readonly Dictionary<DesktopCatalogKind, Button> searchTabs = [];
    private CancellationTokenSource? catalogRequest;
    private string onlineQuery = "";
    private string detailSubtitle = "";

    private void AddDesktopNavigation(StackPanel nav)
    {
        if (Local)
        {
            var folders = new Button { Classes = { "nav" }, Content = "管理本地文件夹" };
            folders.Click += (_, _) => OpenFolderManager();
            nav.Children.Add(folders);
            return;
        }

        var recommend = new Button { Classes = { "nav" }, Content = "每日推荐" };
        recommend.Click += async (_, _) => await OpenRecommendationsAsync();
        var ranks = new Button { Classes = { "nav" }, Content = "排行榜" };
        ranks.Click += async (_, _) => await OpenRanksAsync();
        discoveryNavigation.Add(recommend,"每日推荐");
        discoveryNavigation.Add(ranks,"排行榜");
        onlineNavigation.Add(recommend);
        onlineNavigation.Add(ranks);
        nav.Children.Add(recommend);
        nav.Children.Add(ranks);
    }

    public async Task OpenOnlineSearchAsync(string query, DesktopCatalogKind kind = DesktopCatalogKind.Song)
    {
        if (Local || string.IsNullOrWhiteSpace(query)) return;
        detailSubtitle = "";
        onlineQuery = query.Trim();
        EnsureOnlinePage();
        onlineHeading!.Text = $"“{onlineQuery}”的搜索结果";
        foreach (var (key, tab) in searchTabs)
        {
            tab.IsVisible = true;
            tab.Classes.Set("selected", key == kind);
        }
        Navigate(onlinePage!);
        await RunCatalogAsync(async ct =>
        {
            if (kind == DesktopCatalogKind.Song)
            {
                var result = await Host.Concept.FindDesktopSongs(onlineQuery, ct);
                ct.ThrowIfCancellationRequested();
                ShowSongs(result.Tracks, "search:" + onlineQuery, search: true);
            }
            else
            {
                var results = await Host.Concept.DesktopCall(
                    (gateway, token) => gateway.FindDesktopCollectionsAsync(kind, onlineQuery, token), ct);
                ct.ThrowIfCancellationRequested();
                ShowCollections(results);
            }
            Program.Log("R2_SEARCH_RESULT", new { kind = kind.ToString(), success = true });
        });
    }

    private void EnsureOnlinePage()
    {
        if (onlinePage != null) return;
        onlineSongsPage = SongPage("单曲结果", []);
        onlineDetailPage = SongPage("曲目列表", []);
        onlineHeading = new SelectableTextBlock { FontSize = 20, FontWeight = FontWeight.SemiBold };
        onlineStatus = new SelectableTextBlock { Classes = { "muted" }, Margin = new Thickness(0, 3, 0, 8) };
        onlineResults = new ContentControl();
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (kind, label) in new[]
        {
            (DesktopCatalogKind.Song, "单曲"), (DesktopCatalogKind.Playlist, "歌单"),
            (DesktopCatalogKind.Artist, "歌手"), (DesktopCatalogKind.Album, "专辑")
        })
        {
            var tab = new Button { Content = label, Classes = { "glass-pill" } };
            tab.Click += async (_, _) => await OpenOnlineSearchAsync(onlineQuery, kind);
            searchTabs.Add(kind, tab);
            tabs.Children.Add(tab);
        }
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 8 };
        layout.Children.Add(onlineHeading);
        Grid.SetRow(tabs, 1); layout.Children.Add(tabs);
        Grid.SetRow(onlineStatus, 2); layout.Children.Add(onlineStatus);
        Grid.SetRow(onlineResults, 3); layout.Children.Add(onlineResults);
        onlinePage = new R2Page("酷狗在线", layout);
    }

    private async Task RunCatalogAsync(Func<CancellationToken, Task> operation)
    {
        CancelCatalog();
        if (CurrentPage == onlinePage) Save();
        onlineListKey = null;
        onlineStatus!.Text = "正在加载…";
        onlineResults!.Content = null;
        if (!Host.Concept.Connected)
        {
            onlineStatus.Text = "登录酷狗后可以继续";
            return;
        }

        using var request = new CancellationTokenSource();
        catalogRequest = request;
        try
        {
            await operation(request.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!request.IsCancellationRequested)
                onlineStatus.Text = "酷狗暂未返回结果，可稍后重试";
            Program.Log("R2_CATALOG_FAILED", new { type = error.GetType().Name });
        }
        finally
        {
            if (ReferenceEquals(catalogRequest, request)) catalogRequest = null;
        }
    }

    private async Task OpenRecommendationsAsync()
    {
        PrepareDiscovery("每日推荐");
        await RunCatalogAsync(async ct =>
        {
            var rows = await Host.Concept.DesktopCall((g, t) => g.ReadDesktopRecommendationsAsync(t), ct);
            ct.ThrowIfCancellationRequested();
            ShowSongs(rows, "recommendations");
            Program.Log("R2_DAILY_RECOMMEND", new { count = rows.Length });
        });
    }

    private async Task OpenRanksAsync()
    {
        PrepareDiscovery("排行榜");
        await RunCatalogAsync(async ct =>
        {
            var rows = await Host.Concept.DesktopCall((g, t) => g.ReadDesktopRanksAsync(t), ct);
            ct.ThrowIfCancellationRequested();
            ShowCollections(rows);
            Program.Log("R2_RANKS", new { count = rows.Length });
        });
    }

    private void PrepareDiscovery(string title)
    {
        detailSubtitle = "";
        EnsureOnlinePage();
        foreach (var tab in searchTabs.Values) tab.IsVisible = false;
        onlineHeading!.Text = title;
        Navigate(onlinePage!);
    }

    private void ShowSongs(IEnumerable<CatalogTrack> tracks, string collectionId, bool detail = false, bool search = false)
    {
        var rows = tracks.Select(track => new TrackRow(new CoreTrack(-1,
            "kugou-concept:" + Host.Concept.AccountKey + ":" + track.Id, track.Title,
            track.Artist, track.Album, track.Seconds, track.Cover), this)).ToArray();
        var page = detail ? onlineDetailPage! : onlineSongsPage!;
        InteractionController.For(Lists[page]).IsOnlineSearch = search;
        Fill(page, rows);
        onlineStatus!.Text = rows.Length == 0 ? "" :
            detail && !string.IsNullOrWhiteSpace(detailSubtitle) ? $"{detailSubtitle}  ·  {rows.Length} 首曲目" : $"{rows.Length} 首曲目";
        onlineResults!.Content = page.View;
        onlineListKey = new SongViewKey(page, collectionId);
        if (CurrentPage == onlinePage) Restore();
    }

    private void ShowCollections(DesktopCatalogItem[] results)
    {
        var gallery = new WrapPanel { ItemWidth = 188 };
        foreach (var item in results.Where(item => item.Id is not ("" or "0")))
        {
            var model = new PlaylistCardModel(item.Title, item.Subtitle,
                Host.Concept.Cover(Host.Concept.AccountKey, item.Cover, 172),
                new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => OpenCollectionAsync(item)));
            gallery.Children.Add(new PlaylistCard { DataContext = model });
        }
        onlineStatus!.Text = gallery.Children.Count == 0 ? "未找到匹配内容" : $"{gallery.Children.Count} 项";
        onlineResults!.Content = new ScrollViewer { Content = gallery };
    }

    private async Task OpenCollectionAsync(DesktopCatalogItem item)
    {
        onlineHeading!.Text = item.Title;
        detailSubtitle = item.Subtitle;
        await RunCatalogAsync(async ct =>
        {
            var rows = await Host.Concept.DesktopCall((g, t) => g.ReadDesktopCollectionAsync(item, t), ct);
            ct.ThrowIfCancellationRequested();
            ShowSongs(rows, $"collection:{item.Kind}:{item.Id}", detail: true);
            if (item.Kind is DesktopCatalogKind.Playlist or DesktopCatalogKind.Album)
            {
                try
                {
                    var intro = await Host.Concept.DesktopCall((g, t) => item.Kind == DesktopCatalogKind.Album
                        ? g.ReadDesktopAlbumIntroAsync(item.Id, t) : g.ReadDesktopPlaylistIntroAsync(item.Id, t), ct);
                    if (!ct.IsCancellationRequested && !string.IsNullOrWhiteSpace(intro))
                        onlineStatus!.Text += Environment.NewLine + intro;
                }
                catch (Exception error) when (error is HttpRequestException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
                { Program.Log("R2_COLLECTION_INTRO_UNAVAILABLE", new { type = error.GetType().Name }); }
            }
        });
    }

    private void CancelCatalog()
    {
        var request = catalogRequest;
        catalogRequest = null;
        request?.Cancel();
    }

    internal void PauseOnlineWork() => CancelCatalog();
}
