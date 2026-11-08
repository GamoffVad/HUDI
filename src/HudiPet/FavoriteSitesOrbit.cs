using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HudiPet;

internal sealed class SiteOrb:Button
{
    internal readonly TranslateTransform Travel=new();
    internal readonly ScaleTransform Growth=new(.18,.18);
    internal readonly ScaleTransform Press=new(1,1);
    internal readonly ScaleTransform LivingShape=new(1,1);
    internal readonly RotateTransform LivingTilt=new();
    internal const double MaximumLivingScale=1/.99;
    internal readonly WaterOrbSurface Water;
    private readonly Ellipse highlight;
    internal SiteOrb(FavoriteSite site,double diameter)
    {
        Width=Height=diameter;Cursor=Cursors.Hand;FocusVisualStyle=null;Style=null;BorderThickness=new Thickness(0);Padding=new Thickness(0);Background=Brushes.Transparent;
        AutomationProperties.SetName(this,site.Name);
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));Template=new ControlTemplate(typeof(Button)){VisualTree=presenter};
        var face=CreateFace(site.Icon,site.Text,diameter);face.RenderTransform=Press;face.RenderTransformOrigin=new Point(.5,.5);
        // Only the watery material and rim breathe; labels stay sharp and still.
        var living=new TransformGroup();living.Children.Add(LivingShape);living.Children.Add(LivingTilt);
        Water=face.Children.OfType<WaterOrbSurface>().Single();Water.RenderTransform=living;Water.RenderTransformOrigin=new Point(.5,.5);
        var backing=face.Children.OfType<WaterOrbBacking>().Single();backing.RenderTransform=living;backing.RenderTransformOrigin=new Point(.5,.5);
        foreach(var symbol in face.Children.OfType<FrameworkElement>().Where(child=>child is not WaterOrbSurface and not WaterOrbBacking))symbol.CacheMode=new BitmapCache{RenderAtScale=2};
        highlight=new Ellipse{Stroke=Ui.Accent,StrokeThickness=1.8,Opacity=0,IsHitTestVisible=false,RenderTransform=living,RenderTransformOrigin=new Point(.5,.5)};face.Children.Add(highlight);Content=face;
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        var transform=new TransformGroup();Growth.CenterX=Growth.CenterY=diameter/2;transform.Children.Add(Growth);transform.Children.Add(Travel);RenderTransform=transform;
        PreviewMouseLeftButtonDown+=(_,_)=>AnimatePress(.86,90);
        MouseLeave+=(_,_)=>{if(Mouse.LeftButton==MouseButtonState.Pressed)AnimatePress(1,100);};
        LostMouseCapture+=(_,_)=>{if(!IsPressed)AnimatePress(1,100);};
        GotKeyboardFocus+=(_,_)=>highlight.Opacity=1;LostKeyboardFocus+=(_,_)=>highlight.Opacity=0;
        Unloaded+=(_,_)=>StopLivingMotion();
    }
    private void StartLivingMotion(int index,double delay)
    {
        StopLivingMotion();if(!SystemParameters.ClientAreaAnimation)return;
        Water.Start(index,delay);
        double duration=5+(index*.61803398875%1)*1.4;
        double amplitude=.008+(index%3)*.001,orientation=(index%3-1)*12;
        DoubleAnimationUsingKeyFrames Loop(Func<double,double> value)
        {
            var animation=new DoubleAnimationUsingKeyFrames{Duration=TimeSpan.FromSeconds(duration),BeginTime=TimeSpan.FromMilliseconds(delay),RepeatBehavior=RepeatBehavior.Forever};
            for(int i=0;i<=64;i++)animation.KeyFrames.Add(new LinearDoubleKeyFrame(value(i/64d),KeyTime.FromTimeSpan(TimeSpan.FromSeconds(duration*i/64))));
            animation.Freeze();return animation;
        }
        double Stretch(double phase)=>1+amplitude*Math.Sin(2*Math.PI*phase);
        LivingTilt.Angle=orientation;
        LivingShape.BeginAnimation(ScaleTransform.ScaleXProperty,Loop(Stretch));
        LivingShape.BeginAnimation(ScaleTransform.ScaleYProperty,Loop(phase=>1/Stretch(phase)));
        LivingTilt.BeginAnimation(RotateTransform.AngleProperty,Loop(phase=>orientation+2*Math.Sin(2*Math.PI*phase)));
    }
    internal void StopLivingMotion()
    {
        Water.Stop();
        LivingShape.BeginAnimation(ScaleTransform.ScaleXProperty,null);LivingShape.BeginAnimation(ScaleTransform.ScaleYProperty,null);LivingTilt.BeginAnimation(RotateTransform.AngleProperty,null);
        LivingShape.ScaleX=LivingShape.ScaleY=1;LivingTilt.Angle=0;
    }
    internal bool ContainsPoint(Point point)=>InputHitTest(point) is not null;
    internal static Grid CreateFace(string icon,string? text,double diameter)
    {
        var face=new Grid{Width=diameter,Height=diameter};
        face.Children.Add(new WaterOrbBacking());
        if(!string.IsNullOrWhiteSpace(text))
        {
            // Fit the full label inside the sphere, including when a crowded arc shrinks it.
            var label=new TextBlock{Text=text.Trim(),FontSize=diameter*.32,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(224,241,252)),TextAlignment=TextAlignment.Center,Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Color.FromRgb(10,28,44),BlurRadius=3,ShadowDepth=0,Opacity=.9}};
            face.Children.Add(new Viewbox{Width=diameter*.76,Height=diameter*.5,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=label});
        }
        else if(icon.Length>0)
        {
            double size=diameter;
            // Inset only the clipping mask: keep the original image and its scale.
            // The water rim needs room to move without exposing the icon's edge.
            double iconRadius=size*.45;
            var circle=new EllipseGeometry(new Point(size/2,size/2),iconRadius,iconRadius);circle.Freeze();
            var viewport=new Grid{Width=size,Height=size,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Clip=circle};
            viewport.Children.Add(new Image{Source=FavoriteSitesStore.DecodeIcon(icon),Stretch=Stretch.UniformToFill});
            face.Children.Add(viewport);
        }
        // The transparent moving water covers the icon/label, placing it inside
        // the sphere; the symbol itself remains stable and undistorted.
        face.Children.Add(new WaterOrbSurface());
        return face;
    }
    private void AnimatePress(double scale,int milliseconds)
    {
        if(!SystemParameters.ClientAreaAnimation)return;
        foreach(var property in new[]{ScaleTransform.ScaleXProperty,ScaleTransform.ScaleYProperty})Press.BeginAnimation(property,new DoubleAnimation(scale,TimeSpan.FromMilliseconds(milliseconds)){FillBehavior=FillBehavior.HoldEnd});
    }
    internal void ClickFeedback()
    {
        highlight.Opacity=1;
        if(!SystemParameters.ClientAreaAnimation)return;
        var frames=SiteMotionCurve.Animation(300,0,[0,.52,.8,1],[.86,1.09,.98,1],.2,.65,.3,1);
        Press.BeginAnimation(ScaleTransform.ScaleXProperty,frames);Press.BeginAnimation(ScaleTransform.ScaleYProperty,frames);
    }
    internal void Move(Point from,Point to,bool opening,int index,int count)
    {
        double x=opening?from.X:Travel.X,y=opening?from.Y:Travel.Y;
        double endX=opening?to.X:from.X,endY=opening?to.Y:from.Y;
        Travel.BeginAnimation(TranslateTransform.XProperty,null);Travel.BeginAnimation(TranslateTransform.YProperty,null);
        Travel.X=endX;Travel.Y=endY;
        double scale=opening?.18:Growth.ScaleX,opacity=opening?0:Opacity;
        Growth.BeginAnimation(ScaleTransform.ScaleXProperty,null);Growth.BeginAnimation(ScaleTransform.ScaleYProperty,null);BeginAnimation(OpacityProperty,null);
        Growth.ScaleX=Growth.ScaleY=opening?1:.18;Opacity=opening?1:0;IsHitTestVisible=opening;
        double stagger=opening?Math.Min(30,120d/Math.Max(1,count-1)):Math.Min(20,80d/Math.Max(1,count-1));
        double delay=(opening?index:count-1-index)*stagger;
        if(opening)StartLivingMotion(index,380+delay);
        if(!SystemParameters.ClientAreaAnimation)return;
        double duration=opening?380:230;
        double[] offsets=opening?[0,.38,.6,.82,1]:[0,.48,1];
        DoubleAnimationUsingKeyFrames Curve(double[] values)=>SiteMotionCurve.Animation(duration,delay,offsets,values);
        // Base values stay at the start until each delayed clock begins.
        Travel.X=x;Travel.Y=y;Growth.ScaleX=Growth.ScaleY=scale;Opacity=opacity;
        double dx=to.X-from.X,dy=to.Y-from.Y,overX=Math.Clamp(dx*.075,-10,10),overY=Math.Clamp(dy*.075,-10,10);
        var ax=Curve(opening?[x,from.X+dx*.9,to.X+overX,to.X-overX*.24,to.X]:[x,from.X+dx*.62,from.X]);
        var ay=Curve(opening?[y,from.Y+dy*.9-8,to.Y+overY,to.Y-overY*.24,to.Y]:[y,from.Y+dy*.65-12,from.Y]);
        var size=Curve(opening?[scale,.92,1.14,.966,1]:[scale,.7,.18]);
        var alpha=Curve(opening?[opacity,.32,.65,.92,1]:[opacity,.55,0]);
        Travel.BeginAnimation(TranslateTransform.XProperty,ax);Travel.BeginAnimation(TranslateTransform.YProperty,ay);Growth.BeginAnimation(ScaleTransform.ScaleXProperty,size);Growth.BeginAnimation(ScaleTransform.ScaleYProperty,size);BeginAnimation(OpacityProperty,alpha);
    }
}

