using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

internal static class ArtworkPlaceholder
{
    private static readonly Geometry Note = Geometry.Parse("M9,3 L20,1 L20,15 C20,19 14,21 12,18 C10,15 13,13 17,13 L17,6 L12,7 L12,18 C12,22 6,24 4,21 C2,18 5,16 9,16 Z");
    private static readonly LinearGradientBrush Background = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = new() { new(Color.Parse("#FDE6F1"), 0), new(Color.Parse("#DEEBFF"), 1) }
    };

    internal static Control Create(double size) => new Border
    {
        Width = size, Height = size, Background = Background,
        CornerRadius = new CornerRadius(size >= 100 ? 12 : 6),
        Child = new PathIcon
        {
            Data = Note, Foreground = new SolidColorBrush(Color.Parse("#8E5972")),
            Width = size * .34, Height = size * .34,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        }
    };
}

public sealed class TrackArtwork : ContentControl
{
    public double DisplaySize { get; set; } = 38;
    private int generation;
    private bool attached;

    public TrackArtwork()
    {
        DataContextChanged += (_, _) => { if (attached) UpdateArtwork(); };
        AttachedToVisualTree += (_, _) => { attached = true; UpdateArtwork(); };
        DetachedFromVisualTree += (_, _) => { attached = false; generation++; Content = null; };
    }

    private void UpdateArtwork()
    {
        int revision = ++generation;
        if (DataContext is not TrackRow row || row.Track.Cover == null)
        {
            Content = ArtworkPlaceholder.Create(DisplaySize);
            return;
        }
        if (row.IsProviderTrack)
        {
            var account = row.Track.Key.Split(':', 3)[1];
            Content = row.Owner.Host.Concept.Cover(account, row.Track.Cover, DisplaySize);
            return;
        }

        var image = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        image.Transitions = new() { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new Avalonia.Animation.Easings.CubicEaseOut() } };
        Content = new Border
        {
            Width = DisplaySize, Height = DisplaySize, CornerRadius = new CornerRadius(7), ClipToBounds = true,
            Child = new Grid { Children = { ArtworkPlaceholder.Create(DisplaySize), image } }
        };
        _ = ShowLocalAsync(row, image, revision);
    }

    private async Task ShowLocalAsync(TrackRow row, Image image, int revision)
    {
        try
        {
            Bitmap? bitmap = await row.Owner.Host.Concept.LoadLocalCover(row.Track.Cover!).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (revision != generation || bitmap == null)
                    return;
                image.Source = bitmap;
                image.Opacity = 1;
            });
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            KugouArtwork.Diagnostic("LOCAL_ARTWORK_UNAVAILABLE");
        }
    }
}
