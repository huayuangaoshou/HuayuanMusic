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
    /// <summary>R9 透出色提饱和系数（随 frost 档位由调用方传入，1=不饱和）。</summary>
    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<GlassOptics, double>(nameof(Saturation), 1.28);

    public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public bool IsAtmosphere { get => GetValue(IsAtmosphereProperty); set => SetValue(IsAtmosphereProperty, value); }
    public double Frost { get => GetValue(FrostProperty); set => SetValue(FrostProperty, value); }
    public double Saturation { get => GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }

    static GlassOptics() => AffectsRender<GlassOptics>(RadiusProperty, FrostProperty, SaturationProperty, IsAtmosphereProperty, BoundsProperty);

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
            ActualThemeVariant == ThemeVariant.Dark, IsAtmosphere,
            Math.Max(1.0, Saturation),
            DesignTokens.ShaderDegrade)));
    }

    private readonly record struct Parameters(Rect Bounds, Size Viewport, Point Origin, double Radius,
        double Frost, bool Dark, bool Atmosphere, double Saturation, bool Degrade);

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
                ["atmosphere"] = parameters.Atmosphere ? 1f : 0f,
                ["saturation"] = (float)(parameters.Dark ? DesignTokens.SaturationNight : parameters.Saturation),
                ["degrade"] = parameters.Degrade ? 1f : 0f
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
            uniform float saturation;
            uniform float degrade;

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

            // R9-a 柔化采样：12 tap（中心 + 十字 8 + 对角 3 权重均匀），过渡奶油化无颗粒。
            // degrade 时退化为单点采样，跳过模糊。
            half3 blurSample(float2 sampleAt) {
                half3 acc = field(sampleAt) * .18;
                acc += .10 * (field(sampleAt + float2( 7, 0)) + field(sampleAt + float2(-7, 0)) +
                              field(sampleAt + float2( 0, 7)) + field(sampleAt + float2( 0,-7)));
                acc += .06 * (field(sampleAt + float2(16, 0)) + field(sampleAt + float2(-16, 0)) +
                              field(sampleAt + float2( 0,16)) + field(sampleAt + float2( 0,-16)));
                acc += .045 * (field(sampleAt + float2(10,10)) + field(sampleAt + float2(-10,10)) +
                               field(sampleAt + float2(10,-10)) + field(sampleAt + float2(-10,-10)));
                return acc;
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

                // R9-d 边缘色散：仅距边 <6px 对 R/B 通道做 ±1.5px 偏移采样，中心不受影响；degrade 跳过。
                half3 blurred;
                if (degrade > .5) {
                    blurred = field(sampleAt); // 降级：跳过采样与色散，单点直采
                } else {
                    // 边缘带权重：边缘(distance=0)=1，向内 6px 线性衰减到 0（中心无色散）
                    float edgeW = saturate(1.0 + distance / 6.0);
                    half3 center = blurSample(sampleAt);
                    half3 r = blurSample(sampleAt + float2( 1.5, 0));
                    half3 b = blurSample(sampleAt + float2(-1.5, 0));
                    blurred = center;
                    blurred.r = mix(center.r, r.r, edgeW);
                    blurred.b = mix(center.b, b.b, edgeW);
                }
                half3 milk = mix(half3(1,.989,1), half3(.082,.123,.208), night);
                half3 mixed = mix(blurred, milk, frost);

                // R9-b 透出色提饱和：对「最终奶白混合色」提饱和才是 vibrancy；
                // 对淡源提饱和会被 frost 奶白稀释掉（可见色差≈6%）。sat 已由 C# 端按夜间降为 SaturationNight。
                half3 colored = mix(half3(dot(mixed, half3(.299,.587,.114))), mixed, saturation);

                half3 color = colored;
                float incidence = max(0.0, dot(normal, float2(-.65,-.76)));
                float shine = bevel * (.05 + .19 * incidence);

                // R9-c 边缘 specular 高光：1.5px 亮线（白 alpha ~0.35，角部略强），给玻璃厚度感。
                float spec = 1.0 - smoothstep(0.0, 1.5, -distance);
                float cornerBoost = 1.0 + .55 * smoothstep(.85, 1.0, max(q.x,q.y) / max(extent.x,extent.y) * 2.0);
                float specStrength = spec * cornerBoost;
                color = mix(color, mix(half3(1),half3(.68,.81,1),night), shine + specStrength * .35);

                return half4(color * alpha, alpha);
            }
            """;
    }
}
