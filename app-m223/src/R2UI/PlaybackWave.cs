using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

public sealed class PlaybackWave : Canvas
{
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<PlaybackWave, bool>(nameof(IsPlaying));
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PlaybackWave, IBrush?>(nameof(Fill));

    private static readonly double[] periods = [.88, .91, .86, .92, .89];
    private readonly Border[] bars = new Border[periods.Length];
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private long started;
    private long stopped;

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }

    public PlaybackWave()
    {
        Width = 19;
        Height = 16;
        clock.Tick += (_, _) => Advance();
        EffectiveViewportChanged += (_, _) => UpdateAnimation();
        for (int i = 0; i < bars.Length; i++)
        {
            var bar = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, IsHitTestVisible = false };
            SetLeft(bar, i * 4);
            bars[i] = bar;
            Children.Add(bar);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FillProperty)
            foreach (var bar in bars) bar.Background = Fill;
        else if ((change.Property == IsPlayingProperty || change.Property == IsVisibleProperty) && this.IsAttachedToVisualTree())
            UpdateAnimation();
    }

    private static Vector3D ScaleAt(double time, int index) =>
        new(1, .25 + .75 * (.5 + .5 * Math.Cos((time - index * .14) * Math.Tau / periods[index])), 1);

    private void UpdateAnimation()
    {
        if (!this.IsAttachedToVisualTree() || !IsEffectivelyVisible)
        {
            Stop();
            return;
        }
        if (IsPlaying)
        {
            if (started != 0 && stopped == 0) return;
            started = Stopwatch.GetTimestamp();
            stopped = 0;
            clock.Start();
        }
        else if (started != 0 && stopped == 0)
        {
            stopped = Stopwatch.GetTimestamp();
        }
        Advance();
    }

    private void Advance()
    {
        if (!IsEffectivelyVisible)
        {
            Stop();
            return;
        }
        var activeTime = started == 0 ? 0 : Stopwatch.GetElapsedTime(started, stopped == 0 ? Stopwatch.GetTimestamp() : stopped).TotalSeconds;
        var decay = stopped == 0 ? 1 : Math.Clamp(1 - Stopwatch.GetElapsedTime(stopped).TotalMilliseconds / 160, 0, 1);
        for (int i = 0; i < bars.Length; i++)
        {
            if (ElementComposition.GetElementVisual(bars[i]) is not { } visual) continue;
            visual.CenterPoint = new Vector3D(1.5, 16, 0);
            var scale = started == 0 ? .25 : .25 + (ScaleAt(activeTime, i).Y - .25) * decay;
            visual.Scale = new Vector3D(1, scale, 1);
        }
        if (decay == 0) Stop();
    }

    private void Stop()
    {
        clock.Stop();
        started = stopped = 0;
    }
}
