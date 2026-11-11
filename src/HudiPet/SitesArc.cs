#if HUDI_AVALONIA
using Avalonia;
#else
using System.Windows;
#endif

namespace HudiPet;

internal enum SitesArcSide { Top, Left, Right, Bottom }

internal sealed record SitesArc(Point Origin,Point[] Centers,double Diameter,SitesArcSide Side)
{
    internal double SweepDegrees {get;init;}
    internal double Radius=>Centers.Length>0?new Vector(Centers[0].X-Origin.X,Centers[0].Y-Origin.Y).Length:0;
    internal const double MaximumLivingScale=1/.99;
    internal static double DefaultMaximumSize(double petHeight)=>Math.Clamp(petHeight*.15,32,64);
    internal static SitesArc Arrange(Rect pet,Rect work,int count,double? maximumSphereSize=null)
    {
        if(count<1)throw new ArgumentOutOfRangeException(nameof(count));
        var origin=new Point(pet.Left+pet.Width/2,pet.Top+pet.Height/2);
        double nominal=maximumSphereSize is {} size?FavoriteSitesStore.NormalizeSphereSize(size):DefaultMaximumSize(pet.Height);
        nominal=Math.Min(nominal,Math.Min(work.Width,work.Height)*.3);
        double desiredRadius=Math.Max(pet.Height*.65,Math.Sqrt(pet.Width*pet.Width+pet.Height*pet.Height)/2+nominal/2+12);
        double Diameter(double sweep)
        {
            if(count==1)return nominal;
            if(count==2)sweep=Math.Min(sweep,90);
            double chord=2*desiredRadius*Math.Sin(sweep*Math.PI/180/(2*(count-1)));
            return Math.Min(nominal,chord*.78);
        }
        double Room(SitesArcSide side)=>side switch
        {
            SitesArcSide.Top=>origin.Y-work.Top,SitesArcSide.Bottom=>work.Bottom-origin.Y,
            SitesArcSide.Left=>origin.X-work.Left,_=>work.Right-origin.X
        };
        static double Angle(SitesArcSide side)=>side switch
        {SitesArcSide.Top=>-90,SitesArcSide.Left=>180,SitesArcSide.Right=>0,_=>90};
        Vector Direction(double angle)=>new(Math.Cos(angle*Math.PI/180),Math.Sin(angle*Math.PI/180));
        Vector[] Directions(double middle,double sweep)=>Enumerable.Range(0,count)
            .Select(i=>Direction(count==1?middle:middle-sweep/2+sweep*i/(count-1))).ToArray();

        // Prefer above until its natural semicircle reaches a screen edge.
        double topPadding=Diameter(180)*.57+12;
        var preferred=origin.X+desiredRadius+topPadding>work.Right?SitesArcSide.Left:
            origin.X-desiredRadius-topPadding<work.Left?SitesArcSide.Right:
            origin.Y-desiredRadius-topPadding<work.Top?SitesArcSide.Bottom:SitesArcSide.Top;
        var sides=new[]{preferred}.Concat(Enum.GetValues<SitesArcSide>().OrderByDescending(Room)).Distinct().ToArray();
        SitesArcSide SideFor(double middle)
        {
            var direction=Direction(middle);
            return sides.OrderByDescending(side=>
            {
                var other=Direction(Angle(side));
                return Math.Round(direction.X*other.X+direction.Y*other.Y,8);
            }).First();
        }
        var angles=sides.Select(Angle).Concat(new[]{45d,135,225,315}
            .OrderByDescending(a=>Room(SideFor(a)))).Distinct().ToArray();
        SitesArc? Candidate(double middle,double sweep,double diameter)
        {
            if(count==2)sweep=Math.Min(sweep,90);
            var directions=Directions(middle,sweep);
            double clearance=diameter*MaximumLivingScale/2+12;
            double inset=diameter*.57+12;
            double minimum=0,maximum=double.PositiveInfinity;
            foreach(var direction in directions)
            {
                double x=Math.Abs(direction.X),y=Math.Abs(direction.Y);
                if(x<1e-9&&Math.Min(origin.X-work.Left,work.Right-origin.X)<inset)return null;
                if(y<1e-9&&Math.Min(origin.Y-work.Top,work.Bottom-origin.Y)<inset)return null;
                // One common radius clears the expanded body at every angle.
                double clearX=x<1e-9?double.PositiveInfinity:(pet.Width/2+clearance)/x;
                double clearY=y<1e-9?double.PositiveInfinity:(pet.Height/2+clearance)/y;
                minimum=Math.Max(minimum,Math.Min(clearX,clearY));
                if(x>1e-9)maximum=Math.Min(maximum,((direction.X>0?work.Right-origin.X:origin.X-work.Left)-inset)/x);
                if(y>1e-9)maximum=Math.Min(maximum,((direction.Y>0?work.Bottom-origin.Y:origin.Y-work.Top)-inset)/y);
            }
            if(count>1)minimum=Math.Max(minimum,(diameter*1.14+Math.Min(8,diameter*.22))/(2*Math.Sin(sweep*Math.PI/180/(2*(count-1)))));
            if(!double.IsFinite(maximum)||minimum>maximum||maximum<=0)return null;
            double radius=Math.Clamp(desiredRadius,minimum,maximum);
            return new(origin,directions.Select(v=>origin+v*radius).ToArray(),diameter,SideFor(middle)){SweepDegrees=count==1?0:sweep};
        }
        // Exact centered semicircle normally: no translated circle or endpoints.
        foreach(double scale in new[]{1d,.85,.7,.6})foreach(double middle in angles)
            if(Candidate(middle,180,Diameter(180)*scale) is {} full)return full;
        // At corners, rotate inward and narrow only when a complete semicircle
        // cannot stay on screen and clear the pet. Its center never changes.
        var fineAngles=angles.Concat(Enumerable.Range(0,32).Select(i=>i*11.25)).Distinct().ToArray();
        for(double sweep=170;sweep>=90;sweep-=10)
            foreach(double scale in new[]{1d,.85,.7})foreach(double middle in fineAngles)
                if(Candidate(middle,sweep,Diameter(sweep)*scale) is {} fitted)return fitted;
        for(double sweep=170;sweep>=20;sweep-=10)
            foreach(double scale in new[]{.5,.3,.1})foreach(double middle in fineAngles)
                if(Candidate(middle,sweep,Diameter(sweep)*scale) is {} fitted)return fitted;
        // If the body covers the whole work area, no outside arc can exist.
        // Preserve its center rather than silently shift the circle elsewhere.
        double safeRadius=Math.Max(.001,Math.Min(Math.Min(origin.X-work.Left,work.Right-origin.X),Math.Min(origin.Y-work.Top,work.Bottom-origin.Y))*.5);
        double safeDiameter=Math.Max(.001,Math.Min(nominal,2*safeRadius*Math.Sin(Math.PI/(2*Math.Max(1,count-1)))*.5));
        double fallbackSweep=count==2?90:180;
        return new(origin,Directions(Angle(preferred),fallbackSweep).Select(v=>origin+v*safeRadius).ToArray(),safeDiameter,preferred){SweepDegrees=count==1?0:fallbackSweep};
    }
}
