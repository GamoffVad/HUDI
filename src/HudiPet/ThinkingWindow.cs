using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace HudiPet;

// Three quiet animated pearls over the desktop, without a backing card.
internal sealed class ThinkingWindow:Window
{
    private readonly PetWindow anchor;
    internal readonly List<Image> Dots=[];
    internal ThinkingWindow(PetWindow anchor)
    {
        this.anchor=anchor;Owner=anchor;Title="Модель думает";Width=68;Height=28;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=anchor.Topmost;IsHitTestVisible=false;
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=new Uri("pack://application:,,,/HUDI;component/Assets/thinking-dot.png");bitmap.DecodePixelWidth=64;bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();bitmap.Freeze();
        var dots=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        for(int i=0;i<3;i++)
        {
            var transforms=new TransformGroup();transforms.Children.Add(new ScaleTransform(1,1));transforms.Children.Add(new TranslateTransform());
            var dot=new Image {Source=bitmap,Width=12,Height=12,Margin=new Thickness(3,0,3,0),Opacity=.45,RenderTransform=transforms,RenderTransformOrigin=new Point(.5,.5),IsHitTestVisible=false};
            RenderOptions.SetBitmapScalingMode(dot,BitmapScalingMode.HighQuality);dots.Children.Add(dot);Dots.Add(dot);
        }
        Content=dots;Loaded+=(_,_)=>{Place();StartMotion();};anchor.LocationChanged+=Moved;anchor.SizeChanged+=Sized;
        Closed+=(_,_)=>{anchor.LocationChanged-=Moved;anchor.SizeChanged-=Sized;StopMotion();};
    }
    private static DoubleAnimationUsingKeyFrames Pulse(double rest,double peak,int index)
    {
        var animation=new DoubleAnimationUsingKeyFrames {Duration=TimeSpan.FromMilliseconds(1260),BeginTime=TimeSpan.FromMilliseconds(index*150),RepeatBehavior=RepeatBehavior.Forever};
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(rest,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(peak,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),new KeySpline(.4,0,.2,1)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(rest,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)),new KeySpline(.4,0,.2,1)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(rest,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1260))));return animation;
    }
    private void StartMotion()
    {
        if(!SystemParameters.ClientAreaAnimation)return;
        for(int i=0;i<Dots.Count;i++)
        {
            var dot=Dots[i];var transforms=(TransformGroup)dot.RenderTransform;var scale=(ScaleTransform)transforms.Children[0];var lift=(TranslateTransform)transforms.Children[1];
            dot.BeginAnimation(OpacityProperty,Pulse(.38,.82,i));lift.BeginAnimation(TranslateTransform.YProperty,Pulse(0,-2.5,i));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,Pulse(.94,1.04,i));scale.BeginAnimation(ScaleTransform.ScaleYProperty,Pulse(.94,1.04,i));
        }
    }
    private void StopMotion()
    {
        foreach(var dot in Dots)
        {
            dot.BeginAnimation(OpacityProperty,null);var transforms=(TransformGroup)dot.RenderTransform;
            ((TranslateTransform)transforms.Children[1]).BeginAnimation(TranslateTransform.YProperty,null);
            ((ScaleTransform)transforms.Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty,null);((ScaleTransform)transforms.Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty,null);
        }
    }
    private void Moved(object? sender,EventArgs e)=>Place();
    private void Sized(object sender,SizeChangedEventArgs e)=>Place();
    private void Place()
    {
        var area=ScreenPlacement.WorkArea(anchor);
        Left=Math.Clamp(anchor.Left+anchor.Width/2-Width/2,area.Left,Math.Max(area.Left,area.Right-Width));
        Top=Math.Clamp(anchor.Top-Height-12,area.Top,Math.Max(area.Top,area.Bottom-Height));
    }
}
