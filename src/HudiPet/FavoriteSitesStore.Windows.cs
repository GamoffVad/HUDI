using System.IO;
using System.Windows.Media.Imaging;

namespace HudiPet;

internal sealed partial class FavoriteSitesStore
{
    internal static bool IsThemeIconSize(BitmapSource image)=>image.PixelWidth==128&&image.PixelHeight==128;
    internal static string ImportIcon(string file)
    {
        if(new FileInfo(file).Length>10_000_000)throw new InvalidDataException("Выберите изображение меньше 10 МБ.");
        using var stream=File.OpenRead(file);
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=128;image.StreamSource=stream;image.EndInit();image.Freeze();
        if(image.PixelHeight>4096)throw new InvalidDataException("Выберите квадратное изображение для значка.");
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=new MemoryStream();encoder.Save(output);
        if(output.Length>500_000)throw new InvalidDataException("Изображение слишком большое для значка.");
        return Convert.ToBase64String(output.ToArray());
    }
    internal static BitmapSource DecodeIcon(string value)
    {
        if(value.Length>700_000)throw new InvalidDataException("Значок слишком большой.");
        using var stream=new MemoryStream(Convert.FromBase64String(value));
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=128;image.StreamSource=stream;image.EndInit();image.Freeze();
        if(image.PixelHeight>4096)throw new InvalidDataException("Неподдерживаемый размер значка.");
        return image;
    }
}
