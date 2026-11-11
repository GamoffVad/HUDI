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
            var typing=new InputBurstDetector();var pointer=new InputBurstDetector();
            checks["no_input_no_reaction"]=!typing.Take()&&!pointer.Take();
            typing.Observe(100);checks["first_keystroke_reacts"]=typing.Take();
            pointer.Observe(110);checks["first_pointer_movement_reacts_independently"]=pointer.Take();
            typing.Observe(400);pointer.Observe(420);
            checks["continuous_input_coalesced"]=!typing.Take()&&!pointer.Take();
            typing.Observe(1100);pointer.Observe(1120);
            checks["new_input_after_pause_reacts"]=typing.Take()&&pointer.Take();
            typing.Observe(1900);typing.Reset();
            checks["reset_discards_pending_input"]=!typing.Take();
            var idle=new IdleActivityDetector();checks["no_idle_before_90_seconds"]=idle.Observe(89) is null;
            checks["idle_once_per_pause"]=idle.Observe(90)==DesktopActivity.IdleWaiting&&idle.Observe(150) is null;
            checks["return_once_after_pause"]=idle.Observe(1)==DesktopActivity.ReturnedToWork&&idle.Observe(2) is null;
            var q=new DesktopReactionQueue();
            q.Offer(DesktopActivity.AppSwitched);q.Offer(DesktopActivity.TypingStarted);
            q.Offer(DesktopActivity.PointerMoved);q.Offer(DesktopActivity.PointerMoved);
            checks["active_animation_keeps_all_distinct_events"]=q.Take(true,_=>true) is null&&q.PendingCount==3;
            checks["app_typing_and_pointer_each_react_in_order"]=q.Take(false,_=>true)==PetAction.PeekAround
                &&q.Take(false,_=>true)==PetAction.Think&&q.Take(false,_=>true)==PetAction.LookAround&&q.Take(false,_=>true) is null;
            q.Offer(DesktopActivity.DocumentActivated);checks["nonpreferred_action_is_available_as_fallback"]=
                q.Take(false,a=>a==PetAction.Gymnastics)==PetAction.Gymnastics;
            q.Offer(DesktopActivity.IdleWaiting);checks["unsupported_actions_skipped"]=q.Take(false,_=>false) is null;
            q.Offer(DesktopActivity.DocumentActivated);q.Clear();checks["clear_removes_queued_event"]=q.PendingCount==0;

            var rotation=new DesktopReactionQueue();
            var supported=new[]{PetAction.LookAround,PetAction.Fire,PetAction.Dance,PetAction.Cheer};
            PetAction? Next(bool edge=false)
            {
                rotation.Offer(DesktopActivity.PointerMoved);
                return rotation.Take(false,a=>supported.Contains(a)||(edge&&a==PetAction.EdgeLean));
            }
            var cycle=supported.Select(_=>Next()).ToArray();
            checks["all_supported_animations_play_before_any_repeat"]=cycle.OfType<PetAction>().ToHashSet().SetEquals(supported);
            checks["edge_animation_waits_until_it_fits"]=Next(edge:true)==PetAction.EdgeLean;

            using var edgeMetadata=JsonDocument.Parse("""
                {"side":"right","contactX":770,"minX":210,"maxX":775,"contactStart":20,"contactEnd":70,"minY":40,"maxY":912}
                """);
            bool validEdge=DesktopEdgeCue.TryRead(edgeMetadata.RootElement,960,960,145,out var edge);
            checks["edge_pose_aligns_without_clipping"]=validEdge &&
                edge.TryPlace(0,1920,1580,.45,1895,220,56,out var edgeLeft) &&
                Math.Abs(edgeLeft+(edge.MaxX+1)*.45-1920)<.001;
            checks["edge_pose_avoids_jumping_across_desktop"]=validEdge &&
                !edge.TryPlace(0,1920,200,.45,515,220,56,out _);
            checks["edge_pose_checks_full_vertical_envelope"]=validEdge &&
                !edge.FitsVertically(0,1040,680,.45,705,1030);
            const double pinkScale=409d/960;
            var pinkEdge=new DesktopEdgeCue(848,270,879,56,101,17,883,new double[141]);
            checks["pink_at_bottom_right_is_raised_to_fit_the_full_clip"] =
                pinkEdge.TryPlace(0,3440,3102,pinkScale,3102+672*pinkScale,389*pinkScale,56,out _) &&
                !pinkEdge.FitsVertically(0,1392,1025,pinkScale,1044,1392) &&
                pinkEdge.TryFitVertical(0,1392,1025,pinkScale,1044,1392,32,out var correctedTop) &&
                Math.Abs(correctedTop+884*pinkScale-1392)<.001;
            checks["edge_placement_does_not_teleport_or_resize"] =
                !pinkEdge.TryFitVertical(0,1392,1110,pinkScale,1129,1470,32,out _) &&
                !pinkEdge.TryFitVertical(0,200,0,pinkScale,0,200,32,out _);
            checks["edge_poses_are_not_random_autonomous_moves"]=PetBehavior.AutomaticActions.All(a=>!PetBehavior.IsEdgeAction(a));
            var stepRight=Enumerable.Repeat(770d,145).ToArray();stepRight[18]=810;
            using var dynamicMetadata=JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                side="right",contactX=770,minX=210,maxX=810,contactStart=56,contactEnd=90,stepMaxX=stepRight
            }));
            checks["edge_stride_padding_preserves_later_contact"]=
                DesktopEdgeCue.TryRead(dynamicMetadata.RootElement,960,960,145,out var tracked) &&
                tracked.TryPlace(0,1920,1580,.45,1895,220,56,out double trackedLeft) &&
                tracked.PaddingAt(17)>0 && tracked.PaddingAt(18)==40 && tracked.PaddingAt(56)==0 &&
                Math.Abs(trackedLeft+(810+1-tracked.PaddingAt(18))*.45-1920)<.001;

            string testRoot=Path.Combine(Path.GetFullPath(directory),"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(testRoot);
            string package=Path.Combine(testRoot,"pink.pet");PetCatalogVerification.Package(package,displayName:"Девочка в розовом");
            var catalog=new PetCatalog(Path.Combine(testRoot,"collection"));var pink=catalog.Import(package);catalog.Select(pink);
            checks["pink_enabled_by_default"]=catalog.DesktopReactionsEnabled(pink);
            checks["reactions_supported_for_builtin_and_custom_pets"]=catalog.DesktopReactionsEnabled(PetCatalog.Hudi)
                &&PetCatalog.SupportsDesktopReactions(pink with{Name="Инк"});
            catalog.SetDesktopReactions(pink,false);checks["preference_persists"]=!new PetCatalog(Path.Combine(testRoot,"collection")).DesktopReactionsEnabled(pink);
            catalog.SetDesktopReactions(pink,true);
            var window=new PetWindow(false,true,catalog,clickBindings:new ClickBindingsStore(testRoot));
            try{window.VerifyDesktopReactionIntegration(checks);}finally{window.Close();}
            string edgeFixture=Path.Combine(testRoot,"pink-edge.pet");
            PetCatalogVerification.Package(edgeFixture,displayName:"Девочка в розовом",desktopEdges:true);
            catalog.Select(catalog.Import(edgeFixture));
            var edgeWindow=new PetWindow(false,true,catalog,clickBindings:new ClickBindingsStore(testRoot));
            try{edgeWindow.VerifyDesktopEdgeRelease(checks);}finally{edgeWindow.Close();}
            using(var host=new HwndSource(new HwndSourceParameters("HUDI activity verification"){Width=1,Height=1,WindowStyle=0}))
            {
                var monitor=new DesktopActivityMonitor(host,_=>{});
                checks["native_keyboard_hook_attaches"]=monitor.KeyboardAttached;checks["native_clipboard_listener_attaches"]=monitor.ClipboardAttached;
                monitor.Dispose();checks["keyboard_hook_disposed"]=!monitor.KeyboardAttached;checks["clipboard_listener_disposed"]=!monitor.ClipboardAttached;
            }
            var local=new PetCatalog();
            foreach(var actual in local.List())
            {
                var sprites=local.Open(actual);
                try
                {
                var behavior=new PetBehavior(1,sprites);
                var available=Enum.GetValues<PetAction>().Where(a=>!PetBehavior.IsEdgeAction(a)&&behavior.Supports(a)).ToArray();
                var actualQueue=new DesktopReactionQueue();
                var played=available.Select(_=>
                {
                    actualQueue.Offer(DesktopActivity.PointerMoved);
                    return actualQueue.Take(false,a=>!PetBehavior.IsEdgeAction(a)&&behavior.Supports(a));
                }).OfType<PetAction>().ToHashSet();
                checks[$"{actual.Name}_rotates_all_{available.Length}_available_animations"]=available.Length>0&&played.SetEquals(available);
                }
                finally { if(!actual.BuiltIn)sprites.Dispose(); }
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
    internal void VerifyDesktopEdgeRelease(Dictionary<string,bool> checks)
    {
        RefreshDesktopReactions();SetUserActionsOnly(true);
        var work=ScreenPlacement.WorkArea(this);
        void ReleaseAtBottomRight()
        {
            Width=Height=100;Left=work.Right-100;Top=work.Bottom-75;
            dragging=true;EndPointerGesture();UpdateDesktopEdge();behavior.Update(.01,false);ApplyDesktopEdgePadding();
        }
        ReleaseAtBottomRight();
        checks["drag_release_starts_edge_lean_in_user_actions_mode"]=behavior.CurrentAction==PetAction.EdgeLean&&Math.Abs(Top+Height-work.Bottom)<.01;
        for(int i=0;i<20;i++)behavior.Update(.02,false);
        ReleaseAtBottomRight();
        checks["second_explicit_drag_can_start_edge_peek_immediately"]=behavior.CurrentAction==PetAction.EdgePeek&&behavior.Interruptions==0;
    }

    internal void VerifyDesktopReactionIntegration(Dictionary<string,bool> checks)
    {
        RefreshDesktopReactions();checks["test_runtime_never_installs_hooks"]=DesktopReactionsEnabled&&!DesktopMonitorRunning;
        SetDesktopReactions(false);ObserveDesktopActivity(DesktopActivity.DocumentActivated);
        checks["disabled_monitor_does_not_queue"]=PendingDesktopReactions==0&&!DesktopMonitorRunning;
        SetDesktopReactions(true);dragging=true;ObserveDesktopActivity(DesktopActivity.TypingStarted);
        checks["dragging_suppresses_reactions"]=PendingDesktopReactions==0;dragging=false;
        paused=true;ObserveDesktopActivity(DesktopActivity.IdleWaiting);checks["paused_suppresses_idle"]=PendingDesktopReactions==0;paused=false;
        ObserveDesktopActivity(DesktopActivity.DocumentActivated);paused=true;UpdateDesktopReactions();
        checks["pausing_discards_existing_queue"]=PendingDesktopReactions==0;paused=false;
        QueuePetClick(1);
        ObserveDesktopActivity(DesktopActivity.DocumentActivated);
        checks["unassigned_click_does_not_block_desktop_reactions"]=pendingClicks==0&&PendingDesktopReactions==1;
        desktopReactions.Clear();
        ObserveDesktopActivity(DesktopActivity.IdleWaiting);
        SetUserActionsOnly(true);
        checks["user_actions_only_discards_pending_idle"]=PendingDesktopReactions==0;
        ObserveDesktopActivity(DesktopActivity.IdleWaiting);
        checks["user_actions_only_ignores_idle"]=PendingDesktopReactions==0;
        ObserveDesktopActivity(DesktopActivity.TypingStarted);
        ObserveDesktopActivity(DesktopActivity.PointerMoved);
        ObserveDesktopActivity(DesktopActivity.AppSwitched);
        checks["user_actions_only_keeps_typing_pointer_and_app_reactions"]=PendingDesktopReactions==3;
        checks["user_actions_only_is_saved_per_pet"]=Pets.UserActionsOnly(selectedPet)&&!Pets.UserActionsOnly(PetCatalog.Hudi);
        var manualOnly=new PetBehavior(2,library);
        for(int i=0;i<400;i++)manualOnly.Update(.05,autonomous:false);
        checks["user_actions_only_has_no_autonomous_playback"]=manualOnly.ActionsStarted==0;
        manualOnly.Trigger(PetAction.Dance);manualOnly.Update(.05,autonomous:false);
        checks["user_actions_only_keeps_manual_playback"]=manualOnly.CurrentAction==PetAction.Dance;
        SetUserActionsOnly(false);
        desktopReactions.Clear();
        behavior.Trigger(PetAction.Dance);ObserveDesktopActivity(DesktopActivity.DocumentActivated);UpdateDesktopReactions();
        checks["manual_pending_animation_has_priority"]=PendingDesktopReactions==1;
        behavior.Update(.1,false);checks["manual_action_is_preserved"]=behavior.CurrentAction==PetAction.Dance;
        for(int i=0;i<100;i++)behavior.Update(.05,false);UpdateDesktopReactions();behavior.Update(.1,false);
        checks["deferred_reaction_uses_own_animation"]=behavior.CurrentAction==PetAction.LookAround&&behavior.Interruptions==0;
        desktopReactions=new();ObserveDesktopActivity(DesktopActivity.DocumentActivated);BeginPointerGesture(new Point(1,1),2,true);
        checks["manual_pointer_clears_reaction_queue"]=PendingDesktopReactions==0;
        chat.Dispose();var client=new DesktopReactionTestClient();chat=new ChatSession(client);
        Task request=chat.SendAsync("fixture",new ModelSettings{Model="fixture",UseApiKey=false},"");
        ObserveDesktopActivity(DesktopActivity.IdleWaiting);ObserveDesktopActivity(DesktopActivity.TypingStarted);
        checks["chat_busy_suppresses_idle_and_typing"]=chat.Busy&&PendingDesktopReactions==0;
        client.Reply.SetResult(new ModelReply("fixture"));UiVerification.Advance(80);
        checks["fake_chat_completed_without_network"]=request.IsCompletedSuccessfully;
        SetDesktopReactions(true);ObserveDesktopActivity(DesktopActivity.PointerMoved);
        Pets.SetDesktopReactions(PetCatalog.Hudi,false);
        var original=selectedPet;selectedPet=PetCatalog.Hudi;RefreshDesktopReactions();
        checks["switching_to_opted_out_pet_stops_reactions"]=!DesktopReactionsEnabled&&!DesktopMonitorRunning&&PendingDesktopReactions==0;
        selectedPet=original;RefreshDesktopReactions();
    }
    private sealed class DesktopReactionTestClient:IModelClient
    {
        internal readonly TaskCompletionSource<ModelReply> Reply=new();
        public Task<ModelReply> CompleteAsync(ModelSettings settings,string key,IReadOnlyList<ChatMessage> messages,IProgress<string>? progress,CancellationToken cancellation)=>Reply.Task.WaitAsync(cancellation);
        public Task<string[]> ListModelsAsync(ModelSettings settings,string key,CancellationToken cancellation)=>Task.FromResult(new[]{"fixture"});
    }
}