internal sealed class FavoriteSitesOrbit:Window
{
    private readonly PetWindow pet;
    private readonly FavoriteSite[] sites;
    private readonly double? maximumSphereSize;
    private readonly Canvas canvas=new(){Background=null};
    private readonly List<SiteOrb> orbs=[];
    private SitesArc arc=null!;
    private bool closed,launching;
    private readonly DispatcherTimer dismissTimer=new(){Interval=TimeSpan.FromMilliseconds(320)};
    private readonly DispatcherTimer inputTimer=new(){Interval=TimeSpan.FromMilliseconds(40)};
    private DispatcherTimer? launchTimer;
    private bool lastMouseDown,lastEscapeDown;
    internal bool IsDismissing {get;private set;}
    internal IReadOnlyList<SiteOrb> Orbs=>orbs;
    internal SitesArc Layout=>arc;
    private HwndSource? source;
    [StructLayout(LayoutKind.Sequential)]private struct WindowPosition{public IntPtr Window,InsertAfter;public int X,Y,Width,Height;public uint Flags;}
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [StructLayout(LayoutKind.Sequential)]private struct NativePoint{public int X,Y;}
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out NativePoint point);
    internal FavoriteSitesOrbit(PetWindow pet,FavoriteSite[] sites,double? maximumSphereSize=null)
    {
        this.pet=pet;this.sites=sites;this.maximumSphereSize=maximumSphereSize;Title="HUDI — сайты и программы";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowActivated=false;ShowInTaskbar=false;Topmost=pet.Topmost;Content=canvas;
        // An owned window would always be ABOVE HUDI. This independent no-activate
        // surface is explicitly placed immediately below his native window.
        SourceInitialized+=(_,_)=>{source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source?.AddHook(Hook);};
        Loaded+=(_,_)=>{Place();KeepBehind();for(int i=0;i<orbs.Count;i++)orbs[i].Move(Local(arc.Origin),Local(arc.Centers[i]),true,i,orbs.Count);lastMouseDown=(GetAsyncKeyState(1)&0x8000)!=0;lastEscapeDown=(GetAsyncKeyState(27)&0x8000)!=0;if(!pet.IgnoreGlobalSitesInput)inputTimer.Start();};
        dismissTimer.Tick+=(_,_)=>{dismissTimer.Stop();if(!closed)Close();};inputTimer.Tick+=(_,_)=>PollDismiss();
        pet.LocationChanged+=AnchorMoved;pet.SizeChanged+=AnchorSized;pet.Activated+=AnchorActivated;
        Closed+=(_,_)=>{closed=true;dismissTimer.Stop();inputTimer.Stop();launchTimer?.Stop();foreach(var orb in orbs)orb.StopLivingMotion();pet.LocationChanged-=AnchorMoved;pet.SizeChanged-=AnchorSized;pet.Activated-=AnchorActivated;source?.RemoveHook(Hook);};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){Dismiss();e.Handled=true;}};
    }
    private Point Local(Point screen)=>new(screen.X-Left-arc.Diameter/2,screen.Y-Top-arc.Diameter/2);
    private void Place()
    {
        arc=SitesArc.Arrange(pet.DisplayedBounds,ScreenPlacement.WorkArea(pet),sites.Length,maximumSphereSize);
        var bounds=new Rect(arc.Origin,arc.Origin);foreach(var point in arc.Centers)bounds.Union(point);bounds.Inflate(arc.Diameter/2+24,arc.Diameter/2+24);
        Left=bounds.Left;Top=bounds.Top;Width=bounds.Width;Height=bounds.Height;canvas.Children.Clear();orbs.Clear();
        for(int i=0;i<sites.Length;i++)
        {
            var site=sites[i];var orb=new SiteOrb(site,arc.Diameter){Opacity=0};orb.Click+=(_,_)=>Launch(site,orb);canvas.Children.Add(orb);orbs.Add(orb);
        }
    }
    private void AnchorMoved(object? sender,EventArgs e){if(!closed)Close();}
    private void AnchorActivated(object? sender,EventArgs e){if(!closed)KeepBehind();}
    private void PollDismiss()
    {
        if(closed||IsDismissing)return;
        bool mouse=(GetAsyncKeyState(1)&0x8000)!=0,escape=(GetAsyncKeyState(27)&0x8000)!=0;
        if(escape&&!lastEscapeDown)Dismiss();
        if(mouse&&!lastMouseDown&&GetCursorPos(out var cursor))
        {
            var screen=new Point(cursor.X,cursor.Y);var onPet=pet.PointFromScreen(screen);
            bool insidePet=new Rect(0,0,pet.ActualWidth,pet.ActualHeight).Contains(onPet);
            bool insideSphere=orbs.Any(orb=>orb.ContainsPoint(orb.PointFromScreen(screen)));
            if(!insidePet&&!insideSphere)Dismiss();
        }
        lastMouseDown=mouse;lastEscapeDown=escape;
    }
    private void AnchorSized(object sender,SizeChangedEventArgs e){if(!closed)Close();}
    private void KeepBehind(){if(closed)return;var handle=new WindowInteropHelper(this).Handle;var owner=new WindowInteropHelper(pet).Handle;if(handle!=IntPtr.Zero&&owner!=IntPtr.Zero)SetWindowPos(handle,owner,0,0,0,0,0x0013);}
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x0021){handled=true;return new IntPtr(3);} // MA_NOACTIVATE
        if(message==0x0046&&!closed)
        {
            var position=Marshal.PtrToStructure<WindowPosition>(lParam);var owner=new WindowInteropHelper(pet).Handle;
            if(owner!=IntPtr.Zero){position.InsertAfter=owner;position.Flags=(position.Flags&~0x0004u)|0x0010u;Marshal.StructureToPtr(position,lParam,false);}
        }
        return IntPtr.Zero;
    }
    private void Launch(FavoriteSite site,SiteOrb orb)
    {
        if(launching||IsDismissing||closed)return;launching=true;orb.ClickFeedback();
        void Open()
        {
            launchTimer?.Stop();if(closed||IsDismissing)return;
            try{Process.Start(FavoriteSitesStore.CreateLaunchInfo(site));Dismiss();}
            catch(Exception error) when(error is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException){launching=false;MessageBox.Show(pet,FavoriteSitesStore.IsProgram(site)?"Не удалось запустить программу. Проверьте путь в избранном.":"Не удалось открыть сайт в браузере.","HUDI",MessageBoxButton.OK,MessageBoxImage.Information);}
        }
        if(!SystemParameters.ClientAreaAnimation){Open();return;}
        launchTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(260)};launchTimer.Tick+=(_,_)=>Open();launchTimer.Start();
    }
    internal void Dismiss()
    {
        if(IsDismissing||closed)return;IsDismissing=true;
        for(int i=0;i<orbs.Count;i++)orbs[i].Move(Local(arc.Origin),Local(arc.Centers[i]),false,i,orbs.Count);
        if(SystemParameters.ClientAreaAnimation)dismissTimer.Start();else Close();
    }
}
