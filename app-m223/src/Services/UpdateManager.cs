using System.Net;
using System.Net.Http;
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
    /// Repository owner/name. Replace "Owner" with the real GitHub account before
    /// publishing. Keeping it as a single constant makes that swap trivial.
    /// </summary>
    public const string GitHubRepo = "Owner/HuayuanMusic";

    /// <summary>The local application version, matching the csproj version.</summary>
    public static readonly Version CurrentVersion = new(1, 0, 0);

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        // GitHub requires a User-Agent; without it requests are rejected with 403.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HuayuanMusic/1.0.0 (+https://github.com/Owner/HuayuanMusic)");
        return client;
    }

    /// <summary>Latest release payload parsed from the GitHub API.</summary>
    public sealed record ReleaseInfo(string TagName, string Name, string Body, string? SetupUrl, Version Version)
    {
        public bool IsNewer => Version > CurrentVersion;
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
                        setupUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                            ? urlElement.GetString() : null;
                        break;
                    }
                }
            }

            var release = new ReleaseInfo(tag ?? "", string.IsNullOrWhiteSpace(name) ? tag ?? "" : name,
                body ?? "", setupUrl, version);
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

    /// <summary>
    /// Downloads the installer to a temp path, reporting progress on the UI thread.
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(string url, IProgress<double>? progress, CancellationToken cancellation = default)
    {
        var target = Path.Combine(Path.GetTempPath(), "HuayuanMusic-Setup-Update.exe");
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        await using var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
            received += read;
            if (total is > 0)
                progress?.Report(Math.Clamp((double)received / total.Value, 0, 1));
        }
        return target;
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
