using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    internal void InstallResizeHandles(Window window)
    {
        const double edgeSize = 7;
        var edges = new Grid { ZIndex = 20, IsHitTestVisible = window.WindowState == WindowState.Normal };
        Grid.SetColumnSpan(edges, 2);
        Root.Children.Add(edges);
        window.PropertyChanged += (_, change) =>
        {
            if (change.Property == Window.WindowStateProperty)
                edges.IsHitTestVisible = window.WindowState == WindowState.Normal;
        };

        Add(WindowEdge.West, StandardCursorType.SizeWestEast, HorizontalAlignment.Left, VerticalAlignment.Stretch);
        Add(WindowEdge.East, StandardCursorType.SizeWestEast, HorizontalAlignment.Right, VerticalAlignment.Stretch);
        Add(WindowEdge.North, StandardCursorType.SizeNorthSouth, HorizontalAlignment.Stretch, VerticalAlignment.Top);
        Add(WindowEdge.South, StandardCursorType.SizeNorthSouth, HorizontalAlignment.Stretch, VerticalAlignment.Bottom);
        Add(WindowEdge.NorthWest, StandardCursorType.TopLeftCorner, HorizontalAlignment.Left, VerticalAlignment.Top);
        Add(WindowEdge.NorthEast, StandardCursorType.TopRightCorner, HorizontalAlignment.Right, VerticalAlignment.Top);
        Add(WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        Add(WindowEdge.SouthEast, StandardCursorType.BottomRightCorner, HorizontalAlignment.Right, VerticalAlignment.Bottom);

        void Add(WindowEdge edge, StandardCursorType cursor, HorizontalAlignment horizontal, VerticalAlignment vertical)
        {
            var handle = new Border
            {
                Tag = edge,
                Background = Brushes.Transparent,
                Cursor = new Cursor(cursor),
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Width = horizontal == HorizontalAlignment.Stretch ? double.NaN : edgeSize,
                Height = vertical == VerticalAlignment.Stretch ? double.NaN : edgeSize,
                Margin = new Thickness(horizontal == HorizontalAlignment.Stretch ? edgeSize : 0,
                    vertical == VerticalAlignment.Stretch ? edgeSize : 0)
            };
            handle.PointerPressed += (_, args) =>
            {
                if (window.WindowState != WindowState.Normal || !args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
                    return;
                args.Handled = true;
                window.BeginResizeDrag(edge, args);
            };
            edges.Children.Add(handle);
        }
    }
}
