using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaLyrics;

namespace HuayuanPreview.R2;

// The two brushes share one shaped line and one viewport position.
internal sealed partial class DesktopLyricLine : Control
{
    // R1: 清除艺术字——已唱文字由紫蓝渐变改为纯色 AccentStrong。
    private static readonly IBrush SungBrush = new SolidColorBrush(DesignTokens.AccentStrong);
    private const double ViewportLimit = 710;
    private const double FadeBand = 28;
    // 补丁2：文字可读性由双层投影承担——宽影保任意背景可读，细影保锐度。
    private static readonly IEffect WideShadow = new DropShadowEffect { BlurRadius = 9, OffsetX = 0, OffsetY = 0, Opacity = 0.32, Color = DesignTokens.DesktopLyricShadow };
    private static readonly IEffect SharpShadow = new DropShadowEffect { BlurRadius = 3, OffsetX = 0, OffsetY = 0, Opacity = 0.28, Color = DesignTokens.DesktopLyricShadow };
    private readonly IBrush foreground;
    private string text = "";
    private IReadOnlyList<LyricWord> words = [];
    private TextLayout? normal;
    private TextLayout? sung;
    private Rect inkBounds;
    private LinearGradientBrush? edgeMask;
    private double edgeMaskWidth;
    private (double Start, double End, double Left, double Right)[] timings = [];
    private double start;
    private double duration;
    private double position;
    private double fontSize;
    private FontFamily? family;

    internal DesktopLyricLine(IBrush foreground)
    {
        this.foreground = foreground;
        HorizontalAlignment = HorizontalAlignment.Center;
        MaxWidth = ViewportLimit;
    }

    internal bool HasText => text.Length > 0;
    internal double TextWidth => normal?.Width ?? 0;
    internal Size InkSize => new(Math.Min(inkBounds.Width, ViewportLimit), inkBounds.Height);
    private Point TextOrigin => new(HorizontalOffset - inkBounds.X, -inkBounds.Y);
    private double LineProgress => duration > 0 ? Math.Clamp((position - start) / duration, 0, 1) : 0;
    internal double HorizontalOffset => GetHorizontalOffset(inkBounds, Bounds.Width, LineProgress, SungWidth);

    private static double GetHorizontalOffset(Rect ink, double width, double progress, double sungWidth)
    {
        if (ink.Width <= width || progress <= .10) return 0;
        var travel = ink.Width - width + 24;
        var timeline = travel * Math.Clamp((progress - .10) / .75, 0, 1);
        // Catch up to fast-sung words before the highlight reaches the right edge.
        var follow = (sungWidth - ink.X - width * .44) * Math.Clamp((progress - .10) / .15, 0, 1);
        return -Math.Clamp(Math.Max(timeline, follow), 0, travel);
    }

    internal double SungWidth => GetSungWidth(timings, position, TextWidth * LineProgress);

    private static double GetSungWidth((double Start, double End, double Left, double Right)[] spans,
        double position, double sweep)
    {
        if (spans.Length == 0) return sweep;
        double sungWidth = 0;
        foreach (var word in spans)
        {
            if (position < word.Start) break;
            if (position < word.End)
                return Math.Max(sungWidth, word.Left + (word.Right - word.Left) *
                    ((position - word.Start) / (word.End - word.Start)));
            sungWidth = Math.Max(sungWidth, word.Right);
        }
        return sungWidth;
    }

    internal void SetFont(FontFamily fontFamily, double size)
    {
        if (Equals(family, fontFamily) && fontSize == size) return;
        family = fontFamily;
        fontSize = size;
        Rebuild();
    }

    internal void SetLine(string? value, double lineStart = 0, double lineDuration = 0,
        IReadOnlyList<LyricWord>? wordTimings = null)
    {
        var singleLine = (value ?? "").ReplaceLineEndings(" ").Trim();
        var newWords = wordTimings ?? [];
        bool shapeChanged = singleLine != text || (lineDuration > 0) != (duration > 0) ||
            !ReferenceEquals(words, newWords) || (UsesComposition && (start != lineStart || duration != lineDuration));
        text = singleLine;
        start = lineStart;
        duration = lineDuration;
        words = newWords;
        IsVisible = HasText;
        if (shapeChanged) Rebuild();
        else InvalidateVisual();
    }

