using System.Diagnostics;
using Avalonia.Threading;

namespace HuayuanPreview.R2;

internal sealed class TimerCensus(string name, DispatcherTimer timer)
{
    private static readonly HashSet<TimerCensus> pending = [];
    private int ticks;
    private double totalMs;
    private double maxMs;

    internal TickScope Measure() => new(this, Stopwatch.GetTimestamp());

    internal static void Report()
    {
        foreach (var census in pending) census.ReportAndReset();
        pending.Clear();
    }

    private void ReportAndReset()
    {
        if (maxMs > 50)
            Program.Log("R23_TIMER_CENSUS", new { name, intervalMs = timer.Interval.TotalMilliseconds, ticks, totalMs, maxMs });
        ticks = 0;
        totalMs = 0;
        maxMs = 0;
    }

    internal readonly struct TickScope(TimerCensus owner, long started) : IDisposable
    {
        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (owner.ticks++ == 0) pending.Add(owner);
            owner.totalMs += elapsed;
            owner.maxMs = Math.Max(owner.maxMs, elapsed);
        }
    }
}
