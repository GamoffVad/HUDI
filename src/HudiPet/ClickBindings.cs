using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HudiPet;

internal enum PetClickAction { None, FavoriteSites, Notes, Chat }

internal sealed record ClickBindings(
    PetClickAction Single=PetClickAction.None,
    PetClickAction Double=PetClickAction.FavoriteSites,
    PetClickAction Triple=PetClickAction.Chat,
    PetClickAction ControlSingle=PetClickAction.None,
    PetClickAction ControlDouble=PetClickAction.Notes,
    PetClickAction ControlTriple=PetClickAction.None)
{
    internal PetClickAction Get(int count,bool control)=> (count,control) switch
    {
        (1,false)=>Single,(2,false)=>Double,(3,false)=>Triple,
        (1,true)=>ControlSingle,(2,true)=>ControlDouble,(3,true)=>ControlTriple,
        _=>PetClickAction.None
    };
    internal ClickBindings With(int count,bool control,PetClickAction action)=>(count,control) switch
    {
        (1,false)=>this with{Single=action},(2,false)=>this with{Double=action},(3,false)=>this with{Triple=action},
        (1,true)=>this with{ControlSingle=action},(2,true)=>this with{ControlDouble=action},(3,true)=>this with{ControlTriple=action},
        _=>throw new ArgumentOutOfRangeException(nameof(count))
    };
    internal bool HasLaterAction(int count,bool control)=>Enumerable.Range(count+1,3-count).Any(next=>Get(next,control)!=PetClickAction.None);
    internal string? HintFor(PetClickAction action)
    {
        foreach(bool control in new[]{false,true})foreach(int count in new[]{1,2,3})
            if(Get(count,control)==action)return (control?"Ctrl + ":"")+(count==1?"1 клик":count+" клика");
        return null;
    }
}

internal sealed class ClickBindingsStore
{
    private sealed record Document(int Version,ClickBindings Bindings);
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,Converters={new JsonStringEnumConverter<PetClickAction>(allowIntegerValues:false)}};
    private readonly string path;
    private bool recovered;
    internal ClickBindingsStore(string? directory=null)=>path=Path.Combine(directory??PetWindow.SettingsDirectory,"mouse-controls.json");
    internal static bool IsDataError(Exception error)=>error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException;
    private static void Validate(ClickBindings bindings)
    {
        foreach(bool control in new[]{false,true})foreach(int count in new[]{1,2,3})
            if(!Enum.IsDefined(bindings.Get(count,control)))throw new InvalidDataException("Неизвестное действие клика.");
    }
    private static ClickBindings ReadFile(string file)
    {
        if(new FileInfo(file).Length>16_384)throw new InvalidDataException("Файл управления кликами слишком большой.");
        var document=JsonSerializer.Deserialize<Document>(File.ReadAllText(file),Options);
        if(document is not {Version:1,Bindings:not null})throw new InvalidDataException("Неизвестный формат управления кликами.");
        Validate(document.Bindings);return document.Bindings;
    }
    internal ClickBindings Read()
    {
        recovered=false;if(!File.Exists(path)&&!File.Exists(path+".bak"))return new();
        try{return ReadFile(path);}
        catch(Exception error) when(IsDataError(error))
        {
            if(!File.Exists(path+".bak"))throw new InvalidDataException("Не удалось прочитать управление кликами. Исходный файл сохранён.",error);
            var bindings=ReadFile(path+".bak");recovered=true;return bindings;
        }
    }
    internal void Save(ClickBindings bindings)
    {
        Validate(bindings);Read();Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temporary,JsonSerializer.Serialize(new Document(1,bindings),Options));
            if(File.Exists(path)&&!recovered)File.Replace(temporary,path,path+".bak");else File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
