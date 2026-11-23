using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview.R2;

public sealed partial class ShellModel
{
    internal DesktopDlcState DlcState { get; private set; } = DesktopPreferences.Behavior.Kugou;
    public bool KugouEnabled => DlcState == DesktopDlcState.Enabled;
    internal Task DlcPending { get; private set; } = Task.CompletedTask;
    internal Task ShowTrackCollectionAsync(TrackRow row, bool artist)
    {
        if(!KugouEnabled)return Task.CompletedTask;
        Switch(KugouConceptWorkspace);
        return KugouConceptWorkspace.OpenOnlineSearchAsync(artist ? row.Singer : row.AlbumName,
            artist ? DesktopCatalogKind.Artist : DesktopCatalogKind.Album);
    }
    internal Task ChangeDlc(DesktopDlcState next)
    {
        if (!DlcPending.IsCompleted || next == DlcState) return DlcPending;
        return DlcPending = ApplyDlcAsync(next);
    }

    private async Task ApplyDlcAsync(DesktopDlcState next)
    {
        await DesktopPreferences.SetBehaviorAsync(options => options with { Kugou = next });
        DlcState = next;
        OnPropertyChanged(nameof(KugouEnabled));
        if (next == DesktopDlcState.Enabled)
        {
            await Concept.RestoreDesktopConnectionAsync();
        }
        else
        {
            if (Workspace.IsKugou) Switch(LocalWorkspace);
            KugouConceptWorkspace.PauseOnlineWork();
            Concept.CancelDesktopOperations();
            await Concept.Pending;
            await Concept.CloseDesktopConnectionAsync();
            await Player.Pending;
            if (next == DesktopDlcState.Removed) Adapter.RemoveKugou();
            await Adapter.SaveDesktopQueueAsync();
        }
        Program.Log("DESKTOP_DLC_STATE", new { state = next.ToString(), connected = Concept.Connected });
    }
}

internal sealed class PluginManager : ScrollViewer
{
    internal PluginManager(ShellModel shell)
    {
        var status = new TextBlock { Classes = { "muted" } };
        var activity = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        var enable = new Button { Classes = { "glass-pill" } };
        var remove = new Button { Content = "卸载平台挂载", Classes = { "glass-pill" } };
        var account = new Button { Content = "打开酷狗工作空间", Classes = { "glass-pill" } };
        var inspect = new Button { Content = "检查本地 Contracts 包目录", Classes = { "glass-pill" } };
        async Task ChangeAsync(DesktopDlcState next)
        {
            enable.IsEnabled = remove.IsEnabled = false;
            activity.Text = "正在切换平台状态…";
            try { await shell.ChangeDlc(next); activity.Text = ""; }
            catch (Exception error)
            {
                activity.Text = "平台状态切换未完成";
                Program.Log("DESKTOP_DLC_ERROR", new { type = error.GetType().Name });
            }
            Refresh();
        }
        void Refresh()
        {
            status.Text = shell.DlcState switch { DesktopDlcState.Enabled => "已启用", DesktopDlcState.Disabled => "已停用", _ => "未安装" };
            enable.Content = shell.DlcState switch { DesktopDlcState.Enabled => "停用", DesktopDlcState.Disabled => "启用", _ => "恢复内置 DLC" };
            enable.IsEnabled = true;
            remove.IsEnabled = shell.DlcState != DesktopDlcState.Removed;
            account.IsVisible = shell.KugouEnabled;
        }
        enable.Click += async (_, _) => await ChangeAsync(shell.KugouEnabled ? DesktopDlcState.Disabled : DesktopDlcState.Enabled);
        remove.Click += async (_, _) => await ChangeAsync(DesktopDlcState.Removed);
        account.Click += (_, _) => shell.Switch(shell.KugouConceptWorkspace);
        inspect.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not { } window) return;
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择 Contracts 包目录" });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            activity.Text = File.Exists(Path.Combine(path, "manifest.json"))
                ? "已找到清单。当前版本只加载随应用验证的内置插件。" : "所选目录未包含 manifest.json。";
        };
        var tags = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var text in new[] { "在线搜索", "歌单同步", "逐字歌词", "主版评论", "音质切换", "每日推荐" })
            tags.Children.Add(new Border { Classes = { "glass" }, Padding = new Thickness(10, 5), Child = new TextBlock { Text = text } });
        var actions = new WrapPanel { ItemSpacing = 7, LineSpacing = 7, Children = { enable, remove, account } };
        var body = new StackPanel { Spacing = 12, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6) };
        body.Children.Add(new Border
        {
            Classes = { "glass" }, Padding = new Thickness(18),
            Child = new StackPanel { Spacing = 11, Children = {
                new TextBlock { Text = "酷狗概念版 DLC", FontSize = 21, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = $"版本 {TrustedModuleDescriptor.Current.Version}", Classes = { "muted" } }, status, tags, actions } }
        });
        body.Children.Add(inspect);
        body.Children.Add(activity);
        Content = body;
        Refresh();
    }
}
