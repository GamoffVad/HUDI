using Avalonia.Media.Imaging;
using SkiaSharp;

namespace HudiPet;

internal sealed partial class SpriteLibrary
{
    private readonly Dictionary<string, (Bitmap Image, long Used)> cache = new();
    private long access;

    private SpriteLibrary() : this(OpenBuiltInArchive(), true) { }
    internal static Stream OpenBuiltInArchive() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "sprites.zip"));

    internal Bitmap Get(string id)
    {
        if (cache.TryGetValue(id, out var frame))
        {
            cache[id] = (frame.Image, ++access);
            return frame.Image;
        }
        using var data = new MemoryStream();
        using var entry = (archive.GetEntry(id + ".png") ?? throw new InvalidDataException(id)).Open();
        entry.CopyTo(data);
        data.Position = 0;
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Не удалось прочитать кадр " + id);
        if (codec.Info.Width != Width || codec.Info.Height != Height)
            throw new InvalidDataException("Размер кадра не совпадает с описанием.");
        Bitmap image;
        if (alphaPrepared)
        {
            using var input = new MemoryStream(data.ToArray(), writable: false);
            image = new Bitmap(input);
        }
        else
        {
            using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Не удалось прочитать кадр " + id);
            var pixels = decoded.Pixels;
            for (int i = 0; i < pixels.Length; i++)
            {
                double coverage = Math.Clamp((pixels[i].Alpha - 150) / 70d, 0, 1);
                pixels[i] = pixels[i].WithAlpha((byte)Math.Round(coverage * coverage * (3 - 2 * coverage) * 255));
            }
            decoded.Pixels = pixels;
            using var png = decoded.Encode(SKEncodedImageFormat.Png, 100);
            using var input = png.AsStream();
            image = new Bitmap(input);
        }
        if (cache.Count >= Math.Clamp(64 * 1024 * 1024 / (Width * Height * 4), 4, 64))
        {
            string oldest = cache.MinBy(item => item.Value.Used).Key;
            cache[oldest].Image.Dispose();
            cache.Remove(oldest);
        }
        cache[id] = (image, ++access);
        return image;
    }

    private void DisposeFrames()
    {
        foreach (var frame in cache.Values) frame.Image.Dispose();
        cache.Clear();
    }
}
