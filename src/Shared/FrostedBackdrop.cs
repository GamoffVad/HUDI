using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hudi.Distribution;

internal static class FrostedBackdrop
{
    // Keep both the WPF surface and DWM backdrop square.
    internal const double CornerRadius = 0;
    private const int CornerPreference = 33, SystemBackdropType = 38;
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    internal static Brush Tint => new LinearGradientBrush(Color.FromArgb(64,76,96,116), Color.FromArgb(48,35,50,67), 68);
    internal static Brush Edge => new LinearGradientBrush(Color.FromArgb(184,228,242,252), Color.FromArgb(84,148,174,196), 90);

    internal static void Attach(Window window)
    {
        window.AllowsTransparency = false;
        window.Background = Brushes.Transparent;
        bool nativeCorners = OperatingSystem.IsWindowsVersionAtLeast(10,0,22000);

        void ClipContent()
        {
            if (window.Content is not FrameworkElement content || content.ActualWidth < 1 || content.ActualHeight < 1) return;
            content.Clip = new RectangleGeometry(new Rect(0,0,content.ActualWidth,content.ActualHeight), CornerRadius, CornerRadius);
            if (nativeCorners) return;

            // Older Windows gets an opaque material and a matching region.
            // Never use a region together with the system backdrop on Windows 11.
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var dpi = VisualTreeHelper.GetDpi(window);
            var region = CreateRoundRectRgn(0,0,(int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX)+1,
                (int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY)+1,
                (int)Math.Round(CornerRadius*2*dpi.DpiScaleX), (int)Math.Round(CornerRadius*2*dpi.DpiScaleY));
            if (SetWindowRgn(hwnd,region,true) == 0) DeleteObject(region);
        }

        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (nativeCorners)
            {
                SetWindowRgn(hwnd,IntPtr.Zero,true);
                int square = 1, dark = 1, noBorder = -2;
                DwmSetWindowAttribute(hwnd,CornerPreference,ref square,4);
                DwmSetWindowAttribute(hwnd,20,ref dark,4);
                DwmSetWindowAttribute(hwnd,34,ref noBorder,4);
            }
            if (OperatingSystem.IsWindowsVersionAtLeast(10,0,22621))
            {
                if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
                var margins = new Margins { Left=-1, Right=-1, Top=-1, Bottom=-1 };
                DwmExtendFrameIntoClientArea(hwnd,ref margins);
                int acrylic = 3;
                if (DwmSetWindowAttribute(hwnd,SystemBackdropType,ref acrylic,4) == 0) return;
            }
            window.Background = new SolidColorBrush(Color.FromRgb(27,39,53));
        }

        window.SourceInitialized += (_,_) => Apply();
        window.Loaded += (_,_) => { Apply(); ClipContent(); };
        window.SizeChanged += (_,_) => window.Dispatcher.BeginInvoke(ClipContent,DispatcherPriority.Render);
    }

    internal static int ReadMode(Window window) => DwmGetWindowAttribute(new WindowInteropHelper(window).Handle,SystemBackdropType,out int mode,4) == 0 ? mode : -1;
}
