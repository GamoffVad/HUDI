using System.IO;
using System.Text.Json;

namespace HudiPet;

internal enum DesktopActivity { DocumentActivated, PointerMoved, TypingStarted, FilesCopied, TextCopied, AppSwitched, IdleWaiting, ReturnedToWork }

internal sealed class DesktopReactionQueue
{
    private static readonly PetAction[] AllActions = Enum.GetValues<PetAction>();
    private readonly List<DesktopActivity> pending = [];
    private readonly HashSet<PetAction> played = [];
    private PetAction? last;

    internal int PendingCount => pending.Count;
    internal void Offer(DesktopActivity activity)
    {
        if (!pending.Contains(activity)) pending.Add(activity);
    }

    internal PetAction? Take(bool blocked, Func<PetAction, bool> eligible)
    {
        if (blocked || pending.Count == 0) return null;
        var activity = pending[0];
        pending.RemoveAt(0);
        var available = AllActions.Where(eligible).ToArray();
        if (available.Length == 0) return null;

        var choices = available.Where(action => !played.Contains(action)).ToArray();
        if (choices.Length == 0)
        {
            played.ExceptWith(available);
            choices = available;
        }
        if (choices.Length > 1) choices = choices.Where(action => action != last).ToArray();

        var action = Candidates(activity).Concat(choices).First(choices.Contains);
        played.Add(action);
        last = action;
        return action;
    }

    internal void Clear() => pending.Clear();
    internal void DiscardIdle() => pending.Remove(DesktopActivity.IdleWaiting);
    internal static PetAction[] Candidates(DesktopActivity activity)=>activity switch
    {
        DesktopActivity.DocumentActivated=>[PetAction.Surprise,PetAction.LookAround],
        DesktopActivity.PointerMoved=>[PetAction.LookAround,PetAction.PeekAround,PetAction.LookLeft,PetAction.LookRight,PetAction.WalkAround],
        DesktopActivity.TypingStarted=>[PetAction.Think,PetAction.LookAround,PetAction.PeekAround],
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
    internal static bool SupportsDesktopReactions(LocalPet pet)=>pet.BuiltIn ? pet.Id==Hudi.Id : Guid.TryParseExact(pet.Id,"N",out _);
    private string DesktopPreferencePath(LocalPet pet)
    {
        if(!SupportsDesktopReactions(pet))throw new InvalidDataException("Питомец не найден.");
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
            if(!List().Any(p=>p.Id==pet.Id))throw new InvalidDataException("Питомец не найден.");
            string path=DesktopPreferencePath(pet),temporary=path+".pending";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary,JsonSerializer.Serialize(new{version=1,enabled}));File.Move(temporary,path,true);return true;
        });
    }
}
