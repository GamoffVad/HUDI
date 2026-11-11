namespace HudiPet;

internal enum PetAction { Wink, Think, Crouch, Stretch, Angry, Sad, Surprise, LookAround, Dance, Hood, Pockets, Combat, Salute, March, Notebook, Fire, FireCalm, Dash, Jump, PaintMagic, ShadowMerge, SideStep, CutThroat, LookDown, LookLeft, LookRight, LookUp, Temple, Skeptical, Shrug, Indignation, FireTrick, Impatient, Stroll, AdjustGlasses, Groove, Ironic, SurpriseClassic, IndignantLean, Giggle, Reproach, HeadTap, PeekAround, WalkAround, QuietIdle, HairBrush, Doze, Cheer, EdgeLean, EdgePeek, Gymnastics, Peek }
internal readonly record struct PetPose(string Frame = "neutral", string? BlendFrom = null,
    double Blend = 1, double Breath = 0, int TransitionId = 0);

// Only whole Higgsfield frames are played. No cursor-driven facial processing.
internal sealed class PetBehavior
{
    internal static readonly PetAction[] AutomaticActions =
        [PetAction.Think, PetAction.Crouch, PetAction.Stretch, PetAction.Wink,
         PetAction.Angry, PetAction.Sad, PetAction.Surprise, PetAction.LookAround,
         PetAction.Dance, PetAction.Hood, PetAction.Pockets,
         PetAction.Salute, PetAction.March, PetAction.Notebook, PetAction.Fire, PetAction.FireCalm,
         PetAction.Dash, PetAction.Jump, PetAction.PaintMagic, PetAction.ShadowMerge,
         PetAction.SideStep, PetAction.CutThroat,
         PetAction.LookDown, PetAction.LookLeft, PetAction.LookRight, PetAction.LookUp,
         PetAction.Temple, PetAction.Skeptical, PetAction.Shrug,
         PetAction.Indignation, PetAction.FireTrick, PetAction.Impatient,
         PetAction.Stroll, PetAction.AdjustGlasses, PetAction.Groove, PetAction.Ironic, PetAction.SurpriseClassic,
         PetAction.IndignantLean, PetAction.Giggle, PetAction.Reproach, PetAction.HeadTap, PetAction.PeekAround, PetAction.WalkAround,
         PetAction.QuietIdle, PetAction.HairBrush, PetAction.Doze, PetAction.Cheer, PetAction.Gymnastics, PetAction.Peek];
    private readonly SpriteLibrary library;
    private readonly Random random;
    private readonly Queue<PetAction> bag = new();
    private PetAction? pending, lastAutomatic, requestedRandom, lastRandom;
    private SpriteStep[]? active;
    private int clipIndex, transitionId;
    private double elapsed, breathTime, actionTimer = 4.5, blendTime, blendDuration;
    private double pocketHoldElapsed, pocketHoldDuration;
    private bool pocketHoldVisited, holdingPockets;
    private bool chatThinking, holdingThought, thoughtVisited;
    private double thoughtElapsed;
    internal bool ChatThinking => chatThinking;
    internal bool HoldingThought => holdingThought;
    internal void BeginChatThinking()
    {
        chatThinking = true;
        pending = Supports(PetAction.Think)?PetAction.Think:null;
        if (holdingPockets) pocketHoldDuration = (Math.Floor(pocketHoldElapsed / PocketLoopSeconds) + 1) * PocketLoopSeconds;
    }
    internal void EndChatThinking(bool success)
    {
        chatThinking = false;
        if (pending == PetAction.Think) pending = null;
        if (success && Supports(PetAction.Dance)) pending = PetAction.Dance;
    }
    private string frame = "neutral";
    private string? blendFrom;
    private const double TransitionSeconds = .65;
    private int PocketLoopStart=>library.Holds.TryGetValue("pockets",out var hold)?hold.Start:-1;
    private int PocketLoopEnd=>library.Holds["pockets"].End;
    private double PocketLoopSeconds=>library.Holds.TryGetValue("pockets",out var hold)?hold.Seconds:3.2;
    private ClipHold? ThoughtHold=>library.Holds.TryGetValue("think",out var hold)?hold:null;
    internal int ActionsStarted { get; private set; }
    internal int Interruptions { get; private set; }
    internal int BreathingUpdates { get; private set; }
    internal PetAction? CurrentAction { get; private set; }
    internal int CurrentStepIndex => active is null ? -1 : clipIndex;
    internal bool HoldingPockets => holdingPockets;
    internal double PocketHoldDuration => pocketHoldDuration;
    internal HashSet<PetAction> SeenActions { get; } = [];
    internal bool IsPlaying => active is not null || pending is not null || requestedRandom is not null || blendFrom is not null;
    internal PetBehavior(int? seed = null,SpriteLibrary? sprites=null) {random=seed.HasValue?new Random(seed.Value):new Random();library=sprites??SpriteLibrary.Shared.Value;frame=library.NeutralFrame;}
    internal bool Supports(PetAction action)=>library.Clips.ContainsKey(ClipName(action));
    internal static string ClipName(PetAction action) => action.ToString().ToLowerInvariant();

