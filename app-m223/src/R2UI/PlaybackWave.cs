using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
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
    private long started;

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }

    public PlaybackWave()
    {
        Width = 19;
        Height = 16;
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
        foreach (var bar in bars)
            ElementComposition.GetElementVisual(bar)?.StopAnimation(nameof(CompositionVisual.Scale));
        started = 0;
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
        var animate = IsPlaying && IsEffectivelyVisible;
        var previous = started;
        if (animate && previous != 0) return;
        var elapsed = previous == 0 ? 0 : Stopwatch.GetElapsedTime(previous).TotalSeconds;
        started = animate ? Stopwatch.GetTimestamp() : 0;
        for (int i = 0; i < bars.Length; i++)
        {
            if (ElementComposition.GetElementVisual(bars[i]) is not { } visual) continue;
            visual.StopAnimation(nameof(CompositionVisual.Scale));
            visual.CenterPoint = new Vector3D(1.5, 16, 0);
            var resting = new Vector3D(1, .25, 1);
            visual.Scale = resting;
            if (!animate && previous == 0) continue;
            var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
            if (animate)
            {
                const int samples = 28;
                var pacing = new PlaybackAnimationPacing(periods[i] / samples);
                animation.IterationBehavior = AnimationIterationBehavior.Forever;
                animation.Duration = TimeSpan.FromSeconds(periods[i]);
                for (int sample = 0; sample <= samples; sample++)
                    animation.InsertKeyFrame((float)sample / samples, ScaleAt(sample * periods[i] / samples, i), pacing);
            }
            else
            {
                animation.Duration = TimeSpan.FromMilliseconds(160);
                animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
                animation.InsertKeyFrame(0, ScaleAt(elapsed, i));
                animation.InsertKeyFrame(1, resting, DesignTokens.MotionEffectsFast.Curve);
            }
            visual.StartAnimation(nameof(CompositionVisual.Scale), animation);
        }
    }
}
