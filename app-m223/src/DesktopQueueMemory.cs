using System.Diagnostics;
using System.Text.Json;
using HuayuanMusic.Provider.Kugou;
using HuayuanPreview.R2;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;

namespace HuayuanPreview;

internal sealed partial class CorePlaybackAdapter
{
    private sealed record SavedEntry(Guid Entry, int LocalTrack, CatalogTrack? Online);
    private sealed record SavedQueue(string? Account, Guid Selected, PlayMode Mode, SavedEntry[] Entries, Guid[] Original,
        double PositionSeconds = 0, bool WasPlaying = false);
    private static readonly string DesktopQueuePath = Path.Combine(AppDataPaths.RoamingRoot, "desktop-queue.json");
    private Task queueWrite = Task.CompletedTask;
    private CoreQueueEntry[] lastSavedEntries = [];
    private Guid lastSavedSelection;
    private int lastSavedMode;
    private bool lastSavedPlaying;
    private bool lastSavedPaused;
    private double lastSavedPosition;
    private long lastQueueWriteTimestamp;
    private (Guid Entry, double Position)? desktopResume;
    private readonly HashSet<int> deferredDesktopIds = [];
    private Func<bool>? restoredSession;
    private Task catalogReady = Task.CompletedTask;
    private string? restoredAccount;
    private bool restoringDesktopConnection;
    internal bool DesktopQueueTracking { get; set; }

