using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SkiaSharp;

namespace HuayuanPreview.R2;

/// <summary>Renders the shared procedural atmosphere and its glass optics without copying window pixels.</summary>
public sealed class GlassOptics : Control
{
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<GlassOptics, double>(nameof(Radius), 20);
    public static readonly StyledProperty<bool> IsAtmosphereProperty =
        AvaloniaProperty.Register<GlassOptics, bool>(nameof(IsAtmosphere));
    public static readonly StyledProperty<double> FrostProperty =
        AvaloniaProperty.Register<GlassOptics, double>(nameof(Frost), .64);

    public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public bool IsAtmosphere { get => GetValue(IsAtmosphereProperty); set => SetValue(IsAtmosphereProperty, value); }
    public double Frost { get => GetValue(FrostProperty); set => SetValue(FrostProperty, value); }

    static GlassOptics() => AffectsRender<GlassOptics>(RadiusProperty, FrostProperty, IsAtmosphereProperty, BoundsProperty);

    public GlassOptics() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 2 || Bounds.Height < 2 || TopLevel.GetTopLevel(this) is not { } root)
            return;
        var size = root.ClientSize;
        if (size.Width < 2 || size.Height < 2) return;
        var origin = IsAtmosphere ? default : this.TranslatePoint(default, root) ?? default;
        context.Custom(new Draw(new Parameters(new Rect(Bounds.Size), size, origin,
            Math.Clamp(Radius, 0, Math.Min(Bounds.Width, Bounds.Height) / 2), Math.Clamp(Frost, 0, 1),
            ActualThemeVariant == ThemeVariant.Dark, IsAtmosphere)));
    }

    private readonly record struct Parameters(Rect Bounds, Size Viewport, Point Origin, double Radius,
        double Frost, bool Dark, bool Atmosphere);

    private sealed class Draw(Parameters values) : ICustomDrawOperation
    {
        private static readonly Lazy<SKRuntimeEffect?> Effect = new(() => SKRuntimeEffect.CreateShader(Source, out _));
        private static int atmosphereLogged;
        private static int lensLogged;
        private readonly Parameters parameters = values;

        public Rect Bounds => parameters.Bounds;
        public bool HitTest(Point _) => false;
        public bool Equals(ICustomDrawOperation? other) => other is Draw draw && draw.parameters == parameters;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature || Effect.Value is not { } effect)
                return;
            using var lease = feature.Lease();
            using var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["viewport"] = new[] { (float)parameters.Viewport.Width, (float)parameters.Viewport.Height },
                ["origin"] = new[] { (float)parameters.Origin.X, (float)parameters.Origin.Y },
                ["extent"] = new[] { (float)Bounds.Width, (float)Bounds.Height },
                ["corner"] = (float)parameters.Radius,
                ["frost"] = (float)parameters.Frost,
                ["night"] = parameters.Dark ? 1f : 0f,
                ["atmosphere"] = parameters.Atmosphere ? 1f : 0f
            };
            using var shader = effect.ToShader(uniforms);
            if (shader == null) return;
            using var paint = new SKPaint { IsAntialias = true, Shader = shader };
            lease.SkCanvas.DrawRect(SKRect.Create((float)Bounds.Width, (float)Bounds.Height), paint);
            if (parameters.Atmosphere
                ? Interlocked.Exchange(ref atmosphereLogged, 1) == 0
                : Interlocked.Exchange(ref lensLogged, 1) == 0)
                Program.Log("R2_GLASS_RENDER", new { layer = parameters.Atmosphere ? "atmosphere" : "lens" });
        }

        private const string Source = """
            uniform float2 viewport;
            uniform float2 origin;
            uniform float2 extent;
            uniform float corner;
            uniform float frost;
            uniform float night;
            uniform float atmosphere;

            float glow(float2 uv, float2 center, float2 spread) {
                float2 delta = (uv - center) / spread;
                return exp(-dot(delta, delta) * 1.7);
            }

            half3 field(float2 pos) {
                float2 uv = pos / viewport;
                half3 base = mix(half3(.976,.984,1), half3(.049,.073,.130), night);
                half3 rose = mix(half3(.995,.827,.906), half3(.26,.128,.266), night);
                half3 sky = mix(half3(.742,.872,1), half3(.109,.216,.41), night);
                base = mix(base, rose, .72 * glow(uv, float2(.22,.11), float2(.57,.53)));
                base = mix(base, sky, .77 * glow(uv, float2(.94,.81), float2(.62,.61)));
                return mix(base, mix(half3(1),half3(.11,.17,.27),night),
                    .36 * glow(uv, float2(.53,.45), float2(.53,.54)));
            }

            half4 main(float2 p) {
                if (atmosphere > .5) return half4(field(p), 1);
                float2 centered = p - extent * .5;
                float2 q = abs(centered) - extent * .5 + corner;
                float distance = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - corner;
                float alpha = 1.0 - smoothstep(-.8,.5,distance);
                if (alpha <= 0.0) return half4(0);
                float2 normal = sign(centered) * (q.x > q.y ? float2(1,0) : float2(0,1));
                if (q.x > 0.0 && q.y > 0.0) normal = normalize(max(q,.001)) * sign(centered);
                float bevel = exp(min(distance,0.0) / 12.0);
                float2 sampleAt = origin + p - normal * bevel * 27.0;
                half3 blurred = field(sampleAt) * .44;
                blurred += .14 * (field(sampleAt + float2(18,0)) + field(sampleAt - float2(18,0)) +
                                   field(sampleAt + float2(0,18)) + field(sampleAt - float2(0,18)));
                half3 milk = mix(half3(1,.989,1), half3(.082,.123,.208), night);
                half3 color = mix(blurred, milk, frost);
                float incidence = max(0.0, dot(normal, float2(-.65,-.76)));
                float shine = bevel * (.05 + .19 * incidence);
                color = mix(color, mix(half3(1),half3(.68,.81,1),night), shine);
                return half4(color * alpha, alpha);
            }
            """;
    }
}