    internal void Trigger(PetAction action)
    {
        if(!Supports(action))return;
        if (action == PetAction.Combat && CurrentAction == PetAction.Combat) return;
        if (action == PetAction.Combat || pending != PetAction.Combat) pending = action;
    }

    internal PetAction? TriggerRandomAction()
    {
        var available=Enum.GetValues<PetAction>().Where(action=>!IsEdgeAction(action)&&Supports(action)).ToArray();
        if(available.Length==0)return null;
        var choices=available.Where(a=>a!=lastRandom&&a!=CurrentAction).ToArray();
        if(choices.Length==0)choices=available.Where(a=>a!=lastRandom).ToArray();
        if(choices.Length==0)choices=available;
        requestedRandom=lastRandom=choices[random.Next(choices.Length)];
        // Leave a long pocket hold through its next smooth boundary.
        // The current clip and model-thinking state keep their normal exits.
        if(holdingPockets)pocketHoldDuration=(Math.Floor(pocketHoldElapsed/PocketLoopSeconds)+1)*PocketLoopSeconds;
        else if(CurrentAction==PetAction.Pockets)pocketHoldDuration=0;
        return requestedRandom;
    }

    internal static bool IsEdgeAction(PetAction action)=>action is PetAction.EdgeLean or PetAction.EdgePeek;

