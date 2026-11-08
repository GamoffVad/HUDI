using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HudiPet;

// Interpolate premultiplied RGBA, including alpha, before drawing once.
// Two opacity layers with source-over made opaque fabric 75% opaque at mid-fade.
internal sealed class FrameMixer
{
    private readonly SpriteLibrary library;
    private readonly byte[] output;
    private byte[]? transitionStart;
    private readonly WriteableBitmap bitmap;
    private int transitionId;
    private bool initialized;
    internal byte[] Pixels => output;

    internal FrameMixer(SpriteLibrary? sprites=null)
    {
        library=sprites??SpriteLibrary.Shared.Value;
        output = new byte[library.Width * library.Height * 4];
        bitmap = new WriteableBitmap(library.Width, library.Height, 96, 96, PixelFormats.Pbgra32, null);
    }

    internal static void Lerp(byte[] from, byte[] to, double fraction, byte[] destination)
    {
        int weight = Math.Clamp((int)Math.Round(fraction * 65536), 0, 65536), inverse = 65536 - weight;
        for (int i = 0; i < destination.Length; i++)
            destination[i] = (byte)((from[i] * inverse + to[i] * weight + 32768) >> 16);
    }

    internal BitmapSource Compose(PetPose pose)
    {
        if (pose.TransitionId != transitionId)
        {
            // Capture the actual displayed blend, including an interrupted transition.
            transitionStart = initialized ? (byte[])output.Clone() : library.Pixels(pose.BlendFrom ?? pose.Frame);
            transitionId = pose.TransitionId;
        }
        var source = library.Pixels(pose.Frame);
        source.CopyTo(output, 0);
        if (pose.BlendFrom is not null && pose.Blend < 1)
        {
            transitionStart ??= library.Pixels(pose.BlendFrom);
            Lerp(transitionStart, output, pose.Blend, output);
        }
        else transitionStart = null;
        initialized = true;
        bitmap.WritePixels(new Int32Rect(0, 0, library.Width, library.Height), output, library.Width * 4, 0);
        return bitmap;
    }
}
