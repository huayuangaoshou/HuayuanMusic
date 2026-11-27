using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.Models;

namespace HuayuanPreview.R2;

public sealed class SpectrumCanvas : Control
{
    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<SpectrumCanvas, IBrush?>(nameof(BarBrush));
    public static readonly StyledProperty<Color> PinkProperty =
        AvaloniaProperty.Register<SpectrumCanvas, Color>(nameof(Pink));
    public static readonly StyledProperty<Color> BlueProperty =
        AvaloniaProperty.Register<SpectrumCanvas, Color>(nameof(Blue));

    private const int FullBandCount = 96;
    private const int OpacityLevels = 7;
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private VisualizerBandState[]? source;
    private ImmutableSolidColorBrush[]? palette;
    private Window? owner;
    private bool playing;

    public IBrush? BarBrush { get => GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public Color Pink { get => GetValue(PinkProperty); set => SetValue(PinkProperty, value); }
    public Color Blue { get => GetValue(BlueProperty); set => SetValue(BlueProperty, value); }
    public bool Wide { get; set; }

    static SpectrumCanvas() => AffectsRender<SpectrumCanvas>(BarBrushProperty, BoundsProperty);

    public SpectrumCanvas()
    {
        clock.Tick += (_, _) =>
        {
            if (CanUpdate) InvalidateVisual();
            else clock.Stop();
        };
        EffectiveViewportChanged += (_, _) => UpdateClock();
    }

    internal void SetSource(VisualizerBandState[]? bars)
    {
        source = bars;
        UpdateClock();
        InvalidateVisual();
    }

    internal void SetPlaying(bool value)
    {
        if (playing == value) return;
        playing = value;
        UpdateClock();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null) owner.PropertyChanged += OwnerChanged;
        UpdateClock();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (owner != null) owner.PropertyChanged -= OwnerChanged;
        owner = null;
        clock.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PinkProperty || change.Property == BlueProperty)
        {
            palette = null;
            InvalidateVisual();
        }
        else if (change.Property == IsVisibleProperty)
            UpdateClock();
    }

    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == Window.WindowStateProperty) UpdateClock();
    }

    private bool CanUpdate => playing && source != null && this.IsAttachedToVisualTree() &&
        IsEffectivelyVisible && owner is { IsVisible: true, WindowState: not WindowState.Minimized } &&
        !DesignTokens.ShaderDegrade;

    private void UpdateClock()
    {
        if (CanUpdate) clock.Start();
        else clock.Stop();
    }

    public override void Render(DrawingContext context)
    {
        if (source == null || Bounds.Width < 2 || Bounds.Height < 2) return;
        var count = Math.Min(Math.Max(1, (int)(Bounds.Width / 2)), Wide ? FullBandCount : 48);
        var step = Bounds.Width / count;
        var brush = BarBrush;
        var colors = Wide ? palette ??= CreatePalette(Pink, Blue) : null;
        if (brush == null && colors == null) return;

        var animated = playing && !DesignTokens.ShaderDegrade;
        for (var i = 0; i < count; i++)
        {
            var peak = 6d;
            if (animated)
            {
                var end = (i + 1) * source.Length / count;
                for (var band = i * source.Length / count; band < end; band++)
                    peak = Math.Max(peak, source[band].Height);
            }
            var level = Math.Clamp((peak - 6) / 122, 0, 1);
            var height = 2 + level * (Bounds.Height - 2);
            var fill = colors == null ? brush : colors[i * FullBandCount / count * OpacityLevels +
                (int)Math.Round(level * (OpacityLevels - 1))];
            context.DrawRectangle(fill, null, new Rect((i + .19) * step, Bounds.Height - height, step * .62, height));
        }
    }

    private static ImmutableSolidColorBrush[] CreatePalette(Color pink, Color blue)
    {
        var colors = new ImmutableSolidColorBrush[FullBandCount * OpacityLevels];
        for (var i = 0; i < FullBandCount; i++)
        {
            var mix = i / (double)(FullBandCount - 1);
            byte Blend(byte a, byte b) => (byte)Math.Round(a + (b - a) * mix);
            var hue = Color.FromRgb(Blend(pink.R, blue.R), Blend(pink.G, blue.G), Blend(pink.B, blue.B));
            for (var level = 0; level < OpacityLevels; level++)
                colors[i * OpacityLevels + level] = new ImmutableSolidColorBrush(hue, .10 + .01 * level);
        }
        return colors;
    }
}
