using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;

namespace HuayuanPreview.R2;

internal static class UiThreadStallWatch
{
    internal static void Attach(Window window)
    {
        var clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        var census = new TimerCensus("stallWatch", clock);
        long intervalTicks = Stopwatch.Frequency;
        long nextDue = 0;

        window.Opened += (_, _) =>
        {
            nextDue = Stopwatch.GetTimestamp() + intervalTicks;
            clock.Start();
        };
        window.Closed += (_, _) => clock.Stop();
        clock.Tick += (_, _) =>
        {
            using var timing = census.Measure();
            // Measure from the scheduled tick, including time the dispatcher took to reach Tick itself.
            var due = nextDue;
            nextDue = Stopwatch.GetTimestamp() + intervalTicks;
            Dispatcher.UIThread.Post(() =>
            {
                if (!clock.IsEnabled) return;
                var stallMs = Stopwatch.GetElapsedTime(due).TotalMilliseconds;
                if (stallMs <= 100) return;
                Program.Log("R23_UISTALL", new
                {
                    stallMs,
                    observedAtUtc = DateTimeOffset.UtcNow,
                    gc0 = GC.CollectionCount(0),
                    gc1 = GC.CollectionCount(1),
                    gc2 = GC.CollectionCount(2),
                    gcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds
                });
            }, DispatcherPriority.Background);
        };
    }
}
