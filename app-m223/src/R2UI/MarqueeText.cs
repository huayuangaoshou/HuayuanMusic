using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
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
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private bool overflowCheckPosted;
    private long lastTick;
    private CompositionVisual? animated;

    public MarqueeText()
    {
        ClipToBounds = true;
        glyphs = new Glyphs(this) { IsHitTestVisible = false };
        Template = new FuncControlTemplate<MarqueeText>((_, _) => glyphs);
        clock.Tick += (_, _) => Advance();
        SizeChanged += (_, _) => ScheduleOverflowCheck();
        EffectiveViewportChanged += (_, _) =>
        {
            if (!IsEffectivelyVisible) Pause();
            else ScheduleOverflowCheck();
        };
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
            ScheduleOverflowCheck();
        }
        else if (change.Property == IsVisibleProperty && this.IsAttachedToVisualTree())
        {
            if (IsEffectivelyVisible) ScheduleOverflowCheck();
            else Pause();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ScheduleOverflowCheck();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        Pause();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ScheduleOverflowCheck();
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
        ScheduleOverflowCheck();
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        // The fixed viewport owns hover, including the gaps between moving glyphs.
        if (overflow > 1) context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
    }

    private void ScheduleOverflowCheck()
    {
        if (overflowCheckPosted || !this.IsAttachedToVisualTree()) return;
        overflowCheckPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            overflowCheckPosted = false;
            if (this.IsAttachedToVisualTree() && IsArrangeValid) UpdateOverflow();
        }, DispatcherPriority.Render);
    }

    private void UpdateOverflow()
    {
        var next = Bounds.Width > 0 ? Math.Max(0, Layout().WidthIncludingTrailingWhitespace - Bounds.Width) : 0;
        if (next != overflow)
        {
            Reset();
            var hitTestChanged = (next > 1) != (overflow > 1);
            overflow = next;
            if (hitTestChanged) InvalidateVisual();
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
        if (Bounds.Width <= 0 || !IsArrangeValid || !IsEffectivelyVisible || IsPointerOver) return;
        var w = Layout().WidthIncludingTrailingWhitespace;
        if (w <= Bounds.Width + 1)
        {
            Reset();
            return;
        }
        if (animated != null || ElementComposition.GetElementVisual(glyphs) is not { } visual) return;
        animated = visual;
        Program.Log("R23_MARQUEE_START", new { text = Text, textW = w, boundsW = Bounds.Width, overflow });
        lastTick = Stopwatch.GetTimestamp();
        clock.Start();
    }

    private void Advance()
    {
        if (!IsArrangeValid || !IsEffectivelyVisible || IsPointerOver || animated is null)
        {
            Pause();
            return;
        }
        var now = Stopwatch.GetTimestamp();
        phase = (phase + Stopwatch.GetElapsedTime(lastTick, now).TotalSeconds) % Period;
        lastTick = now;
        var translation = TranslationAt(phase);
        if (animated.Translation != translation) animated.Translation = translation;
    }

    private void Pause()
    {
        clock.Stop();
        animated = null;
    }

    private void Reset()
    {
        Pause();
        if (ElementComposition.GetElementVisual(glyphs) is { } visual) visual.Translation = default;
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
