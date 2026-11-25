using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Rendering;
using AvaloniaLyrics;

namespace HuayuanPreview.R2;

// The two brushes share one shaped line and one viewport position.
internal sealed class DesktopLyricLine : Control, ICustomHitTest
{
    // R1: 清除艺术字——已唱文字由紫蓝渐变改为纯色 AccentStrong。
    private static readonly IBrush SungBrush = new SolidColorBrush(DesignTokens.AccentStrong);
    private const double ViewportLimit = 710;
    // 补丁2：文字可读性由双层投影承担——宽影保任意背景可读，细影保锐度。
    private static readonly IEffect WideShadow = new DropShadowEffect { BlurRadius = 10, OffsetX = 0, OffsetY = 0, Opacity = 0.5, Color = Color.Parse("#0B1020") };
    private static readonly IEffect SharpShadow = new DropShadowEffect { BlurRadius = 2, OffsetX = 0, OffsetY = 0, Opacity = 0.9, Color = Color.Parse("#0B1020") };
    private readonly IBrush foreground;
    private string text = "";
    private IReadOnlyList<LyricWord> words = [];
    private TextLayout? normal;
    private TextLayout? sung;
    private Geometry? glyphOutline;
    private Rect inkBounds;
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
        ClipToBounds = true;
    }

    internal bool HasText => text.Length > 0;
    internal double TextWidth => normal?.Width ?? 0;
    internal Size InkSize => new(Math.Min(inkBounds.Width, ViewportLimit), inkBounds.Height);
    private Point TextOrigin => new(HorizontalOffset - inkBounds.X, -inkBounds.Y);
    bool ICustomHitTest.HitTest(Point point) => HitsInk(point);
    internal bool HitsInk(Point point) =>
        new Rect(Bounds.Size).Contains(point) && glyphOutline?.FillContains(point - (Vector)TextOrigin) == true;

    private double LineProgress => duration > 0 ? Math.Clamp((position - start) / duration, 0, 1) : 0;
    internal double HorizontalOffset
    {
        get
        {
            var progress = LineProgress;
            if (inkBounds.Width <= Bounds.Width || progress <= .10) return 0;
            var travel = inkBounds.Width - Bounds.Width + 24;
            var timeline = travel * Math.Clamp((progress - .10) / .75, 0, 1);
            // Catch up to fast-sung words before the highlight reaches the right edge.
            var follow = (SungWidth - inkBounds.X - Bounds.Width * .44) * Math.Clamp((progress - .10) / .15, 0, 1);
            return -Math.Clamp(Math.Max(timeline, follow), 0, travel);
        }
    }

    internal double SungWidth
    {
        get
        {
            if (timings.Length == 0) return TextWidth * LineProgress;
            double sungWidth = 0;
            foreach (var word in timings)
            {
                if (position < word.Start) break;
                if (position < word.End)
                    return Math.Max(sungWidth, word.Left + (word.Right - word.Left) *
                        ((position - word.Start) / (word.End - word.Start)));
                sungWidth = Math.Max(sungWidth, word.Right);
            }
            return sungWidth;
        }
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
            !ReferenceEquals(words, newWords);
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
        glyphOutline = null;
        inkBounds = default;
        timings = [];
        if (HasText && family != null)
        {
            var typeface = new Typeface(family, weight: FontWeight.Bold);
            normal = new TextLayout(text, typeface, fontSize, foreground, textWrapping: TextWrapping.NoWrap);
            glyphOutline = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, fontSize, foreground).BuildGeometry(default);
            inkBounds = glyphOutline?.Bounds ?? default;
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
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(InkSize.Width, availableSize.Width), InkSize.Height);

    public override void Render(DrawingContext context)
    {
        if (normal == null) return;
        var viewport = new Rect(Bounds.Size);
        var origin = TextOrigin;
        // 双层阴影：宽影保任意背景可读（BlurRadius 10 / Opacity 0.5），细影保锐度（BlurRadius 2 / Opacity 0.9）。
        using (context.PushEffect(WideShadow, viewport))
            normal.Draw(context, origin);
        using (context.PushEffect(SharpShadow, viewport))
            normal.Draw(context, origin);
        normal.Draw(context, origin);
        if (sung != null)
        {
            double right = Math.Clamp(origin.X + SungWidth, 0, viewport.Width);
            if (right <= 0) return;
            using (context.PushClip(new Rect(0, 0, right, viewport.Height))) sung.Draw(context, origin);
        }
    }
}
