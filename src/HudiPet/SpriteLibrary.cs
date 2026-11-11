using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace HudiPet;

internal sealed partial class SpriteLibrary : IDisposable
{
    internal static readonly Lazy<SpriteLibrary> Shared = new(() => new SpriteLibrary());
    private readonly Stream resource;
    private readonly ZipArchive archive;
    private readonly bool alphaPrepared;
    internal int Width { get; }
    internal int Height { get; }
    internal SpriteBounds DisplayBounds { get; }
    internal bool HorizontalMirrored { get; }
    internal string[] Left { get; }
    internal string[] Right { get; }
    internal string[] Up { get; }
    internal string[] Down { get; }
    internal Dictionary<string, SpriteStep[]> Clips { get; }
    internal string[] FrameIds { get; }
    internal double OrientationFrameSeconds { get; }
    internal string NeutralFrame { get; }
    internal Dictionary<string,ClipHold> Holds { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string,DesktopEdgeCue> DesktopEdges { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal string DisplayName { get; }
    internal string? PetVersion { get; }
    internal bool IsBuiltIn { get; }
    internal double BreathAmplitude { get; }

    internal static SpriteLibrary Open(string path)=>new(File.OpenRead(path),false);
    private SpriteLibrary(Stream inputResource,bool builtIn)
    {
        resource=inputResource;IsBuiltIn=builtIn;
        try{archive = new ZipArchive(resource, ZipArchiveMode.Read);}catch{resource.Dispose();throw;}
        try
        {
        if(archive.Entries.Count>11000||archive.Entries.Sum(x=>x.Length)>1024L*1024*1024)throw new InvalidDataException("Пакет питомца слишком большой.");
        using var input = (archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing manifest")).Open();
        if(archive.GetEntry("manifest.json")!.Length>8*1024*1024)throw new InvalidDataException("Описание питомца слишком большое.");
        using var json = JsonDocument.Parse(input);
        var root = json.RootElement;
        Width = root.GetProperty("frameSize")[0].GetInt32();
        Height = root.GetProperty("frameSize")[1].GetInt32();
        if(Width<1||Height<1||Width>2048||Height>2048||(long)Width*Height>1048576)throw new InvalidDataException("Размер кадров должен быть не больше 2048 по стороне и 1 мегапикселя.");
        DisplayBounds=new SpriteBounds(0,0,Width,Height);
        if(root.TryGetProperty("displayBounds",out var display))
        {
            var values=display.EnumerateArray().Select(x=>x.GetDouble()).ToArray();
            if(values.Length!=4||values.Any(x=>!double.IsFinite(x))||values[0]<0||values[1]<0||values[2]<=0||values[3]<=0||values[0]+values[2]>Width||values[1]+values[3]>Height)throw new InvalidDataException("Некорректная область отображения питомца.");
            DisplayBounds=new SpriteBounds(values[0],values[1],values[2],values[3]);
        }
        DisplayName=root.TryGetProperty("name",out var name)?name.GetString()??"":"";
        if(root.TryGetProperty("petVersion",out var revision))
        {
            if(revision.ValueKind!=JsonValueKind.String||revision.GetString() is not {Length:>0 and <=32} version||
                !Version.TryParse(version,out var parsed)||parsed.Build<0||parsed.Revision!=-1||parsed.ToString(3)!=version)
                throw new InvalidDataException("Версия питомца должна быть записана в формате 1.2.3.");
            PetVersion=version;
        }
        BreathAmplitude=root.TryGetProperty("breathAmplitude",out var breathing)?breathing.GetDouble():builtIn?.002:0;
        if(!double.IsFinite(BreathAmplitude)||BreathAmplitude<0||BreathAmplitude>.01)throw new InvalidDataException("Амплитуда дыхания должна быть от 0 до 0,01.");
        HorizontalMirrored = root.TryGetProperty("horizontalMirrored", out var mirrored) && mirrored.GetBoolean();
        alphaPrepared = root.TryGetProperty("alphaPrepared", out var prepared) ? prepared.GetBoolean() : !builtIn;
        OrientationFrameSeconds = root.TryGetProperty("orientationFrameSeconds", out var interval) ? interval.GetDouble() : .11;
        NeutralFrame=root.TryGetProperty("neutral",out var neutral)?neutral.GetString()??"neutral":"neutral";
        string[] Array(string name) => root.TryGetProperty(name,out var items)?items.EnumerateArray().Select(x => x.GetString()!).ToArray():[NeutralFrame];
        Left = Array("left"); Right = Array("right"); Up = Array("up"); Down = Array("down");
        FrameIds = Array("frames");
        if(FrameIds.Length is <1 or >10000||FrameIds.Distinct(StringComparer.Ordinal).Count()!=FrameIds.Length||!FrameIds.Contains(NeutralFrame))throw new InvalidDataException("Пакету нужны уникальные кадры и нейтральная поза.");
        Clips = new Dictionary<string, SpriteStep[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in root.GetProperty("clips").EnumerateObject())
            Clips[clip.Name] = clip.Value.EnumerateArray().Select(s => new SpriteStep(s.GetProperty("frame").GetString()!, s.GetProperty("seconds").GetDouble())).ToArray();
        if(root.TryGetProperty("roles",out var roles))foreach(var role in roles.EnumerateObject())
        {if(!Clips.TryGetValue(role.Value.GetString()??"",out var steps))throw new InvalidDataException("Не найдена анимация для роли "+role.Name);Clips[role.Name]=steps;}
        if(!Clips.ContainsKey("lookaround")&&Clips.TryGetValue("idle",out var idle))Clips["lookaround"]=idle;
        if(root.TryGetProperty("desktopEdge",out var desktopEdge)&&desktopEdge.ValueKind==JsonValueKind.Object)
            foreach(var entry in desktopEdge.EnumerateObject())
                if(Clips.TryGetValue(entry.Name,out var motion)&&DesktopEdgeCue.TryRead(entry.Value,Width,Height,motion.Length,out var cue))
                    DesktopEdges[entry.Name]=cue;
        var known=FrameIds.ToHashSet(StringComparer.Ordinal);
        if(Clips.Values.Any(steps=>steps.Length is <1 or >20000||steps.Any(s=>!known.Contains(s.Frame)||!double.IsFinite(s.Seconds)||s.Seconds<.01||s.Seconds>60)))throw new InvalidDataException("Некорректные кадры или длительности анимации.");
        if(builtIn){Holds["think"]=new(38,48,3.2);Holds["pockets"]=new(72,86,3.2);}
        if(root.TryGetProperty("holds",out var holds))foreach(var hold in holds.EnumerateObject())
            Holds[hold.Name]=new(hold.Value.GetProperty("start").GetInt32(),hold.Value.GetProperty("end").GetInt32(),hold.Value.GetProperty("seconds").GetDouble());
        foreach(var hold in Holds)
            if(!Clips.TryGetValue(hold.Key,out var steps)||hold.Value.Start<0||hold.Value.End<hold.Value.Start||hold.Value.End>=steps.Length||!double.IsFinite(hold.Value.Seconds)||hold.Value.Seconds<.2||hold.Value.Seconds>60)throw new InvalidDataException("Неверный диапазон удержания позы.");
        foreach (var frame in FrameIds)
            if(string.IsNullOrWhiteSpace(frame)||frame.Contains('\\')||frame.Contains(':')||frame.StartsWith('/')||frame.Split('/').Any(part=>part is ".." or "." or "")||archive.GetEntry(frame + ".png") is not {} entry||entry.Length>8*1024*1024)throw new InvalidDataException("Недопустимый или отсутствующий кадр: "+frame);
        }
        catch{archive.Dispose();resource.Dispose();throw;}
    }
    internal void ValidateFrames()
    {
        byte[] header=new byte[24];
        foreach(string frame in FrameIds)
        {
            using var stream=archive.GetEntry(frame+".png")!.Open();stream.ReadExactly(header);
            if(!header.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16,4))!=Width||System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20,4))!=Height)throw new InvalidDataException("Размер или формат кадра не совпадает с описанием: "+frame);
            Get(frame);
        }
        Get(NeutralFrame);
    }
    public void Dispose(){DisposeFrames();archive.Dispose();resource.Dispose();}
}

internal readonly record struct SpriteStep(string Frame, double Seconds);
internal readonly record struct ClipHold(int Start,int End,double Seconds);

internal readonly partial record struct SpriteBounds(double X,double Y,double Width,double Height);
