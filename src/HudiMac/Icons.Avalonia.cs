using SkiaSharp;

namespace HudiPet;

internal sealed partial class FavoriteSitesStore
{
    internal static bool IsThemeIconSize(SKImageInfo image) => image.Width == 128 && image.Height == 128;

    internal static string ImportIcon(string path)
    {
        if (new FileInfo(path).Length > 10_000_000)
            throw new InvalidDataException("Выберите изображение меньше 10 МБ.");
        using var file = File.OpenRead(path);
        using var codec = SKCodec.Create(file) ?? throw new InvalidDataException("Формат значка не поддерживается.");
        if (codec.Info.Width > 4096 || codec.Info.Height > 4096)
            throw new InvalidDataException("Размер значка не должен превышать 4096 пикселей.");
        using var image = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Не удалось открыть значок.");
        using var scaled = new SKBitmap(128, 128);
        using var canvas = new SKCanvas(scaled);
        int side = Math.Min(image.Width, image.Height);
        int x = (image.Width - side) / 2, y = (image.Height - side) / 2;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(image, new SKRect(x, y, x + side, y + side), new SKRect(0, 0, 128, 128));
        using var png = scaled.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(png.ToArray());
    }

    internal static SKImageInfo DecodeIcon(string value)
    {
        if (value.Length > 700_000) throw new InvalidDataException("Значок слишком большой.");
        using var stream = new MemoryStream(Convert.FromBase64String(value));
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Не удалось прочитать значок.");
        var info = codec.Info;
        if (info.Width is < 1 or > 4096 || info.Height is < 1 or > 4096)
            throw new InvalidDataException("Неподдерживаемый размер значка.");
        return info;
    }
}

internal static partial class PetIconLibrary
{
    internal static PetIcon[] Create(PetAppearance theme) => symbols.Select(symbol =>
    {
        using var bitmap = new SKBitmap(128, 128);
        using var canvas = new SKCanvas(bitmap);
        using var path = SKPath.ParseSvgPathData(symbol.Path);
        using var paint = new SKPaint
        {
            Color = SKColor.Parse(theme.Accent), IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 8,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
        };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawPath(path, paint);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new PetIcon(symbol.Id, symbol.Name, Convert.ToBase64String(png.ToArray()));
    }).ToArray();
}
