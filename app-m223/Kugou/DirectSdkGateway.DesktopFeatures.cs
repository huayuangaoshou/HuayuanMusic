using System.Globalization;
using System.Text.Json;
using HuayuanMusic.Provider.Contracts;
using KuGou.Net.Abstractions;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace HuayuanMusic.Provider.Kugou;

public enum DesktopCatalogKind { Song, Playlist, Artist, Album, Rank }
public sealed record DesktopCatalogItem(DesktopCatalogKind Kind, string Id, string Title, string Subtitle, string? Cover);
public sealed record DesktopAccount(string Name, string? Avatar);
public sealed record DesktopLyric(string Text, string Format);
public sealed record DesktopSuggestion(string Title, string Artist, string Query)
{
    public override string ToString() => Artist.Length > 0 ? Title + " · " + Artist : Title;
}

public sealed partial class DirectSdkGateway
{
    private sealed class DesktopClients(IKgTransport transport, KgSessionManager account)
    {
        internal SearchClient Search { get; } = new(new RawSearchApi(transport), account);
        internal UserClient User { get; } = new(new RawUserApi(transport), account);
        internal PlaylistClient Playlists { get; } = new(new RawPlaylistApi(transport, NullLogger<RawPlaylistApi>.Instance), account);
        internal AlbumClient Albums { get; } = new(new RawAlbumApi(transport));
        internal ArtistClient Artists { get; } = new(new RawArtistApi(transport, account), new RawSearchApi(transport), account);
        internal RecommendClient Recommend { get; } = new(new RawDiscoveryApi(transport), account);
        internal RankClient Ranks { get; } = new(new RawRankApi(transport));
        internal LyricClient Lyrics { get; } = new(new RawLyricApi(transport));
        internal CommentClient Comments { get; } = new(new RawCommentApi(transport));
        internal SongClient Songs { get; } = new(new RawSongApi(transport, account));
    }

    // Metadata routes use the live account over HTTPS. Playback permission and media
    // still go through the existing grant and CDN implementation.
    private Task<T> DesktopRead<T>(Func<DesktopClients, Task<T>> operation, CancellationToken ct) => Serial(async () =>
    {
        if (!desktop) throw new InvalidOperationException("Desktop session required");
        DemandLogin();
        using var client = new HttpClient(new KgSignatureHandler(session) { InnerHandler = new DesktopSdkWire(ct) })
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        return await operation(new DesktopClients(new KgHttpTransport(client, 1), session)).ConfigureAwait(false);
    }, ct);

