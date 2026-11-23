using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview.R2;

public sealed partial class ShellModel
{
    internal string? DownloadingTrack { get; private set; }
    internal int? DownloadPercent { get; private set; }
    internal event Action? DownloadChanged;

    internal void SetDownloadState(string? identity, int? percent = null)
    {
        DownloadingTrack = identity;
        DownloadPercent = percent;
        DownloadChanged?.Invoke();
    }

    private HashSet<string> downloadedNames = new(StringComparer.OrdinalIgnoreCase);
    private CoreTrack[]? indexedDownloads;
    private DesktopOptions? indexedOptions;
    private HashSet<string> downloadedPaths = new(StringComparer.OrdinalIgnoreCase);

    internal bool IsDownloaded(TrackRow row)
    {
        var tracks = Player.State.Tracks;
        var options = DesktopPreferences.Behavior;
        if (!ReferenceEquals(tracks, indexedDownloads) || !ReferenceEquals(options, indexedOptions))
        {
            indexedDownloads = tracks;
            indexedOptions = options;
            var roots = new[] { DesktopPreferences.DownloadFolderFor(options) }
                .Concat(options.PreviousDownloadFolders ?? []).Select(Path.TrimEndingDirectorySeparator)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            downloadedPaths = tracks.Where(track => Path.IsPathFullyQualified(track.Key) &&
                Path.GetDirectoryName(track.Key) is { } parent && roots.Contains(parent) &&
                Path.GetExtension(track.Key).ToLowerInvariant() is ".flac" or ".mp3" or ".m4a" or ".ogg")
                .Select(track => track.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            downloadedNames = downloadedPaths.Select(key => Path.GetFileNameWithoutExtension(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return row.IsProviderTrack ? downloadedNames.Contains(DesktopSongDownload.FileStem(row.Singer, row.Name))
            : downloadedPaths.Contains(row.Track.Key);
    }
}

public partial class R2ShellView
{
    private CancellationTokenSource? downloadRequest;

    internal async Task DownloadSongAsync(TrackRow row)
    {
        if (model is not { } shell || !row.CanUseOnlineActions) return;
        if (row.IsDownloaded) { ShowToast("这首歌已下载"); return; }
        if (downloadRequest != null) { ShowToast("已有歌曲正在下载"); return; }
        using var request = new CancellationTokenSource();
        downloadRequest = request;
        shell.SetDownloadState(row.CatalogIdentity);
        var progress = new Progress<int>(value =>
        {
            if (!ReferenceEquals(downloadRequest, request)) return;
            shell.SetDownloadState(row.CatalogIdentity, value);
        });
        try
        {
            ShowToast("正在下载歌曲…");
            string path = await shell.Concept.SaveDesktopSongAsync(row.Track.Key, DesktopPreferences.Quality,
                DesktopPreferences.DownloadFolder, ((IProgress<int>)progress).Report, request.Token);
            while (shell.Adapter.State.Importing)
                await Task.Delay(150, request.Token);
            await shell.Adapter.ImportFilesAsync([path], request.Token);
            bool imported = shell.Adapter.State.Tracks.Any(track =>
                string.Equals(track.Key, path, StringComparison.OrdinalIgnoreCase));
            shell.LocalWorkspace.RefreshPlayingRows();
            shell.KugouConceptWorkspace.RefreshPlayingRows();
            ShowToast(imported ? $"已下载：{Path.GetFileName(path)}" : "歌曲已保存，请从下载目录导入曲库");
        }
        catch (OperationCanceledException) { ShowToast("已取消下载"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or
            InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            ShowToast("暂时无法下载这首歌，请检查音质权限和下载位置");
            Program.Log("DESKTOP_SONG_DOWNLOAD_FAILED", new { type = error.GetType().Name });
        }
        finally
        {
            downloadRequest = null;
            shell.SetDownloadState(null);
        }
    }

    private void OpenDownloadFolder()
    {
        try
        {
            var folder = DesktopPreferences.DownloadFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException)
        { ShowToast("暂时无法打开下载目录"); }
    }
}
