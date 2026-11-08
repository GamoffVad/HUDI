using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HudiPet;

internal sealed class DialogSurface : Border
{
    internal const double SquareSize=320;
    internal const double ContentInset=24;
    internal bool SquareFrame {get;set;}
    internal static readonly DependencyProperty RevealProgressProperty=DependencyProperty.Register(nameof(RevealProgress),typeof(double),typeof(DialogSurface),
        new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender,(sender,_)=>
        {
            var surface=(DialogSurface)sender;
            if(surface.Child is UIElement content)content.Opacity=surface.RevealProgress>=1?1:Math.Clamp((surface.RevealProgress-.92)/.08,0,1);
            surface.OutlineChanged?.Invoke();
        }));
    internal double RevealProgress {get=>(double)GetValue(RevealProgressProperty);set=>SetValue(RevealProgressProperty,value);}
    internal event Action? OutlineChanged;

    internal static Point[] Outline(Size size)=>[new(0,0),new(size.Width,0),new(size.Width,size.Height),new(0,size.Height)];
    internal Point[] CurrentOutline(Size size)=>Contour(size,0);
    private Point[] Contour(Size size,double inset)
    {
        double progress=Math.Clamp(RevealProgress,0,1);
        double width=16+(size.Width-16)*progress,height=16+(size.Height-16)*progress;
        var bounds=new Rect((size.Width-width)/2+inset,size.Height-height+inset,Math.Max(0,width-2*inset),Math.Max(0,height-2*inset));
        double radius=Math.Max(0,Math.Min(width,height)/2*(1-progress)-inset);
        if(radius<.01)return [bounds.TopLeft,bounds.TopRight,bounds.BottomRight,bounds.BottomLeft];
        Point[] centers=[new(bounds.Right-radius,bounds.Top+radius),new(bounds.Right-radius,bounds.Bottom-radius),
            new(bounds.Left+radius,bounds.Bottom-radius),new(bounds.Left+radius,bounds.Top+radius)];
        var points=new List<Point>();
        for(int corner=0;corner<4;corner++)for(int step=0;step<=8;step++)
        {
            double angle=(-90+corner*90+step*90d/8)*Math.PI/180;
            points.Add(new Point(centers[corner].X+Math.Cos(angle)*radius,centers[corner].Y+Math.Sin(angle)*radius));
        }
        return points.ToArray();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if(!SquareFrame){base.OnRender(drawing);return;}
        var outer=DialogBackdrop.Polygon(CurrentOutline(RenderSize));
        drawing.DrawGeometry(Background,null,outer);

        // Equal insets keep all four sides and corners the same thickness.
        const double thickness=2;
        var inner=DialogBackdrop.Polygon(Contour(RenderSize,thickness));
        drawing.DrawGeometry(BorderBrush,null,new CombinedGeometry(GeometryCombineMode.Exclude,outer,inner));
    }
}