    internal async Task RestoreDesktopQueueAsync(P2AWorkspace concept)
    {
        if (!DesktopPreferences.Behavior.RestoreQueue || !File.Exists(DesktopQueuePath)) return;
        var elapsed = Stopwatch.StartNew();
        var initialSelection = root.Player.CurrentPlayingSong;
        var initialQueue = root.Player.PlaybackQueue.ToArray();
        var saved = await Task.Run(() =>
        {
            try
            {
                if (new FileInfo(DesktopQueuePath).Length > 8 * 1024 * 1024) return null;
                return JsonSerializer.Deserialize<SavedQueue>(File.ReadAllText(DesktopQueuePath));
            }
            catch (Exception error) when(error is IOException or UnauthorizedAccessException or JsonException)
            { Program.Log("DESKTOP_QUEUE_READ_FAILED",new { type=error.GetType().Name }); return null; }
        });
        if (saved?.Entries == null || saved.Original == null || saved.Entries.Length > PlaybackQueueManager.MaxQueueSize ||
            !Enum.IsDefined(saved.Mode) || saved.Entries.Any(entry => entry.Entry == Guid.Empty) ||
            saved.Entries.Select(entry => entry.Entry).Distinct().Count() != saved.Entries.Length) return;
        if (disposed != 0 || !root.Coordinator.Player.IsStopped ||
            !ReferenceEquals(initialSelection, root.Player.CurrentPlayingSong) ||
            !initialQueue.SequenceEqual(root.Player.PlaybackQueue)) return;
        restoredAccount = DesktopPreferences.Behavior.Kugou == DesktopDlcState.Enabled &&
            (!concept.Connected || concept.AccountKey == saved.Account) ? saved.Account : null;
        var entries = new Dictionary<Guid, SongItem>();
        var admitted = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in saved.Entries)
        {
            int id = entry.LocalTrack;
            if (entry.Online is { } track)
            {
                if (string.IsNullOrEmpty(restoredAccount) || track.Id is not { Length: 32 } ||
                    !track.Id.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(track.Title)) continue;
                if (!admitted.TryGetValue(track.Id, out id))
                {
                    id = AdmitLazyDesktopSong(track, "standard", () => restoredSession?.Invoke() == true &&
                        Kugou.CatalogTrackForPlaylist(track.Id) != null, CancellationToken.None,
                        publish: false, account: restoredAccount);
                    admitted.Add(track.Id, id);
                    deferredDesktopIds.Add(id);
                }
            }
            if (!items.TryGetValue(id, out var item) ||
                (entry.Online == null && item.LocalSourceType != "LocalFile")) continue;
            var song = item.NewQueueEntry();
            song.QueueEntryId = entry.Entry;
            entries.Add(entry.Entry, song);
        }
        var visible = saved.Entries.Where(item => entries.ContainsKey(item.Entry)).Select(item => entries[item.Entry]).ToArray();
        var original = saved.Original.Where(entries.ContainsKey).Distinct().Select(id => entries[id]).ToArray();
        RebuildTrackSnapshot();
        desktopResume = null;
        root.Player.RestoreDesktopQueue(visible, original.Length == visible.Length ? original : visible, saved.Selected, saved.Mode);
        if (root.Player.CurrentPlayingSong?.QueueEntryId == saved.Selected &&
            double.IsFinite(saved.PositionSeconds) && saved.PositionSeconds > 0)
            await SeekAsync(saved.PositionSeconds);
        Publish();
        Program.Log("DESKTOP_QUEUE_RESTORED", new
        {
            count = visible.Length, online = admitted.Count, playing = State.Playing,
            position = State.Position, wasPlaying = saved.WasPlaying, elapsedMs = elapsed.Elapsed.TotalMilliseconds
        });
    }

    internal async Task RestoreDesktopConnectionAndCatalogAsync(P2AWorkspace concept)
    {
        var connection = ConnectAsync();
        var completion = catalogReady = CompleteAsync();
        await connection;
        if (await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(3))) != completion)
            Program.Log("DESKTOP_QUEUE_CATALOG_DEFERRED", new { type = "Timeout" });

        async Task ConnectAsync()
        {
            restoringDesktopConnection = true;
            try { await concept.RestoreDesktopConnectionAsync(); }
            finally { restoringDesktopConnection = false; }
        }

        async Task CompleteAsync()
        {
            await connection;
            if (disposed != 0 || deferredDesktopIds.Count == 0 || !concept.Connected ||
                concept.AccountKey != restoredAccount) return;

            var generation = Kugou.Generation;
            var provider = Kugou;
            bool SessionCurrent() => ReferenceEquals(provider, Kugou) && provider.Generation == generation &&
                provider.Enabled && provider.LoggedIn && provider.DirectAccountCacheKey == restoredAccount;
            var catalog = deferredDesktopIds.Select(id => providerMetadata[id]).ToArray();
            try
            {
                await concept.DesktopCall(async (gateway, ct) =>
                {
                    await gateway.RestoreDesktopCatalogAsync(catalog, ct);
                    return true;
                }, CancellationToken.None);
                if (disposed != 0 || !SessionCurrent() || deferredDesktopIds.Count == 0) return;
                restoredSession = SessionCurrent;
                deferredDesktopIds.Clear();
                concept.NotifyRestoredDesktopCatalog();
                Publish();
                Program.Log("DESKTOP_QUEUE_CATALOG_READY", new { count = catalog.Length });
            }
            catch (Exception error)
            {
                Program.Log("DESKTOP_QUEUE_CATALOG_DEFERRED", new { type = error.GetType().Name });
            }
        }
    }

    private bool DesktopQueueEntryIsPending(SongItem song) =>
        deferredDesktopIds.Count > 0 && ProviderStreamRegistry.IsProvider(song.LocalFilePath) &&
        deferredDesktopIds.Contains(Id(song));

    // The startup caller stops waiting for the catalog after three seconds. An explicit
    // play request can join that same work without restarting the handshake or the queue.
    private async Task WaitForDesktopQueueEntryAsync(SongItem song)
    {
        if (!DesktopQueueEntryIsPending(song)) return;
        var ready = catalogReady;
        if (DesktopPreferences.Behavior.Kugou == DesktopDlcState.Enabled && !ready.IsCompleted)
        {
            try { await ready.WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (TimeoutException) { }
        }
        if (DesktopQueueEntryIsPending(song) || !ProviderStreamRegistry.Allows(song.LocalFilePath))
            throw new InvalidOperationException("在线曲目暂不可用");
    }

    internal void StartDesktopQueueTracking()
    {
        if (DesktopQueueTracking) return;
        DesktopQueueTracking = true;
        RememberQueueSnapshot(State);
        Changed += TrackDesktopQueue;
    }

    private void TrackDesktopQueue(PlaybackSnapshot snapshot)
    {
        if (!DesktopQueueTracking) return;
        if (desktopResume is { } pending && pending.Entry != snapshot.CurrentEntryId)
            desktopResume = null;
        bool queueChanged = snapshot.CurrentEntryId != lastSavedSelection || snapshot.Mode != lastSavedMode ||
            !snapshot.QueueEntries.SequenceEqual(lastSavedEntries);
        bool transportChanged = snapshot.Playing != lastSavedPlaying || snapshot.Paused != lastSavedPaused;
        bool positionChanged = Math.Abs(snapshot.Position - lastSavedPosition) >= .05;
        if (queueChanged || transportChanged || positionChanged &&
            (!snapshot.Playing || Stopwatch.GetElapsedTime(lastQueueWriteTimestamp) >= TimeSpan.FromSeconds(30)))
        {
            RememberQueueSnapshot(snapshot);
            _ = SaveDesktopQueueAsync();
        }
    }

    private void RememberQueueSnapshot(PlaybackSnapshot snapshot)
    {
        lastSavedEntries = snapshot.QueueEntries;
        lastSavedSelection = snapshot.CurrentEntryId;
        lastSavedMode = snapshot.Mode;
        lastSavedPlaying = snapshot.Playing;
        lastSavedPaused = snapshot.Paused;
        lastSavedPosition = snapshot.Position;
        lastQueueWriteTimestamp = Stopwatch.GetTimestamp();
    }

    private void ApplyDesktopResumePosition(SongItem song)
    {
        var requested = desktopResume;
        desktopResume = null;
        if (requested is not { } pending || pending.Entry != song.QueueEntryId || pending.Position <= 0)
            return;
        try
        {
            var player = root.Coordinator.Player;
            var length = player.GetDuration().TotalSeconds;
            var position = length > 0 ? Math.Clamp(pending.Position, 0, Math.Max(0, length - .25)) : pending.Position;
            player.SetPosition(TimeSpan.FromSeconds(position));
            root.Player.CurrentPositionSeconds = player.GetPosition().TotalSeconds;
            Program.Log("DESKTOP_QUEUE_RESUMED", new { requested = position, actual = root.Player.CurrentPositionSeconds });
        }
        catch (Exception error) { Program.Log("DESKTOP_QUEUE_RESUME_FAILED", new { type = error.GetType().Name }); }
    }

    internal Task SaveDesktopQueueAsync()
    {
        var account = Kugou.DirectAccountCacheKey ?? restoredAccount;
        CatalogTrack? OnlineMetadata(int id) => providerTrackKeys.TryGetValue(id, out var key) &&
            key.StartsWith($"kugou-concept:{account}:", StringComparison.Ordinal)
                ? providerMetadata.GetValueOrDefault(id) : null;
        var player = root.Coordinator.Player;
        double position = player.IsStopped ? State.Position : player.GetPosition().TotalSeconds;
        if (State.CurrentEntryId == Guid.Empty || !double.IsFinite(position) || position < 0) position = 0;
        SavedQueue snapshot;
        using (new R2.ListEventMeter("queuePersistSnapshot", State.QueueEntries.Length))
            snapshot = new SavedQueue(account, State.CurrentEntryId, (PlayMode)State.Mode,
            State.QueueEntries.Select(entry => new SavedEntry(entry.EntryId, entry.TrackId, OnlineMetadata(entry.TrackId)))
                .Where(entry => entry.Online != null || !providerTrackKeys.ContainsKey(entry.LocalTrack)).ToArray(),
            root.Player.OriginalControlledQueue.Select(song => song.QueueEntryId).ToArray(), position, player.IsPlaying);
        return queueWrite = PersistAsync(queueWrite,snapshot);
    }

    private static async Task PersistAsync(Task prior, SavedQueue snapshot)
    {
        await prior;
        try
        {
            await Task.Run(() =>
            {
                using var timing = new R2.ListEventMeter("queuePersistWrite", snapshot.Entries.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(DesktopQueuePath)!);
                File.WriteAllText(DesktopQueuePath+".tmp",JsonSerializer.Serialize(snapshot));
                File.Move(DesktopQueuePath+".tmp",DesktopQueuePath,true);
            });
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        { Program.Log("DESKTOP_QUEUE_WRITE_FAILED",new { type=error.GetType().Name }); }
    }
}
