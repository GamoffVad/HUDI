using System.Windows.Media.Animation;

namespace HudiPet;

// Browser easing transforms the whole timeline before keyframe interpolation.
// WPF's per-keyframe easing does not: sample the approved HTML timeline instead.
internal static class SiteMotionCurve
{
    internal static double Progress(double time,double x1=.2,double y1=.72,double x2=.24,double y2=1)
    {
        if(time<=0)return 0;if(time>=1)return 1;
        static double Bezier(double t,double a,double b)=>3*(1-t)*(1-t)*t*a+3*(1-t)*t*t*b+t*t*t;
        double low=0,high=1;
        for(int i=0;i<30;i++){double t=(low+high)/2;if(Bezier(t,x1,x2)<time)low=t;else high=t;}
        return Bezier((low+high)/2,y1,y2);
    }
    internal static double Value(double time,double[] offsets,double[] values,double x1=.2,double y1=.72,double x2=.24,double y2=1)
    {
        double progress=Progress(time,x1,y1,x2,y2);
        for(int i=1;i<offsets.Length;i++)if(progress<=offsets[i])return values[i-1]+(values[i]-values[i-1])*(progress-offsets[i-1])/(offsets[i]-offsets[i-1]);
        return values[^1];
    }
    internal static DoubleAnimationUsingKeyFrames Animation(double duration,double delay,double[] offsets,double[] values,double x1=.2,double y1=.72,double x2=.24,double y2=1)
    {
        var animation=new DoubleAnimationUsingKeyFrames{Duration=TimeSpan.FromMilliseconds(duration),BeginTime=TimeSpan.FromMilliseconds(delay),FillBehavior=FillBehavior.HoldEnd};
        const int samples=120;
        for(int i=0;i<=samples;i++)animation.KeyFrames.Add(new LinearDoubleKeyFrame(Value(i/(double)samples,offsets,values,x1,y1,x2,y2),KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration*i/samples))));
        animation.Freeze();return animation;
    }
}
