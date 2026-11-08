using System.Windows;
using System.Windows.Media.Animation;

namespace HudiPet;

internal sealed class PopupMotion
{
    private readonly DialogSurface surface;
    internal bool IsRunning {get;private set;}
    internal PopupMotion(DialogSurface surface)=>this.surface=surface;
    internal void Play()
    {
        surface.BeginAnimation(DialogSurface.RevealProgressProperty,null);
        IsRunning=false;
        if(!SystemParameters.ClientAreaAnimation){surface.RevealProgress=1;return;}
        surface.RevealProgress=0;
        IsRunning=true;
        var reveal=new DoubleAnimationUsingKeyFrames {Duration=TimeSpan.FromMilliseconds(360),FillBehavior=FillBehavior.HoldEnd};
        reveal.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        reveal.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40))));
        reveal.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)),new CubicEase {EasingMode=EasingMode.EaseOut}));
        reveal.Completed+=(_,_)=>Stop();surface.BeginAnimation(DialogSurface.RevealProgressProperty,reveal);
    }
    internal void Stop()
    {
        surface.BeginAnimation(DialogSurface.RevealProgressProperty,null);surface.RevealProgress=1;IsRunning=false;
    }
}
