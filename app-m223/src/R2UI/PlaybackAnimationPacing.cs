using Avalonia.Animation.Easings;

namespace HuayuanPreview.R2;

/// <summary>Samples continuous compositor motion at the dock's original 32 ms visual cadence.</summary>
internal sealed class PlaybackAnimationPacing(double segmentSeconds) : Easing
{
    private readonly int frames = Math.Max(1, (int)Math.Round(segmentSeconds / .032));

    public override double Ease(double progress) => Math.Floor(progress * frames) / frames;
}
