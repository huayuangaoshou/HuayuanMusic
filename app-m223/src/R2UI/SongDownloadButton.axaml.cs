using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

public partial class SongDownloadButton : UserControl
{
    private TrackRow? row;
    private bool attached;

    public SongDownloadButton()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { attached = true; BindRow(); };
        DetachedFromVisualTree += (_, _) => { attached = false; BindRow(); };
        DataContextChanged += (_, _) => BindRow();
        IsVisible = false;
    }

    private void BindRow()
    {
        if (row != null)
        {
            row.PropertyChanged -= RowChanged;
            row.Owner.Host.DownloadChanged -= RefreshState;
        }

        row = attached ? DataContext as TrackRow : null;
        if (row != null)
        {
            row.PropertyChanged += RowChanged;
            row.Owner.Host.DownloadChanged += RefreshState;
        }
        RefreshState();
    }

    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrackRow.CanUseOnlineActions) or nameof(TrackRow.IsDownloaded))
            RefreshState();
    }

    private void RefreshState()
    {
        IsVisible = row?.CanUseOnlineActions == true;
        if (!IsVisible || row == null) return;

        var host = row.Owner.Host;
        bool busy = host.DownloadingTrack == row.CatalogIdentity;
        bool saved = !busy && row.IsDownloaded;
        DownloadIcon.IsVisible = !busy && !saved;
        DownloadedIcon.IsVisible = saved;
        InlineProgress.IsVisible = busy;
        InlineProgress.IsIndeterminate = busy && host.DownloadPercent == null;
        InlineProgress.Value = host.DownloadPercent ?? 0;
        string label = busy ? host.DownloadPercent is { } percent ? $"下载中 {percent}%" : "下载中" :
            saved ? "已下载到本地" : "下载歌曲";
        AutomationProperties.SetName(ActionButton, label);
        ToolTip.SetTip(ActionButton, label);
    }

    private async void Download(object? sender, RoutedEventArgs e)
    {
        if (row is { } track && this.FindAncestorOfType<R2ShellView>() is { } shell)
            await shell.DownloadSongAsync(track);
    }
}