    private sealed class DesktopSdkWire(CancellationToken lifetime) : HttpMessageHandler
    {
        private static readonly HttpMessageInvoker Sender = new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false, Credentials = null
        });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri is not { Scheme: "https", Port: 443, UserInfo.Length: 0, Fragment.Length: 0 } uri ||
                request.Headers.Contains("Cookie") || request.Headers.Contains("Authorization") ||
                !Allowed(uri.IdnHost, uri.AbsolutePath, request.Method))
                throw new UnauthorizedAccessException("DLC metadata route refused");

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
            var response = await Sender.SendAsync(request, cancellation.Token).ConfigureAwait(false);
            try
            {
                await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, cancellation.Token).ConfigureAwait(false);
                return response;
            }
            catch { response.Dispose(); throw; }
        }

        private static bool Allowed(string host, string path, HttpMethod method) => host switch
        {
            "lyrics.kugou.com" => method == HttpMethod.Get && path is "/v1/search" or "/search" or "/download",
            "openapi.kugou.com" => method == HttpMethod.Post && path == "/kmr/v1/audio_group/author",
            "gateway.kugou.com" => method == HttpMethod.Get
                ? path is "/v2/getSearchTip" or "/v3/search/song" or "/v1/search/special" or "/v1/search/author"
                    or "/v1/search/album" or "/pubsongs/v2/get_other_list_file_nofilt" or "/ocean/v6/rank/list"
                : method == HttpMethod.Post && path is "/v3/get_my_info" or "/everyday_song_recommend"
                    or "/openapi/kmr/v2/rank/audio" or "/v1/album_audio/lite" or "/v2/get_res_privilege/lite"
                    or "/v7/get_all_list" or "/cloudlist.service/v6/add_song" or "/v4/delete_songs"
                    or "/mcomment/v1/cmtlist" or "/mcomment/v1/cmt_classify_list" or "/kmr/v2/albums",
            _ => false
        };
    }

    public Task<DesktopAccount?> ReadDesktopAccountAsync(CancellationToken ct) => DesktopRead<DesktopAccount?>(async sdk =>
    {
        var profile = await sdk.User.GetUserInfoAsync().ConfigureAwait(false);
        return profile is null || string.IsNullOrWhiteSpace(profile.Name) ? null : new(profile.Name, ArtworkUrl.Normalize(profile.Pic));
    }, ct);

    public Task<string?> ReadDesktopPlaylistIntroAsync(string id, CancellationToken ct) => Serial(async () =>
    {
        if (!desktop || id.Length is <1 or >128 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) return null;
        DemandLogin();
        using var route = details.Begin([id], ct);
        var info = await playlist.GetInfoAsync(id).WaitAsync(ct).ConfigureAwait(false);
        return info?.Intro;
    }, ct);

    public Task<string?> ReadDesktopAlbumIntroAsync(string id, CancellationToken ct) => DesktopRead(async sdk =>
    {
        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var albumId) || albumId <= 0) return null;
        var root = await sdk.Albums.GetDetailRawAsync(id).ConfigureAwait(false);
        return FindIntro(root, 0);

        static string? FindIntro(JsonElement node, int depth)
        {
            if (depth >= 6) return null;
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("intro", out var intro) && intro.ValueKind == JsonValueKind.String)
                    return intro.GetString();
                foreach (var name in new[] { "data", "albums", "list", "info" })
                    if (node.TryGetProperty(name, out var nested) && FindIntro(nested, depth + 1) is { Length: >0 } found)
                        return found;
            }
            if (node.ValueKind == JsonValueKind.Array)
                foreach (var entry in node.EnumerateArray().Take(3))
                    if (FindIntro(entry, depth + 1) is { Length: >0 } found) return found;
            return null;
        }
    }, ct);

    public Task<string?> ReadDesktopFavoritesIdAsync(CancellationToken ct) => DesktopRead(async sdk =>
        (await FindDesktopFavoritesAsync(sdk).ConfigureAwait(false))?.GlobalId, ct);

    private async Task<UserPlaylistItem?> FindDesktopFavoritesAsync(DesktopClients sdk)
    {
        var reply = await sdk.User.GetPlaylistsAsync(pageSize: 50).ConfigureAwait(false);
        if (reply == null || Number(reply.UserId) != session.Session.UserId) return null;
        var official = reply.Playlists.Where(item => item.IsDefault == 2).ToArray();
        return official.Length == 1 && official[0].ListId > 0 && !string.IsNullOrWhiteSpace(official[0].GlobalId)
            ? official[0] : null;
    }

    public Task<DesktopSuggestion[]> SuggestDesktopAsync(string query, CancellationToken ct) =>
        DesktopRead(async sdk =>
        {
            var raw = await sdk.Search.SearchSuggestRawAsync(query, albumTipCount: 1, correctTipCount: 2,
                mvTipCount: 1, musicTipCount: 8).ConfigureAwait(false);
            var result = new List<DesktopSuggestion>();
            ReadSuggestionTerms(raw, result, 0);
            return DistinctSuggestions(result.OrderByDescending(item => item.Artist.Length > 0));
        }, ct);

    private static DesktopSuggestion[] DistinctSuggestions(IEnumerable<DesktopSuggestion> terms) =>
        terms.Where(term => !string.IsNullOrWhiteSpace(term.Title) && term.Query.Length is >0 and <=120)
            .DistinctBy(term => term.Query, StringComparer.OrdinalIgnoreCase).Take(8).ToArray();

    private static void ReadSuggestionTerms(JsonElement node, List<DesktopSuggestion> terms, int depth)
    {
        if (depth > 6 || terms.Count >= 24) return;
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray().Take(18))
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text) terms.Add(new(text, "", text));
                else ReadSuggestionTerms(item, terms, depth + 1);
            }
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            string title = "", artist = "", hint = "";
            foreach (var item in node.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.String) continue;
                var text = item.Value.GetString()!.Trim();
                switch (item.Name.ToLowerInvariant())
                {
                    case "songname": case "song_name": case "audioname": case "title": title = text; break;
                    case "singername": case "singer_name": case "artist": case "authorname": case "author_name": artist = text; break;
                    case "keyword": case "name": case "word": case "tip": case "suggestion": case "hintinfo": case "text": hint = text; break;
                }
            }
            if (title.Length > 0) terms.Add(new(title, artist, title));
            else if (hint.Length > 0) terms.Add(new(hint, artist, hint));
            foreach (var item in node.EnumerateObject())
                if (item.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    ReadSuggestionTerms(item.Value, terms, depth + 1);
        }
    }

    public Task<DesktopCatalogItem[]> FindDesktopCollectionsAsync(DesktopCatalogKind kind, string query, CancellationToken ct) =>
        DesktopRead(async sdk => kind switch
        {
            DesktopCatalogKind.Playlist => (await sdk.Search.SearchSpecialAsync(query).ConfigureAwait(false) ?? [])
                .Select(x => Item(kind, string.IsNullOrWhiteSpace(x.GlobalId) ? Number(x.ListId) : x.GlobalId, x.Name, x.CreatorName, x.Cover)).ToArray(),
            DesktopCatalogKind.Album => (await sdk.Search.SearchAlbumAsync(query).ConfigureAwait(false) ?? [])
                .Select(x => Item(kind, Number(x.AlbumId), x.Name, x.SingerName, x.Cover)).ToArray(),
            DesktopCatalogKind.Artist => (await sdk.Search.SearchAuthorAsync(query).ConfigureAwait(false) ?? [])
                .Select(x => Item(kind, Number(x.AuthorId), x.Name, $"{x.AudioCount} 首", x.Cover)).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        }, ct);

    public Task<DesktopCatalogItem[]> ReadDesktopRanksAsync(CancellationToken ct) => DesktopRead(async sdk =>
        (await sdk.Ranks.GetAllRanksAsync(withSong: 0).ConfigureAwait(false))?.Info
        .Select(x => Item(DesktopCatalogKind.Rank, Number(x.FileId), x.Name, "酷狗榜单", x.Cover)).ToArray() ?? [], ct);

    public Task<CatalogTrack[]> ReadDesktopRecommendationsAsync(CancellationToken ct) => DesktopRead(async sdk =>
    {
        var reply = await sdk.Recommend.GetRecommendedSongsAsync().ConfigureAwait(false);
        return Remember((reply?.Songs ?? []).Select(x =>
            Track(x.Hash, x.Name, x.SingerName, x.AlbumName, 0, ParseId(x.AlbumId), x.AudioId, x.SizableCover)));
    }, ct);

    public Task<CatalogTrack[]> ReadDesktopCollectionAsync(DesktopCatalogItem item, CancellationToken ct) => DesktopRead(async sdk =>
    {
        IEnumerable<CatalogTrack> entries = item.Kind switch
        {
            DesktopCatalogKind.Playlist => (await sdk.Playlists.GetSongsAsync(item.Id, pageSize: 50).ConfigureAwait(false))?.Songs
                .Select(x => Track(x.Hash, x.Name, string.Join(" / ", x.Singers.Select(a => a.Name)), x.Album?.Name ?? "",
                    x.DurationMs / 1000, ParseId(x.AlbumId), 0, x.Cover ?? x.Album?.Cover)) ?? [],
            DesktopCatalogKind.Album => (await sdk.Albums.GetSongsAsync(item.Id, pageSize: 50).ConfigureAwait(false) ?? [])
                .Select(x => Track(x.Hash, x.Name, x.Singer, x.AlbumInfo.AlbumName, x.DurationMs / 1000, ParseId(x.AlbumId), 0, x.Cover)),
            DesktopCatalogKind.Artist => (await sdk.Artists.GetAudiosAsync(item.Id, pageSize: 50).ConfigureAwait(false))?.Songs
                .Select(x => Track(x.Hash, x.Name, x.SingerName, x.AlbumName, (int)(x.Duration / 1000), x.AlbumId,
                    x.AlbumAudioId, x.TransParam?.UnionCover)) ?? [],
            DesktopCatalogKind.Rank => (await sdk.Ranks.GetRankSongsAsync(int.Parse(item.Id, CultureInfo.InvariantCulture), pageSize: 50).ConfigureAwait(false))?.RankSongLists
                .Select(x => Track(x.Hash, x.Name, string.Join(" / ", x.Authors.Select(a => a.AuthorName)), "",
                    x.DurationMs / 1000, x.AlbumId, 0, null)) ?? [],
            _ => []
        };
        return Remember(entries);
    }, ct);

    public Task<DesktopLyric?> ReadDesktopLyricAsync(string trackId, CancellationToken ct) => DesktopRead<DesktopLyric?>(async sdk =>
    {
        if (!catalog.TryGetValue(trackId, out var track)) return null;
        var reply = await sdk.Lyrics.SearchLyricAsync(track.Id,
            track.AlbumAudioId > 0 ? Number(track.AlbumAudioId) : null, track.Title, "no").ConfigureAwait(false);
        if (reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("candidates", out var list) ||
            list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0) return null;
        var first = list[0];
        if (!first.TryGetProperty("id", out var id) || !first.TryGetProperty("accesskey", out var access)) return null;
        var format = first.TryGetProperty("fmt", out var value) && value.ToString() == "lrc" ? "lrc" : "krc";
        var decoded = await sdk.Lyrics.GetLyricAsync(id.ToString(), access.ToString(), format).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(decoded.DecodedContent) ? null : new(decoded.DecodedContent, format);
    }, ct);

    public Task<string> ReadDesktopQualityAsync(CatalogTrack track, string preferred, CancellationToken ct) => DesktopRead(async sdk =>
    {
        var choices = await sdk.Songs.GetPrivilegeLiteAsync(track.Id, Number(track.AlbumId)).ConfigureAwait(false);
        var requested = QualityRank(preferred);
        var best = 0;
        var pending = new Stack<PrivilegeLiteData>(choices ?? []);
        while (pending.TryPop(out var item))
        {
            var rank = QualityRank(item.Quality);
            if (rank <= requested) best = Math.Max(best, rank);
            foreach (var related in item.RelateGoods) pending.Push(related);
        }
        return best == 2 ? AudioQuality.Lossless : best == 1 ? AudioQuality.High : "standard";
    }, ct);

    private static int QualityRank(string? quality) => AudioQuality.Normalize(quality) switch
    {
        AudioQuality.Lossless or "lossless" or "sq" => 2, AudioQuality.High or "hq" => 1, _ => 0
    };

    public Task<bool> ToggleDesktopFavoriteAsync(string trackId, CancellationToken ct) => DesktopRead(async sdk =>
    {
        if (!catalog.TryGetValue(trackId, out var track))
            throw new InvalidOperationException("Please refresh this song before favoriting");
        var list = await FindDesktopFavoritesAsync(sdk).ConfigureAwait(false);
        if (list == null)
            throw new InvalidOperationException("Could not verify the account's favorites playlist");

        const int pageSize = 50;
        for (int page = 1; ; page++)
        {
            ct.ThrowIfCancellationRequested();
            var pageData = await sdk.Playlists.GetSongsAsync(list.GlobalId, page, pageSize).ConfigureAwait(false);
            if (pageData == null) throw new InvalidDataException("Favorites could not be read");
            var matches = pageData.Songs.Where(item => item.Hash.Equals(track.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 0)
            {
                if (matches.Any(item => item.FileId <= 0)) throw new InvalidDataException("Missing favorite member identity");
                var removed = await sdk.Playlists.RemoveSongsAsync(Number(list.ListId), matches.Select(item => (long)item.FileId)).ConfigureAwait(false);
                if (removed?.Status != 1) throw new InvalidOperationException("Server did not remove favorite");
                return false;
            }
            if (page * pageSize >= pageData.Count) break;
            if (page >= 100 || pageData.Songs.Count == 0) throw new InvalidDataException("Incomplete favorites listing");
        }
        var added = await sdk.Playlists.AddSongsAsync(Number(list.ListId),
            [(track.Title, track.Id, Number(track.AlbumId), "0")]).ConfigureAwait(false);
        if (added?.Status != 1) throw new InvalidOperationException("Server did not add favorite");
        return true;
    }, ct);

    private CatalogTrack[] Remember(IEnumerable<CatalogTrack> entries)
    {
        var valid = entries.Where(track => track.Id.Length == 32 && track.Id.All(Uri.IsHexDigit) &&
            !string.IsNullOrWhiteSpace(track.Title)).DistinctBy(track => track.Id).ToArray();
        foreach (var track in valid) catalog[track.Id] = track;
        return valid;
    }

    public Task RestoreDesktopCatalogAsync(IEnumerable<CatalogTrack> entries, CancellationToken ct) => Serial(() =>
    {
        if (!desktop) throw new InvalidOperationException("Desktop session required");
        DemandLogin();
        Remember(entries.Where(entry => !catalog.ContainsKey(entry.Id)).Select(entry =>
            Track(entry.Id, entry.Title, entry.Artist, entry.Album, entry.Seconds, entry.AlbumId, entry.AlbumAudioId, entry.Cover)));
        return Task.FromResult(true);
    }, ct);

    private static CatalogTrack Track(string? hash, string title, string artist, string album, int seconds, long albumId, long audioId, string? cover) =>
        new((hash ?? "").ToLowerInvariant(), title, artist, album, Math.Max(0, seconds), albumId, audioId, ArtworkUrl.Normalize(cover));
    private static DesktopCatalogItem Item(DesktopCatalogKind kind, string id, string title, string subtitle, string? cover) =>
        new(kind, id, title, subtitle, ArtworkUrl.Normalize(cover));
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static long ParseId(string? value) => long.TryParse(value, out var id) ? id : 0;
}
