using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
namespace HuayuanPreview;
// Bounded synthetic-texture feasibility sample. No arbitrary sibling/window capture.
internal sealed class RefractionProbe:Control {
 public bool Software{get;set;}public float Phase{get;set;}public float LensX{get;set;}=320;public float Stretch{get;set;}=1;
 public static string LastStatus="NOT_RENDERED";public static bool LastGpu;public static int Draws;
 public RefractionProbe(){PointerMoved+=(_,e)=>{var p=e.GetPosition(this);LensX=(float)Math.Clamp(p.X,106,534);Phase=(float)p.X/16;Stretch=e.GetCurrentPoint(this).Properties.IsLeftButtonPressed?1.12f:1;InvalidateVisual();};}
 public override void Render(DrawingContext c){base.Render(c);c.Custom(new Operation(new Rect(Bounds.Size),Software,Phase,LensX,Stretch));}
 sealed class Operation(Rect bounds,bool fallback,float phase,float lensX,float stretch):ICustomDrawOperation {
  public Rect Bounds=>bounds;public bool HitTest(Point p)=>bounds.Contains(p);public bool Equals(ICustomDrawOperation? other)=>false;public void Dispose(){}
  const string Source="""
   uniform shader background;
   uniform float2 center;
   uniform float2 box;
   half4 main(float2 p) {
     float2 q=abs(p-center)-float2(box.x-box.y,0.0);
     float d=length(max(q,0.0))+min(max(q.x,q.y),0.0)-box.y;
     if(d>0.0) return background.eval(p);
     float2 n=normalize(float2(sign(p.x-center.x)*max(q.x,0.001),sign(p.y-center.y)*max(q.y,0.001)));
     float rim=exp(-abs(d)/12.0);
     half4 c=background.eval(p+n*rim*12.0);
     return half4(mix(c.rgb,half3(0.85,0.9,1.0),0.08)+half3(rim*0.13),1.0);
   }
   """;
  public void Render(ImmediateDrawingContext context){
   var feature=context.TryGetFeature<ISkiaSharpApiLeaseFeature>();if(feature==null){LastStatus="NO_SKIA_FEATURE";return;}
   using var lease=feature.Lease();var canvas=lease.SkCanvas;LastGpu=lease.GrContext!=null;
   var saved=canvas.SaveCount;try{
    using var surface=SKSurface.Create(new SKImageInfo(640,200));var bg=surface.Canvas;bg.Clear(SKColor.Parse("#19243f"));using var paint=new SKPaint{IsAntialias=true};
    for(int x=0;x<640;x+=16){paint.Color=(x/16)%2==0?SKColor.Parse("#8ca7e7"):SKColor.Parse("#d79fc4");bg.DrawRect(x+phase%16,0,6,200,paint);}for(int y=0;y<200;y+=32){paint.Color=SKColor.Parse("#4a687d");bg.DrawRect(0,y,640,2,paint);}
    using var image=surface.Snapshot();canvas.Save();canvas.Scale((float)(bounds.Width/640),(float)(bounds.Height/200));
    if(fallback){canvas.DrawImage(image,0,0);paint.Color=new SKColor(48,62,91,255);canvas.DrawRoundRect(lensX-106,72,212,56,28,28,paint);LastStatus="OPAQUE_FALLBACK";}
    else{using var effect=SKRuntimeEffect.CreateShader(Source,out var error);if(effect==null)throw new InvalidOperationException(error);using var sampler=image.ToShader(SKShaderTileMode.Clamp,SKShaderTileMode.Clamp);var uniforms=new SKRuntimeEffectUniforms(effect){["center"]=new float[]{lensX,100},["box"]=new float[]{106*stretch,28*(2-stretch)}};var children=new SKRuntimeEffectChildren(effect){["background"]=sampler};using var shader=effect.ToShader(uniforms,children);paint.Shader=shader;canvas.DrawRect(0,0,640,200,paint);paint.Shader=null;LastStatus="SAMPLED_REFRACTION";}
    canvas.Restore();Draws++;if(Draws<=3)Program.Log("REFRACTION_NATIVE_DRAW",new{LastStatus,LastGpu,phase,lensX,region="640x200 synthetic texture only"});
   }catch(Exception ex){LastStatus="SAFE_ERROR: "+ex.Message;Program.Log("REFRACTION_FAIL",ex.ToString());using var safe=new SKPaint{Color=SKColor.Parse("#283553")};canvas.DrawRect(new SKRect(0,0,640,200),safe);}finally{canvas.RestoreToCount(saved);}
  }
 }
}
