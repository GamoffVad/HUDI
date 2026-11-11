using System.Windows;
using System.Windows.Interop;

namespace HudiPet;

internal sealed partial class PetWindow
{
    private DesktopActivityMonitor? desktopMonitor;
    private DesktopReactionQueue desktopReactions=new();
    internal bool DesktopReactionsAvailable=>PetCatalog.SupportsDesktopReactions(selectedPet);
    internal bool DesktopReactionsEnabled {get;private set;}
    internal bool UserActionsOnly {get;private set;}
    internal int PendingDesktopReactions=>desktopReactions.PendingCount;
    internal bool DesktopMonitorRunning=>desktopMonitor is not null;
    internal string DesktopReactionsStatus=>desktopMonitor is {KeyboardAttached:false}
        ?"Windows не подключила обработчик клавиатуры: реакция на набор пока недоступна. Остальные события работают."
        :desktopMonitor is {ClipboardAttached:false}
        ?"Windows не подключила обработчик буфера: реакция на копирование пока недоступна. Остальные события работают."
        :"Мышь, открытие программ и документов, набор текста, копирование и паузы. Содержимое текста не читается.";
    internal void SetDesktopReactions(bool enabled)
    {
        Pets.SetDesktopReactions(selectedPet,enabled);RefreshDesktopReactions();
    }
    internal void SetUserActionsOnly(bool enabled)
    {
        Pets.SetUserActionsOnly(selectedPet,enabled);
        UserActionsOnly=enabled;
        if(enabled)desktopReactions.DiscardIdle();
    }
    private void RefreshDesktopReactions()
    {
        DisposeDesktopReactions();desktopReactions=new();
        UserActionsOnly=Pets.UserActionsOnly(selectedPet);
        DesktopReactionsEnabled=Pets.DesktopReactionsEnabled(selectedPet);
        if(!DesktopReactionsEnabled||uiTest||smokeTest||!IsLoaded)return;
        if(PresentationSource.FromVisual(this) is HwndSource source)
            desktopMonitor=new(source,ObserveDesktopActivity);
    }
    internal void ObserveDesktopActivity(DesktopActivity activity)
    {
        if(!DesktopReactionsEnabled||shuttingDown||DesktopInteractionBlocked)return;
        if(UserActionsOnly&&activity==DesktopActivity.IdleWaiting)return;
        desktopReactions.Offer(activity);
    }
    private bool DesktopInteractionBlocked=>paused||dragging||dragOrigin is not null||pendingClicks>0||chat.Busy||shuttingDown
        ||favoriteSitesOrbit is not null||OwnedWindows.Cast<Window>().Any(w=>w.IsVisible);
    internal void UpdateDesktopReactions()
    {
        if(!DesktopReactionsEnabled)return;
        if(DesktopInteractionBlocked){desktopReactions.Clear();return;}
        if(desktopReactions.Take(behavior.IsPlaying||behavior.ChatThinking,action=>behavior.Supports(action)
            && (!PetBehavior.IsEdgeAction(action)||CanPlaceDesktopEdge(action,out _))) is not {} reaction)return;
        if(PetBehavior.IsEdgeAction(reaction))PlayDesktopEdge(reaction);
        else behavior.Trigger(reaction);
    }
    private void DisposeDesktopReactions(){desktopMonitor?.Dispose();desktopMonitor=null;desktopReactions.Clear();}
}
