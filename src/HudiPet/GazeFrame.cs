using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HudiPet;

// A small screen effect follows the pointer continuously. The generated body/head pixels do not move.
internal sealed record EyeLight(BitmapSource Image, Int32Rect Bounds, Point Center, double Width, double Height);
internal sealed class GazeFrame
{
    internal BitmapSource Body { get; }
    internal EyeLight[] Eyes { get; }
    private readonly byte[] bodyPixels;
    private readonly int W, H;
    private GazeFrame(BitmapSource body, EyeLight[] eyes, byte[] pixels) { Body=body; Eyes=eyes; bodyPixels=pixels; W=body.PixelWidth; H=body.PixelHeight; }
    private sealed record Region(int X0,int Y0,int X1,int Y1,int Count)
    { internal double X => (X0+X1)*.5; internal double Y => (Y0+Y1)*.5; }

    internal static GazeFrame Create(BitmapSource original)
    {
        int W=original.PixelWidth,H=original.PixelHeight;
        double scale=H/768d;
        var source=new byte[W*H*4];original.CopyPixels(source,W*4,0);var body=(byte[])source.Clone();
        int top=H,bottom=0;
        for(int y=0;y<H;y++)for(int x=0;x<W;x++)if(source[(y*W+x)*4+3]>160){top=Math.Min(top,y);bottom=y;}
        int yEnd=Math.Min(H-1,top+(int)((bottom-top)*.48));
        var visited=new bool[W*H];var found=new List<Region>();var queue=new Queue<int>();
        bool Blue(int p)=>source[p+3]>200&&source[p]>125&&source[p]-source[p+2]>20&&source[p+1]-source[p+2]>10;
        for(int y=top;y<yEnd;y++)for(int x=12;x<W-12;x++)
        {
            int seed=y*W+x;if(visited[seed]||!Blue(seed*4))continue;
            visited[seed]=true;queue.Enqueue(seed);int x0=x,x1=x,y0=y,y1=y,count=0;
            while(queue.Count>0)
            {
                int n=queue.Dequeue(),nx=n%W,ny=n/W;count++;x0=Math.Min(x0,nx);x1=Math.Max(x1,nx);y0=Math.Min(y0,ny);y1=Math.Max(y1,ny);
                foreach(int next in new[]{n-1,n+1,n-W,n+W})
                    if(next>=0&&next<visited.Length&&!visited[next]&&next/W>=top&&next/W<yEnd&&Math.Abs(next%W-nx)<=1&&Blue(next*4)){visited[next]=true;queue.Enqueue(next);}
            }
            // Blue drawstrings are outside the black visor: accept lights with dark surroundings.
            int dark=0,sampled=0;
            for(int side=0;side<4;side++)for(int k=1;k<=3;k++)
            {
                int tx=side<2?x0+(x1-x0)*k/4:(side==2?x0-6:x1+6);
                int ty=side>=2?y0+(y1-y0)*k/4:(side==0?y0-6:y1+6);
                if(tx<0||tx>=W||ty<0||ty>=H)continue;int p=(ty*W+tx)*4;sampled++;
                if(source[p+3]>220&&Math.Max(source[p],Math.Max(source[p+1],source[p+2]))<175&&source[p]-source[p+2]<24)dark++;
            }
            if(count>=Math.Max(3,8*scale*scale)&&x1-x0>=1&&y1-y0>=1&&dark>=sampled*.45)found.Add(new Region(x0,y0,x1,y1,count));
        }
        var largest=found.OrderByDescending(r=>r.Count).FirstOrDefault();
        var regions=largest is null?[]:found.Where(r=>Math.Abs(r.Y-largest.Y)<70*scale&&r.Count>=largest.Count*.035).OrderByDescending(r=>r.Count).Take(2).OrderBy(r=>r.X).ToArray();
        var lights=new List<EyeLight>();
        foreach(var region in regions)
        {
            int pad=Math.Max(9,(int)Math.Round(24*scale)),x0=Math.Max(1,region.X0-pad),y0=Math.Max(1,region.Y0-pad),x1=Math.Min(W-2,region.X1+pad),y1=Math.Min(H-2,region.Y1+pad),width=x1-x0+1,height=y1-y0+1;
            // Harmonic inpainting follows the curved visor's illumination continuously.
            // Fixed boundaries avoid both a flat replacement square and a residual eye halo.
            var background=new float[width*height*3];
            int stride=width*3;
            for(int py=0;py<height;py++)for(int px=0;px<width;px++)for(int c=0;c<3;c++)
            {
                double u=px/(double)(width-1);
                background[py*stride+px*3+c]=py==0||py==height-1||px==0||px==width-1
                    ?source[((py+y0)*W+px+x0)*4+c]
                    :(float)(source[((py+y0)*W+x0)*4+c]*(1-u)+source[((py+y0)*W+x1)*4+c]*u);
            }
            for(int pass=0;pass<80;pass++)
                for(int py=1;py<height-1;py++)for(int px=1;px<width-1;px++)
                {
                    int q=py*stride+px*3;
                    for(int c=0;c<3;c++)background[q+c]=(background[q+c-3]+background[q+c+3]+background[q+c-stride]+background[q+c+stride])*.25f;
                }
            var light=new byte[width*height*4];
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
            {
                int p=(y*W+x)*4,q=((y-y0)*width+x-x0)*4;
                int backgroundIndex=((y-y0)*width+x-x0)*3;
                double blueExcess=(source[p]-source[p+2])-(background[backgroundIndex]-background[backgroundIndex+2]);
                double mask=Math.Clamp((blueExcess-1)/6,0,1);
                double alpha=0;
                for(int c=0;c<3;c++)
                {
                    body[p+c]=(byte)Math.Clamp(Math.Round(background[backgroundIndex+c]),0,255);
                    double lit=body[p+c]+(source[p+c]-body[p+c])*mask;
                    if(lit>body[p+c])alpha=Math.Max(alpha,(lit-body[p+c])/(255d-body[p+c]));
                    else if(lit<body[p+c])alpha=Math.Max(alpha,(body[p+c]-lit)/(double)body[p+c]);
                }
                int a=Math.Clamp((int)Math.Ceiling(alpha*255),0,255);light[q+3]=(byte)a;
                for(int c=0;c<3;c++)light[q+c]=(byte)Math.Clamp(Math.Round((source[p+c]-body[p+c])*mask+body[p+c]*a/255d),0,a);
            }
            var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,light,width*4);bitmap.Freeze();
            lights.Add(new EyeLight(bitmap,new Int32Rect(x0,y0,width,height),new Point(region.X,region.Y),region.X1-region.X0+1,region.Y1-region.Y0+1));
        }
        var clean=BitmapSource.Create(W,H,96,96,PixelFormats.Pbgra32,null,body,W*4);clean.Freeze();
        return new GazeFrame(clean,lights.ToArray(),body);
    }

    internal Vector ConvergedOffset(EyeLight eye)
    {
        if (Eyes.Length != 2) return new Vector();
        var center = new Point((Eyes[0].Center.X + Eyes[1].Center.X) / 2,
                               (Eyes[0].Center.Y + Eyes[1].Center.Y) / 2);
        double separation = (Eyes[1].Center - Eyes[0].Center).Length;
        // Match the final spacing in the approved Higgsfield hover clip (27.13 / 78.09).
        // Keep wide/closed eye shapes from overlapping, and retain the source head perspective.
        double ratio = Math.Clamp(Math.Max(27.13 / 78.09,
            Math.Max(Eyes[0].Width, Eyes[1].Width) * .95 / Math.Max(1, separation)), 0, .85);
        return (center - eye.Center) * (1 - ratio);
    }

    internal Vector Offset(EyeLight eye, Point pointer)
    {
        Vector direction=pointer-eye.Center;double distance=direction.Length;if(distance<.01)return new Vector();
        direction/=distance;
        double scale=H/768d;
        double radiusX=Math.Clamp(eye.Width*.42,2*scale,15*scale),radiusY=Math.Clamp(eye.Height*.3,5*scale,12*scale);
        double limit=1/Math.Sqrt(direction.X*direction.X/(radiusX*radiusX)+direction.Y*direction.Y/(radiusY*radiusY));
        double amount=Math.Min(limit,distance*.07);
        // Restrict only the distance travelled; the direction remains exactly toward the cursor.
        for(int attempt=0;attempt<8;attempt++)
        {
            var offset=direction*amount;
            bool Fits(double x,double y){int ix=(int)Math.Round(x),iy=(int)Math.Round(y);if(ix<0||ix>=W||iy<0||iy>=H)return false;int p=(iy*W+ix)*4;return bodyPixels[p+3]>180&&Math.Max(bodyPixels[p],Math.Max(bodyPixels[p+1],bodyPixels[p+2]))<145;}
            bool inside=true;
            foreach(var corner in new[]{new Point(-.45,-.45),new Point(.45,-.45),new Point(-.45,.45),new Point(.45,.45)})
                inside&=Fits(eye.Center.X+offset.X+corner.X*eye.Width,eye.Center.Y+offset.Y+corner.Y*eye.Height);
            if(inside)return offset;amount*=.65;
        }
        return direction*amount;
    }
}
