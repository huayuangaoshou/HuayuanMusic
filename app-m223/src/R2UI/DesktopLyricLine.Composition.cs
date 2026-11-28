using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

internal sealed partial class DesktopLyricLine
{
    private static readonly object ClearFrame = new();
    private CompositionCustomVisual? composedVisual;
    private Size publishedViewport;
    private double publishedScale;

    internal bool AnimateInComposition { get; init; }
    internal bool UsesComposition => composedVisual != null;

    internal readonly record struct PlaybackClock(double Position, long Timestamp, bool Running)
    {
        internal double Now => Position + (Running ? Stopwatch.GetElapsedTime(Timestamp).TotalSeconds : 0);
    }

    internal void PublishClock(PlaybackClock clock) => composedVisual?.SendHandlerMessage(clock);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!AnimateInComposition || DesignTokens.ShaderDegrade ||
            ElementComposition.GetElementVisual(this) is not { } host) return;

        composedVisual = host.Compositor.CreateCustomVisual(new LineRenderer());
        ElementComposition.SetElementChildVisual(this, composedVisual);
        PublishFrame(Bounds.Size);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (composedVisual != null)
        {
            composedVisual.SendHandlerMessage(ClearFrame);
            ElementComposition.SetElementChildVisual(this, null);
            composedVisual = null;
            publishedViewport = default;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void PublishFrame(Size size)
    {
        if (composedVisual == null || normal == null || size.Width <= 0 || size.Height <= 0) return;
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (publishedViewport == size && publishedScale == scale) return;

        const double gutter = DesktopLyricWindow.ShadowGutter;
        var pixels = new PixelSize((int)Math.Ceiling((inkBounds.Width + 2 * gutter) * scale),
            (int)Math.Ceiling((inkBounds.Height + 2 * gutter) * scale));
        var dpi = new Vector(96 * scale, 96 * scale);
        var origin = new Point(gutter - inkBounds.X, gutter - inkBounds.Y);
        var baseImage = new RenderTargetBitmap(pixels, dpi);
        RenderTargetBitmap? highlight = null;
        try
        {
            RasterizeNormal(baseImage, normal, origin);
            if (sung != null)
            {
                highlight = new RenderTargetBitmap(pixels, dpi);
                using var context = highlight.CreateDrawingContext();
                sung.Draw(context, origin);
            }

            var frame = new LineFrame(baseImage, highlight, new Rect(size), inkBounds, TextWidth, start,
                duration, timings, inkBounds.Width > size.Width ? CreateEdgeMask(size.Width).ToImmutable() : null);
            composedVisual.Size = new Vector(size.Width, size.Height);
            composedVisual.SendHandlerMessage(frame);
            publishedViewport = size;
            publishedScale = scale;
        }
        catch
        {
            baseImage.Dispose();
            highlight?.Dispose();
            throw;
        }
    }

    private static void RasterizeNormal(RenderTargetBitmap image, TextLayout layout, Point origin)
    {
        var viewport = new Rect(image.Size);
        // A fresh bitmap context resets the DPI transform after each platform effect.
        using (var context = image.CreateDrawingContext())
        using (context.PushEffect(WideShadow, viewport))
            layout.Draw(context, origin);
        using (var context = image.CreateDrawingContext(clear: false))
        using (context.PushEffect(SharpShadow, viewport))
            layout.Draw(context, origin);
        using (var context = image.CreateDrawingContext(clear: false))
            layout.Draw(context, origin);
    }

    private sealed record LineFrame(RenderTargetBitmap Normal, RenderTargetBitmap? Sung, Rect Viewport,
        Rect Ink, double TextWidth, double Start, double Duration,
        (double Start, double End, double Left, double Right)[] Timings, IImmutableBrush? Mask) : IDisposable
    {
        internal double End { get; } = Timings.Aggregate(Start + Duration, (end, word) => Math.Max(end, word.End));

        public void Dispose()
        {
            Normal.Dispose();
            Sung?.Dispose();
        }
    }

    private sealed class LineRenderer : CompositionCustomVisualHandler
    {
        private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1d / 36);
        private LineFrame? frame;
        private PlaybackClock clock;
        private TimeSpan nextFrame;
        private bool requested;
        private double offset;
        private double sungRight;

        public override Rect GetRenderBounds() =>
            new Rect(0, 0, EffectiveSize.X, EffectiveSize.Y).Inflate(DesktopLyricWindow.ShadowGutter);

        public override void OnMessage(object message)
        {
            if (message is PlaybackClock anchor)
            {
                clock = anchor;
                if (!clock.Running || !requested) UpdateFrame();
                return;
            }
            if (message is not LineFrame && !ReferenceEquals(message, ClearFrame)) return;
            frame?.Dispose();
            frame = message as LineFrame;
            UpdateFrame(force: true);
        }

        public override void OnAnimationFrameUpdate()
        {
            requested = false;
            var now = CompositionNow;
            if (now >= nextFrame)
            {
                nextFrame = now - nextFrame > FrameInterval ? now + FrameInterval : nextFrame + FrameInterval;
                UpdateFrame();
            }
            else
            {
                RequestFrame();
            }
        }

        private void UpdateFrame(bool force = false)
        {
            if (frame is not { } line)
            {
                if (force) Invalidate();
                return;
            }

            double position = clock.Now;
            var progress = line.Duration > 0 ? Math.Clamp((position - line.Start) / line.Duration, 0, 1) : 0;
            var swept = GetSungWidth(line.Timings, position, line.TextWidth * progress);
            var x = GetHorizontalOffset(line.Ink, line.Viewport.Width, progress, swept);
            var right = Math.Clamp(x - line.Ink.X + swept, 0, line.Viewport.Width);
            if (force || Math.Abs(x - offset) >= .25 || Math.Abs(right - sungRight) >= .25 ||
                (!clock.Running && (x != offset || right != sungRight)))
            {
                offset = x;
                sungRight = right;
                Invalidate();
            }
            RequestFrame();
        }

        private void RequestFrame()
        {
            if (requested || !clock.Running || frame is not { Duration: > 0 } line) return;
            if (clock.Now >= line.End) return;
            requested = true;
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext context)
        {
            if (frame is not { } line) return;
            if (line.Mask != null)
            {
                using (context.PushOpacityMask(line.Mask, line.Viewport))
                    Draw(context, line);
            }
            else
            {
                Draw(context, line);
            }
        }

        private void Draw(ImmediateDrawingContext context, LineFrame line)
        {
            const double gutter = DesktopLyricWindow.ShadowGutter;
            var dest = new Rect(new Point(offset - gutter, -gutter), line.Normal.Size);
            var source = new Rect(0, 0, line.Normal.PixelSize.Width, line.Normal.PixelSize.Height);
            context.DrawBitmap(line.Normal, source, dest);
            if (line.Sung == null || sungRight <= 0) return;
            using (context.PushClip(new Rect(0, 0, sungRight, line.Viewport.Height)))
                context.DrawBitmap(line.Sung, source, dest);
        }
    }
}
