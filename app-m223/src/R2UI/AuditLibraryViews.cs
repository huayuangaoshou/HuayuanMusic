using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

public sealed partial class WorkspaceState
{
    private StateViews MakeSongEmptyState(string title)
    {
        var view = new StateViews();
        void FocusSearch() => Root.FindAncestorOfType<R2ShellView>()?.FocusSearch();
        switch (title)
        {
            case "本地曲库":
                view.Empty("还没有歌。放一首喜欢的进来。", "管理本地文件夹", OpenFolderManager);
                break;
            case "收藏":
                view.Empty("还没有收藏。喜欢哪首歌，就点亮它的红心。", "浏览曲库", () => Navigate(CatalogPage));
                break;
            case "搜索结果":
            case "单曲结果":
                view.Empty("没有找到这首歌。试试另一个关键词。", "编辑关键词", FocusSearch);
                break;
            default:
                view.Empty("这里还没有歌曲。去曲库找点喜欢的。", "浏览曲库", () => Navigate(CatalogPage));
                break;
        }
        return view;
    }

    private Button NewPlaylistButton()
    {
        var button = new Button { Content="+ 新建歌单", Classes={ "glass-pill" } };
        button.Click += async (_, _) => await EditLocalPlaylistAsync(button,0);
        return button;
    }

    internal async Task EditLocalPlaylistAsync(Control anchor,long id,bool remove=false)
    {
        if (!Local || TopLevel.GetTopLevel(anchor) is not Window owner) return;
        var playlist = Host.Player.State.Lists.FirstOrDefault(item=>item.Id==id);
        if (id!=0 && playlist==null) return;
        var title = remove ? "删除自建歌单" : id==0 ? "新建歌单" : "重命名歌单";
        var action = new Button { Classes={ "primary" }, Content=remove?"删除":"保存", IsDefault=true };
        var cancel = new Button { Content="取消", IsCancel=true };
        var input = new TextBox { Text=playlist?.Name??"", MaxLength=80, PlaceholderText="歌单名称" };
        var panel = new StackPanel { Spacing=12 };
        panel.Children.Add(remove
            ? new TextBlock { Text="仅删除歌单及其成员关系，保留音乐文件。", TextWrapping=TextWrapping.Wrap }
            : input);
        panel.Children.Add(new StackPanel { Orientation=Orientation.Horizontal, Spacing=8, Children={ action,cancel } });
        var answer = await ModalOverlay.ShowAsync<string?>(owner, panel,
            new(title, 400, InitialFocus: remove ? cancel : input), action, cancel,
            () => remove ? "" : input.Text?.Trim(), () => remove || !string.IsNullOrWhiteSpace(input.Text));
        if (answer==null) return;
        await Host.Adapter.PlaylistAsync(remove?"delete":id==0?"create":"rename",answer,id);
        if (Host.Adapter.State.Error.Length>0) Host.Player.Notify("未能保存歌单，请检查名称后重试");
        else if(remove && CurrentPage==DetailPage && SelectedPlaylistId==id) Navigate(galleryPage);
    }

    /// <summary>Delete local tracks (from the library) or playlist members (from the playlist)
    /// with a glass-style confirmation dialog. Never touches files on disk.</summary>
    internal async Task<bool> RequestRemoveTracksAsync(TrackRow[] rows, Control? anchor = null)
    {
        if (!Local || rows.Length == 0) return false;
        rows = rows.Where(r => r.CanRemoveLocally).DistinctBy(r => r.Identity).ToArray();
        if (rows.Length == 0) return false;

        var fromPlaylist = rows.All(r => r.IsPlaylistMember);
        var fromLibrary = rows.All(r => r.IsLocalTrack && !r.IsPlaylistMember);
        if (!fromPlaylist && !fromLibrary) return false;

        var owner = anchor == null ? TopLevel.GetTopLevel(Root) as Window : TopLevel.GetTopLevel(anchor) as Window;
        if (owner == null) return false;

        var single = rows.Length == 1;
        var scopeLabel = fromPlaylist ? "歌单" : "本地曲库";
        var title = single ? $"{(fromPlaylist ? "从歌单移除" : "从列表移除")}「{rows[0].Name}」" : $"删除选中的 {rows.Length} 首歌曲";
        var message = new TextBlock
        {
            Text = $"仅从{scopeLabel}移除，不会删除硬盘上的本地音频原文件。",
            TextWrapping = TextWrapping.Wrap
        };
        var confirm = new Button { Classes = { "primary" }, Content = "确认移除", IsDefault = true };
        var cancel = new Button { Content = "取消", IsCancel = true };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(message);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, cancel } });
        var result = await ModalOverlay.ShowAsync(owner, panel, new(title, 400, InitialFocus: cancel),
            confirm, cancel, () => true);
        if (!result) return false;

        if (fromPlaylist) Host.Player.Remove(rows);
        else await Host.Adapter.RemoveLocalTracksAsync(rows.Select(r => r.Track.Id).ToArray());
        return Host.Adapter.State.Error.Length == 0;
    }
}
