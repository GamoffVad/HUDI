using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HudiPet;

internal sealed class PetVisual : FrameworkElement
{
    private readonly SpriteLibrary library;
    private readonly FrameMixer mixer;
    private BitmapSource image = null!;
    private string current = "";
    private double breath;
    internal const double BreathAmplitude = .002;
    internal int PoseChanges { get; private set; }
    internal PetVisual(SpriteLibrary? sprites=null)
    {
        library=sprites??SpriteLibrary.Shared.Value;mixer=new(library);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Apply(new PetPose(library.NeutralFrame));
    }
    internal void Apply(PetPose pose)
    {
        if (pose.Frame != current) { current = pose.Frame; PoseChanges++; }
        image = mixer.Compose(pose);
        breath = pose.Breath;
        InvalidateVisual();
    }
    private void Draw(DrawingContext context, int width, int height)
    {
        context.PushTransform(new ScaleTransform(width / (double)library.Width, height / (double)library.Height));
        context.PushTransform(new ScaleTransform(1, 1 + library.BreathAmplitude * breath, library.Width / 2d, library.Height - 15));
        // Draw the intact source frame. Never cut out, fill, move or redraw the eyes.
        context.DrawImage(image, new Rect(0, 0, library.Width, library.Height));
        context.Pop(); context.Pop();
    }
    internal bool ContainsCharacter(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return false;
        double baseline = library.Height - 15;
        int x = (int)Math.Floor(point.X / ActualWidth * library.Width);
        double nativeY = point.Y / ActualHeight * library.Height;
        int y = (int)Math.Floor(baseline + (nativeY - baseline) / (1 + library.BreathAmplitude * breath));
        return x >= 0 && x < library.Width && y >= 0 && y < library.Height && mixer.Pixels[(y * library.Width + x) * 4 + 3] > 180;
    }
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) =>
        ContainsCharacter(parameters.HitPoint) ? new PointHitTestResult(this, parameters.HitPoint) : null;
    protected override void OnRender(DrawingContext context) => Draw(context, (int)Math.Round(ActualWidth), (int)Math.Round(ActualHeight));
    internal BitmapSource Snapshot(int width = 384, int height = 500)
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) Draw(context, width, height);
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(drawing); result.Freeze(); return result;
    }
}
