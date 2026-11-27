using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace HuayuanPreview.R2;

public sealed class MarqueeText : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<MarqueeText,string?>(nameof(Text));
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty,value); }
    private FormattedText? formatted;
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private double overflow, phase, offset;
    private long last;
    private bool attached;

    public MarqueeText()
    {
        ClipToBounds=true;
        clock.Tick+= (_,_)=>Advance();
        SizeChanged+= (_,_)=>UpdateOverflow();
        AttachedToVisualTree+= (_,_)=>{attached=true;UpdateOverflow();};
        DetachedFromVisualTree+= (_,_)=>{attached=false;clock.Stop();};
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==TextProperty||change.Property==ForegroundProperty||change.Property==FontSizeProperty||
           change.Property==FontFamilyProperty||change.Property==FontWeightProperty||change.Property==FontStyleProperty)
        {
            formatted=null;
            phase=offset=0;
            InvalidateMeasure();
            InvalidateVisual();
            if(attached)UpdateOverflow();
        }
    }

    private FormattedText TextLayout() => formatted ??= new FormattedText(Text ?? "",CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,new Typeface(FontFamily,FontStyle,FontWeight),FontSize,Foreground);

    protected override Size MeasureOverride(Size availableSize)
    {
        var text=TextLayout();
        return new Size(Math.Min(availableSize.Width,text.WidthIncludingTrailingWhitespace), text.Height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var text=TextLayout();
        context.DrawText(text,new Point(-offset,Math.Max(0,(Bounds.Height-text.Height)/2)));
    }

    private void UpdateOverflow()
    {
        var next=Bounds.Width>0 ? Math.Max(0,TextLayout().WidthIncludingTrailingWhitespace-Bounds.Width) : 0;
        if(Math.Abs(next-overflow)>0.5){overflow=next;phase=offset=0;}
        if(!attached||overflow<=1){clock.Stop();OpacityMask=null;InvalidateVisual();return;}
        var edge=Math.Min(.08,6/Bounds.Width);
        OpacityMask=new LinearGradientBrush { GradientStops=[
            new(Colors.Transparent,0),new(Colors.Black,edge),new(Colors.Black,1-edge),new(Colors.Transparent,1)]};
        if(!clock.IsEnabled){last=Stopwatch.GetTimestamp();clock.Start();}
    }

    private void Advance()
    {
        var now=Stopwatch.GetTimestamp();
        var elapsed=Stopwatch.GetElapsedTime(last,now).TotalSeconds;
        last=now;
        if(IsPointerOver||!IsEffectivelyVisible)return;
        const double hold=1.7, speed=34;
        var travel=overflow/speed;
        phase=(phase+elapsed)%(2*(hold+travel));
        var next=phase<=hold ? 0 : phase<=hold+travel ? (phase-hold)*speed :
            phase<=2*hold+travel ? overflow : Math.Max(0,overflow-(phase-2*hold-travel)*speed);
        if(Math.Abs(next-offset)<.05)return;
        offset=next;
        InvalidateVisual();
    }
}
