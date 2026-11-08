using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace HudiPet;

internal sealed record LocalPet(string Id,string Name,string? Package,string Hash="")
{
    internal bool BuiltIn=>Package is null;
    public override string ToString()=>Name;
}

internal sealed class PetCatalog
{
    private readonly string root;
    internal static readonly LocalPet Hudi=new("hudi","HUDI",null);
    internal PetCatalog(string? directory=null)=>root=Path.GetFullPath(directory??Path.Combine(PetWindow.SettingsDirectory,"pets"));
    internal LocalPet[] List()
    {
        var pets=new List<LocalPet>{Hudi};
        if(!Directory.Exists(root))return pets.ToArray();
        foreach(string folder in Directory.EnumerateDirectories(root))
        {
            string id=Path.GetFileName(folder);if(!Guid.TryParseExact(id,"N",out _))continue;
            try
            {
                using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"pet.json")));
                string name=json.RootElement.GetProperty("name").GetString()??"";
                string package=Path.Combine(folder,"pet.zip");
                if(name.Length is >0 and <=80&&File.Exists(package))pets.Add(new(id,name,package,json.RootElement.GetProperty("sha256").GetString()??""));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException){ }
        }
        return pets.Take(1).Concat(pets.Skip(1).OrderBy(p=>p.Name)).ToArray();
    }
    internal LocalPet Selected()
    {
        try{string id=File.ReadAllText(Path.Combine(root,"selected.txt")).Trim();return List().FirstOrDefault(p=>p.Id==id)??Hudi;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return Hudi;}
    }
    internal void Select(LocalPet pet)
    {
        if(!List().Any(p=>p.Id==pet.Id))throw new InvalidDataException("Питомец больше не найден в коллекции.");
        Directory.CreateDirectory(root);string temporary=Path.Combine(root,"selected.pending");File.WriteAllText(temporary,pet.Id);File.Move(temporary,Path.Combine(root,"selected.txt"),true);
    }
    internal SpriteLibrary Open(LocalPet pet)=>pet.BuiltIn?SpriteLibrary.Shared.Value:SpriteLibrary.Open(pet.Package!);
    internal Task<LocalPet> ImportAsync(string file)=>Task.Run(()=>Import(file));
    internal LocalPet Import(string file)
    {
        if(new FileInfo(file).Length>512L*1024*1024)throw new InvalidDataException("Пакет должен быть не больше 512 МБ.");
        Directory.CreateDirectory(root);string id=Guid.NewGuid().ToString("N");string folder=Path.Combine(root,id);
        Directory.CreateDirectory(folder);string staged=Path.Combine(folder,"pet.pending");
        bool complete=false;
        try
        {
            File.Copy(file,staged,false);
            string hash;using(var input=File.OpenRead(staged))hash=Convert.ToHexString(SHA256.HashData(input));
            if(List().FirstOrDefault(p=>p.Hash==hash) is {} existing)return existing;
            using var library=SpriteLibrary.Open(staged);library.ValidateFrames();
            string name=string.IsNullOrWhiteSpace(library.DisplayName)?Path.GetFileNameWithoutExtension(file):library.DisplayName.Trim();
            if(name.Length is <1 or >80)throw new InvalidDataException("Название питомца должно содержать от 1 до 80 символов.");
            library.Dispose();
            string package=Path.Combine(folder,"pet.zip");File.Move(staged,package);
            File.WriteAllText(Path.Combine(folder,"pet.json"),JsonSerializer.Serialize(new{name,sha256=hash},new JsonSerializerOptions{WriteIndented=true}));
            complete=true;return new(id,name,package,hash);
        }
        finally
        {
            if(!complete)
            {
                // Only this import's known files in its fresh GUID directory.
                foreach(string name in new[]{"pet.pending","pet.zip","pet.json"}){string path=Path.Combine(folder,name);if(File.Exists(path))File.Delete(path);}
                if(!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);
            }
        }
    }
}
