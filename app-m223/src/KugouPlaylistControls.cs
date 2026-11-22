using Avalonia.Controls;
using Avalonia.VisualTree;
using HuayuanMusic.Provider.Contracts;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview;

public sealed partial class PreviewWindow
{
    StackPanel? kugouOfficialPlaylists;
    readonly List<Button> kugouPlaylistPlayButtons=new();
    Func<string,CatalogTrack?>? playlistCatalogSelfTest;
    void SelectKugouRow(StackPanel view,int row){view.Tag=row;foreach(var item in view.Children.OfType<StackPanel>())if(item.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock title)title.FontWeight=Equals(item.Tag,row)?Avalonia.Media.FontWeight.SemiBold:Avalonia.Media.FontWeight.Normal;}
    void DemandPlaylistAccount(CorePlaybackAdapter a,string? owner,string? key){DemandPlaylistConsent();if(!a.Kugou.LoggedIn||a.Kugou.Account!=owner||(key!=null&&a.Kugou.DirectAccountCacheKey!=key))throw new UnauthorizedAccessException("账号已改变，请同步当前账号的歌单后播放");}

    async Task LoadKugouPlaylistsAsync(CorePlaybackAdapter adapter)
    {
        DemandSdk(adapter);
        try
        {
            KugouFeedback("正在只读同步酷狗官方歌单，曲目较多时会分批读取；本次不写回官方平台。");
            var lists=await adapter.Kugou.ReadAsync(adapter.Kugou.Account??"");
            ShowKugouOfficialPlaylists(adapter,lists);
            var key=adapter.Kugou.DirectAccountCacheKey;
            if(key==null)
            {
                KugouFeedback($"酷狗只读同步完成：{lists.Count} 个歌单；仅内存快照，未写回官方");
                return;
            }
            try
            {
                await Task.Run(()=>PlatformPlaylistSnapshotStore.Save(key,lists));
                KugouFeedback($"酷狗只读同步完成：{lists.Count} 个歌单，元数据已独立保存至本机；未修改官方歌单");
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                KugouFeedback($"官方歌单已读取：{lists.Count} 个；但本地快照保存失败（{ex.GetType().Name}），内存数据仍可查看");
            }
        }
        catch(InvalidDataException ex)
        {
            // All validation messages originate from this isolated adapter, not raw JSON.
            var message=ex.Message.Length>175?ex.Message[..175]:ex.Message;
            KugouFeedback("只读歌单结构校验未通过："+message+"；旧快照保持不变");
        }
    }

    void ShowOfflineKugouSnapshot(CorePlaybackAdapter adapter)
    {
        try
        {
            var data=PlatformPlaylistSnapshotStore.LoadLatest();
            if(data==null)
            {
                KugouFeedback("本地尚无酷狗官方歌单快照；可以登录后只读同步一次");
                return;
            }
            ShowKugouOfficialPlaylists(adapter,data.Playlists,fromCache:true,cachedAt:data.SyncedAt,cachedAccount:data.AccountKey);
            KugouFeedback($"本地快照已读取：{data.Playlists.Count} 个酷狗歌单（仅元数据，不联网、不播放）");
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException)
        {
            KugouFeedback("本地快照读取失败："+ex.GetType().Name+"；不会覆盖当前在线数据");
        }
    }

    // Official platform playlists are displayed in their own DLC section.
    // These are in-memory metadata snapshots, not a merge into local playlists.
    void ShowKugouOfficialPlaylists(CorePlaybackAdapter adapter,IReadOnlyList<ProviderPlaylist> lists,bool fromCache=false,DateTimeOffset? cachedAt=null,string? cachedAccount=null)
    {
        if(kugouOfficialPlaylists is not { } section)return;
        section.Children.Clear();kugouPlaylistPlayButtons.Clear();
        var owner=adapter.Kugou.Account;var accountKey=adapter.Kugou.DirectAccountCacheKey;
        if(lists.Count==0)
        {
            section.Children.Add(T("当前账号没有返回歌单。本地和其他音乐平台歌单未受影响。",13,true));
            return;
        }
        section.Children.Add(T(fromCache
            ?$"酷狗本地快照 · {lists.Count} 个歌单 · 同步于 {cachedAt?.ToLocalTime():yyyy-MM-dd HH:mm} · 不联网"
            :$"酷狗账号 · {lists.Count} 个歌单 · 本次只读同步",13,true));
        foreach(var list in lists)
        {
            // Render only 50 rows at a time to protect the Avalonia UI from huge libraries.
            var view=V();
            var expander=new Expander
            {
                Header=$"{list.Name} · {list.Tracks.Count} 首（酷狗）",
                IsExpanded=false,
                Content=view
            };
            var artwork=ArtworkImage(list.CoverUrl,42,cachedAccount??accountKey??owner,online:!fromCache);
            expander.Header=H(artwork,T($"{list.Name} · {list.Tracks.Count} 首（酷狗）",13));
            expander.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty){foreach(var tile in view.GetVisualDescendants().OfType<ArtworkTile>()){if(expander.IsExpanded)tile.Reload();else tile.Cancel();}}};
            view.Children.Add(NB("查看曲目（每页 50 首）",()=>ShowPage(0)));
            section.Children.Add(expander);

