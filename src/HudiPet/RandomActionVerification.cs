namespace HudiPet;

internal static class RandomActionVerification
{
    private static bool Finish(PetBehavior engine,double seconds)
    {
        for(int i=0;i<Math.Ceiling(seconds/.05)&&engine.IsPlaying;i++)engine.Update(.05,false);
        return !engine.IsPlaying;
    }
    internal static Dictionary<string,bool> Run(SpriteLibrary library)
    {
        var checks=new Dictionary<string,bool>();var engine=new PetBehavior(39,library);
        int available=Enum.GetValues<PetAction>().Count(engine.Supports);
        // A random request can wait for one complete action and then play another.
        double finishBudget=2*library.Clips.Values.Max(steps=>steps.Sum(step=>step.Seconds))+
            library.Holds.Values.Select(hold=>hold.Seconds*4).DefaultIfEmpty(0).Max()+5;
        PetAction? previous=null;bool supported=true,different=true,played=true,finished=true;var seen=new HashSet<PetAction>();
        for(int i=0;i<Math.Max(40,available*4);i++)
        {
            var selected=engine.TriggerRandomAction();
            supported&=selected is {} value&&engine.Supports(value);
            different&=available<2||selected!=previous;
            engine.Update(.05,false);played&=engine.CurrentAction==selected;
            finished&=Finish(engine,finishBudget);if(selected is {} action)seen.Add(action);previous=selected;
        }
        checks["random_choices_are_supported"]=supported;
        checks["successive_random_clicks_differ"]=different&&seen.Count>=Math.Min(2,available);
        checks["each_choice_plays_and_finishes"]=played&&finished;
        checks["random_actions_do_not_interrupt"]=engine.Interruptions==0;

        var burst=new PetBehavior(12,library);PetAction? last=null;
        for(int i=0;i<30;i++)last=burst.TriggerRandomAction();
        burst.Update(.05,false);checks["rapid_clicks_keep_latest_choice"]=burst.CurrentAction==last;
        checks["rapid_clicks_do_not_accumulate"]=Finish(burst,finishBudget)&&burst.ActionsStarted==1;

        var active=new PetBehavior(15,library);var first=Enum.GetValues<PetAction>().First(active.Supports);active.Trigger(first);
        active.Update(0,false);
        var next=active.TriggerRandomAction();
        if(active.Supports(PetAction.Combat))for(int i=0;i<100&&next!=PetAction.Combat;i++)next=active.TriggerRandomAction();
        int starts=active.ActionsStarted;active.Update(.01,false);
        checks["current_clip_is_allowed_to_finish"]=active.ActionsStarted==starts&&active.Interruptions==0;
        checks["queued_choice_eventually_plays"]=Finish(active,finishBudget)&&next is {} queued&&active.SeenActions.Contains(queued)&&active.Interruptions==0;

        var thinking=new PetBehavior(22,library);thinking.BeginChatThinking();
        for(int i=0;i<20;i++)thinking.Update(.05,false);
        var afterThinking=thinking.TriggerRandomAction();
        for(int i=0;i<100;i++)thinking.Update(.05,false);
        checks["model_thinking_keeps_priority"]=thinking.ChatThinking&&thinking.SeenActions.All(a=>a==PetAction.Think);
        thinking.EndChatThinking(false);
        checks["random_click_waits_for_model_reply"]=Finish(thinking,finishBudget)&&afterThinking is {} after&&thinking.SeenActions.Contains(after)&&thinking.Interruptions==0;
        return checks;
    }
}