    private void Rest() => actionTimer = 2 + random.NextDouble() * 3;
    private static double Ease(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private void TransitionTo(string target, double duration)
    {
        transitionId++;
        blendFrom = frame == target ? null : frame;
        blendTime = 0;
        blendDuration = duration;
        frame = target;
    }
    private void Start(PetAction action)
    {
        pending = null;
        if (active is not null) Interruptions++;
        CurrentAction = action;
        ActionsStarted++;
        SeenActions.Add(action);
        active = library.Clips[ClipName(action)];
        clipIndex = 0;
        elapsed = 0;
        holdingPockets = pocketHoldVisited = false;
        holdingThought = thoughtVisited = false;
        thoughtElapsed = 0;
        pocketHoldElapsed = 0;
        pocketHoldDuration = action == PetAction.Pockets && PocketLoopStart>=0 && random.NextDouble() < .6
            ? random.Next(2, 5) * PocketLoopSeconds : 0;
        TransitionTo(active[0].Frame, action == PetAction.Combat ? .4 : TransitionSeconds);
        Rest();
    }

    private void AdvanceClip(double dt)
    {
        if (holdingThought)
        {
            var hold=ThoughtHold!.Value;
            double before = thoughtElapsed;
            thoughtElapsed += dt;
            if (!chatThinking && (int)(thoughtElapsed / hold.Seconds) > (int)(before / hold.Seconds))
            {
                holdingThought = false; elapsed = 0; clipIndex = hold.Start; frame = active![hold.Start].Frame;
                dt = thoughtElapsed % hold.Seconds;
            }
            else
            {
                double cycle = thoughtElapsed % hold.Seconds / hold.Seconds;
                double leg = cycle <= .5 ? cycle * 2 : 2 - cycle * 2;
                frame = active![hold.Start + (int)Math.Round((hold.End-hold.Start) * Ease(leg))].Frame;
                return;
            }
        }
        if (holdingPockets)
        {
            pocketHoldElapsed += dt;
            if (pocketHoldElapsed < pocketHoldDuration)
            {
                // A quiet forward/backward passage through intact source frames.
                // Zero velocity at both ends prevents a loop-seam jump.
                double cycle = pocketHoldElapsed % PocketLoopSeconds / PocketLoopSeconds;
                double leg = cycle <= .5 ? cycle * 2 : 2 - cycle * 2;
                int index = PocketLoopStart + (int)Math.Round((PocketLoopEnd - PocketLoopStart) * Ease(leg));
                frame = active![index].Frame;
                return;
            }
            holdingPockets = false;
            dt = pocketHoldElapsed - pocketHoldDuration;
            frame = active![PocketLoopStart].Frame;
        }
        elapsed += dt;
        while (active is not null && elapsed >= active[clipIndex].Seconds)
        {
            elapsed -= active[clipIndex].Seconds;
            if (++clipIndex >= active.Length)
            {
                active = null;
                CurrentAction = null;
                breathTime = 0;
                // Rest exactly where the source performance ended. A forced
                // dissolve back to "neutral" caused the reported body/face snap.
                Rest();
                return;
            }
            frame = active[clipIndex].Frame;
            if (CurrentAction == PetAction.Think && ThoughtHold is {} hold && clipIndex == hold.Start && chatThinking && !thoughtVisited)
            {
                thoughtVisited = holdingThought = true; thoughtElapsed = elapsed = 0; return;
            }
            if (CurrentAction == PetAction.Pockets && clipIndex == PocketLoopStart && !pocketHoldVisited)
            {
                pocketHoldVisited = true;
                if (pocketHoldDuration > 0)
                {
                    holdingPockets = true;
                    pocketHoldElapsed = 0;
                    elapsed = 0;
                    return;
                }
            }
        }
    }
    internal PetPose Update(double dt, bool autonomous = true)
    {
        dt = double.IsFinite(dt) ? Math.Clamp(dt, 0, .15) : 0;
        if (blendFrom is not null && (blendTime += dt) >= blendDuration) blendFrom = null;
        if (pending == PetAction.Combat) Start(PetAction.Combat);
        if (active is not null && blendFrom is null)
        {
            AdvanceClip(dt);
        }
        else if (autonomous && active is null && blendFrom is null)
        {
            breathTime = (breathTime + dt) % 4.8;
            BreathingUpdates++;
            actionTimer -= dt;
        }
        if (chatThinking && Supports(PetAction.Think) && pending is null && active is null && blendFrom is null) pending = PetAction.Think;
        if(!chatThinking&&requestedRandom is {} requested&&pending is null&&active is null&&blendFrom is null)
        {pending=requested;requestedRandom=null;}
        if (autonomous && !chatThinking && actionTimer <= 0 && pending is null && active is null && blendFrom is null)
        {
            if (bag.Count == 0)
            {
                var choices = AutomaticActions.Where(Supports).OrderBy(_ => random.Next()).ToList();
                if (choices.Count>1&&choices[0] == lastAutomatic) (choices[0], choices[1]) = (choices[1], choices[0]);
                foreach (var action in choices) bag.Enqueue(action);
            }
            if(bag.Count>0){pending = bag.Dequeue(); lastAutomatic = pending;}else Rest();
        }
        if (pending is { } next && active is null && blendFrom is null) Start(next);
        double t = blendDuration > 0 ? Math.Clamp(blendTime / blendDuration, 0, 1) : 1;
        return new PetPose(frame, blendFrom, blendFrom is null ? 1 : Ease(t),
            autonomous && active is null && blendFrom is null ? .5 - .5 * Math.Cos(breathTime * Math.PI * 2 / 4.8) : 0, transitionId);
    }
}
