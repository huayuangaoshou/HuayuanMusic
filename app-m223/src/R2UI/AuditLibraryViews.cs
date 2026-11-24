using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

public sealed partial class WorkspaceState
{
    private Control MakeSongEmptyState(string title)
    {
        var label = title switch
        {
            "收藏" => "暂无收藏歌曲，点击歌曲旁边的红心即可收藏",
            "本地曲库" => "暂无本地音乐，可添加文件夹或导入音频文件",
            "歌单详情" => "这份歌单还没有歌曲",
            "搜索结果" or "单曲结果" => "没有找到匹配的歌曲，试试其他关键词",
            _ => "暂无歌曲"
        };
        var panel = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        if (title == "收藏")
        {
            var heart = new Avalonia.Controls.Shapes.Path { Width=32, Height=32, Stretch=Stretch.Uniform };
            heart.Classes.Add("empty-heart");
            panel.Children.Add(heart);
        }
        panel.Children.Add(new TextBlock { Text=label, Classes={ "muted" }, TextAlignment=TextAlignment.Center });
        return panel;
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
        var dialog = new Window { Title=title, Width=400, SizeToContent=SizeToContent.Height, CanResize=false,
            WindowStartupLocation=WindowStartupLocation.CenterOwner, RequestedThemeVariant=owner.ActualThemeVariant,
            Content=new Border { Classes={ "glass", "glass-float" }, Margin=new Thickness(12), Padding=new Thickness(14), Child=panel } };
        action.Click += (_, _) =>
        {
            if (remove || !string.IsNullOrWhiteSpace(input.Text)) dialog.Close(remove?"":input.Text.Trim());
        };
        cancel.Click += (_, _) => dialog.Close(null);
        var answer=await dialog.ShowDialog<string?>(owner);
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
        var dialog = new Window
        {
            Title = title, Width = 400, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, RequestedThemeVariant = owner.ActualThemeVariant,
            Content = new Border { Classes = { "glass", "glass-float" }, Margin = new Thickness(12), Padding = new Thickness(14), Child = panel }
        };
        var result = false;
        confirm.Click += (_, _) => { result = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
        if (!result) return false;

        if (fromPlaylist) Host.Player.Remove(rows);
        else await Host.Adapter.RemoveLocalTracksAsync(rows.Select(r => r.Track.Id).ToArray());
        return Host.Adapter.State.Error.Length == 0;
    }
}
