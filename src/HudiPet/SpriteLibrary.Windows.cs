using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HudiPet;

internal sealed partial class SpriteLibrary
{
    private readonly Dictionary<string, (BitmapSource image, byte[] pixels, long used)> cache = new();
    private long access;
    internal int CachedFrames => cache.Count;
    private SpriteLibrary() : this(OpenBuiltInArchive(),true) { }
    internal static Stream OpenBuiltInArchive()=>Assembly.GetExecutingAssembly().GetManifestResourceStream("HudiPet.sprites.zip")
        ?? throw new InvalidDataException("Missing whole-frame sprite library.");
    private void DisposeFrames()=>cache.Clear();

    internal BitmapSource Get(string id)
    {
        if (cache.TryGetValue(id, out var found))
        {
            cache[id] = (found.image, found.pixels, ++access);
            return found.image;
        }
        using var encoded = new MemoryStream();
        using (var input = (archive.GetEntry(id + ".png") ?? throw new InvalidDataException(id)).Open())
        {byte[] buffer=new byte[65536];int count;while((count=input.Read(buffer))>0){if(encoded.Length+count>8*1024*1024)throw new InvalidDataException("Кадр слишком большой.");encoded.Write(buffer,0,count);}}
        encoded.Position = 0;
        var image = BitmapDecoder.Create(encoded, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if(image.PixelWidth!=Width||image.PixelHeight!=Height)throw new InvalidDataException("Размер кадра не совпадает с описанием.");
        var converted = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        converted.CopyPixels(pixels, image.PixelWidth * 4, 0);
        // Generated cutouts sometimes include a broad, low-opacity studio haze.
        // An alpha coverage threshold removes that haze while retaining a soft silhouette edge.
        for (int p=0;!alphaPrepared&&p<pixels.Length;p+=4)
        {
            int oldAlpha=pixels[p+3];
            double coverage=Math.Clamp((oldAlpha-150)/70d,0,1);
            int alpha=(int)Math.Round(coverage*coverage*(3-2*coverage)*255);
            if(oldAlpha>0)for(int c=0;c<3;c++)pixels[p+c]=(byte)Math.Clamp((int)Math.Round(pixels[p+c]*alpha/(double)oldAlpha),0,alpha);
            pixels[p+3]=(byte)alpha;
        }
        var detached = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, image.PixelWidth * 4);
        detached.Freeze();
        if (cache.Count >= Math.Clamp(64*1024*1024/(Width*Height*4),2,64)) { var oldest=cache.MinBy(x => x.Value.used).Key;cache.Remove(oldest); }
        cache[id] = (detached, pixels, ++access);
        return detached;
    }
    internal byte[] Pixels(string id) { Get(id); return cache[id].pixels; }
}

internal readonly partial record struct SpriteBounds
{
    public static implicit operator Rect(SpriteBounds value)=>new(value.X,value.Y,value.Width,value.Height);
}
