using HuayuanMusic.Provider.Contracts;
using HuayuanMusic.Provider.Mock;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using ManagedBass;

namespace HuayuanPreview;

public sealed partial class PreviewWindow
{
    private static string[] SnapshotSyntheticDbRows()
    {
        using var connection = AppDatabase.CreateConnection();
        var records = new List<string>();
        foreach (var (table, sql) in new[] {
            ("kv", "SELECT scope,key,value,updated_at FROM kv_store ORDER BY scope,key"),
            ("tracks", "SELECT * FROM tracks ORDER BY id"),
            ("playlists", "SELECT * FROM playlists ORDER BY id"),
            ("items", "SELECT * FROM playlist_tracks ORDER BY playlist_id,position,track_id")
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var columns = Enumerable.Range(0, reader.FieldCount)
                    .Select(i => reader.IsDBNull(i) ? "<NULL>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "");
                records.Add(table + ":" + string.Join("\u001f", columns));
            }
        }
        return records.ToArray();
    }

    internal async Task ProviderBridgeIntegrationTest(OfflineWindowRoot root, CorePlaybackAdapter adapter)
    {
        var checks = 0;
        void Check(bool pass, string name)
        {
            Program.Log(pass ? "P18B_PASS" : "P18B_FAIL", new { name });
            if (!pass) throw new InvalidOperationException("P18B: " + name);
            checks++;
        }
        async Task ExpectAsync<T>(Func<Task> work, string name) where T : Exception
        {
            try { await work(); }
            catch (T) { Check(true, name); return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + name);
        }

        try
        {
            Check(!adapter.State.Playing && root.Coordinator.Player.IsStopped &&
                Bass.CurrentDevice == -1, "No playback or device before mocked provider");
            Check(OfflineStartupNetwork.Requests == 1, "Only synthetic preflight request");
            var beforeQueue = adapter.State.Queue.ToArray();
            var beforeTracks = adapter.State.Tracks.ToArray();
            var beforeLists = adapter.State.Lists.ToArray();
            var beforeFav = adapter.State.Favorites.ToArray();
            var beforeHistory = adapter.State.History.ToArray();
            var beforeDbRows = SnapshotSyntheticDbRows();
            var mock = new MockProvider();
            var providers = adapter.Providers;
            providers.Mount(mock);
            Check(!providers.IsEnabled(mock.ProviderId), "Provider starts disabled in real UI process");
            await ExpectAsync<InvalidOperationException>(() =>
                providers.ReadAsync("mock-music", "isolated-demo"), "Disabled module has no read access");
            providers.Enable("mock-music");
            Check(providers.IsEnabled("mock-music"), "Explicit enable in controlled runtime");
            Check(mock.Capabilities == ProviderCapability.PlaylistRead, "Read-only capabilities");
            await ExpectAsync<UnauthorizedAccessException>(() =>
                providers.ReadAsync("mock-music", "wrong-alias"), "Unknown account alias rejected");
            var playlists = await providers.ReadAsync("mock-music", "isolated-demo");
            Check(playlists.Count == 1 && playlists[0].Tracks.Count == 2, "Provider playlist read through live adapter");
            Check(playlists[0].Tracks.All(t => t.ProviderId == "mock-music"), "Provenance preserved");
            Check(playlists[0].Tracks.All(t => adapter.State.Tracks.All(local =>
                local.Key != t.ProviderId + ":" + t.ProviderTrackId)), "Mock metadata not inserted into local library");
            Check(adapter.State.Queue.SequenceEqual(beforeQueue), "Provider read does not modify production queue");
            Check(adapter.State.Tracks.SequenceEqual(beforeTracks), "Provider read does not modify local tracks");
            Check(adapter.State.Lists.SequenceEqual(beforeLists), "Provider read does not modify saved local playlists");
            Check(adapter.State.Favorites.SequenceEqual(beforeFav), "Provider read does not modify local favorites");
            Check(adapter.State.History.SequenceEqual(beforeHistory), "Provider read does not modify history");
            mock.FailNextRead = true;
            await ExpectAsync<IOException>(() =>
                providers.ReadAsync("mock-music", "isolated-demo"), "Simulated provider outage isolated");
            Check(providers.Snapshot("mock-music").Count == 1, "Outage retains metadata");
            Check(adapter.State.Queue.SequenceEqual(beforeQueue) &&
                adapter.State.Tracks.SequenceEqual(beforeTracks), "Outage leaves local playback sources untouched");
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            await ExpectAsync<OperationCanceledException>(() =>
                providers.ReadAsync("mock-music", "isolated-demo", cancel.Token), "Cancellation respected");
            providers.Disable("mock-music");
            await ExpectAsync<InvalidOperationException>(() =>
                providers.ReadAsync("mock-music", "isolated-demo"), "Disabled module refuses reads");
            Check(providers.Snapshot("mock-music").Count == 1, "Disable retains nonsecret metadata");
            providers.Unmount("mock-music");
            Check(mock.IsDisposed, "Unmounted module disposed");
            Check(providers.Snapshot("mock-music").Count == 1, "Unmount preserves read-only snapshot");
            await ExpectAsync<InvalidOperationException>(() =>
                providers.ReadAsync("mock-music", "isolated-demo"), "Unmount refuses new reads");
            Check(!adapter.State.Playing && root.Coordinator.Player.IsStopped,
                "Mock lifecycle does not start audio");
            Check(OfflineStartupNetwork.Requests == 1, "Mock lifecycle triggers no network requests");
            Check(SnapshotSyntheticDbRows().SequenceEqual(beforeDbRows),
                "Mock lifecycle does not mutate local SQLite rows");
            Program.Log("P18B_COMPLETE", new { checks, provider = "mock-music", enabled = false,
                realEngineFollows = true, networkRequests = OfflineStartupNetwork.Requests });
            // Same process: execute the preexisting complete real-core BASS, favorites,
            // lyric, history, queue and UI acceptance sequence after mock failure/unmount.
            await IntegrationTest(root, adapter);
        }
        catch (Exception error)
        {
            Program.Result = 2;
            Program.Log("P18B_FAILED", error.ToString());
            Close();
        }
    }
}
