using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HudiPet;

// All spheres share the approved Higgsfield loop (17 MiB) and one render subscription.
// Chroma removal and the fixed crop are prepared offline; retain the source cadence.
internal static class WaterOrbFrames
{
    internal const int Count=120,Fps=24,Size=192;
    private static readonly Lazy<Task<BitmapSource[]>> pending=new(()=>Task.Run(Load));
    internal static Task<BitmapSource[]> Ready=>pending.Value;
    internal static void WarmUp()=>_=Ready;
    internal static BitmapSource? Get(int frame)=>Ready.IsCompletedSuccessfully?Ready.Result[frame%Count]:null;
    private static BitmapSource[] Load()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("HUDI.water-orb.zip")??throw new InvalidDataException("Missing water orb animation.");
        using var archive=new ZipArchive(stream,ZipArchiveMode.Read);
        var frames=new BitmapSource[Count];
        for(int index=0;index<Count;index++)
        {
            using var input=(archive.GetEntry($"frame-{index:D3}.png")??throw new InvalidDataException("Missing water frame.")).Open();
            using var encoded=new MemoryStream();input.CopyTo(encoded);encoded.Position=0;
            var source=BitmapDecoder.Create(encoded,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
            if(source.PixelWidth!=Size||source.PixelHeight!=Size)throw new InvalidDataException("Invalid water frame dimensions.");
            var image=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);image.Freeze();frames[index]=image;
        }
        return frames;
    }
}

// A soft blue body beneath symbols makes the orb less transparent without
// hiding labels behind a solid wash. The source water stays above the symbols.
internal sealed class WaterOrbBacking:FrameworkElement
{
    private readonly Brush fill=PetAppearance.Current.Disc;
    private static Brush CreateFill()
    {
        var brush=new RadialGradientBrush
        {
            GradientStops=new GradientStopCollection
            {
                new(Color.FromArgb(122,129,168,197),0),
                new(Color.FromArgb(122,129,168,197),.72),
                new(Color.FromArgb(96,129,168,197),.92),
                new(Color.FromArgb(0,129,168,197),1)
            }
        };
        brush.Freeze();return brush;
    }
    internal WaterOrbBacking(){IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext context)=>context.DrawEllipse(fill,null,
        new Point(ActualWidth/2,ActualHeight/2),ActualWidth*.5,ActualHeight*.5);
}

internal sealed class WaterOrbSurface:FrameworkElement
{
    private static readonly HashSet<WaterOrbSurface> active=[];
    private static readonly Stopwatch clock=Stopwatch.StartNew();
    private double begins;
    private int offset;
    internal int FrameIndex {get;private set;}
    internal bool IsAnimating=>active.Contains(this);
    internal static int ActiveCount=>active.Count;
    private static readonly Brush fallback=CreateFallback();
    private static Brush CreateFallback()
    {
        var brush=new RadialGradientBrush(Color.FromArgb(18,129,168,197),Color.FromArgb(115,129,168,197));brush.Freeze();return brush;
    }
    internal WaterOrbSurface()
    {
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        Unloaded+=(_,_)=>Stop();
    }
    private async void RefreshWhenReady()
    {
        try{await WaterOrbFrames.Ready;if(IsLoaded)InvalidateVisual();}
        catch(IOException){/* The packaged resource is verified before release. */}
    }
    internal void Start(int index,double delay)
    {
        Stop();if(!SystemParameters.ClientAreaAnimation)return;
        offset=index*37%WaterOrbFrames.Count;FrameIndex=offset;begins=clock.Elapsed.TotalSeconds+delay/1000;
        if(active.Count==0)CompositionTarget.Rendering+=RenderAll;
        active.Add(this);InvalidateVisual();
    }
    internal void Stop()
    {
        if(active.Remove(this)&&active.Count==0)CompositionTarget.Rendering-=RenderAll;
    }
    private static void RenderAll(object? sender,EventArgs args)
    {
        double now=clock.Elapsed.TotalSeconds;
        foreach(var surface in active)
        {
            int frame=(surface.offset+(int)(Math.Max(0,now-surface.begins)*WaterOrbFrames.Fps))%WaterOrbFrames.Count;
            if(frame==surface.FrameIndex)continue;
            surface.FrameIndex=frame;surface.InvalidateVisual();
        }
    }
    protected override void OnRender(DrawingContext context)
    {
        // Soft light on a shallow cartoon disc. The middle stays clear for icons.
        double w=ActualWidth,h=ActualHeight;if(w<=0||h<=0)return;
        var shine=new LinearGradientBrush(Color.FromArgb(60,255,255,255),Colors.Transparent,90);
        var rim=new StreamGeometry();using(var path=rim.Open())
        {path.BeginFigure(new Point(w*.2,h*.17),false,false);path.BezierTo(new Point(w*.36,h*.025),new Point(w*.64,h*.025),new Point(w*.8,h*.17),true,false);}
        context.DrawGeometry(null,new Pen(shine,Math.Max(1,w*.035)){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},rim);
    }
    protected override HitTestResult? HitTestCore(PointHitTestParameters hit)
    {
        if(ActualWidth<=0||ActualHeight<=0)return null;
        double x=(hit.HitPoint.X-ActualWidth/2)/(ActualWidth/2),y=(hit.HitPoint.Y-ActualHeight/2)/(ActualHeight/2);
        return x*x+y*y<=1?new PointHitTestResult(this,hit.HitPoint):null;
    }
}
