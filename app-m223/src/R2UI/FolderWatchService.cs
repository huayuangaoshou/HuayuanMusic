using Avalonia.Threading;

namespace HuayuanPreview.R2;

internal sealed class FolderWatchService(ShellModel shell)
{
    private static readonly string[] Patterns =
        ["*.ape", "*.mp3", "*.flac", "*.wav", "*.ogg", "*.m4a", "*.aac", "*.webm", "*.dsf", "*.dff"];
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(Paths);
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromSeconds(30) };
    private Task scan = Task.CompletedTask;
    private int changePosted;
    private bool enabled;
    private bool disposed;
    private bool rescanPending;
    private TimerCensus? debounceCensus;

    internal async Task SetEnabledAsync(bool value)
    {
        if (disposed) return;
        enabled = value;
        debounce.Tick -= OnDebounce;
        if (!enabled)
        {
            Sync([]);
            debounce.Stop();
            rescanPending = false;
            return;
        }
        debounce.Tick += OnDebounce;
        try { Sync(await shell.Adapter.ReadManagedFoldersAsync()); }
        catch (Exception error) { ReportFailure(error); }
    }

    internal void Sync(ManagedMusicFolder[] folders)
    {
        if (disposed) return;
        var paths = enabled ? folders.Select(folder => folder.Path).Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(Paths) : new HashSet<string>(Paths);
        foreach (var path in watchers.Keys.Where(path => !paths.Contains(path)).ToArray())
        {
            watchers[path].Dispose();
            watchers.Remove(path);
        }
        foreach (var path in paths.Where(path => !watchers.ContainsKey(path)))
        {
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size
                };
                foreach (var pattern in Patterns) watcher.Filters.Add(pattern);
                watcher.Created += OnChange;
                watcher.Changed += OnChange;
                watcher.Deleted += OnChange;
                watcher.Renamed += OnChange;
                watcher.Error += (_, args) => { ReportFailure(args.GetException()); PostChange(); };
                watcher.EnableRaisingEvents = true;
                watchers.Add(path, watcher);
            }
            catch (Exception error)
            {
                watcher?.Dispose();
                ReportFailure(error);
            }
        }
        Program.Log("R23_FOLDER_WATCH", new { enabled, active = watchers.Count });
    }

    private void OnChange(object sender, FileSystemEventArgs args) => PostChange();

    private void PostChange()
    {
        if (Interlocked.Exchange(ref changePosted, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref changePosted, 0);
            if (!enabled || disposed || watchers.Count == 0) return;
            debounce.Stop();
            debounce.Start();
        });
    }

    private void OnDebounce(object? sender, EventArgs args)
    {
        using var timing = (debounceCensus ??= new TimerCensus("folderWatchDebounce", debounce)).Measure();
        debounce.Stop();
        if (!enabled || disposed || watchers.Count == 0) return;
        if (!scan.IsCompleted) rescanPending = true;
        else scan = ScanAsync();
    }

    private async Task ScanAsync()
    {
        do
        {
            rescanPending = false;
            try
            {
                Program.Log("R23_FOLDER_SCAN_START", new { watchers = watchers.Count });
                await shell.LocalWorkspace.ScanWatchedFoldersAsync(() => enabled && !disposed && watchers.Count > 0);
                Program.Log("R23_FOLDER_SCAN_END", new
                {
                    tracks = shell.Adapter.State.Tracks.Length, failed = shell.Adapter.State.Error.Length > 0
                });
            }
            catch (Exception error) { ReportFailure(error); }
        } while (rescanPending && enabled && !disposed);
    }

    internal async Task StopAsync()
    {
        await SetEnabledAsync(false);
        disposed = true;
        await scan;
    }

    private static void ReportFailure(Exception error) =>
        Program.Log("R23_FOLDER_WATCH_FAILED", new { type = error.GetType().Name });
}
