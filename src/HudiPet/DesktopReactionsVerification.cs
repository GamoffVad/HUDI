using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace HudiPet;

internal static class DesktopReactionsVerification
{
    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Initialize();
        var checks=new Dictionary<string,bool>();string? error=null;
        try
        {
            var typing=new TypingBurstDetector();typing.KeyDown(100);typing.KeyDown(200);
            checks["single_keys_do_not_react"]=!typing.Poll(250);typing.KeyDown(350);
            checks["typing_burst_detected"]=typing.Poll(400);typing.KeyDown(600);
            checks["continuous_typing_only_once"]=!typing.Poll(700);typing.KeyDown(5000);typing.KeyDown(5100);typing.KeyDown(5200);
            checks["new_typing_after_pause_detected"]=typing.Poll(5250);typing.Reset();
            checks["foreground_change_discards_old_keys"]=!typing.Poll(5300);
            var idle=new IdleActivityDetector();checks["no_idle_before_90_seconds"]=idle.Observe(89) is null;
            checks["idle_once_per_pause"]=idle.Observe(90)==DesktopActivity.IdleWaiting&&idle.Observe(150) is null;
            checks["return_once_after_pause"]=idle.Observe(1)==DesktopActivity.ReturnedToWork&&idle.Observe(2) is null;
            var q=new DesktopReactionQueue();q.Offer(DesktopActivity.DocumentActivated,0);
            checks["active_animation_not_interrupted"]=q.Take(100,true,_=>true) is null&&q.PendingCount==1;
            checks["event_plays_after_animation"]=q.Take(200,false,_=>true)==PetAction.Surprise;
            q.Offer(DesktopActivity.TypingStarted,1000);checks["global_cooldown_defers_event"]=q.PendingCount==1&&q.Take(1000,false,_=>true) is null;
            checks["document_then_typing_reacts_once_after_cooldown"]=q.Take(12200,false,_=>true)==PetAction.LookAround&&q.Take(12201,false,_=>true) is null;
            q.Offer(DesktopActivity.DocumentActivated,26000);checks["same_event_cooldown"]=q.PendingCount==0;
            q.Offer(DesktopActivity.AppSwitched,26000);q.Offer(DesktopActivity.TextCopied,27000);
            checks["queue_bounded_and_latest_wins"]=q.PendingCount==1&&q.Take(27001,false,_=>true)==PetAction.Giggle;
            q.Offer(DesktopActivity.DocumentActivated,60000);checks["stale_events_expire"]=q.Take(79000,false,_=>true) is null;
            q.Offer(DesktopActivity.DocumentActivated,80000);checks["unsupported_action_uses_available_fallback"]=q.Take(80001,false,a=>a==PetAction.LookAround)==PetAction.LookAround;
            q.Offer(DesktopActivity.IdleWaiting,120000);checks["unsupported_actions_skipped"]=q.Take(120001,false,_=>false) is null;
            q.Offer(DesktopActivity.DocumentActivated,130000);q.Clear();checks["clear_removes_queued_event"]=q.PendingCount==0;

            string testRoot=Path.Combine(Path.GetFullPath(directory),"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(testRoot);
            string package=Path.Combine(testRoot,"pink.pet");PetCatalogVerification.Package(package,displayName:"Девочка в розовом");
            var catalog=new PetCatalog(Path.Combine(testRoot,"collection"));var pink=catalog.Import(package);catalog.Select(pink);
            checks["pink_enabled_by_default"]=catalog.DesktopReactionsEnabled(pink);
            checks["other_pets_disabled"]=!catalog.DesktopReactionsEnabled(PetCatalog.Hudi)&&!PetCatalog.SupportsDesktopReactions(pink with{Name="Инк"});
            catalog.SetDesktopReactions(pink,false);checks["preference_persists"]=!new PetCatalog(Path.Combine(testRoot,"collection")).DesktopReactionsEnabled(pink);
            catalog.SetDesktopReactions(pink,true);
            var window=new PetWindow(false,true,catalog,clickBindings:new ClickBindingsStore(testRoot));
            try{window.VerifyDesktopReactionIntegration(checks);}finally{window.Close();}
            using(var host=new HwndSource(new HwndSourceParameters("HUDI activity verification"){Width=1,Height=1,WindowStyle=0}))
            {
                var monitor=new DesktopActivityMonitor(host,_=>{});
                checks["native_keyboard_hook_attaches"]=monitor.KeyboardAttached;checks["native_clipboard_listener_attaches"]=monitor.ClipboardAttached;
                monitor.Dispose();checks["keyboard_hook_disposed"]=!monitor.KeyboardAttached;checks["clipboard_listener_disposed"]=!monitor.ClipboardAttached;
            }
            var actual=new PetCatalog().List().FirstOrDefault(PetCatalog.SupportsDesktopReactions);
            if(actual is not null)
            {
                using var sprites=SpriteLibrary.Open(actual.Package!);
                var behavior=new PetBehavior(1,sprites);
                checks["all_events_map_to_existing_pink_animations"]=Enum.GetValues<DesktopActivity>().All(e=>DesktopReactionQueue.Candidates(e).Any(behavior.Supports));
            }
        }
        catch(Exception ex){error=ex.ToString();}
        bool success=error is null&&checks.Values.All(v=>v);
        File.WriteAllText(Path.Combine(directory,"desktop-reactions-tests.json"),JsonSerializer.Serialize(new{success,checks,error},new JsonSerializerOptions{WriteIndented=true}));
        app.Shutdown();return success?0:1;
    }
}

