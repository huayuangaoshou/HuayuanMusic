using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using KugouAvaloniaPlayer.Services;

namespace HuayuanPreview
{
    internal sealed record ManagedMusicFolder(long Id, string Path);

    internal sealed partial class CorePlaybackAdapter
    {
        internal Task<ManagedMusicFolder[]> ReadManagedFoldersAsync() => Task.Run(async () =>
        {
            await using var db = await AppDatabase.CreateConnectionAsync();
            await using var query = db.CreateCommand();
            query.CommandText = "SELECT id,source_ref FROM playlists WHERE kind = 'ImportedFolder' AND source_ref IS NOT NULL ORDER BY id";
            await using var rows = await query.ExecuteReaderAsync();
            var result = new List<ManagedMusicFolder>();
            while (await rows.ReadAsync()) result.Add(new(rows.GetInt64(0), rows.GetString(1)));
            return result.ToArray();
        });

        internal Task AddManagedFoldersAsync(IEnumerable<string> paths) => Run("ImportFolders", async () =>
        {
            await Task.Run(async () =>
            {
                foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                    await Store.ImportFolderAsync(path).ConfigureAwait(false);
            });
            await ReloadAllAsync();
        });

        internal Task RescanManagedFoldersAsync() => Run("RescanFolders", async () =>
        {
            var folders = await ReadManagedFoldersAsync();
            await Task.Run(async () =>
            {
                foreach (var folder in folders)
                    if (Directory.Exists(folder.Path)) await Store.ImportFolderAsync(folder.Path).ConfigureAwait(false);
            });
            await ReloadAllAsync();
        });

        internal Task RemoveManagedFolderAsync(long id) => Run("RemoveFolder", async () =>
        {
            if (!(await ReadManagedFoldersAsync()).Any(folder => folder.Id == id))
                throw new InvalidOperationException("文件夹来源已变化");
            await Task.Run(() => Store.DeletePlaylistAsync(id));
            await ReloadAllAsync();
        });
    }
}

namespace HuayuanPreview.R2
{
    public sealed partial class WorkspaceState
    {
        private R2Page? folderPage;
        private StackPanel? folderList;
        private TextBlock? folderStatus;
        private bool folderBusy;
        internal Task PendingFolderWork { get; private set; } = Task.CompletedTask;

        private async void OpenFolderManager()
        {
            if (folderPage == null)
            {
                folderList = new StackPanel { Spacing = 5 };
                folderStatus = new TextBlock { Classes = { "muted" } };
                var add = new Button { Classes = { "primary" }, Content = "添加文件夹" };
                var refresh = new Button { Content = "重新扫描" };
                add.Click += async (_, _) =>
                {
                    if (folderBusy || TopLevel.GetTopLevel(add) is not { } owner) return;
                    var selected = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        AllowMultiple = true, Title = "选择本地音乐目录"
                    });
                    var paths = selected.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToArray();
                    if (paths.Length > 0) await SubmitFolderActionAsync(() => Host.Adapter.AddManagedFoldersAsync(paths));
                };
                refresh.Click += async (_, _) => await SubmitFolderActionAsync(Host.Adapter.RescanManagedFoldersAsync);
                var controls = new WrapPanel { Children = { add, refresh } };
                folderPage = new R2Page("本地文件夹", new ScrollViewer
                {
                    Content = new StackPanel { Spacing = 12, Children = { controls, folderStatus, folderList } }
                });
            }
            Navigate(folderPage);
            await SubmitFolderActionAsync(() => Task.CompletedTask);
        }

        private Task SubmitFolderActionAsync(Func<Task> operation) =>
            folderBusy ? PendingFolderWork : PendingFolderWork = RunFolderActionAsync(operation);

        private async Task RunFolderActionAsync(Func<Task> operation)
        {
            folderBusy = true;
            folderStatus!.Text = "正在处理本地文件夹…";
            try
            {
                await operation();
                var folders = await Host.Adapter.ReadManagedFoldersAsync();
                folderList!.Children.Clear();
                foreach (var folder in folders)
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                    row.Children.Add(new TextBlock { Text = folder.Path, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
                    var remove = new Button { Content = "移除扫描来源" };
                    remove.Click += async (_, _) => await SubmitFolderActionAsync(() => Host.Adapter.RemoveManagedFolderAsync(folder.Id));
                    Grid.SetColumn(remove, 1);
                    row.Children.Add(remove);
                    folderList.Children.Add(row);
                }
                folderStatus.Text = Host.Player.State.Error.Length > 0 ? Host.Player.State.Error :
                    folders.Length == 0 ? "尚未添加文件夹" : $"{folders.Length} 个扫描来源";
                Program.Log("R2_LOCAL_FOLDERS", new { count = folders.Length, tracks = Host.Player.State.Tracks.Length });
            }
            catch (Exception error)
            {
                folderStatus.Text = "本地文件夹操作未完成";
                Program.Log("R2_LOCAL_FOLDER_FAILED", new { type = error.GetType().Name });
            }
            finally { folderBusy = false; }
        }
    }

    public sealed partial class ShellModel
    {
        internal CorePlaybackAdapter Adapter { get; private set; } = null!;
    }
}
