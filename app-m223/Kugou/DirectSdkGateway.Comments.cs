using System.Text.Json;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Clients;

namespace HuayuanMusic.Provider.Kugou;

public sealed record DesktopComment(long Id, string Author, string Time, string Body, string? Avatar, int Likes);
public sealed record DesktopCommentPage(DesktopComment[] Hot, DesktopComment[] Latest, int Total, bool More, bool PlatformHot);

public sealed partial class DirectSdkGateway
{
    public Task<DesktopCommentPage?> ReadDesktopCommentsAsync(string trackId, int page, CancellationToken ct) =>
        DesktopRead<DesktopCommentPage?>(async sdk =>
        {
            if (page < 1 || !catalog.TryGetValue(trackId, out var track)) return null;
            var mixId = track.AlbumAudioId;
            if (mixId <= 0)
            {
                var metadata = await sdk.Songs.GetPrivilegeLiteAsync(track.Id, Number(track.AlbumId)).ConfigureAwait(false);
                var exact = metadata?.Where(item => item.Hash.Equals(track.Id, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(item => item.AlbumAudioId > 0);
                mixId = exact?.AlbumAudioId ?? 0;
                if (mixId <= 0) return null;
                catalog[trackId] = track with { AlbumAudioId = mixId };
            }

            const int pageSize = 20;
            var raw = await sdk.Comments.GetMusicCommentsRawAsync(Number(mixId), page, pageSize).ConfigureAwait(false);
            var response = CommentClient.ParseMusicComments(raw);
            if (response == null) return null;
            if (response.Status.HasValue && response.Status != 1)
                throw new InvalidDataException("酷狗评论查询未成功");
            var latest = ConvertComments(response.Comments ?? []);
            DesktopComment[] hot = [];
            if (page == 1)
            {
                hot = ReadHot(raw);
                var categories = response.ClassifyList ?? [];
                var category = categories.FirstOrDefault(item => item.Label.Contains("热评") || item.Label.Contains("精彩"))
                    ?? categories.FirstOrDefault();
                if (hot.Length == 0 && latest.Length > 0 && category != null)
                {
                    try
                    {
                        var sorted = await sdk.Comments.GetMusicCommentClassifyAsync(Number(mixId), Number(category.Id), pageSize: 8, sort: 2)
                            .ConfigureAwait(false);
                        var popular = CommentClient.ParseMusicComments(sorted);
                        if (popular?.Status == 1) hot = ConvertComments(popular.Comments ?? []);
                    }
                    catch (Exception error) when (error is HttpRequestException or JsonException && !ct.IsCancellationRequested) { }
                }
            }
            bool official = hot.Length > 0;
            if (page == 1 && !official)
                hot = latest.Where(item => item.Likes > 0).OrderByDescending(item => item.Likes).Take(4).ToArray();
            var total = Math.Max(response.Count, response.CombineCount);
            var more = latest.Length > 0 && (response.MaxPage > page || total > page * pageSize);
            return new(hot.Take(8).ToArray(), latest, total, more, official);
        }, ct);

    private static DesktopComment[] ReadHot(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object) return [];
        if (raw.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object) raw = data;
        foreach (var key in new[] { "hot_list", "hot_comment_list", "hot_comments", "top_list" })
        {
            if (!raw.TryGetProperty(key, out var items) || items.ValueKind != JsonValueKind.Array) continue;
            return ConvertComments(items.EnumerateArray().Take(8)
                .Select(CommentClient.ParseMusicComment)
                .OfType<MusicCommentItem>());
        }
        return [];
    }

    private static DesktopComment[] ConvertComments(IEnumerable<MusicCommentItem> items) => items
        .Where(item => !string.IsNullOrWhiteSpace(item.Content))
        .Select(item => new DesktopComment(item.Id, item.UserName, item.AddTime, item.Content,
            Provider.Contracts.ArtworkUrl.Normalize(item.UserPic), Math.Max(item.Like?.Count ?? 0, item.Like?.LikeNum ?? 0)))
        .ToArray();

}