internal sealed partial class PetWindow
{
    internal void VerifyDesktopReactionIntegration(Dictionary<string,bool> checks)
    {
        RefreshDesktopReactions();checks["test_runtime_never_installs_hooks"]=DesktopReactionsEnabled&&!DesktopMonitorRunning;
        SetDesktopReactions(false);ObserveDesktopActivity(DesktopActivity.DocumentActivated,0);
        checks["disabled_monitor_does_not_queue"]=PendingDesktopReactions==0&&!DesktopMonitorRunning;
        SetDesktopReactions(true);dragging=true;ObserveDesktopActivity(DesktopActivity.TypingStarted,0);
        checks["dragging_suppresses_reactions"]=PendingDesktopReactions==0;dragging=false;
        paused=true;ObserveDesktopActivity(DesktopActivity.IdleWaiting,0);checks["paused_suppresses_idle"]=PendingDesktopReactions==0;paused=false;
        ObserveDesktopActivity(DesktopActivity.DocumentActivated,0);paused=true;UpdateDesktopReactions(1);
        checks["pausing_discards_existing_queue"]=PendingDesktopReactions==0;paused=false;
        QueuePetClick(1);
        ObserveDesktopActivity(DesktopActivity.DocumentActivated,0);
        checks["unassigned_click_does_not_block_desktop_reactions"]=pendingClicks==0&&PendingDesktopReactions==1;
        desktopReactions.Clear();
        ObserveDesktopActivity(DesktopActivity.IdleWaiting,0);
        SetUserActionsOnly(true);
        checks["user_actions_only_discards_pending_idle"]=PendingDesktopReactions==0;
        ObserveDesktopActivity(DesktopActivity.IdleWaiting,1);
        checks["user_actions_only_ignores_idle"]=PendingDesktopReactions==0;
        ObserveDesktopActivity(DesktopActivity.TypingStarted,2);
        checks["user_actions_only_keeps_typing_reaction"]=PendingDesktopReactions==1;
        checks["user_actions_only_is_saved_per_pet"]=Pets.UserActionsOnly(selectedPet)&&!Pets.UserActionsOnly(PetCatalog.Hudi);
        var manualOnly=new PetBehavior(2,library);
        for(int i=0;i<400;i++)manualOnly.Update(.05,autonomous:false);
        checks["user_actions_only_has_no_autonomous_playback"]=manualOnly.ActionsStarted==0;
        manualOnly.Trigger(PetAction.Dance);manualOnly.Update(.05,autonomous:false);
        checks["user_actions_only_keeps_manual_playback"]=manualOnly.CurrentAction==PetAction.Dance;
        SetUserActionsOnly(false);
        desktopReactions.Clear();
        behavior.Trigger(PetAction.Dance);ObserveDesktopActivity(DesktopActivity.DocumentActivated,0);UpdateDesktopReactions(100);
        checks["manual_pending_animation_has_priority"]=PendingDesktopReactions==1;
        behavior.Update(.1,false);checks["manual_action_is_preserved"]=behavior.CurrentAction==PetAction.Dance;
        for(int i=0;i<100;i++)behavior.Update(.05,false);UpdateDesktopReactions(1000);behavior.Update(.1,false);
        checks["deferred_reaction_uses_own_animation"]=behavior.CurrentAction==PetAction.LookAround&&behavior.Interruptions==0;
        desktopReactions=new();ObserveDesktopActivity(DesktopActivity.DocumentActivated,0);BeginPointerGesture(new Point(1,1),2,true);
        checks["manual_pointer_clears_reaction_queue"]=PendingDesktopReactions==0;
        chat.Dispose();var client=new DesktopReactionTestClient();chat=new ChatSession(client);
        Task request=chat.SendAsync("fixture",new ModelSettings{Model="fixture",UseApiKey=false},"");
        ObserveDesktopActivity(DesktopActivity.IdleWaiting,1000);ObserveDesktopActivity(DesktopActivity.TypingStarted,1000);
        checks["chat_busy_suppresses_idle_and_typing"]=chat.Busy&&PendingDesktopReactions==0;
        client.Reply.SetResult(new ModelReply("fixture"));UiVerification.Advance(80);
        checks["fake_chat_completed_without_network"]=request.IsCompletedSuccessfully;
        SetDesktopReactions(true);var original=selectedPet;selectedPet=PetCatalog.Hudi;RefreshDesktopReactions();
        checks["switching_other_pet_stops_reactions"]=!DesktopReactionsEnabled&&!DesktopMonitorRunning&&PendingDesktopReactions==0;
        selectedPet=original;RefreshDesktopReactions();
    }
    private sealed class DesktopReactionTestClient:IModelClient
    {
        internal readonly TaskCompletionSource<ModelReply> Reply=new();
        public Task<ModelReply> CompleteAsync(ModelSettings settings,string key,IReadOnlyList<ChatMessage> messages,IProgress<string>? progress,CancellationToken cancellation)=>Reply.Task.WaitAsync(cancellation);
        public Task<string[]> ListModelsAsync(ModelSettings settings,string key,CancellationToken cancellation)=>Task.FromResult(new[]{"fixture"});
    }
}
