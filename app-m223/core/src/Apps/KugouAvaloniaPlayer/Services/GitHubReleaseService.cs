using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KugouAvaloniaPlayer.Services;

public sealed record GitHubReleaseInfo(string Title, string PublishedAt, string Summary, string Url);

public interface IGitHubReleaseService
{
    Task<IReadOnlyList<GitHubReleaseInfo>> GetRecentReleasesAsync(int count, CancellationToken cancellationToken = default);
}

/// <summary>
/// P1 safety gate: don't fetch or present KA Music releases as private fork updates.
/// </summary>
public sealed class GitHubReleaseService : IGitHubReleaseService
{
    public Task<IReadOnlyList<GitHubReleaseInfo>> GetRecentReleasesAsync(int count, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<GitHubReleaseInfo>>(System.Array.Empty<GitHubReleaseInfo>());
}
