using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

public sealed class PlaybackWave : Control
{
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<PlaybackWave, bool>(nameof(IsPlaying));
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PlaybackWave, IBrush?>(nameof(Fill));

    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private long started;
    private long stopped;
    private bool attached;
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    static PlaybackWave() => AffectsRender<PlaybackWave>(FillProperty);

    public PlaybackWave()
    {
        Width = 19;
        Height = 16;
        clock.Tick += (_, _) =>
        {
            InvalidateVisual();
            if (!IsPlaying && Stopwatch.GetElapsedTime(stopped).TotalMilliseconds >= 160) clock.Stop();
        };
    }

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        if (IsPlaying) Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        clock.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsPlayingProperty || !attached) return;
        if (IsPlaying) Start();
        else stopped = Stopwatch.GetTimestamp();
    }

    private void Start()
    {
        started = Stopwatch.GetTimestamp();
        clock.Start();
    }

    public override void Render(DrawingContext context)
    {
        var activeTime = IsPlaying ? Stopwatch.GetElapsedTime(started).TotalSeconds :
            clock.IsEnabled ? Stopwatch.GetElapsedTime(started, stopped).TotalSeconds : 0;
        var amplitude = IsPlaying ? 1 : clock.IsEnabled ?
            Math.Clamp(1 - Stopwatch.GetElapsedTime(stopped).TotalMilliseconds / 160, 0, 1) : 0;
        for (int bar = 0; bar < 4; bar++)
        {
            var phase = (activeTime - bar * .14) * (Math.Tau / .88);
            var height = 4 + 12 * amplitude * (.5 + .5 * Math.Cos(phase));
            context.DrawRectangle(Fill, null, new Rect(bar * 5, Bounds.Height - height, 3, height), 1.5, 1.5);
        }
    }
}
