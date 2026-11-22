using KugouAvaloniaPlayer.Services;
using HuayuanPreview.R2;

namespace HuayuanPreview;

internal sealed partial class CorePlaybackAdapter
{
    internal Task PlayDesktopListAsync(TrackRow[] rows, int start, P2AWorkspace concept) => Run("DesktopList", async () =>
    {
        if (rows.Length == 0 || start < 0 || start >= rows.Length) throw new InvalidOperationException("歌曲列表已变化");
        if (rows.Length > PlaybackQueueManager.MaxQueueSize) throw new InvalidOperationException("歌曲列表超过队列容量");
        var providerBySource = providerTrackKeys.ToDictionary(entry => items[entry.Key].LocalFilePath!, entry => entry.Value);
        string ExistingKey(KugouAvaloniaPlayer.ViewModels.SongItem song) =>
            song.LocalFilePath is { } path && providerBySource.TryGetValue(path, out var key) ? key : "local:" + song.LocalTrackId;
        var original = root.Player.OriginalControlledQueue;
        var requestedKeys = rows.Select(row => row.CatalogIdentity).ToArray();
        var existingKeys = original.Select(ExistingKey).ToArray();
        if (existingKeys.Length == requestedKeys.Length &&
            existingKeys.Order(StringComparer.Ordinal).SequenceEqual(requestedKeys.Order(StringComparer.Ordinal)))
        {
            var key = requestedKeys[start];
            var occurrence = requestedKeys.Take(start).Count(item => item == key);
            int index = Enumerable.Range(0, existingKeys.Length).Where(i => existingKeys[i] == key).ElementAt(occurrence);
            await root.Player.PlayControlledQueueEntryAsync(original[index]);
        }
        else
        {
            var known = providerTrackKeys.GroupBy(entry => entry.Value).ToDictionary(group => group.Key, group => group.First().Key);
            try
            {
                var entries = new KugouAvaloniaPlayer.ViewModels.SongItem[rows.Length];
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    var id = row.Track.Id;
                    if (row.IsProviderTrack && !known.TryGetValue(row.CatalogIdentity, out id))
                    {
                        id = await concept.M223AdmitAsync(row.Track.Key, publish: false);
                        known.Add(row.CatalogIdentity, id);
                    }
                    entries[i] = Song(id).NewQueueEntry();
                }
                RebuildTrackSnapshot();
                await root.Player.PlaySongAsync(entries[start], entries);
            }
            finally
            {
                PruneUnusedProviderTracks();
                RebuildTrackSnapshot();
                collectionDirty = true;
            }
        }
        if (!root.Player.IsPlayingAudio) throw new IOException("音频未开始");
        if (PersistQueue) root.Player.FlushControlledQueue();
        await root.ViewModel.History.LoadAsync();
        RefreshRecords();
    });
}
