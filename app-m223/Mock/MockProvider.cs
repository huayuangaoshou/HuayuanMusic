using HuayuanMusic.Provider.Contracts;

namespace HuayuanMusic.Provider.Mock;

/// <summary>Synthetic, deterministic, zero-network platform. No user account required.</summary>
public sealed class MockProvider : IProviderModule, IProviderPlaylistReader
{
    private bool disposed;
    public string ProviderId => "mock-music";
    public ProviderCapability Capabilities => ProviderCapability.PlaylistRead;
    public IProviderPlaylistReader PlaylistReader => this;
    public bool FailNextRead { get; set; }
    public int ReadCount { get; private set; }
    public bool IsDisposed => disposed;

    public Task<IReadOnlyList<ProviderPlaylist>> ReadAsync(
        string accountAlias, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(accountAlias, "isolated-demo", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Only the non-secret isolated demo account is allowed.");
        ReadCount++;
        if (FailNextRead)
        {
            FailNextRead = false;
            throw new IOException("Synthetic provider outage.");
        }
        IReadOnlyList<ProviderPlaylist> lists =
        [
            new ProviderPlaylist(
                ProviderId, "playlist-01", "模拟平台的歌单",
                [
                    new ProviderTrackRef(ProviderId, "track-01", "Mock Song A", "Mock Artist", "Mock Album"),
                    new ProviderTrackRef(ProviderId, "track-02", "Mock Song B", "Mock Artist", "Mock Album")
                ])
        ];
        return Task.FromResult(lists);
    }

    public void Dispose() => disposed = true;
}
