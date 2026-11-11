using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace HudiPet;

internal sealed record PetNote(string Id,string Text,DateTimeOffset Created,DateTimeOffset Updated)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? RichText { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public NoteSize? WindowSize { get; init; }
    [JsonIgnore] public string Title => Text.Split('\n').Select(s=>s.Trim()).FirstOrDefault(s=>s.Length>0) is { } first ? (first.Length>72?first[..72]+"…":first) : "Без текста";
}

internal sealed record NoteSize(double Width, double Height)
{
    internal const double MinWidth = 216, MinHeight = 105, MaxWidth = 1000, MaxHeight = 900;
    internal bool IsValid => double.IsFinite(Width) && double.IsFinite(Height)
        && Width is >= MinWidth and <= MaxWidth && Height is >= MinHeight and <= MaxHeight;
}

internal sealed class NotesStore
{
    internal const int MaxTextLength=20_000;
    internal const int MaxRichLength=4_000_000;
    private const long MaxFileLength=50_000_000;
    private sealed record Document(int Version,PetNote[] Notes);
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Encoder=JavaScriptEncoder.Create(UnicodeRanges.All)};
    private readonly string path;
    internal bool RecoveredBackup {get;private set;}
    internal NotesStore(string? directory=null)=>path=Path.Combine(directory??AppStorage.Directory,"notes.json");
    private static PetNote[] ReadFile(string file)
    {
        if(new FileInfo(file).Length>MaxFileLength)throw new InvalidDataException("Файл заметок слишком большой.");
        var document=JsonSerializer.Deserialize<Document>(File.ReadAllText(file))??throw new InvalidDataException("Файл заметок пуст.");
        if(document.Version!=1||document.Notes is null||document.Notes.Length>1000||document.Notes.Any(n=>n is null||!Guid.TryParse(n.Id,out _)||n.Text is null||n.Text.Length>MaxTextLength||n.RichText?.Length>MaxRichLength)||document.Notes.Select(n=>n.Id).Distinct().Count()!=document.Notes.Length)
            throw new InvalidDataException("Не удалось прочитать формат заметок.");
        return document.Notes;
    }
    internal PetNote[] Read()
    {
        RecoveredBackup=false;
        if(!File.Exists(path)&&!File.Exists(path+".bak"))return [];
        try{return ReadFile(path).OrderByDescending(n=>n.Updated).ToArray();}
        catch(Exception error) when(error is IOException or JsonException)
        {
            if(!File.Exists(path+".bak"))throw new InvalidDataException("Не удалось прочитать заметки. Исходный файл сохранён.",error);
            var recovered=ReadFile(path+".bak");RecoveredBackup=true;return recovered.OrderByDescending(n=>n.Updated).ToArray();
        }
    }
    internal void Put(PetNote note)
    {
        if(!Guid.TryParse(note.Id,out _)||note.Text.Length>MaxTextLength||note.RichText?.Length>MaxRichLength)throw new InvalidDataException("Заметка слишком большая.");
        var notes=Read().Where(n=>n.Id!=note.Id).Prepend(note).ToArray();
        if(notes.Length>1000)throw new InvalidDataException("Сохранено 1000 заметок. Удалите ненужные, чтобы добавить новую.");
        Write(notes);
    }
    internal void Delete(string id)=>Write(Read().Where(n=>n.Id!=id).ToArray());
    private void Write(PetNote[] notes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var file=File.Create(temporary))JsonSerializer.Serialize(file,new Document(1,notes),Json);
            if(new FileInfo(temporary).Length>MaxFileLength)throw new InvalidDataException("Файл заметок слишком большой.");
            if(File.Exists(path)&&!RecoveredBackup)File.Replace(temporary,path,path+".bak");
            else File.Move(temporary,path,true);
            RecoveredBackup=false;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
