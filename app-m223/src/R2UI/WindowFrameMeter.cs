using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace HuayuanPreview.R2;

/// <summary>Counts passive draw callbacks for each window on one shared reporting interval.</summary>
internal sealed class WindowFrameMeter
{
    private static readonly List<WindowFrameMeter> visible = [];
    private static readonly DispatcherTimer reportClock = new() { Interval = TimeSpan.FromSeconds(5) };
    private static long lastReport;
    private static long sample;

    private readonly Window window;
    private readonly string name;
    private readonly object measurements = new();
    private readonly List<double> intervals = new(640);
    private CompositionCustomVisual? observer;
    private TimeSpan? lastFrame;
    private int frames;
    private bool running;
    private bool closed;

    static WindowFrameMeter() => reportClock.Tick += (_, _) => Report();

    private WindowFrameMeter(Window window, string name)
    {
        this.window = window;
        this.name = name;
        window.Opened += (_, _) =>
        {
            if (observer == null && ElementComposition.GetElementVisual(window) is { } source)
            {
                observer = source.Compositor.CreateCustomVisual(new DrawObserver(this));
                ResizeObserver();
                ElementComposition.SetElementChildVisual(window, observer);
            }
            UpdateVisibility();
        };
        window.SizeChanged += (_, _) => ResizeObserver();
        window.Closed += (_, _) =>
        {
            closed = true;
            Stop();
            if (observer != null) ElementComposition.SetElementChildVisual(window, null);
            observer = null;
        };
        window.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty || change.Property == Window.WindowStateProperty)
                UpdateVisibility();
        };
    }

    internal static void Attach(Window window, string name) => _ = new WindowFrameMeter(window, name);

    private void ResizeObserver()
    {
        if (observer != null)
            observer.Size = new Vector(window.ClientSize.Width, window.ClientSize.Height);
    }

    private void UpdateVisibility()
    {
        if (closed || !window.IsVisible || window.WindowState == WindowState.Minimized)
        {
            Stop();
            return;
        }
        if (running || observer == null) return;
        lock (measurements) running = true;
        visible.Add(this);
        if (!reportClock.IsEnabled)
        {
            lastReport = Stopwatch.GetTimestamp();
            reportClock.Start();
        }
    }

    private void Stop()
    {
        if (!running) return;
        lock (measurements)
        {
            running = false;
            intervals.Clear();
            frames = 0;
            lastFrame = null;
        }
        visible.Remove(this);
        if (visible.Count == 0) reportClock.Stop();
    }

    private void CountDraw(TimeSpan now)
    {
        lock (measurements)
        {
            if (!running || lastFrame == now) return;
            if (lastFrame is { } previous) intervals.Add((now - previous).TotalMilliseconds);
            lastFrame = now;
            frames++;
        }
    }

    private (int Frames, double? P95) TakeSample()
    {
        lock (measurements)
        {
            intervals.Sort();
            var count = intervals.Count;
            var result = (frames, count == 0 ? (double?)null : intervals[(int)Math.Ceiling(count * .95) - 1]);
            frames = 0;
            intervals.Clear();
            return result;
        }
    }

    private static void Report()
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = Stopwatch.GetElapsedTime(lastReport, now).TotalSeconds;
        lastReport = now;
        var observedAtUtc = DateTimeOffset.UtcNow;
        sample++;
        foreach (var meter in visible)
        {
            var (frames, p95) = meter.TakeSample();
            Program.Log("R23_FPS", new
            {
                window = meter.name,
                fps = Math.Round(frames / seconds, 2),
                p95IntervalMs = p95 is { } interval ? Math.Round(interval, 2) : (double?)null,
                visible = true,
                sample,
                observedAtUtc,
                seconds = Math.Round(seconds, 3)
            });
        }
    }

    private sealed class DrawObserver(WindowFrameMeter meter) : CompositionCustomVisualHandler
    {
        public override void OnRender(ImmediateDrawingContext drawingContext) => meter.CountDraw(CompositionNow);
    }
}
