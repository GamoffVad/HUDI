using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
        using var input = (archive.GetEntry(id + ".png") ?? throw new InvalidDataException(id)).Open();
        using var codec = SKCodec.Create(input) ?? throw new InvalidDataException("Не удалось прочитать кадр " + id);
        if (codec.Info.Width != Width || codec.Info.Height != Height)
            throw new InvalidDataException("Размер кадра не совпадает с описанием.");
        var image = new WriteableBitmap(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        try
        {
            using var pixels = image.Lock();
            var format = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            if (alphaPrepared)
            {
                if (codec.GetPixels(format, pixels.Address, pixels.RowBytes, SKCodecOptions.Default) != SKCodecResult.Success)
                    throw new InvalidDataException("Не удалось декодировать кадр " + id);
            }
            else
            {
                using var decoded = SKBitmap.Decode(codec, format.WithAlphaType(SKAlphaType.Unpremul))
                    ?? throw new InvalidDataException("Не удалось декодировать кадр " + id);
                var colors = decoded.Pixels;
                for (int i = 0; i < colors.Length; i++)
                {
                    double coverage = Math.Clamp((colors[i].Alpha - 150) / 70d, 0, 1);
                    colors[i] = colors[i].WithAlpha((byte)Math.Round(coverage * coverage * (3 - 2 * coverage) * 255));
                }
                decoded.Pixels = colors;
                using var source = decoded.PeekPixels();
                if (!source.ReadPixels(format, pixels.Address, pixels.RowBytes, 0, 0))
                    throw new InvalidDataException("Не удалось скопировать кадр " + id);
            }
        }
        catch
        {
            image.Dispose();
            throw;
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
