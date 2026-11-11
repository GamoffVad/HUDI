using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HudiPet;

internal sealed record LocalPet(string Id,string Name,string? Package,string Hash="")
{
    internal bool BuiltIn=>Package is null;
    public override string ToString()=>Name;
}

internal sealed partial class PetCatalog
{
    private readonly string root;
    internal static readonly LocalPet Hudi=new("hudi","HUDI",null);
    internal PetCatalog(string? directory=null)
    {
        root=Path.GetFullPath(directory??Path.Combine(AppStorage.Directory,"pets"));
        Locked(()=>{Reconcile();return true;});
    }
    private T Locked<T>(Func<T> action)
    {
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
        using var mutex=new Mutex(false,"Local\\HUDI.PetCatalog."+key);
        bool acquired=false;
        try
        {
            try{acquired=mutex.WaitOne(TimeSpan.FromMinutes(3));}catch(AbandonedMutexException){acquired=true;}
            if(!acquired)throw new IOException("Коллекция питомцев занята другим импортом.");
            return action();
        }
        finally{if(acquired)mutex.ReleaseMutex();}
    }
    private static string NameKey(string name)=>string.Join(" ",name.Normalize(NormalizationForm.FormKC).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static bool SamePet(LocalPet a,LocalPet b)=>NameKey(a.Name)==NameKey(b.Name)||(!string.IsNullOrEmpty(a.Hash)&&string.Equals(a.Hash,b.Hash,StringComparison.OrdinalIgnoreCase));
    private string SelectedId()
    {
        try{return File.ReadAllText(Path.Combine(root,"selected.txt")).Trim();}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){return Hudi.Id;}
    }
    private List<LocalPet> ReadEntries()
    {
        var pets=new List<LocalPet>();
        if(!Directory.Exists(root))return pets;
        foreach(string folder in Directory.EnumerateDirectories(root))
        {
            string id=Path.GetFileName(folder);if(!Guid.TryParseExact(id,"N",out _))continue;
            try
            {
                if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;
                using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"pet.json")));
                string name=json.RootElement.GetProperty("name").GetString()??"";
                string filename=json.RootElement.TryGetProperty("package",out var value)?value.GetString()??"":"pet.zip";
                if(filename!=Path.GetFileName(filename)||!filename.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))continue;
                string package=Path.Combine(folder,filename);
                if(name.Length is >0 and <=80&&File.Exists(package))pets.Add(new(id,name,package,json.RootElement.GetProperty("sha256").GetString()??""));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException){ }
        }
        return pets;
    }
    private List<LocalPet[]> Groups()
    {
        string selected=SelectedId();var groups=new List<List<LocalPet>>();
        foreach(var pet in ReadEntries().OrderByDescending(p=>p.Id==selected).ThenByDescending(p=>File.GetLastWriteTimeUtc(p.Package!)).ThenBy(p=>p.Id,StringComparer.Ordinal))
        {
            var matches=groups.Where(g=>g.Any(p=>SamePet(p,pet))).ToArray();
            if(matches.Length==0)groups.Add(new(){pet});
            else{matches[0].Add(pet);foreach(var other in matches.Skip(1)){matches[0].AddRange(other);groups.Remove(other);}}
        }
        return groups.Select(g=>g.OrderByDescending(p=>p.Id==selected).ThenByDescending(p=>File.GetLastWriteTimeUtc(p.Package!)).ThenBy(p=>p.Id,StringComparer.Ordinal).ToArray()).ToList();
    }
    private void Archive(LocalPet pet,string category)
    {
        string folder=Path.GetFullPath(Path.Combine(root,pet.Id));
        if(!Guid.TryParseExact(pet.Id,"N",out _)||!string.Equals(Path.GetDirectoryName(folder),root,StringComparison.OrdinalIgnoreCase)
            ||(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Недопустимый путь к пакету питомца.");
        string archive=Path.Combine(root,category);Directory.CreateDirectory(archive);
        if((File.GetAttributes(archive)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Недопустимая папка архива питомцев.");
        Directory.Move(folder,Path.Combine(archive,pet.Id+"-"+Guid.NewGuid().ToString("N")));
    }
    private void Reconcile()
    {
        foreach(var duplicate in Groups().SelectMany(g=>g.Skip(1)))
        {
            // A running older instance can still hold a ZIP open. Hide the duplicate
            // now; the next startup can archive it after that handle is released.
            try{Archive(duplicate,"duplicates");}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){ }
        }
    }
    internal LocalPet[] List()
    {
        return new[]{Hudi}.Concat(Groups().Select(g=>g[0]).OrderBy(p=>p.Name)).ToArray();
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
    internal void Remove(LocalPet pet)
    {
        Locked(()=>{RemoveCore(pet);return true;});
    }
    private void RemoveCore(LocalPet pet)
    {
        if(pet.BuiltIn||!Guid.TryParseExact(pet.Id,"N",out _))throw new InvalidDataException("Встроенного HUDI удалить нельзя.");
        var existing=List().SingleOrDefault(p=>p.Id==pet.Id)??throw new InvalidDataException("Питомец больше не найден в коллекции.");
        string folder=Path.GetFullPath(Path.Combine(root,existing.Id));
        if(!string.Equals(Path.GetDirectoryName(folder),root,StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFullPath(pet.Package!),Path.GetFullPath(existing.Package!),StringComparison.OrdinalIgnoreCase)
            || (File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidDataException("Недопустимый путь к пакету питомца.");
        // Remove hidden legacy duplicates too, so deleting a pet cannot resurrect one.
        foreach(var member in Groups().Single(g=>g.Any(p=>p.Id==existing.Id)))Archive(member,"removed");
    }
    internal Task<LocalPet> ImportAsync(string file)=>Task.Run(()=>Import(file));
    internal LocalPet Import(string file)=>Locked(()=>ImportCore(file));
    private LocalPet ImportCore(string file)
    {
        Reconcile();
        if(new FileInfo(file).Length>1024L*1024*1024)throw new InvalidDataException("Пакет должен быть не больше 1 ГБ.");
        Directory.CreateDirectory(root);string id=Guid.NewGuid().ToString("N");string folder=Path.Combine(root,id);
        Directory.CreateDirectory(folder);string staged=Path.Combine(folder,"pet.pending");
        bool complete=false;
        try
        {
            File.Copy(file,staged,false);
            string hash;using(var input=File.OpenRead(staged))hash=Convert.ToHexString(SHA256.HashData(input));
            if(List().FirstOrDefault(p=>string.Equals(p.Hash,hash,StringComparison.OrdinalIgnoreCase)) is {} existing)return existing;
            using var library=SpriteLibrary.Open(staged);library.ValidateFrames();
            _=PetAppearance.FromPackage(staged); // Validate optional portable theme before publishing any files.
            string name=string.IsNullOrWhiteSpace(library.DisplayName)?Path.GetFileNameWithoutExtension(file):library.DisplayName.Trim();
            if(name.Length is <1 or >80)throw new InvalidDataException("Название питомца должно содержать от 1 до 80 символов.");
            if(NameKey(name)==NameKey(Hudi.Name))throw new InvalidDataException("Название HUDI зарезервировано для встроенного питомца.");
            library.Dispose();
            if(List().FirstOrDefault(p=>!p.BuiltIn&&NameKey(p.Name)==NameKey(name)) is {} prior)
            {
                string target=Path.GetDirectoryName(prior.Package!)!;
                string filename="pet-"+hash.ToLowerInvariant()+".zip",version=Path.Combine(target,filename);
                // Publish a new immutable ZIP so the active renderer can keep its
                // previous version open until the user applies this update.
                if(!File.Exists(version))File.Move(staged,version);
                string metadata=Path.Combine(target,"pet.json");
                File.Copy(metadata,Path.Combine(target,"revision-"+Guid.NewGuid().ToString("N")+".json"));
                string pending=Path.Combine(target,"pet.json.pending");
                File.WriteAllText(pending,JsonSerializer.Serialize(new{name,sha256=hash,package=filename},new JsonSerializerOptions{WriteIndented=true}));
                File.Move(pending,metadata,true);
                return new(prior.Id,name,version,hash);
            }
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
