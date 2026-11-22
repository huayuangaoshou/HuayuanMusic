using ManagedBass;
using System.Diagnostics;

namespace SimpleAudio;

public sealed class PlaybackCutoff(CancellationToken cancellationToken,bool persistHistory=true)
{
    internal CancellationToken CancellationToken { get; } = cancellationToken;
    internal TaskCompletionSource<(double Milliseconds, bool NativeStopped)> Terminal { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool PersistHistory { get; }=persistHistory;
    public Task<(double Milliseconds, bool NativeStopped)> Completion => Terminal.Task;
}

public partial class SimpleAudioPlayer
{
    // Leave 200 ms for Windows timer and native dispatch latency.
    private static readonly TimeSpan CutoffSchedule = TimeSpan.FromMilliseconds(5800);
    private PlaybackCutoff? _cutoff;
    private Timer? _cutoffTimer;

    internal bool PlayWithCutoff(PlaybackCutoff cutoff)
    {
        lock (BassDeviceGate)
        {
            if (Stream == 0 || _cutoff != null || cutoff.CancellationToken.IsCancellationRequested)
                return false;

            int channel = Stream;
            // Immediately before native ChannelPlay, so the measured budget includes its latency.
            long start = Stopwatch.GetTimestamp();
            if (!Bass.ChannelPlay(channel))
                return false;

            _cutoff = cutoff;
            try
            {
                TimeSpan remaining = CutoffSchedule - Stopwatch.GetElapsedTime(start);
                _cutoffTimer = new Timer(_ => {
                    lock (BassDeviceGate)
                    {
                        if (!ReferenceEquals(_cutoff, cutoff) || Stream != channel)
                            return;

                        _cutoff = null; // This callback owns completion.
                        bool stopped = Bass.ChannelStop(channel);
                        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        try
                        {
                            Stop(); // Frees the original native stream and releases its Provider grant.
                            cutoff.Terminal.TrySetResult((elapsed, stopped && Stream == 0));
                        }
                        catch (Exception ex)
                        {
                            cutoff.Terminal.TrySetException(ex);
                        }
                    }
                }, null, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
                return true;
            }
            catch
            {
                Stop();
                throw;
            }
        }
    }
}

