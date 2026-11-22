using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

public partial class SongRowView : UserControl
{
    public SongRowView() => InitializeComponent();

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
