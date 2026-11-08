using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HudiPet;

// A compositor-owned, click-through surface below the WPF text. Its geometric
// clip masks the blur itself, rather than clipping only the window's paint.
internal sealed class ShapedBackdrop : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint {public float X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {public int Left,Top,Right,Bottom;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateDelegate(IntPtr window,out IntPtr state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UpdateDelegate(IntPtr state,[In] NativePoint[] points,int count,int x,int y,int width,int height,float opacity,int visible);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyDelegate(IntPtr state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr WindowDelegate(IntPtr state);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out NativeRect rectangle);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    private sealed class Api
    {
        internal readonly CreateDelegate Create;
        internal readonly UpdateDelegate Update;
        internal readonly DestroyDelegate Destroy;
        internal readonly WindowDelegate Window;
        internal Api()
        {
            using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("HUDI.Backdrop.dll")??throw new FileNotFoundException("Missing shaped backdrop renderer.");
            using var buffer=new MemoryStream();stream.CopyTo(buffer);byte[] bytes=buffer.ToArray();
            string hash=Convert.ToHexString(SHA256.HashData(bytes));
            string directory=Path.Combine(PetWindow.SettingsDirectory,"native",hash);
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,"Hudi.Backdrop.dll");
            if(!File.Exists(path)||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))!=hash)
            {
                string pending=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                File.WriteAllBytes(pending,bytes);File.Move(pending,path,true);
            }
            IntPtr library=NativeLibrary.Load(path);
            Create=Marshal.GetDelegateForFunctionPointer<CreateDelegate>(NativeLibrary.GetExport(library,"HudiBackdropCreate"));
            Update=Marshal.GetDelegateForFunctionPointer<UpdateDelegate>(NativeLibrary.GetExport(library,"HudiBackdropUpdate"));
            Destroy=Marshal.GetDelegateForFunctionPointer<DestroyDelegate>(NativeLibrary.GetExport(library,"HudiBackdropDestroy"));
            Window=Marshal.GetDelegateForFunctionPointer<WindowDelegate>(NativeLibrary.GetExport(library,"HudiBackdropWindow"));
        }
    }
    private static readonly Lazy<Api> Renderer=new(()=>new Api());
    private readonly Window content;
    private IntPtr state;
    internal IntPtr Handle=>state==IntPtr.Zero?IntPtr.Zero:Renderer.Value.Window(state);
    internal ShapedBackdrop(Window window)
    {
        content=window;
        Marshal.ThrowExceptionForHR(Renderer.Value.Create(new WindowInteropHelper(window).Handle,out state));
    }
    internal void Update(Point[] outline)
    {
        if(state==IntPtr.Zero)return;
        var dpi=VisualTreeHelper.GetDpi(content);
        var points=outline.Select(point=>new NativePoint{X=(float)(point.X*dpi.DpiScaleX),Y=(float)(point.Y*dpi.DpiScaleY)}).ToArray();
        IntPtr window=new WindowInteropHelper(content).Handle;
        if(!GetWindowRect(window,out var bounds))return;
        bool visible=content.IsVisible&&content.Opacity>0&&content.WindowState!=WindowState.Minimized;
        Marshal.ThrowExceptionForHR(Renderer.Value.Update(state,points,points.Length,bounds.Left,bounds.Top,
            bounds.Right-bounds.Left,bounds.Bottom-bounds.Top,(float)content.Opacity,visible?1:0));
    }
    public void Dispose()
    {
        if(state==IntPtr.Zero)return;
        IntPtr previous=state;state=IntPtr.Zero;Renderer.Value.Destroy(previous);
    }
}
