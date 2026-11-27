using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using KugouAvaloniaPlayer.Services;

namespace HuayuanPreview
{
    internal sealed record ManagedMusicFolder(long Id, string Path);

    internal sealed partial class CorePlaybackAdapter
    {
        private async Task<HashSet<long>> ReadEditablePlaylistIdsAsync()
        {
            await using var db=await AppDatabase.CreateConnectionAsync();
            await using var query=db.CreateCommand();
            // 精准黑名单保护：仅排除在线同步歌单(JellyfinLibrary)，
            // 手动新建(Manual)与本地导入文件夹(ImportedFolder)等本地歌单均允许删除。
            query.CommandText="SELECT id FROM playlists WHERE kind <> 'JellyfinLibrary'";
            await using var rows=await query.ExecuteReaderAsync();
            var ids=new HashSet<long>();
            while(await rows.ReadAsync())ids.Add(rows.GetInt64(0));
            return ids;
        }

        internal Task RemoveLocalTracksAsync(int[] ids) => Run("RemoveLocalTracks", () => RemoveLocalTracksCoreAsync(ids));

        private async Task RemoveLocalTracksCoreAsync(int[] ids, bool missingFiles = false)
        {
            // Rescan removes records for paths already checked on disk. Opening those paths
            // through Song() would require existing media in an isolated library.
            var tracks = ids.Distinct().Select(id => missingFiles ? items[id] : Song(id)).ToArray();
            if(tracks.Any(track=>track.LocalSourceType!="LocalFile"))
                throw new InvalidOperationException("只能移除本地曲目");
            await using(var db=await AppDatabase.CreateConnectionAsync())
            {
                using var transaction=db.BeginTransaction();
                foreach(var track in tracks)
                {
                    await using var delete=db.CreateCommand();
                    delete.Transaction=transaction;
                    delete.CommandText="DELETE FROM tracks WHERE id=$id AND source_type='LocalFile'";
                    delete.Parameters.AddWithValue("$id",track.LocalTrackId);
                    await delete.ExecuteNonQueryAsync();
                }
                await transaction.CommitAsync();
            }
            var removed=ids.ToHashSet();
            foreach(var entry in root.Player.PlaybackQueue.Where(entry=>removed.Contains(Id(entry))).ToArray())
                await root.Player.RemoveFromQueueCommand.ExecuteAsync(entry);
            await ReloadAllAsync();
            RefreshRecords();
            if(PersistQueue)root.Player.FlushControlledQueue();
        }

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
            var known = items.Where(pair => pair.Value.LocalSourceType == "LocalFile")
                .Select(pair => (Id: pair.Key, Path: pair.Value.LocalFilePath)).ToArray();
            var removed = await Task.Run(async () =>
            {
                var scanned = new List<string>();
                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder.Path)) continue;
                    await Store.ImportFolderAsync(folder.Path).ConfigureAwait(false);
                    scanned.Add(folder.Path);
                }
                return known.Where(entry => entry.Path != null && scanned.Any(folder => IsMissingWithin(folder, entry.Path)))
                    .Select(entry => entry.Id).ToArray();
            });
            if (removed.Length == 0) await ReloadAllAsync();
            else await RemoveLocalTracksCoreAsync(removed, missingFiles: true);

            static bool IsMissingWithin(string folder, string file)
            {
                var relative = Path.GetRelativePath(folder, file);
                if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    Path.IsPathRooted(relative) || !Directory.Exists(folder)) return false;
                try { return File.GetAttributes(file).HasFlag(FileAttributes.Directory); }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return true; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
            }
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
                var import = new Button { Content = "导入音频文件" };
                import.Click += async (_, _) =>
                {
                    if (folderBusy || TopLevel.GetTopLevel(import) is not { } owner) return;
                    var selected = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        AllowMultiple = true, Title = "导入音频文件",
                        FileTypeFilter = [new FilePickerFileType("音频文件")
                        { Patterns = ["*.mp3", "*.flac", "*.wav", "*.m4a", "*.ogg", "*.opus", "*.aac"] }]
                    });
                    var paths=selected.Select(file=>file.TryGetLocalPath()).OfType<string>().ToArray();
                    if(paths.Length>0)await SubmitFolderActionAsync(()=>Host.Adapter.ImportFilesAsync(paths,CancellationToken.None));
                };
                var controls = new WrapPanel { Children = { add, import, refresh } };
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

        internal async Task ScanWatchedFoldersAsync(Func<bool> stillEnabled)
        {
            while (folderBusy) await PendingFolderWork;
            if (stillEnabled()) await SubmitFolderActionAsync(Host.Adapter.RescanManagedFoldersAsync);
        }

        private async Task RunFolderActionAsync(Func<Task> operation)
        {
            folderBusy = true;
            if (folderStatus != null) folderStatus.Text = "正在处理本地文件夹…";
            try
            {
                await operation();
                var folders = await Host.Adapter.ReadManagedFoldersAsync();
                Host.FolderWatch?.Sync(folders);
                if (folderList == null || folderStatus == null) return;
                folderList.Children.Clear();
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
                var tracks = Host.Adapter.State.Tracks.Count(track=>!track.Key.StartsWith("kugou-concept:",StringComparison.Ordinal));
                folderStatus.Text = Host.Adapter.State.Error.Length > 0 ? "本地文件夹操作未完成" :
                    $"已导入 {tracks} 首本地音乐 · 自动扫描文件夹 {folders.Length} 个";
                Program.Log("R2_LOCAL_FOLDERS", new { count = folders.Length, tracks = Host.Player.State.Tracks.Length });
            }
            catch (Exception error)
            {
                if (folderStatus != null) folderStatus.Text = "本地文件夹操作未完成";
                Program.Log("R2_LOCAL_FOLDER_FAILED", new { type = error.GetType().Name });
            }
            finally { folderBusy = false; }
        }
    }

    public sealed partial class ShellModel
    {
        internal CorePlaybackAdapter Adapter { get; private set; } = null!;
        internal FolderWatchService? FolderWatch { get; set; }
    }
}
