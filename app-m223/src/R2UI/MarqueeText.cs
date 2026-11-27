using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

public sealed class MarqueeText : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<MarqueeText, string?>(nameof(Text));
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private const double Hold = 1.7, Speed = 34;
    private readonly Glyphs glyphs;
    private FormattedText? formatted;
    private double overflow, phase, maskWidth;
    private long started;
    private CompositionVisual? animated;

    public MarqueeText()
    {
        ClipToBounds = true;
        glyphs = new Glyphs(this) { IsHitTestVisible = false };
        Template = new FuncControlTemplate<MarqueeText>((_, _) => glyphs);
        SizeChanged += (_, _) => UpdateOverflow();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == ForegroundProperty ||
            change.Property == FontSizeProperty || change.Property == FontFamilyProperty ||
            change.Property == FontWeightProperty || change.Property == FontStyleProperty)
        {
            formatted = null;
            Reset();
            glyphs.InvalidateVisual();
            InvalidateMeasure();
            if (this.IsAttachedToVisualTree()) UpdateOverflow();
        }
        else if (change.Property == IsVisibleProperty && this.IsAttachedToVisualTree())
        {
            if (IsEffectivelyVisible && !IsPointerOver) Start();
            else Pause();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UpdateOverflow();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        Pause();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Reset();
        base.OnDetachedFromVisualTree(e);
    }

    private FormattedText Layout() => formatted ??= new FormattedText(Text ?? "", CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyle, FontWeight), FontSize, Foreground);

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Layout();
        var size = new Size(Math.Min(availableSize.Width, text.WidthIncludingTrailingWhitespace), text.Height);
        glyphs.Measure(new Size(text.WidthIncludingTrailingWhitespace, text.Height));
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        glyphs.Arrange(new Rect(0, 0, Math.Max(finalSize.Width, Layout().WidthIncludingTrailingWhitespace), finalSize.Height));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        // The fixed viewport owns hover, including the gaps between moving glyphs.
        if (overflow > 1) context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
    }

    private void UpdateOverflow()
    {
        var next = Bounds.Width > 0 ? Math.Max(0, Layout().WidthIncludingTrailingWhitespace - Bounds.Width) : 0;
        if (Math.Abs(next - overflow) > .5 || next <= 1 && overflow > 1)
        {
            Reset();
            overflow = next;
            InvalidateVisual();
        }
        if (overflow <= 1)
        {
            OpacityMask = null;
            maskWidth = 0;
            return;
        }
        if (maskWidth != Bounds.Width)
        {
            maskWidth = Bounds.Width;
            var edge = Math.Min(.08, 6 / maskWidth);
            OpacityMask = new LinearGradientBrush { GradientStops =
                [new(Colors.Transparent, 0), new(Colors.Black, edge), new(Colors.Black, 1 - edge), new(Colors.Transparent, 1)] };
        }
        if (!IsPointerOver) Start();
    }

    private double Period => 2 * (Hold + overflow / Speed);
    private Vector3D TranslationAt(double time)
    {
        var travel = overflow / Speed;
        var offset = time <= Hold ? 0 : time <= Hold + travel ? (time - Hold) * Speed :
            time <= 2 * Hold + travel ? overflow : Math.Max(0, overflow - (time - 2 * Hold - travel) * Speed);
        return new Vector3D(-offset, 0, 0);
    }

    private void Start()
    {
        if (animated != null || overflow <= 1 || !IsEffectivelyVisible ||
            ElementComposition.GetElementVisual(glyphs) is not { } visual) return;
        var period = Period;
        var travel = overflow / Speed;
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.Duration = TimeSpan.FromSeconds(period);
        animation.InsertKeyFrame(0, TranslationAt(phase));
        var corners = new[] { 0d, Hold, Hold + travel, 2 * Hold + travel }
            .Select(point => (Point: point, Relative: (point - phase + period) % period))
            .OrderBy(corner => corner.Relative);
        var previous = 0d;
        foreach (var (point, relative) in corners)
        {
            if (relative <= .00001 || relative >= period - .00001) continue;
            animation.InsertKeyFrame((float)(relative / period), TranslationAt(point), new PlaybackAnimationPacing(relative - previous));
            previous = relative;
        }
        animation.InsertKeyFrame(1, TranslationAt(phase), new PlaybackAnimationPacing(period - previous));
        visual.StartAnimation(nameof(CompositionVisual.Translation), animation);
        animated = visual;
        started = Stopwatch.GetTimestamp();
    }

    private void Pause()
    {
        if (animated == null) return;
        phase = (phase + Stopwatch.GetElapsedTime(started).TotalSeconds) % Period;
        animated.StopAnimation(nameof(CompositionVisual.Translation));
        animated.Translation = TranslationAt(phase);
        animated = null;
    }

    private void Reset()
    {
        animated?.StopAnimation(nameof(CompositionVisual.Translation));
        if (ElementComposition.GetElementVisual(glyphs) is { } visual) visual.Translation = default;
        animated = null;
        phase = 0;
    }

    private sealed class Glyphs(MarqueeText owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            var text = owner.Layout();
            context.DrawText(text, new Point(0, Math.Max(0, (Bounds.Height - text.Height) / 2)));
        }
    }
}
