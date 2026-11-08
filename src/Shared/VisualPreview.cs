using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hudi.Distribution;
internal interface IWindowClipProvider
{
    Geometry GetContentClip(Size size);
}
internal static class VisualPreview
{
    internal static void Save(Window window,string path)
    {
        var content=(FrameworkElement)window.Content;
        bool fixedHeight=window.SizeToContent is SizeToContent.Manual or SizeToContent.Width&&double.IsFinite(window.Height);
        content.Measure(new Size(window.Width,fixedHeight?window.Height:double.PositiveInfinity));
        var size=new Size(window.Width,fixedHeight?window.Height:content.DesiredSize.Height);content.Arrange(new Rect(size));content.UpdateLayout();
        // Wrapped text can invalidate the first measure during template layout.
        size=new Size(content.ActualWidth,content.ActualHeight);
        // Preview windows are not shown, so the deferred native SizeChanged
        // callback cannot refresh a clip left by the previous composer height.
        content.Clip=window is IWindowClipProvider provider?provider.GetContentClip(size):new RectangleGeometry(new Rect(size),FrostedBackdrop.CornerRadius,FrostedBackdrop.CornerRadius);
        content.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);
    }
}
