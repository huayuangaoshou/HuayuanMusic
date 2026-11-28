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
        int index = -1;
        if (original.Count == rows.Length)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var selectedKey = rows[start].CatalogIdentity;
            var occurrence = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                var key = rows[i].CatalogIdentity;
                counts[key] = counts.GetValueOrDefault(key) + 1;
                if (i < start && key == selectedKey) occurrence++;
            }
            for (int i = 0; i < original.Count; i++)
            {
                var key = ExistingKey(original[i]);
                if (!counts.TryGetValue(key, out int remaining) || remaining == 0)
                {
                    index = -1;
                    break;
                }
                counts[key] = remaining - 1;
                if (key == selectedKey && occurrence-- == 0) index = i;
            }
        }
        if (index >= 0)
        {
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
            }
        }
        if (!root.Player.IsPlayingAudio) throw new IOException("音频未开始");
        if (PersistQueue) root.Player.FlushControlledQueue();
        await root.ViewModel.History.LoadAsync();
        RefreshRecords();
    });
}
