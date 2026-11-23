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
    private sealed record SavedQueue(string? Account, Guid Selected, PlayMode Mode, SavedEntry[] Entries, Guid[] Original);
    private static readonly string DesktopQueuePath = Path.Combine(AppDataPaths.RoamingRoot, "desktop-queue.json");
    private Task queueWrite = Task.CompletedTask;
    private CoreQueueEntry[] lastSavedEntries = [];
    private Guid lastSavedSelection;
    private int lastSavedMode;
    internal bool DesktopQueueTracking { get; set; }

    internal async Task RestoreDesktopQueueAsync(P2AWorkspace concept)
    {
        if (!DesktopPreferences.Behavior.RestoreQueue || !File.Exists(DesktopQueuePath)) return;
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
        bool online = concept.Connected && saved.Account != null && concept.AccountKey == saved.Account;
        if (online)
        {
            var catalog = saved.Entries.Select(entry => entry.Online).OfType<CatalogTrack>().ToArray();
            await concept.DesktopCall(async (gateway, ct) => { await gateway.RestoreDesktopCatalogAsync(catalog, ct); return true; }, CancellationToken.None);
        }
        var entries = new Dictionary<Guid, SongItem>();
        var admitted = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in saved.Entries)
        {
            int id = entry.LocalTrack;
            if (entry.Online is { } track)
            {
                if (!online || track.Id.Length != 32 || Kugou.CatalogTrackForPlaylist(track.Id) == null) continue;
                if (!admitted.TryGetValue(track.Id, out id))
                    admitted[track.Id] = id = await concept.M223AdmitAsync($"kugou-concept:{saved.Account}:{track.Id}", publish:false);
            }
            if (!items.ContainsKey(id)) continue;
            var song = Song(id).NewQueueEntry();
            song.QueueEntryId = entry.Entry;
            entries.Add(entry.Entry, song);
        }
        var visible = saved.Entries.Where(item => entries.ContainsKey(item.Entry)).Select(item => entries[item.Entry]).ToArray();
        var original = saved.Original.Where(entries.ContainsKey).Distinct().Select(id => entries[id]).ToArray();
        RebuildTrackSnapshot();
        root.Player.RestoreDesktopQueue(visible, original.Length == visible.Length ? original : visible, saved.Selected, saved.Mode);
        collectionDirty = true;
        Publish();
        Program.Log("DESKTOP_QUEUE_RESTORED", new { count = visible.Length, online = admitted.Count, playing = State.Playing });
    }

    internal void StartDesktopQueueTracking()
    {
        DesktopQueueTracking=true;
        lastSavedEntries = State.QueueEntries;
        lastSavedSelection = State.CurrentEntryId;
        lastSavedMode = State.Mode;
        Changed += snapshot =>
        {
            if(!DesktopQueueTracking)return;
            if (snapshot.CurrentEntryId == lastSavedSelection && snapshot.Mode == lastSavedMode && snapshot.QueueEntries.SequenceEqual(lastSavedEntries)) return;
            lastSavedEntries = snapshot.QueueEntries;
            lastSavedSelection = snapshot.CurrentEntryId;
            lastSavedMode = snapshot.Mode;
            _ = SaveDesktopQueueAsync();
        };
    }

    internal Task SaveDesktopQueueAsync()
    {
        var account = Kugou.DirectAccountCacheKey;
        CatalogTrack? OnlineMetadata(int id) => providerTrackKeys.TryGetValue(id,out var key)
            ? Kugou.CatalogTrackForPlaylist(key[(key.LastIndexOf(':')+1)..]) : null;
        var snapshot = new SavedQueue(account, State.CurrentEntryId, (PlayMode)State.Mode,
            State.QueueEntries.Select(entry => new SavedEntry(entry.EntryId, entry.TrackId, OnlineMetadata(entry.TrackId))).ToArray(),
            root.Player.OriginalControlledQueue.Select(song => song.QueueEntryId).ToArray());
        return queueWrite = PersistAsync(queueWrite,snapshot);
    }

    private static async Task PersistAsync(Task prior, SavedQueue snapshot)
    {
        await prior;
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DesktopQueuePath)!);
                File.WriteAllText(DesktopQueuePath+".tmp",JsonSerializer.Serialize(snapshot));
                File.Move(DesktopQueuePath+".tmp",DesktopQueuePath,true);
            });
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        { Program.Log("DESKTOP_QUEUE_WRITE_FAILED",new { type=error.GetType().Name }); }
    }
}
