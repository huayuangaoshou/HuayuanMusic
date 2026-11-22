using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HuayuanMusic.Provider.Contracts;

/// <summary>Capability disclosure, NOT an authorization grant.</summary>
[Flags]
public enum ProviderCapability
{
    None = 0,
    PlaylistRead = 1,
    PlaylistWrite = 2,
    MediaResolve = 4
}

/// <summary>Identity must include provider; a catalog entry is not a playable URL.</summary>
public sealed record ProviderTrackRef(
    string ProviderId,
    string ProviderTrackId,
    string Title,
    string Artist,
    string Album) { public string? CoverUrl {get;init;} public long? AlbumId {get;init;} }

public sealed record ProviderPlaylist(
    string ProviderId,
    string ProviderPlaylistId,
    string Name,
    IReadOnlyList<ProviderTrackRef> Tracks) { public string? CoverUrl {get;init;} }

/// <summary>AccountAlias is a non-secret selector, not a token or cookie.</summary>
public interface IProviderPlaylistReader
{
    Task<IReadOnlyList<ProviderPlaylist>> ReadAsync(
        string accountAlias,
        CancellationToken cancellationToken = default);
}

/// <summary>No player, SQLite, network client, credentials, or write API is exposed.</summary>
public interface IProviderModule : IDisposable
{
    string ProviderId { get; }
    ProviderCapability Capabilities { get; }
    IProviderPlaylistReader PlaylistReader { get; }
}
