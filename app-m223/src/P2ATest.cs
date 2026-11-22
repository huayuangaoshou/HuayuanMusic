using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HuayuanPreview.R2;

namespace HuayuanPreview;
public sealed partial class PreviewWindow
{
    async Task ClickP2A(ShellModel shell,string text)
    {
        var button=shell.KugouConceptWorkspace.Root.GetVisualDescendants().OfType<Button>()
            .Single(b=>b.IsVisible&&b.Content?.ToString()==text);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await shell.Concept.Pending;
    }

    async Task P2AChecksAsync(ShellModel shell,CorePlaybackAdapter adapter,Action<bool,string> check)
    {
        var concept=shell.Concept;
        var workspace=shell.KugouConceptWorkspace;
        var snapshot=concept.Snapshot!;
        var queue=adapter.State.QueueEntries.ToArray();
        await adapter.QueueEntryAsync("play",queue[0].EntryId);
        check(adapter.State.Playing&&shell.SourceLabel=="歌曲来源：本地音乐","P2A_LOCAL_SOURCE_WHILE_BROWSING_CONCEPT");
        check(snapshot.Playlists.Count==7&&snapshot.Playlists[0].Tracks.Count==61,"P2A_SIGNED_INDEX_AND_TWO_SONG_PAGES");
        check(snapshot.Playlists.Count(p=>p.CoverUrl!=null)==2,"P2A_DETAIL_ID_URL_FAILURE_AND_MISSING");
        check(snapshot.Playlists[0].Tracks[0].ProviderTrackId==snapshot.Playlists[0].Tracks[1].ProviderTrackId&&
            workspace.Members().Take(2).Select(row=>row.Identity).Distinct().Count()==2,"P2A_DUPLICATE_MEMBERS_RETAINED");
        check(workspace.Lists[workspace.SearchPage].ItemCount==1,"P2A_SDK_SEARCH_READBACK");
        bool denied=false;
        try { await adapter.Kugou.ResolveAsync(snapshot.Playlists[0].Tracks[0].ProviderTrackId,"standard"); }
        catch(UnauthorizedAccessException) { denied=true; }
        check(denied&&adapter.Kugou.RealSongSourceRequestCount==0,"P2A_AUDIO_PERMISSION_DENIED_BEFORE_SOURCE_REQUEST");
        workspace.Navigate(workspace.Pages.Single(p=>p.DisplayName=="歌单"));
        for(int i=0;i<60&&workspace.Root.GetVisualDescendants().OfType<ArtworkTile>().Count(t=>t.IsArtworkLoaded)<2;i++)await Task.Delay(40);
        check(workspace.Root.GetVisualDescendants().OfType<ArtworkTile>().Count(t=>t.IsArtworkLoaded)==2,"P2A_ACTUAL_CARD_COVERS_AND_PLACEHOLDERS");
        await R2Capture("p2a-playlist-artwork");
        workspace.OpenPlaylist(1);
        for(int i=0;i<60&&!workspace.Root.GetVisualDescendants().OfType<ArtworkTile>().Any(t=>t.IsArtworkLoaded);i++)await Task.Delay(40);
        check(workspace.Root.GetVisualDescendants().OfType<ArtworkTile>().Any(t=>t.IsArtworkLoaded),"P2A_ACTUAL_SONG_COVER");
        check(!workspace.Root.GetVisualDescendants().OfType<Button>().Any(b=>b.IsVisible&&b.Name is "PlayButton" or "AddButton"),"P2A_NO_PLATFORM_PLAY_OR_ADD_BUTTONS");
        await R2Capture("p2a-song-artwork");
        workspace.Navigate(workspace.Pages.Single(p=>p.DisplayName=="账号状态"));
        await Task.Delay(80);
        await ClickP2A(shell,"测试读取失败");
        check(concept.Connected&&ReferenceEquals(snapshot,concept.Snapshot)&&concept.Status.Contains("失败"),"P2A_FAILURE_KEEPS_READBACK");
        await R2Capture("p2a-read-failure");
        await ClickP2A(shell,"测试封面超时");
        check(concept.Connected&&concept.Snapshot!.Playlists.Count==7&&concept.Snapshot.Playlists.Count(p=>p.CoverUrl!=null)==1,"P2A_DETAIL_TIMEOUT_KEEPS_ALL_METADATA");
        var slow=workspace.Root.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="测试封面超时");
        slow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var canceled=concept.Pending;
        await Task.Delay(120);
        await ClickP2A(shell,"撤销合成会话");
        await canceled;
        check(!concept.Connected&&!adapter.Kugou.Enabled&&concept.Snapshot!.Playlists.Count==7,"P2A_REVOKE_CANCELS_AND_RETAINS_METADATA");
        await R2Capture("p2a-revoked");
        await ClickP2A(shell,"连接合成账号 B");
        check(concept.Connected&&concept.Snapshot!.AccountKey!=snapshot.AccountKey&&concept.Snapshot.Playlists.Count==1&&
            concept.Snapshot.Playlists[0].ProviderPlaylistId=="p2a-B-1"&&workspace.Lists[workspace.SearchPage].ItemCount==0,"P2A_ACCOUNT_PARTITION_AND_SEARCH_RESET");
        await ClickP2A(shell,"测试账号失效");
        check(!concept.Connected&&!adapter.Kugou.Enabled&&concept.Status.Contains("失效")&&
            PlatformPlaylistSnapshotStore.LoadLatest(snapshot.AccountKey)?.Playlists.Count==7,"P2A_EXPIRY_REVOKES_AND_PERSISTS_OLD_PARTITION");
        check(queue.SequenceEqual(adapter.State.QueueEntries)&&shell.Player.DisplayPlaybackQueue.Select(x=>x.QueueId).SequenceEqual(queue.Select(x=>x.EntryId))&&
            adapter.ProviderStreamCount==0&&adapter.State.Playing&&adapter.State.CurrentEntryId==queue[0].EntryId,"P2A_NO_QUEUE_MUTATION_OR_PROVIDER_STREAM");
        await adapter.StopAsync();
    }
}
