using System.IO;
using System.Text.Json;

namespace HudiPet;

internal enum DesktopActivity { DocumentActivated, TypingStarted, FilesCopied, TextCopied, AppSwitched, IdleWaiting, ReturnedToWork }

internal sealed class DesktopReactionQueue
{
    private (DesktopActivity activity,long expires)? pending;
    private readonly Dictionary<DesktopActivity,long> lastPlayed=[];
    private long nextAllowed;
    internal int PendingCount=>pending.HasValue?1:0;
    internal void Offer(DesktopActivity activity,long now)
    {
        if(lastPlayed.TryGetValue(activity,out long last)&&now-last<45000)return;
        pending=(activity,now+18000);
    }
    internal PetAction? Take(long now,bool blocked,Func<PetAction,bool> supported)
    {
        if(pending is not {} value)return null;
        if(now>value.expires){pending=null;return null;}
        if(blocked||now<nextAllowed)return null;
        pending=null;
        foreach(var action in Candidates(value.activity))
        {
            if(!supported(action))continue;
            nextAllowed=now+12000;lastPlayed[value.activity]=now;return action;
        }
        return null;
    }
    internal void Clear()=>pending=null;
    internal void DiscardIdle()
    {
        if(pending is { activity: DesktopActivity.IdleWaiting })pending=null;
    }
    internal static PetAction[] Candidates(DesktopActivity activity)=>activity switch
    {
        DesktopActivity.DocumentActivated=>[PetAction.Surprise,PetAction.LookAround],
        DesktopActivity.TypingStarted=>[PetAction.LookAround,PetAction.PeekAround],
        DesktopActivity.FilesCopied=>[PetAction.HeadTap,PetAction.Giggle],
        DesktopActivity.TextCopied=>[PetAction.Giggle,PetAction.LookAround],
        DesktopActivity.AppSwitched=>[PetAction.PeekAround,PetAction.LookAround],
        DesktopActivity.IdleWaiting=>[PetAction.Impatient],
        DesktopActivity.ReturnedToWork=>[PetAction.Giggle,PetAction.Dance],
        _=>[]
    };
}

internal sealed partial class PetCatalog
{
    internal static bool SupportsDesktopReactions(LocalPet pet)=>!pet.BuiltIn&&NameKey(pet.Name)==NameKey("Девочка в розовом");
    private string DesktopPreferencePath(LocalPet pet)
    {
        if(!Guid.TryParseExact(pet.Id,"N",out _)||!List().Any(p=>p.Id==pet.Id))throw new InvalidDataException("Питомец не найден.");
        return Path.Combine(root,pet.Id,"desktop-reactions.json");
    }
    internal bool DesktopReactionsEnabled(LocalPet pet)
    {
        if(!SupportsDesktopReactions(pet))return false;
        string path=DesktopPreferencePath(pet);if(!File.Exists(path))return true;
        try
        {
            using var json=JsonDocument.Parse(File.ReadAllText(path));return json.RootElement.GetProperty("enabled").GetBoolean();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException){return false;}
    }
    internal void SetDesktopReactions(LocalPet pet,bool enabled)
    {
        if(!SupportsDesktopReactions(pet))return;
        Locked(()=>
        {
            string path=DesktopPreferencePath(pet),temporary=path+".pending";
            File.WriteAllText(temporary,JsonSerializer.Serialize(new{version=1,enabled}));File.Move(temporary,path,true);return true;
        });
    }
}