    internal void SetPosition(double seconds)
    {
        if (position == seconds) return;
        position = seconds;
        if (duration > 0) InvalidateVisual();
    }

    private void Rebuild()
    {
        normal?.Dispose();
        sung?.Dispose();
        normal = sung = null;
        inkBounds = default;
        timings = [];
        if (HasText && family != null)
        {
            var typeface = new Typeface(family, weight: FontWeight.Bold);
            normal = new TextLayout(text, typeface, fontSize, foreground, textWrapping: TextWrapping.NoWrap);
            inkBounds = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, fontSize, foreground).BuildGeometry(default)?.Bounds ?? default;
            sung = duration > 0
                ? new TextLayout(text, typeface, fontSize, SungBrush, textWrapping: TextWrapping.NoWrap) : null;
            var spans = new List<(double, double, double, double)>();
            int cursor = 0;
            foreach (var word in words)
            {
                var wordText = word.Text.ReplaceLineEndings(" ").Trim();
                if (wordText.Length == 0) continue;
                int index = text.IndexOf(wordText, cursor, StringComparison.Ordinal);
                if (index < 0) continue;
                cursor = index + wordText.Length;
                double left = normal.HitTestTextPosition(index).X;
                double right = cursor == text.Length ? TextWidth : normal.HitTestTextPosition(cursor).X;
                spans.Add((word.Start.TotalSeconds, (word.Start + word.Duration).TotalSeconds, left, right));
            }
            timings = spans.ToArray();
        }
        InvalidateMeasure();
        InvalidateVisual();
        if (composedVisual == null) return;
        publishedViewport = default;
        if (normal == null) composedVisual.SendHandlerMessage(ClearFrame);
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(InkSize.Width, availableSize.Width), InkSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        PublishFrame(finalSize);
        return base.ArrangeOverride(finalSize);
    }

    public override void Render(DrawingContext context)
    {
        if (normal == null || UsesComposition) return;
        var viewport = new Rect(Bounds.Size);
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        if (inkBounds.Width > viewport.Width)
        {
            using (context.PushOpacityMask(GetEdgeMask(viewport.Width), viewport))
                DrawLine(context, viewport, normal);
        }
        else
        {
            DrawLine(context, viewport, normal);
        }
    }

    private IBrush GetEdgeMask(double width)
    {
        if (edgeMask is not null && edgeMaskWidth == width) return edgeMask;

        edgeMaskWidth = width;
        return edgeMask = CreateEdgeMask(width);
    }

    private static LinearGradientBrush CreateEdgeMask(double width)
    {
        double fade = Math.Min(FadeBand / width, .5);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            [
                new(Colors.Transparent, 0),
                new(Colors.White, fade),
                new(Colors.White, 1 - fade),
                new(Colors.Transparent, 1)
            ]
        };
    }

    private void DrawLine(DrawingContext context, Rect viewport, TextLayout normal)
    {
        var shadowViewport = viewport.Inflate(DesktopLyricWindow.ShadowGutter);
        var origin = TextOrigin;
        DrawNormal(context, shadowViewport, normal, origin);
        if (sung != null)
        {
            double right = Math.Clamp(origin.X + SungWidth, 0, viewport.Width);
            if (right <= 0) return;
            using (context.PushClip(new Rect(0, 0, right, viewport.Height))) sung.Draw(context, origin);
        }
    }

    private static void DrawNormal(DrawingContext context, Rect shadowViewport, TextLayout normal, Point origin)
    {
        // 双层阴影：宽影保任意背景可读（BlurRadius 9 / Opacity 0.32），细影保锐度（BlurRadius 3 / Opacity 0.28）。
        using (context.PushEffect(WideShadow, shadowViewport))
            normal.Draw(context, origin);
        using (context.PushEffect(SharpShadow, shadowViewport))
            normal.Draw(context, origin);
        normal.Draw(context, origin);
    }
}
