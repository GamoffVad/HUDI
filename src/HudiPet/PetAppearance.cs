using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HudiPet;

internal sealed record PetIcon(string Id,string Name,string Image);
internal sealed partial record PetAppearance(int Version,string Name,string Accent,string Surface,string Text,string Finish,PetIcon[] Icons)
{
    internal static PetAppearance Preset(string name,bool icons=true)
    {
        var (accent,surface,text,finish)=name switch {
            "Девочка в розовом"=>("#D98499","#F6E5D3","#512F2A","soft"),
            "Девушка в очках"=>("#B6A0DE","#292534","#F3EBFF","satin"),
            "Инк"=>("#DB777E","#26262B","#F3ECEB","ink"),
            "Кудряшка"=>("#CBAE8F","#302C2A","#F6EEE5","soft"),
            _=>("#8ACCEB","#263746","#EAF5FD","satin") };
        var theme=new PetAppearance(1,name,accent,surface,text,finish,[]);
        return icons?theme with {Icons=PetIconLibrary.Create(theme)}:theme;
    }
    internal static PetAppearance Parse(Stream input)
    {
        using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=input.Read(buffer))>0){bytes.Write(buffer,0,read);if(bytes.Length>4_000_000)throw new InvalidDataException("Тема питомца слишком большая.");}
        var result=JsonSerializer.Deserialize<PetAppearance>(bytes.ToArray())??throw new InvalidDataException("Тема не найдена.");
        if(result.Version!=1||string.IsNullOrWhiteSpace(result.Name)||result.Name.Length>80||result.Finish is not ("soft" or "satin" or "ink")
            ||new[]{result.Accent,result.Surface,result.Text}.Any(c=>c is null||!Regex.IsMatch(c,"^#[0-9a-fA-F]{6}$"))
            ||result.Icons is null||result.Icons.Length!=10||result.Icons.Any(i=>i is null||string.IsNullOrEmpty(i.Id)||i.Id.Length>40||string.IsNullOrEmpty(i.Name)||i.Name.Length>60||string.IsNullOrEmpty(i.Image))
            ||result.Icons.Select(i=>i.Id).Distinct().Count()!=10)throw new InvalidDataException("Неверный формат темы питомца.");
        foreach(var icon in result.Icons)
        {
            var image=FavoriteSitesStore.DecodeIcon(icon.Image);
            if(!FavoriteSitesStore.IsThemeIconSize(image))throw new InvalidDataException("Значки темы должны быть 128 × 128.");
        }
        return result;
    }
    internal string Serialize()=>JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true});
    internal static PetAppearance? FromPackage(string package)
    {
        using var zip=ZipFile.OpenRead(package);
        var entry=zip.GetEntry("appearance.json");if(entry is null)return null;
        if(entry.Length>4_000_000)throw new InvalidDataException("Тема питомца слишком большая.");
        using var input=entry.Open();return Parse(input);
    }
}

internal sealed partial class PetCatalog
{
    internal PetAppearance Appearance(LocalPet pet)=>Locked(()=>
    {
        if(!List().Any(p=>p.Id==pet.Id))throw new InvalidDataException("Питомец не найден.");
        string folder=Path.Combine(root,pet.Id),file=Path.Combine(folder,"appearance.json");
        if(File.Exists(file)){using var input=File.OpenRead(file);return PetAppearance.Parse(input);}
        var theme=pet.BuiltIn?PetAppearance.Preset(pet.Name):PetAppearance.FromPackage(pet.Package!)??PetAppearance.Preset(pet.Name);
        Directory.CreateDirectory(folder);string pending=file+".pending";File.WriteAllText(pending,theme.Serialize());File.Move(pending,file,true);return theme;
    });
    internal void ExportWithAppearance(LocalPet pet,string destination)
    {
        var theme=Appearance(pet);
        string final=Path.GetFullPath(destination),temporary=final+"."+Guid.NewGuid().ToString("N")+".pending";
        if(pet.Package is not null&&string.Equals(final,Path.GetFullPath(pet.Package),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Выберите отдельный файл для экспорта.");
        try
        {
            using(Stream input=pet.BuiltIn?SpriteLibrary.OpenBuiltInArchive():File.OpenRead(pet.Package!))
            using(var source=new ZipArchive(input,ZipArchiveMode.Read))
            using(var output=ZipFile.Open(temporary,ZipArchiveMode.Create))
            {
                foreach(var entry in source.Entries.Where(e=>e.FullName!="appearance.json"))
                {using var from=entry.Open();using var to=output.CreateEntry(entry.FullName,CompressionLevel.NoCompression).Open();from.CopyTo(to);}
                using var writer=new StreamWriter(output.CreateEntry("appearance.json",CompressionLevel.Optimal).Open());writer.Write(theme.Serialize());
            }
            File.Move(temporary,final,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
