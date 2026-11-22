using HuayuanMusic.Provider.Contracts;

namespace HuayuanPreview;

/// <summary>
/// Controlled in-process read-only provider registry. Prototype only: does not grant
/// filesystem/network permissions or execute untrusted plugins; never stores credentials.
/// No core player/database/audio object is passed to any provider.
/// </summary>
internal sealed class ControlledProviderHost : IDisposable
{
    readonly Dictionary<string, IProviderModule> modules = new(StringComparer.Ordinal);
    readonly HashSet<string> enabled = new(StringComparer.Ordinal);
    readonly Dictionary<string, ProviderPlaylist[]> metadata = new(StringComparer.Ordinal);
    bool disposed;

    public void Mount(IProviderModule module)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(module);
        var id = module.ProviderId;
        if (string.IsNullOrWhiteSpace(id) ||
            !id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            throw new ArgumentException("Invalid provider identity");
        if (!modules.TryAdd(id, module))
            throw new InvalidOperationException("Duplicate provider identity");
        // Important: mounting never enables data access or playback.
    }

    public void Enable(string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!modules.ContainsKey(id))
            throw new InvalidOperationException("Provider not installed");
        enabled.Add(id);
    }

    public void Disable(string id) => enabled.Remove(id);
    public bool IsEnabled(string id) => enabled.Contains(id);

    public async Task<IReadOnlyList<ProviderPlaylist>> ReadAsync(
        string id, string alias, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!enabled.Contains(id) || !modules.TryGetValue(id, out var module))
            throw new InvalidOperationException("Provider disabled or missing");
        if (!module.Capabilities.HasFlag(ProviderCapability.PlaylistRead))
            throw new NotSupportedException("Provider does not declare playlist-read");
        cancellationToken.ThrowIfCancellationRequested();
        var result = await module.PlaylistReader.ReadAsync(alias, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Copy and validate all untrusted provider metadata BEFORE publishing it.
        var copies = result.Select(p => new ProviderPlaylist(
            p.ProviderId, p.ProviderPlaylistId, p.Name,
            p.Tracks.Select(t => t with { }).ToArray())).ToArray();
        if (copies.Any(p => p.ProviderId != id || string.IsNullOrWhiteSpace(p.ProviderPlaylistId) ||
            p.Tracks.Any(t => t.ProviderId != id || string.IsNullOrWhiteSpace(t.ProviderTrackId))))
            throw new InvalidDataException("Invalid or cross-provider track identity");
        metadata[id + "\u001f" + alias] = copies;
        if (id == "mock-music") metadata[id] = copies; // Legacy single-fixture snapshot only; never a real multi-account provider.
        return copies;
    }

    public IReadOnlyList<ProviderPlaylist> Snapshot(string id) =>
        metadata.TryGetValue(id, out var lists) ? lists : [];

    public IReadOnlyList<ProviderPlaylist> Snapshot(string id,string alias) =>
        metadata.TryGetValue(id + "\u001f" + alias,out var lists) ? lists : [];

    public void Unmount(string id)
    {
        enabled.Remove(id);
        if (modules.Remove(id, out var module))
            module.Dispose();
        // Retained non-secret metadata belongs to the host, not to the module instance.
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var module in modules.Values) module.Dispose();
        modules.Clear();
        enabled.Clear();
    }
}

internal sealed partial class CorePlaybackAdapter
{
    internal readonly ControlledProviderHost Providers = new();
}
