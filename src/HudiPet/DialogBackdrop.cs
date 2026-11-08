using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using System.ComponentModel;
using Hudi.Distribution;

namespace HudiPet;

internal static class DialogBackdrop
{
    private sealed class State
    {
        internal bool Active;
        internal bool? AppliedRequest;
        internal Action? Clip;
        internal Func<Size,Point[]>? Outline;
        internal bool RequestedMatte;
        internal ShapedBackdrop? Shaped;
    }
    private static readonly ConditionalWeakTable<Window,State> States=new();
    internal static bool IsMatteApplied(Window window)=>States.TryGetValue(window,out var state)&&state.Active;
    internal static IntPtr ShapedHandle(Window window)=>States.TryGetValue(window,out var state)?state.Shaped?.Handle??IntPtr.Zero:IntPtr.Zero;
    internal static string? LastError {get;private set;}
    internal static void Refresh(Window window){if(States.TryGetValue(window,out var state))state.Clip?.Invoke();}
    [StructLayout(LayoutKind.Sequential)] private struct Accent {public int State,Flags,Color,Animation;}
    [StructLayout(LayoutKind.Sequential)] private struct CompositionData {public int Attribute;public IntPtr Data;public int Size;}
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    internal static Geometry Polygon(Point[] points)
    {
        var geometry=new StreamGeometry();
        using(var context=geometry.Open()){context.BeginFigure(points[0],true,true);context.PolyLineTo(points.Skip(1).ToArray(),true,false);}
        geometry.Freeze();return geometry;
    }
    internal static Geometry CreateClip(Window window,Size size)=>States.TryGetValue(window,out var state)&&state.Outline is not null
        ?Polygon(state.Outline(size)):new RectangleGeometry(new Rect(size),FrostedBackdrop.CornerRadius,FrostedBackdrop.CornerRadius);
    internal static void SetOutline(Window window,Func<Size,Point[]>? outline)
    {
        var state=States.GetOrCreateValue(window);state.Outline=outline;SetMatte(window,state.RequestedMatte);state.Clip?.Invoke();
    }
    internal static bool SetMatte(Window window,bool enabled)
    {
        var state=States.GetOrCreateValue(window);var handle=new WindowInteropHelper(window).Handle;
        state.RequestedMatte=enabled;
        if(handle==IntPtr.Zero)return false;
        bool shaped=enabled&&state.Outline is not null;
        if(!shaped&&state.Shaped is not null){state.Shaped.Dispose();state.Shaped=null;state.Active=false;}
        ApplyAccent(window,state,enabled&&!shaped);
        if(shaped)
        {
            try
            {
                state.Shaped??=new ShapedBackdrop(window);
                state.Active=true;state.Clip?.Invoke();LastError=null;
            }
            catch(Exception error)
            {
                state.Shaped?.Dispose();state.Shaped=null;state.Active=false;LastError=error.ToString();
                System.IO.File.WriteAllText(System.IO.Path.Combine(PetWindow.SettingsDirectory,"backdrop-error.txt"),LastError);
            }
        }
        return state.Active;
    }
    private static bool ApplyAccent(Window window,State state,bool enabled)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(state.AppliedRequest==enabled)return state.Active;
        // Blur behind the real alpha surface, without an opaque Acrylic tint.
        var policy=new Accent {State=enabled?3:0};int size=Marshal.SizeOf<Accent>();var memory=Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy,memory,false);var data=new CompositionData {Attribute=19,Data=memory,Size=size};
            bool success=SetWindowCompositionAttribute(handle,ref data)!=0;
            state.Active=success&&enabled;state.AppliedRequest=success?enabled:null;
            if(!state.Active)SetWindowRgn(handle,IntPtr.Zero,true);
            state.Clip?.Invoke();return state.Active;
        }
        catch(EntryPointNotFoundException){return false;}
        finally{Marshal.FreeHGlobal(memory);}
    }
    internal static void Attach(Window window)
    {
        // Transparent WPF brushes alone do not expose the desktop. The HWND
        // must be layered, with per-pixel alpha and no DWM Acrylic underneath.
        window.AllowsTransparency=true;
        window.Background=Brushes.Transparent;
        var state=States.GetOrCreateValue(window);
        void ClipContent()
        {
            if(window.Content is not FrameworkElement content||content.ActualWidth<1||content.ActualHeight<1)return;
            content.Clip=CreateClip(window,content.RenderSize);
            if(state.Shaped is not null)
            {
                state.Shaped.Update(state.Outline!(content.RenderSize));
            }
            else if(state.Active)
            {
                // GDI regions have a binary edge. Keep it one physical pixel
                // outside WPF's curve so it cannot cut off antialiased pixels.
                // WPF still owns the visible radius; the region only bounds blur.
                var dpi=VisualTreeHelper.GetDpi(window);
                var region=CreateRoundRectRgn(-1,-1,(int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX)+2,(int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY)+2,
                    (int)Math.Round(FrostedBackdrop.CornerRadius*2*dpi.DpiScaleX)+2,(int)Math.Round(FrostedBackdrop.CornerRadius*2*dpi.DpiScaleY)+2);
                if(SetWindowRgn(new WindowInteropHelper(window).Handle,region,true)==0)DeleteObject(region);
            }
        }
        state.Clip=ClipContent;
        window.Loaded+=(_,_)=>ClipContent();
        window.SizeChanged+=(_,_)=>window.Dispatcher.BeginInvoke(ClipContent,DispatcherPriority.Render);
        window.DpiChanged+=(_,_)=>window.Dispatcher.BeginInvoke(ClipContent,DispatcherPriority.Render);
        window.LocationChanged+=(_,_)=>{if(state.Shaped is not null)ClipContent();};
        window.IsVisibleChanged+=(_,_)=>{if(state.Shaped is not null)ClipContent();};
        window.Activated+=(_,_)=>{if(state.Shaped is not null)ClipContent();};
        window.Deactivated+=(_,_)=>{if(state.Shaped is not null)ClipContent();};
        var opacity=DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty,typeof(Window));
        EventHandler opacityChanged=(_,_)=>{if(state.Shaped is not null)ClipContent();};
        opacity.AddValueChanged(window,opacityChanged);
        window.Closed+=(_,_)=>{opacity.RemoveValueChanged(window,opacityChanged);state.Shaped?.Dispose();state.Shaped=null;state.Active=false;};
    }
}
