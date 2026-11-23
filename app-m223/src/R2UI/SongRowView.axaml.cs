using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

public partial class SongRowView : UserControl
{
    public SongRowView() => InitializeComponent();

    private void CommentInline(object? sender, RoutedEventArgs args)
    {
        if (DataContext is TrackRow row) this.FindAncestorOfType<R2ShellView>()?.OpenSongComments(row);
    }

    private async void DownloadInline(object? sender, RoutedEventArgs args)
    {
        if (DataContext is TrackRow row && this.FindAncestorOfType<R2ShellView>() is { } shell)
            await shell.DownloadSongAsync(row);
    }

    private void PlayInline(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not TrackRow row) return;
        var list = this.FindAncestorOfType<ListBox>();
        if (list != null && InteractionController.Find(list) is { } controller && Program.Desktop)
            controller.PlayRow(row);
        else
            row.PlayCommand.Execute(null);
    }
}
