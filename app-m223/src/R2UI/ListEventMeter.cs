using System.Diagnostics;

namespace HuayuanPreview.R2;

internal readonly struct ListEventMeter(string kind, int itemCount) : IDisposable
{
    private readonly long started = Stopwatch.GetTimestamp();

    public void Dispose()
    {
        Program.Log("R23_LIST_EVENT", new { kind, itemCount, elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds });
    }
}