            void ShowPage(int first)
            {
                foreach(var old in view.Children.OfType<StackPanel>().SelectMany(row=>row.Children.OfType<StackPanel>()).SelectMany(row=>row.Children.OfType<Button>()))kugouPlaylistPlayButtons.Remove(old);
                view.Children.Clear();
                if(fromCache||!adapter.Kugou.LoggedIn||!adapter.Kugou.SdkConnected)
                    view.Children.Add(T("离线元数据仅供查看。需要在线登录并同步后才能申请歌曲播放权限。",12,true));
                if(list.Tracks.Count==0)
                {
                    view.Children.Add(T("此歌单暂无曲目。",12,true));return;
                }
                int stop=Math.Min(first+50,list.Tracks.Count);
                view.Children.Add(T($"曲目 {first+1}～{stop} / {list.Tracks.Count}（只读）",12,true));
                for(int i=first;i<stop;i++)
                {
                    var item=list.Tracks[i];var rowIndex=i;
                    var track=fromCache||!adapter.Kugou.LoggedIn?null:
                        adapter.Kugou.CatalogTrackForPlaylist(item.ProviderTrackId)??(KugouAvaloniaPlayer.Services.Startup.OfflineGuiApplication.SelfTest?playlistCatalogSelfTest?.Invoke(item.ProviderTrackId):null);
                    if(track!=null)track=track with{Cover=item.CoverUrl}; // Preserve the selected playlist occurrence, not a different album entry sharing its hash.
                    if(track==null)
                    {
                        view.Children.Add(H(ArtworkImage(item.CoverUrl,36,cachedAccount??accountKey??owner,online:!fromCache,eligible:()=>expander.IsExpanded),T($"{i+1}. {item.Title} · {item.Artist}（当前仅元数据 / 暂不能播放）",12,true)));
                        continue;
                    }
                    var row=V(
                        T($"{i+1}. {item.Title} · {item.Artist}",13),
                        H(
                            NB("加入本体队列",()=>KugouAction(async()=>{
                                DemandPlaylistAccount(adapter,owner,accountKey);
                                SelectKugouRow(view,rowIndex);
                                int id=await adapter.AdmitKugouAsync(track,"standard");
                                await adapter.QueueAsync("append",id);
                                if(adapter.State.Error.Length>0||!adapter.State.Queue.Contains(id))
                                    throw new InvalidOperationException("加入队列失败，请检查队列状态");
                                KugouFeedback("已加入播放队列");
                            })),
                            NB("播放此曲",()=>KugouAction(async()=>{
                                DemandPlaylistAccount(adapter,owner,accountKey);
                                SelectKugouRow(view,rowIndex);
                                int id=await adapter.AdmitKugouAsync(track,"standard");
                                await adapter.PlayAsync(id);
                                if(adapter.State.Error.Length>0||adapter.State.CurrentId!=id||
                                   !adapter.State.Playing)
                                    throw new InvalidOperationException("歌单播放没有成功启动；请检查本体播放状态");
                                KugouFeedback("正在播放");
                            }))
                        )
                    );row.Orientation=Avalonia.Layout.Orientation.Horizontal;row.Spacing=12;row.Children.Insert(0,ArtworkImage(item.CoverUrl,36,cachedAccount??accountKey??owner,online:!fromCache,eligible:()=>expander.IsExpanded));row.Tag=rowIndex;view.Children.Add(row);
                }
                foreach(var button in view.Children.OfType<StackPanel>().SelectMany(row=>row.Children.OfType<StackPanel>()).SelectMany(row=>row.Children.OfType<Button>()))if(!kugouPlaylistPlayButtons.Contains(button))kugouPlaylistPlayButtons.Add(button);
                if(view.Tag is int selected)SelectKugouRow(view,selected);
                if(first>0)view.Children.Add(NB("上一页",()=>ShowPage(Math.Max(0,first-50))));
                if(stop<list.Tracks.Count)view.Children.Add(NB("下一页",()=>ShowPage(stop)));
            }
        }
    }
}
