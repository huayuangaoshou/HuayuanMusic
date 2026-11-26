using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HuayuanPreview.Services;

/// <summary>
/// Zero-dependency update checker for GitHub Releases. Uses only the built-in
/// <see cref="HttpClient"/> and <see cref="System.Text.Json"/> — no third-party
/// NuGet packages — so it stays fully offline-buildable.
/// </summary>
public static class UpdateManager
{
    /// <summary>
    /// Repository owner/name used for release checks.
    /// </summary>
    public const string GitHubRepo = "huayuangaoshou/HuayuanMusic";

    /// <summary>The application's assembly version normalized to a three-part release version.</summary>
    public static readonly Version CurrentVersion = NormalizeVersion(
        typeof(UpdateManager).Assembly.GetName().Version!);

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

    private static readonly HttpClient Client = CreateClient();
    private static readonly HttpClient DownloadClient = CreateClient(Timeout.InfiniteTimeSpan);
    private static readonly TimeSpan FirstByteTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    // Third-party download mirrors. Replace a domain here when it stops serving release assets.
    private static readonly string[] DownloadMirrors = ["https://ghfast.top/", "https://gh-proxy.com/", "https://ghproxy.net/"];

    private static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(20)
        };
        // GitHub requires a User-Agent; without it requests are rejected with 403.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"HuayuanMusic/{CurrentVersion} (+https://github.com/{GitHubRepo})");
        return client;
    }

    /// <summary>Latest release payload parsed from the GitHub API.</summary>
    public sealed record ReleaseInfo(string TagName, string Name, string Body, string? SetupUrl, Version Version)
    {
        public string? SetupDigest { get; init; }
        public long? SetupSize { get; init; }
        public bool IsNewer => NormalizeVersion(Version) > CurrentVersion;
    }

    /// <summary>Result of a lookup. <see cref="Release"/> is null when nothing actionable was found.</summary>
    public sealed record CheckResult(ReleaseInfo? Release, string Message);

    /// <summary>
    /// Queries the latest GitHub release. Any network failure, 404, or an
    /// unconfigured repository is silently swallowed into a friendly message so
    /// listening is never interrupted.
    /// </summary>
    public static async Task<CheckResult> CheckAsync(CancellationToken cancellation = default)
    {
        var url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new CheckResult(null, "尚未发布线上版本，敬请期待");

            if (!response.IsSuccessStatusCode)
                return new CheckResult(null, "暂时无法连接更新服务器");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation).ConfigureAwait(false);

            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var name = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
            var body = root.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String
                ? bodyElement.GetString()
                : "";

            var version = ParseVersion(tag);
            if (version == null)
                return new CheckResult(null, "线上版本信息无法识别");

            // Prefer a "-Setup" .exe asset for the one-click installer.
            string? setupUrl = null;
            string? setupDigest = null;
            long? setupSize = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var assetName = asset.TryGetProperty("name", out var assetNameElement)
                        ? assetNameElement.GetString() : null;
                    if (assetName == null) continue;
                    if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        && assetName.Contains("-Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        var urlValue = ReadString(asset, "browser_download_url");
                        if (!IsOfficialInstallerUrl(urlValue)) continue;

                        setupUrl = urlValue;
                        setupDigest = ReadString(asset, "digest");
                        if (asset.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number
                            && size.TryGetInt64(out var bytes) && bytes > 0)
                            setupSize = bytes;
                        break;
                    }
                }
            }

            var release = new ReleaseInfo(tag ?? "", string.IsNullOrWhiteSpace(name) ? tag ?? "" : name,
                body ?? "", setupUrl, version) { SetupDigest = setupDigest, SetupSize = setupSize };
            return new CheckResult(release, release.IsNewer ? "发现新版本" : "当前已是最新版本");
        }
        catch (HttpRequestException)
        {
            return new CheckResult(null, "网络不可用，无法检查更新");
        }
        catch (TaskCanceledException)
        {
            return new CheckResult(null, "检查更新超时");
        }
        catch (JsonException)
        {
            return new CheckResult(null, "更新信息解析失败");
        }
    }

    public enum DownloadStage { Direct, Mirror, Verifying }

    public readonly record struct DownloadProgress(DownloadStage Stage, double? Fraction = null);

    public static Task<string> DownloadInstallerAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress,
        CancellationToken cancellation = default) =>
        DownloadInstallerAsync(release, progress, DownloadClient, FirstByteTimeout, StallTimeout, cancellation);

    internal static string? GetManualDownloadUrl(ReleaseInfo release) =>
        IsOfficialInstallerUrl(release.SetupUrl) ? DownloadMirrors[0] + release.SetupUrl : null;

    private static bool IsOfficialInstallerUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) &&
        url.Scheme == Uri.UriSchemeHttps && url.Host == "github.com" && url.IsDefaultPort &&
        url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0 &&
        url.AbsolutePath.StartsWith($"/{GitHubRepo}/releases/download/", StringComparison.OrdinalIgnoreCase) &&
        url.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static byte[]? ReadDigest(string? digest)
    {
        const string prefix = "sha256:";
        if (digest?.Length != prefix.Length + 64 || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        try { return Convert.FromHexString(digest.AsSpan(prefix.Length)); }
        catch (FormatException) { return null; }
    }

    internal static async Task<string> DownloadInstallerAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress,
        HttpClient client, TimeSpan firstByteTimeout, TimeSpan stallTimeout, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var expected = ReadDigest(release.SetupDigest);
        if (!IsOfficialInstallerUrl(release.SetupUrl) || expected == null)
        {
            Program.Log("UPDATE_SOURCE", new { source = "github.com", result = "missing_installer_or_digest" });
            throw new InvalidDataException("Missing trusted installer URL or SHA-256.");
        }

        string[] sources = [release.SetupUrl!, .. DownloadMirrors.Select(prefix => prefix + release.SetupUrl)];
        string target = Path.Combine(Path.GetTempPath(), $"HuayuanMusic-Setup-{Guid.NewGuid():N}.exe");
        string partial = target + ".download";
        for (var i = 0; i < sources.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var source = new Uri(sources[i]);
            var stage = i == 0 ? DownloadStage.Direct : DownloadStage.Mirror;
            var failure = "download_failed";
            bool receiving = false;
            bool verified = false;
            Program.Log("UPDATE_SOURCE", new { source = source.Host, result = "attempt" });
            progress?.Report(new(stage));
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                deadline.CancelAfter(firstByteTimeout);
                using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var total = release.SetupSize ?? response.Content.Headers.ContentLength;
                    var buffer = new byte[81920];
                    long received = 0;
                    while (await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false) is > 0 and var read)
                    {
                        receiving = true;
                        received += read;
                        if (release.SetupSize is > 0 && received > release.SetupSize.Value)
                            throw new InvalidDataException("Installer exceeds the release asset size.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                        deadline.CancelAfter(stallTimeout);
                        if (total is > 0)
                            progress?.Report(new(stage, Math.Clamp((double)received / total.Value, 0, 1)));
                    }
                }

                progress?.Report(new(DownloadStage.Verifying, 1));
                byte[] actual;
                await using (var file = File.OpenRead(partial))
                    actual = await SHA256.HashDataAsync(file, cancellation).ConfigureAwait(false);
                if (CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    cancellation.ThrowIfCancellationRequested();
                    File.Move(partial, target);
                    verified = true;
                    Program.Log("UPDATE_SOURCE", new { source = source.Host, result = "verified" });
                    return target;
                }

                failure = "hash_mismatch";
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                failure = receiving ? "stall_timeout" : "first_byte_timeout";
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException)
            {
                failure = error.GetType().Name;
            }
            finally
            {
                if (!verified) File.Delete(partial);
            }

            cancellation.ThrowIfCancellationRequested();
            Program.Log("UPDATE_SOURCE", new { source = source.Host, result = failure });
            if (i + 1 < sources.Length)
                Program.Log("UPDATE_SOURCE", new { source = source.Host, result = "switch", next = new Uri(sources[i + 1]).Host });
        }

        throw new IOException("No download source delivered a verified installer.");
    }

    /// <summary>
    /// Parses a leading semantic version from a tag such as "v1.2.3" or "1.2.3".
    /// Returns null when no numeric version can be recognised.
    /// </summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var match = Regex.Match(tag, @"(\d+)\.(\d+)\.(\d+)");
        if (!match.Success) return null;
        try
        {
            return new Version(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value));
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
