using System.Net;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview;

internal sealed class M223Media : HttpMessageHandler
{
    internal const string Address = "https://audio.kugoucdn.com/m223-synthetic.wav";
    internal static string? Fault => P2AWorkspace.M223Normal
        ? Environment.GetEnvironmentVariable("M223_OFFLINE_FAULT") : null;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.Method != HttpMethod.Get || request.RequestUri != new Uri(Address))
            throw new UnauthorizedAccessException("Unknown synthetic media request");
        Program.Log("M223_MEDIA_FIXTURE", new { result = Fault ?? "audio" });
        if (Fault == "decode")
        {
            var content = new ByteArrayContent(new byte[2048]);
            content.Headers.ContentType = new("audio/wav");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
        return Task.FromResult(Fault == "media"
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : F2SyntheticMedia.AudioResponse());
    }
}

internal sealed partial class P2AWorkspace
{
    internal bool M223CanQueue(string key) => M223Track(key) != null;

    CatalogTrack? M223Track(string key)
    {
        if (!M223Normal || (Program.Desktop ? !Connected : !M210Approved) || transitioning || AccountKey is not { } account)
            return null;
        if (!key.StartsWith("kugou-concept:" + account + ":", StringComparison.Ordinal))
            return null;
        return core.Kugou.CatalogTrackForPlaylist(key[(key.LastIndexOf(':') + 1)..]);
    }

    internal Task<string> SaveDesktopSongAsync(string key, string quality, string folder, Action<int> progress,
        CancellationToken cancellation)
    {
        if (!Program.Desktop || M223Track(key) is not { } track)
            throw new UnauthorizedAccessException("当前无法下载此歌曲");
        return Task.Run(() => DesktopSongDownload.SaveAsync(core.Kugou, track, quality, folder, progress, cancellation),
            cancellation);
    }

    internal async Task<int> M223AdmitAsync(string key, bool publish = true)
    {
        var track = M223Track(key) ?? throw new UnauthorizedAccessException("当前会话未授权这首平台曲目");
        return await core.AdmitKugouAsync(track, "standard", publish: publish);
    }
}
