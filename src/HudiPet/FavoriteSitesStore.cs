using System.IO;
using System.Diagnostics;
using System.Text.Json;

namespace HudiPet;

internal sealed record FavoriteSite(string Id,string Name,string Url,string Icon,string? Text=null,string Kind="site",string? LibraryIcon=null);

internal sealed partial class FavoriteSitesStore
{
    private sealed record Document(int Version,FavoriteSite[] Sites,double? MaximumSphereSize=null);
    private readonly string path;
    internal const int MaximumSites=100;
    internal const int MaximumTextLength=32;
    internal const double MinimumSphereSize=24,MaximumAllowedSphereSize=128;
    internal double? MaximumSphereSize {get;private set;}
    internal static double NormalizeSphereSize(double value)=>double.IsFinite(value)?Math.Round(Math.Clamp(value,MinimumSphereSize,MaximumAllowedSphereSize)):54;
    internal bool RecoveredBackup {get;private set;}
    internal FavoriteSitesStore(string? directory=null)=>path=Path.Combine(directory??AppStorage.Directory,"favorite-sites.json");
    internal static string NormalizeUrl(string value)
    {
        value=value.Trim();
        if(!value.Contains("://",StringComparison.Ordinal))value="https://"+value;
        if(value.Length>4096||!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http")||string.IsNullOrWhiteSpace(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Укажите адрес сайта http:// или https://.");
        return uri.AbsoluteUri;
    }
    internal static bool IsProgram(FavoriteSite site)=>site.Kind=="program";
    internal static string NormalizeProgramPath(string value)
    {
        value=value.Trim().Trim('"');
        string extension=Path.GetExtension(Path.TrimEndingDirectorySeparator(value));
        bool supported=OperatingSystem.IsMacOS()?extension.Equals(".app",StringComparison.OrdinalIgnoreCase)
            :extension.Equals(".exe",StringComparison.OrdinalIgnoreCase)||extension.Equals(".lnk",StringComparison.OrdinalIgnoreCase);
        if(value.Length>4096||!Path.IsPathFullyQualified(value)||!supported)
            throw new InvalidDataException(OperatingSystem.IsMacOS()?"Выберите приложение .app.":"Выберите программу .exe или ярлык .lnk.");
        return Path.GetFullPath(value);
    }
    private static string NormalizeTarget(FavoriteSite site)=>site.Kind switch
    {
        null or "site"=>NormalizeUrl(site.Url),
        "program"=>NormalizeProgramPath(site.Url),
        _=>throw new InvalidDataException("Неизвестный тип избранного.")
    };
    internal static ProcessStartInfo CreateLaunchInfo(FavoriteSite site)
    {
        string target=NormalizeTarget(site);
        if(OperatingSystem.IsMacOS()&&IsProgram(site))
        {
            if(!Directory.Exists(target))throw new DirectoryNotFoundException("Приложение не найдено. Обновите путь в избранном.");
            return new ProcessStartInfo("/usr/bin/open"){ArgumentList={"--",target}};
        }
        if(IsProgram(site)&&!File.Exists(target))throw new FileNotFoundException("Программа не найдена. Обновите путь в избранном.",target);
        return new ProcessStartInfo(target){UseShellExecute=true,WorkingDirectory=IsProgram(site)?Path.GetDirectoryName(target)!:string.Empty};
    }
    private static void Validate(FavoriteSite[] sites)
    {
        if(sites.Length>MaximumSites||sites.Any(s=>s is null||!Guid.TryParse(s.Id,out _)||string.IsNullOrWhiteSpace(s.Name)||s.Name.Length>100||s.Icon is null||(string.IsNullOrEmpty(s.Icon)&&string.IsNullOrWhiteSpace(s.Text))||s.Text?.Length>MaximumTextLength||s.Url is null)||sites.Select(s=>s.Id).Distinct().Count()!=sites.Length)
            throw new InvalidDataException("Не удалось прочитать список любимых сайтов.");
        foreach(var site in sites){NormalizeTarget(site);if(site.Icon.Length>0)DecodeIcon(site.Icon);}
    }
    private static Document ReadFile(string file)
    {
        if(new FileInfo(file).Length>70_000_000)throw new InvalidDataException("Файл любимых сайтов слишком большой.");
        var document=JsonSerializer.Deserialize<Document>(File.ReadAllText(file))??throw new InvalidDataException("Список сайтов пуст.");
        if(document.Version!=1||document.Sites is null)throw new InvalidDataException("Неизвестный формат списка сайтов.");
        Validate(document.Sites);return document;
    }
    internal FavoriteSite[] Read()
    {
        RecoveredBackup=false;
        FavoriteSite[] Apply(Document document){MaximumSphereSize=document.MaximumSphereSize is { } size?NormalizeSphereSize(size):null;return document.Sites;}
        if(!File.Exists(path)&&!File.Exists(path+".bak")){MaximumSphereSize=null;return [];}
        try{return Apply(ReadFile(path));}
        catch(Exception error) when(IsDataError(error))
        {
            if(!File.Exists(path+".bak"))throw new InvalidDataException("Не удалось прочитать любимые сайты. Исходный файл сохранён.",error);
            var recovered=ReadFile(path+".bak");RecoveredBackup=true;return Apply(recovered);
        }
    }
    internal static bool IsDataError(Exception error)=>error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or FormatException or NotSupportedException or ArgumentException;
    internal void Put(FavoriteSite site)
    {
        site=site with {Name=site.Name.Trim(),Url=NormalizeTarget(site),Kind=site.Kind??"site",Text=string.IsNullOrWhiteSpace(site.Text)?null:site.Text.Trim()};
        var current=Read().ToList();int index=current.FindIndex(s=>s.Id==site.Id);
        if(index<0)current.Add(site);else current[index]=site;
        Write(current.ToArray());
    }
    internal void Delete(string id)=>Write(Read().Where(s=>s.Id!=id).ToArray());
    internal void SetMaximumSphereSize(double value)
    {
        var sites=Read();var previous=MaximumSphereSize;MaximumSphereSize=NormalizeSphereSize(value);
        try{Write(sites);}catch{MaximumSphereSize=previous;throw;}
    }
    private void Write(FavoriteSite[] sites)
    {
        Validate(sites);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temporary,JsonSerializer.Serialize(new Document(1,sites,MaximumSphereSize),new JsonSerializerOptions{WriteIndented=true}));
            if(File.Exists(path)&&!RecoveredBackup)File.Replace(temporary,path,path+".bak");else File.Move(temporary,path,true);
            RecoveredBackup=false;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
