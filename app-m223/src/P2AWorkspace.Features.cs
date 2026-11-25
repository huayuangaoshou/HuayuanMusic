using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview;

internal sealed partial class P2AWorkspace
{
    internal DesktopAccount? Profile { get; private set; }
    internal HashSet<string> DesktopFavorites { get; } = new(StringComparer.OrdinalIgnoreCase);
    // R12-4 红心点击节流：记录正在切换收藏的歌曲 key，防连点风暴（同歌重入直接忽略）。
    private readonly HashSet<string> favoriteInFlight = new(StringComparer.OrdinalIgnoreCase);

    internal async Task LoadDesktopProfileAsync(CancellationToken ct)
    {
        var key = AccountKey;
        try
        {
            var profile = await DesktopCall((gateway, token) => gateway.ReadDesktopAccountAsync(token), ct);
            if (key == AccountKey && Connected)
            {
                Profile = profile;
                Changed?.Invoke();
                Program.Log("DESKTOP_PROFILE_STATUS", new { profile = profile != null, avatar = profile?.Avatar != null });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Program.Log("DESKTOP_PROFILE_UNAVAILABLE", new { type = error.GetType().Name });
        }
    }

    internal Task<T> DesktopCall<T>(Func<DirectSdkGateway, CancellationToken, Task<T>> operation, CancellationToken ct) =>
        Task.Run(() => core.Kugou.UseDesktopCatalogAsync(gateway => operation(gateway, ct), ct), ct);

    internal Task<SearchPage> FindDesktopSongs(string query, CancellationToken ct) =>
        Task.Run(() => core.Kugou.SearchAsync(query, size: 30, ct: ct), ct);

    internal async Task LoadDesktopFavoritesAsync(CancellationToken ct)
    {
        var key = AccountKey;
        if (key == null) return;
        try
        {
            var id = await DesktopCall((gateway, token) => gateway.ReadDesktopFavoritesIdAsync(token), ct);
            if (key != AccountKey || ct.IsCancellationRequested) return;
            var favorite = Snapshot?.Playlists.SingleOrDefault(list => list.ProviderPlaylistId == id);
            if (favorite == null) return;
            DesktopFavorites.Clear();
            DesktopFavorites.UnionWith(favorite.Tracks.Select(track => track.ProviderTrackId));
            Changed?.Invoke();
            Program.Log("DESKTOP_FAVORITES_READ", new { count = DesktopFavorites.Count });
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Program.Log("DESKTOP_FAVORITES_UNAVAILABLE", new { type = error.GetType().Name });
        }
    }

    internal async Task ToggleDesktopFavoriteAsync(string trackKey, CancellationToken ct)
    {
        var key = AccountKey;
        var id = trackKey[(trackKey.LastIndexOf(':') + 1)..];
        if (key == null || !trackKey.StartsWith("kugou-concept:" + key + ":", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("歌曲不属于当前酷狗账号");
        // R12-4 节流：同歌收藏切换进行中，直接忽略（防连点风暴）。
        lock (favoriteInFlight)
        {
            if (!favoriteInFlight.Add(id)) return;
        }
        try
        {
            var liked = await DesktopCall((gateway, token) => gateway.ToggleDesktopFavoriteAsync(id, token), ct);
            if (key != AccountKey) return;
            if (liked) DesktopFavorites.Add(id); else DesktopFavorites.Remove(id);
            Changed?.Invoke();
        }
        finally
        {
            lock (favoriteInFlight) favoriteInFlight.Remove(id);
        }
    }

    internal bool IsDesktopFavorite(string trackKey) =>
        AccountKey is { } key && trackKey.StartsWith("kugou-concept:" + key + ":", StringComparison.Ordinal) &&
        DesktopFavorites.Contains(trackKey[(trackKey.LastIndexOf(':') + 1)..]);
}
